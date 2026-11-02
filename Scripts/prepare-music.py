"""Download explicitly licensed music from its author and build offline examples.

Sources and licenses are reviewed in Samples/music-sources.json. The author is
credited in the application and every redistributed project. No runtime network.
"""
from concurrent.futures import ThreadPoolExecutor
from hashlib import sha256
import json
import os
from pathlib import Path
import sys
from urllib.request import Request, urlopen
import time

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'Analyzer'))
os.environ.setdefault('NUMBA_CACHE_DIR',str(ROOT/'TestResults/music-numba-cache'))
from analyzer import analyze, atomic_json


def download(track):
    source=ROOT/'Samples/MusicSources'/(track['id']+'.mp3')
    source.parent.mkdir(parents=True,exist_ok=True)
    if not source.exists():
        print('Downloading',track['title'],flush=True)
        request=Request(track['downloadUrl'],headers={'User-Agent':'ChoreoPrototype/0.2 (licensed demo music)'})
        started=time.monotonic();chunks=[];received=0
        with urlopen(request,timeout=15) as response:
            while True:
                chunk=response.read(256*1024)
                if not chunk:break
                chunks.append(chunk);received+=len(chunk)
                if received%(1024*1024)==0:print(track['title'],received//1024,'KB',flush=True)
                if received>30*1024*1024 or time.monotonic()-started>180:raise TimeoutError('Music download limit: '+track['title'])
        payload=b''.join(chunks)
        if len(payload)>30*1024*1024 or len(payload)<10000:
            raise ValueError('Unexpected music payload: '+track['title'])
        temporary=source.with_suffix('.download')
        temporary.write_bytes(payload)
        import soundfile as sf
        if sf.info(str(temporary)).duration<track['excerptStart']+track['excerptDuration']:
            raise ValueError('Downloaded audio is too short: '+track['title'])
        temporary.replace(source)
    track['sha256']=sha256(source.read_bytes()).hexdigest()
    return source


def main():
    manifest=json.loads((ROOT/'Samples/music-sources.json').read_text(encoding='utf-8'))
    tracks=manifest['tracks']
    with ThreadPoolExecutor(max_workers=3) as pool:
        sources=list(pool.map(download,tracks))
    for track,source in zip(tracks,sources):
        output=ROOT/'Assets/StreamingAssets/MusicLibrary'/track['id']
        if not (output/'analysis.json').exists():
            print('Analyzing',track['title'],flush=True)
            result=analyze(dict(schemaVersion=1,sourcePath=str(source),outputDir=str(output),startSeconds=track['excerptStart'],durationSeconds=track['excerptDuration']))
            result['sourceName']=track['title']+'.mp3'
            atomic_json(output/'analysis.json',result)
        credit=(f'{track["title"]} — Kevin MacLeod (incompetech.com)\n'
                f'Licensed under Creative Commons: By Attribution 4.0 License\n{track["licenseUrl"]}\n'
                f'Source: {track["sourceUrl"]}\n'
                f'Changes: excerpt {track["excerptStart"]}–{track["excerptStart"]+track["excerptDuration"]} seconds, PCM WAV conversion; '
                '0.75x and 0.5x pitch-preserving time-stretch versions.\n'
                f'Original SHA-256: {track["sha256"]}\nRetrieved: 2026-11-02\n')
        (output/'CREDITS.txt').write_text(credit,encoding='utf-8')
        print(track['title'],'ready',flush=True)
    destination=ROOT/'Assets/StreamingAssets/MusicLibrary/catalog.json'
    atomic_json(destination,manifest)
    print('All licensed examples are ready.',flush=True)


if __name__=='__main__':main()
