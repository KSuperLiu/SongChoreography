"""Package the verified public Windows build; never include source FBXs/keys."""
import argparse
import hashlib
import json
import shutil
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def sha256_file(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--version', default='0.6.0')
    args = parser.parse_args()
    build = ROOT / 'Builds/PublicAIStudio'
    meta = json.loads((build / 'BUILD.json').read_text(encoding='utf-8-sig'))
    verification = json.loads((ROOT / 'Docs/ReleaseValidation/REPAIR66_VERIFICATION.json').read_text(encoding='utf-8'))
    assert meta['version'] == args.version and not meta['requiresGeneration']
    assert meta['characters'] == 1 and meta['motions'] == 243
    assert meta['reviewedMotions'] == 12 and meta['transitions'] == 15
    assert meta['characterSignature'] == verification['characterSignature']
    assert meta['playbackRevision'] == verification['playbackRevision'] == 6
    for name in ('ChoreoAIStudio.exe', 'UnityPlayer.dll', 'Runtime/Python/python.exe', 'Runtime/Python/LICENSE.txt'):
        assert (build / name).is_file(), name
    music = build / 'ChoreoAIStudio_Data/StreamingAssets/MusicLibrary'
    catalog = json.loads((music / 'catalog.json').read_text(encoding='utf-8-sig'))
    assert [t['id'] for t in catalog['tracks']] == ['original-100']
    assert sha256_file(music / 'original100.wav') == catalog['tracks'][0]['sha256']
    assert not list(build.rglob('*.fbx')) and not list(build.rglob('*.anim'))
    assert not any('mixkit-' in p.name.lower() or p.name == '.env' for p in build.rglob('*'))
    meta['gitCommit'] = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    meta['publicMusic'] = ['original-100']
    (build / 'BUILD.json').write_text(json.dumps(meta, indent=2) + '\n', encoding='utf-8')
    shutil.copy2(ROOT / 'LICENSE', build / 'LICENSE.txt')
    shutil.copy2(ROOT / 'THIRD_PARTY_NOTICES.md', build / 'THIRD_PARTY_NOTICES.md')
    docs = build / 'Docs'
    docs.mkdir(exist_ok=True)
    shutil.copytree(ROOT / 'Docs/ReleaseValidation', docs / 'ReleaseValidation', dirs_exist_ok=True)
    for name in ('REPAIR66_REVIEW.md', 'Repair66Live.png', 'Repair66NewJazz-front.jpg', 'Repair66NewJazz-side.jpg'):
        shutil.copy2(ROOT / 'Docs' / name, docs / name)
    readme = '''CHOREO AI Studio 0.6.0 / Windows x64

1. 解压整个目录，运行 ChoreoAIStudio.exe；不要只复制 EXE。
2. 点击左侧“100 BPM 练习示例”，使用原创伴奏开始练习，无需 API Key。
3. “动作库设置 → New Jazz 推荐”可查看 12 段推荐动作。
4. “使用 New Jazz 推荐 → 保存选择并返回”可恢复推荐选择。
5. 可选择自己的 WAV/MP3，截取 30～60 秒后分析，校正首拍与 BPM，再编排。
6. 支持原速、0.75×、0.5×、镜像、镜头、锁定与区间循环。

内置 243 段动作预览、12 段已复查推荐及 15 条连接。原 66 段待修复原片中，
53 段通过本轮技术指标；另外 13 段仍保留连续性风险标记，未放进推荐编排。
模型与动作已经编译在程序数据中；不提供原始第三方 FBX 导出。
公开包只附原创练习伴奏，不附本机 Mixkit 录音。便携 Python 与音频依赖已包含。

源码：https://github.com/KSuperLiu/SongChoreography
更新：https://github.com/KSuperLiu/SongChoreography/releases
许可与第三方内容：THIRD_PARTY_NOTICES.md
复查统计：Docs/REPAIR66_REVIEW.md
'''
    (build / 'START-HERE.zh-CN.txt').write_text(readme, encoding='utf-8-sig')
    release = ROOT / 'Release'
    release.mkdir(exist_ok=True)
    filename = f'ChoreoAIStudio-{args.version}-Windows-x64.zip'
    archive = release / filename
    prefix = f'ChoreoAIStudio-{args.version}'
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as output:
        for path in sorted(build.rglob('*')):
            if not path.is_file() or '__pycache__' in path.parts or path.suffix in ('.pyc', '.nbc', '.nbi'):
                continue
            output.write(path, prefix + '/' + path.relative_to(build).as_posix())
    assert archive.stat().st_size < 2 * 1024 ** 3, 'Release asset exceeds GitHub upload limit.'
    digest = sha256_file(archive)
    (release / 'SHA256SUMS.txt').write_text(digest + '  ' + filename + '\n', encoding='ascii')
    with zipfile.ZipFile(archive) as check:
        assert check.testzip() is None
        assert prefix + '/ChoreoAIStudio.exe' in check.namelist()
    print(json.dumps(dict(file=str(archive),bytes=archive.stat().st_size,sha256=digest,gitCommit=meta['gitCommit']),indent=2))


if __name__ == '__main__':
    main()
