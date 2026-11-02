"""Local, cancellable music analysis. All timestamps refer to the selected excerpt.

Protocol v1: --request request.json, progress.json / analysis.json in outputDir.
No network access or model download is used. A cancel.flag stops at checkpoints;
the caller may terminate the process during an expensive decode/time-stretch.
"""
from __future__ import annotations

import argparse
import json
import math
import os
from pathlib import Path
import sys
import time
import traceback


def atomic_json(path: Path, value: dict):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(value, ensure_ascii=False, allow_nan=False, indent=2), encoding='utf-8')
    # Unity may briefly hold progress.json open without FILE_SHARE_DELETE on Windows.
    for attempt in range(8):
        try:
            os.replace(temporary, path)
            break
        except PermissionError:
            if attempt == 7:
                raise
            time.sleep(.025 * (attempt + 1))


class Cancelled(Exception):
    pass


def analyze(request: dict):
    if request.get('schemaVersion') != 1:
        raise ValueError('不支持的分析请求版本。')
    out = Path(request['outputDir']).resolve()
    out.mkdir(parents=True, exist_ok=True)
    # Never require write access to the installed Python site-packages directory.
    os.environ.setdefault('NUMBA_CACHE_DIR', str(out / '.numba-cache'))

    def progress(value, message):
        if (out / 'cancel.flag').exists():
            raise Cancelled('已取消分析。')
        atomic_json(out / 'progress.json', dict(schemaVersion=1, progress=value, message=message, state='running'))

    progress(.02, '正在加载音乐分析组件…')
    import numpy as np
    import soundfile as sf
    import librosa

    source = Path(request['sourcePath']).resolve()
    if source.suffix.lower() not in ('.wav', '.mp3'):
        raise ValueError('请选择 WAV 或 MP3 文件。')
    start = float(request.get('startSeconds', 0))
    requested_duration = float(request.get('durationSeconds', 40))
    if not math.isfinite(start) or start < 0 or not math.isfinite(requested_duration) or not 30 <= requested_duration <= 60:
        raise ValueError('起点必须大于等于零，片段长度应为 30～60 秒。')
    progress(.08, '正在解码所选片段…')
    # Seek before decoding: an hour-long source must not become an hour-long allocation.
    with sf.SoundFile(str(source)) as audio:
        source_duration = len(audio) / audio.samplerate
        if start >= source_duration or source_duration - start < 30 - .001:
            raise ValueError('起点之后至少需要 30 秒音频，请调整起点。')
        audio.seek(round(start * audio.samplerate))
        signal = audio.read(round(min(requested_duration, source_duration - start) * audio.samplerate), dtype='float32', always_2d=True)
        original_sr = audio.samplerate
    if signal.shape[1] > 2:
        signal = signal.mean(axis=1, keepdims=True)
    if not np.isfinite(signal).all():
        raise ValueError('音频包含无效采样值，请换一个文件。')
    if float(np.sqrt(np.mean(signal.astype('float64') ** 2))) < 1e-5:
        raise ValueError('片段为静音或音量过低，无法检测节拍。请选择有清晰节奏的片段。')
    signal = librosa.resample(signal.T, orig_sr=original_sr, target_sr=44100, axis=-1).T
    sr = 44100
    duration = len(signal) / sr
    sf.write(str(out / 'normal.wav'), signal, sr, subtype='PCM_16')
    progress(.2, '正在提取节拍与强弱…')
    mono = signal.mean(axis=1)
    analysis_sr = 22050
    y = librosa.resample(mono, orig_sr=sr, target_sr=analysis_sr)
    hop = 256
    onset = librosa.onset.onset_strength(y=y, sr=analysis_sr, hop_length=hop)
    tempo, frames = librosa.beat.beat_track(onset_envelope=onset, sr=analysis_sr, hop_length=hop, trim=False)
    bpm = float(np.asarray(tempo).reshape(-1)[0])
    beats = librosa.frames_to_time(frames, sr=analysis_sr, hop_length=hop)
    beats = beats[(beats >= 0) & (beats < duration)]
    rms = librosa.feature.rms(y=y, hop_length=hop)[0]
    normalized = np.clip(rms / max(float(np.percentile(rms, 95)), 1e-8), 0, 1)
    energy = [float(normalized[min(round(float(t) * analysis_sr / hop), len(normalized)-1)]) for t in beats]
    warnings = ['自动节拍不等同于乐句或八拍起点，请试听并校正。']
    usable = len(beats) >= 8 and math.isfinite(bpm) and 40 <= bpm <= 240 and float(np.std(onset)) > .005
    if not usable:
        bpm = 120.
        beats = np.empty(0)
        energy = []
        warnings.append('未找到可靠节拍；请手动设置 BPM、首拍和八拍起点。')
    for index, (rate, filename) in enumerate(((.75, 'slow075.wav'), (.5, 'slow050.wav'))):
        progress(.4 + index * .26, f'正在准备 {rate:g} 倍速音频（保持音高）…')
        slow = librosa.effects.time_stretch(signal.T, rate=rate).T
        progress(.61 + index * .26, '正在保存慢速音频…')
        sf.write(str(out / filename), slow, sr, subtype='PCM_16')
    result = dict(schemaVersion=1, sourceName=source.name, sourceStartSeconds=start,
                  sourceDurationSeconds=source_duration, durationSeconds=duration, sampleRate=sr,
                  normalAudio='normal.wav', slow075Audio='slow075.wav', slow050Audio='slow050.wav',
                  bpm=bpm, beats=[float(t) for t in beats], energy=energy,
                  needsManualGrid=not usable, warnings=warnings)
    progress(.96, '正在完成分析…')
    atomic_json(out / 'analysis.json', result)
    atomic_json(out / 'progress.json', dict(schemaVersion=1, progress=1., message='分析完成', state='complete'))
    return result


