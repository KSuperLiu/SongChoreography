"""Fetch one verified FineDance source take directly from its author's archive.

Personal non-commercial learning only. Read the upstream LICENSE first; this
script and the MIT project license do not grant redistribution rights to data.
Requires the locally installed 7-Zip extractor, which is not bundled here.
"""
import argparse
import datetime
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import urllib.request

ROOT=Path(__file__).resolve().parents[1]
REVISION='0476cd42619e57fb6ec4840e81acaf7743b1507d'
SOURCE='https://drive.usercontent.google.com/download?id=1zQvWG9I0H4U3Zrm8d_QD_ehenZvqfQfS&export=download&confirm=t'
LICENSE='https://raw.githubusercontent.com/li-ronghui/FineDance/'+REVISION+'/LICENSE'
PREFIX_BYTES=3242629
PREFIX_SHA='ca598abcf6458656cf58378854513e82f3c45c432d821c155595eb7bc5346b23'
POSE_SHA='0eb42e5df55c3f8075c760fd079855a51e1ec0a45e7850b5776a8a0d0e75bda7'
LABEL_SHA='ce62039a15092106aeae198bc59dd9466035e556a75dc58cf8949bc21bdb69ed'
LICENSE_SHA='24956792f21e4dfe7c630a7995c60fca9729b7feed51cc972ae0b873a299e310'
SKELETON='https://raw.githubusercontent.com/li-ronghui/FineDance/'+REVISION+'/smplx_neu_J_1.npy'
SKELETON_SHA='aa80c6e1b28e9d43470f250a2d168bacf2cb0cc5e8688ff2e48e71f4db13ba6c'


def checked(payload,expected,name):
    if hashlib.sha256(payload).hexdigest()!=expected:
        raise ValueError(name+' changed or is incomplete. Review the upstream source; do not bypass verification.')
    return payload


def fetch(output,extractor,personal_use):
    if not personal_use:
        raise ValueError('Read https://github.com/li-ronghui/FineDance/blob/main/LICENSE then select --personal-use for your permitted local non-commercial learning purpose.')
    extractor=extractor or shutil.which('7z') or 'C:/Program Files/7-Zip/7z.exe'
    if not Path(extractor).is_file():raise ValueError('7-Zip not found; specify --extractor PATH.')
    output=Path(output).resolve()
    cache=output/'downloads/001-prefix.rar'
    cache.parent.mkdir(parents=True,exist_ok=True)
    if not cache.exists():
        request=urllib.request.Request(SOURCE,headers={'Range':f'bytes=0-{PREFIX_BYTES-1}','User-Agent':'ChoreoPersonalLearning/0.5'})
        with urllib.request.urlopen(request,timeout=60) as response:
            if response.status!=206 or not response.headers.get('Content-Range','').startswith(f'bytes 0-{PREFIX_BYTES-1}/'):
                raise ValueError('Server did not honor a partial download; the 4.4 GB full archive was not downloaded.')
            payload=response.read(PREFIX_BYTES+1)
        cache.write_bytes(checked(payload,PREFIX_SHA,'Archive prefix'))
    prefix=checked(cache.read_bytes(),PREFIX_SHA,'Archive prefix')
    # This non-solid prefix contains the complete first motion. Extraction goes
    # to stdout, so an archive cannot write files outside the chosen output path.
    process=subprocess.run([str(extractor),'x',str(cache),'-so','finedance/motion/001.npy','-bd'],stdout=subprocess.PIPE,stderr=subprocess.PIPE)
    pose=checked(process.stdout,POSE_SHA,'Motion 001')
    label=checked(prefix[91:165],LABEL_SHA,'Source label 001')
    request=urllib.request.Request(LICENSE,headers={'User-Agent':'ChoreoPersonalLearning/0.5'})
    with urllib.request.urlopen(request,timeout=30) as response:license_text=checked(response.read(50000),LICENSE_SHA,'Source license')
    with urllib.request.urlopen(SKELETON,timeout=30) as response:skeleton=checked(response.read(50000),SKELETON_SHA,'Neutral source skeleton')
    destination=output/'source';destination.mkdir(parents=True,exist_ok=True)
    (destination/'001.npy').write_bytes(pose)
    (destination/'001.json').write_bytes(label)
    (destination/'smplx_neu_J_1.npy').write_bytes(skeleton)
    (output/'LICENSE.txt').write_bytes(license_text)
    record=dict(schemaVersion=1,personalUseOnly=True,source='https://github.com/li-ronghui/FineDance',
        sourceRevision=REVISION,archive=SOURCE,sourceLabel=json.loads(label),motionSha256=POSE_SHA,neutralSkeletonSha256=SKELETON_SHA,
        retrievedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),redistributionAllowed=False)
    (output/'SOURCE.json').write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(dict(prepared=True,personalUseOnly=True,sourceLabel=record['sourceLabel'],motionSha256=POSE_SHA,output=str(output)),ensure_ascii=False))


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--personal-use',action='store_true')
    parser.add_argument('--output',default=str(ROOT/'PersonalAssets/FineDance'))
    parser.add_argument('--extractor')
    args=parser.parse_args()
    fetch(args.output,args.extractor,args.personal_use)
