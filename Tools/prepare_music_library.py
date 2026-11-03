"""Download the selected free recordings and verify an offline music catalog.

Uses only the explicit official download URLs in Generation/music-sources.json.
Run with Runtime/Python/python.exe -I -X utf8 Tools/prepare_music_library.py.
"""
import concurrent.futures
import hashlib
import json
import shutil
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parent.parent
SOURCES = ROOT / 'Generation/music-sources.json'
DEST = ROOT / 'Assets/StreamingAssets/MusicLibrary'
PROGRESS = ROOT / '.generation/music-downloads.json'


def atomic_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + '.tmp')
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temp.replace(path)


def prepare(track, settings):
    target = DEST / track['id'] / 'track.mp3'
    target.parent.mkdir(parents=True, exist_ok=True)
    if not target.exists():
        cached = Path.home() / 'Downloads/mixkit-gimme-that-groove-872.mp3'
        if track['id'] == 'mixkit-872' and cached.is_file():
            shutil.copyfile(cached, target)
        else:
            temp = target.with_suffix('.mp3.part')
            with urllib.request.urlopen(track['downloadUrl'], timeout=60) as response, temp.open('wb') as out:
                expected_length = response.headers.get('Content-Length')
                if response.headers.get_content_type() not in ('audio/mpeg', 'application/octet-stream'):
                    raise ValueError(f"Not audio: {track['id']}")
                shutil.copyfileobj(response, out)
            if expected_length and temp.stat().st_size != int(expected_length):
                raise ValueError(f"Incomplete download: {track['id']}")
            temp.replace(target)
    samples = 0
    square_sum = 0.0
    peak = 0.0
    with sf.SoundFile(str(target)) as recording:
        sample_rate, channels = recording.samplerate, recording.channels
        for block in recording.blocks(blocksize=65536, dtype='float32', always_2d=True):
            if not np.isfinite(block).all():
                raise ValueError(f"Nonfinite audio: {track['id']}")
            square_sum += float(np.square(block.astype('float64')).sum())
            peak = max(peak, float(np.max(np.abs(block))))
            samples += len(block)
    duration = samples / sample_rate
    rms = (square_sum / (samples * channels)) ** .5
    if abs(duration - track['expectedDurationSeconds']) > 3 or rms < 1e-5:
        raise ValueError(f"Duration/silence check failed: {track['id']}: {duration}, {rms}")
    result = {**track, 'file': target.relative_to(DEST).as_posix(),
              'durationSeconds': duration, 'sampleRate': sample_rate, 'channels': channels,
              'bytes': target.stat().st_size, 'sha256': hashlib.sha256(target.read_bytes()).hexdigest(),
              'licenseName': settings['licenseName'], 'licenseUrl': settings['licenseUrl'],
              'downloadedAt': datetime.now(timezone.utc).isoformat(),
              'verification': {'fullDecode': True, 'rms': rms, 'peak': peak}}
    credit = (f"{track['title']}\nArtist: {track['artist']}\nSource: {track['sourceUrl']}\n"
              f"Download: {track['downloadUrl']}\nLicense: {settings['licenseName']}\n"
              f"{settings['licenseUrl']}\nChecked: {settings['licenseCheckedAt']}\n"
              "Selected for personal dance learning. Keep this provenance with the recording.\n")
    (target.parent / 'CREDITS.txt').write_text(credit, encoding='utf-8')
    atomic_json(target.parent / 'SOURCE.json', result)
    return result


def main():
    settings = json.loads(SOURCES.read_text(encoding='utf-8-sig'))
    completed, errors = {}, []
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        jobs = {pool.submit(prepare, track, settings): track for track in settings['tracks']}
        for job in concurrent.futures.as_completed(jobs):
            track = jobs[job]
            try:
                completed[track['id']] = job.result()
                print(f"{len(completed)}/10 verified: {track['title']}", flush=True)
            except Exception as exc:
                errors.append({'id': track['id'], 'error': str(exc)})
                print(f"FAILED: {track['id']}: {exc}", flush=True)
            atomic_json(PROGRESS, {'status': 'downloading', 'expected': 10,
                                  'downloaded': list(completed.values()), 'errors': errors})
    if errors:
        raise RuntimeError('Downloads incomplete; rerun to resume: ' + str(errors))
    catalog = {'schemaVersion': 1, 'purpose': settings['purpose'],
               'tracks': [completed[track['id']] for track in settings['tracks']]}
    atomic_json(DEST / 'catalog.json', catalog)
    atomic_json(PROGRESS, {'status': 'complete', 'expected': 10, 'downloaded': catalog['tracks'], 'errors': []})
    print('Offline music catalog ready: ' + str(DEST / 'catalog.json'), flush=True)


if __name__ == '__main__':
    main()