def make_demo(path: Path, bpm=120., duration=40.):
    """Original synthesized metronome/backing track; never a third-party song."""
    import numpy as np
    import soundfile as sf
    sr = 44100
    y = np.zeros(round(duration * sr), dtype=np.float32)
    rng = np.random.default_rng(42)
    for n, at in enumerate(np.arange(.25, duration, 60 / bpm)):
        t = np.arange(round(.12 * sr)) / sr
        hit = .5 * np.sin(2 * np.pi * (80 * t + 70 * .025 * (1-np.exp(-t/.025)))) * np.exp(-t * 45)
        hit += .16 * rng.normal(size=len(t)) * np.exp(-t * 120)
        if n % 4 == 0:
            hit += .2 * np.sin(2*np.pi*880*t) * np.exp(-t*35)
        begin = round(at * sr)
        count = min(len(hit), len(y)-begin)
        y[begin:begin+count] += hit[:count]
    t = np.arange(len(y)) / sr
    y += .025 * np.sin(2*np.pi*220*t) * (.5+.5*np.sin(2*np.pi*bpm/60*t))
    path.parent.mkdir(parents=True, exist_ok=True)
    sf.write(str(path), y, sr, subtype='PCM_16')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--request', type=Path)
    parser.add_argument('--demo', type=Path)
    args = parser.parse_args()
    if args.demo:
        make_demo(args.demo)
        return 0
    if not args.request:
        parser.error('--request 或 --demo 为必填项')
    out = None
    try:
        request = json.loads(args.request.read_text(encoding='utf-8-sig'))
        out = Path(request['outputDir'])
        analyze(request)
        return 0
    except Exception as exc:
        state = 'cancelled' if isinstance(exc, Cancelled) else 'error'
        if out:
            atomic_json(out / 'progress.json', dict(schemaVersion=1, progress=0., state=state, message=str(exc)))
        print(str(exc), file=sys.stderr)
        if state == 'error':
            traceback.print_exc(file=sys.stderr)
        return 2 if state == 'cancelled' else 1


if __name__ == '__main__':
    raise SystemExit(main())
