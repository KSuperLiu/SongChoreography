"""Make a deterministic original 100 BPM instrumental for beat verification."""
import json
from pathlib import Path
import wave
import numpy as np

ROOT = Path(__file__).resolve().parent.parent
SR = 44100
BPM = 100
BEAT = 60 / BPM
DURATION = 64 * BEAT


def make_track():
    rng = np.random.default_rng(20261103)
    audio = np.zeros((round(DURATION * SR), 2), np.float64)

    def add(signal, at, volume=1, pan=0):
        start = round(at * SR)
        if start >= len(audio):
            return
        n = min(len(signal), len(audio) - start)
        signal = signal[:n] * volume
        audio[start:start+n, 0] += signal * np.sqrt((1-pan)/2)
        audio[start:start+n, 1] += signal * np.sqrt((1+pan)/2)

    def clock(length):
        return np.arange(round(length * SR)) / SR

    def kick():
        t = clock(.32)
        phase = 2*np.pi*(46*t + 50*.028*(1-np.exp(-t/.028)))
        return np.sin(phase)*np.exp(-t*15) + rng.normal(size=len(t))*.07*np.exp(-t*160)

    def snare():
        t = clock(.22)
        noise = rng.normal(size=len(t));noise -= np.convolve(noise, np.ones(13)/13, mode="same")
        return noise*.4*np.exp(-t*22) + .25*np.sin(2*np.pi*185*t)*np.exp(-t*25)

    def hat(opened=False):
        t = clock(.16 if opened else .065)
        noise = rng.normal(size=len(t));noise -= np.convolve(noise, np.ones(7)/7, mode="same")
        return noise*.22*np.exp(-t*(25 if opened else 65))

    def note(midi, duration, bass=False):
        t = clock(duration);f = 440*2**((midi-69)/12)
        if bass:
            tone = np.sin(2*np.pi*f*t)+.26*np.sin(4*np.pi*f*t)+.07*np.sin(6*np.pi*f*t)
            envelope = np.minimum(t/.006, 1)*np.exp(-t*5)*np.minimum((duration-t)/.015, 1)
        else:
            tone = np.sin(2*np.pi*f*t)+.18*np.sin(4*np.pi*f*t)+.06*np.sin(6*np.pi*f*t)
            envelope = np.minimum(t/.008, 1)*np.exp(-t*7)*np.minimum((duration-t)/.03, 1)
        return tone*envelope

    # Straight quarter-note anchors, syncopated bass and offbeat chord stabs.
    # Every 8-count boundary is an exact sample-known musical phrase boundary.
    chords = [[60,63,67,70,74], [63,67,70,74], [56,60,63,67], [55,58,62,65]]
    roots = [36,39,32,31]
    for bar in range(16):
        at = bar*4*BEAT
        for beat in [0, 2]:add(kick(),at+beat*BEAT,.74)
        if bar%2:add(kick(),at+2.75*BEAT,.36)
        for beat in [1, 3]:add(snare(),at+beat*BEAT,.36)
        for eighth in range(8):add(hat(eighth==7 and bar%4==3),at+eighth*.5*BEAT,.36 if eighth%2==0 else .23, .25 if eighth%2 else -.2)
        root=roots[(bar//2)%4]
        for offset, pitch, length in [(0,root,.42),(1.5,root+12,.22),(2,root,.38),(3.5,root+7,.24)]:
            add(note(pitch,length,True),at+offset*BEAT,.38)
        for offset in [.5,2.5]:
            chord=sum(note(p,.23) for p in chords[(bar//2)%4])/len(chords[(bar//2)%4])
            add(chord,at+offset*BEAT,.32,-.22)
        if bar%2==1:
            for j,pitch in enumerate([79,77,74]):add(note(pitch,.16),at+(2.75+j*.25)*BEAT,.09,.3)
    audio *= np.minimum(np.arange(len(audio))/80,1)[:,None]
    fade = round(.08*SR);audio[-fade:] *= np.linspace(1,0,fade)[:,None]
    audio = np.tanh(audio*1.15)
    audio *= .88 / max(1, np.max(np.abs(audio)))
    out = ROOT / ".generation/practice100"
    out.mkdir(parents=True,exist_ok=True)
    with wave.open(str(out/"original-100bpm.wav"),"wb") as w:
        w.setnchannels(2);w.setsampwidth(2);w.setframerate(SR)
        w.writeframes((audio*32767).astype("<i2").tobytes())
    metadata=dict(title="Original Groove · 100 BPM",bpm=BPM,firstBeatSeconds=0,
                  durationSeconds=DURATION,beats=64,sampleRate=SR,
                  authorship="Original procedural composition created for this project; no external recording or paid service.",
                  generator="Tools/make_practice_track.py",seed=20261103)
    (out/"SOURCE.json").write_text(json.dumps(metadata,indent=2),encoding="utf-8")
    request=dict(schemaVersion=1,sourcePath=str(out/"original-100bpm.wav"),outputDir=str(out/"analyzed"),startSeconds=0,durationSeconds=DURATION)
    (out/"request.json").write_text(json.dumps(request),encoding="utf-8")
    print(json.dumps(metadata))


if __name__ == "__main__":
    make_track()
