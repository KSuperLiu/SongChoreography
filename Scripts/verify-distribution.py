"""Check that public source/builds do not include local learning motion assets."""
import argparse
import json
from pathlib import Path
import subprocess

ROOT=Path(__file__).resolve().parents[1]


def check_index():
    paths=subprocess.check_output(['git','ls-files','-z'],cwd=ROOT).decode('utf-8').split('\0')[:-1]
    restricted=[]
    for path in paths:
        lower=path.lower()
        if (lower.startswith('personalassets/') or '/personalmotioncredits/' in lower
            or lower.startswith('assets/resources/personalmotioncatalog.json')
            or (lower.startswith('assets/resources/motions/') and Path(lower).name.startswith('personal-'))):
            restricted.append(path)
    if restricted:raise ValueError('Restricted assets in Git index: '+', '.join(restricted))
    return dict(check='git-index',passed=True,trackedFiles=len(paths))


def check_build(path,personal=False):
    path=Path(path)
    manifest=json.loads((path/'DISTRIBUTION.json').read_text(encoding='utf-8-sig'))
    if manifest.get('schemaVersion')!=1:raise ValueError('Unsupported distribution manifest.')
    if manifest.get('personalUseOnly') is not personal or manifest.get('containsRestrictedMotions') is not personal:
        raise ValueError('Distribution manifest does not match the requested build scope.')
    credits=path/'ChoreoStudio_Data/StreamingAssets/PersonalMotionCredits'
    if credits.exists()!=personal:raise ValueError('Unexpected personal motion credits directory.')
    if personal and not (credits/'LICENSE.txt').exists():raise ValueError('Missing source license.')
    if not (path/'ChoreoStudio.exe').exists():raise ValueError('Missing Windows player.')
    return dict(check='personal-build' if personal else 'public-build',passed=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--git-index',action='store_true')
    parser.add_argument('--build')
    parser.add_argument('--personal',action='store_true')
    args=parser.parse_args()
    if not args.git_index and not args.build:parser.error('Choose --git-index or --build DIRECTORY.')
    if args.git_index:print(json.dumps(check_index()))
    if args.build:print(json.dumps(check_build(args.build,args.personal)))
