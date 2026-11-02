import hashlib,html,json,shutil
from pathlib import Path
root=Path(__file__).resolve().parents[1]
def read(path):return json.loads((root/path).read_text(encoding='utf-8-sig'))
evidence=root/'TestResults/AnatomyPlayback051FinalProbe'
cases=[c for c in read('TestResults/AnatomyPlayback051FinalProbe/results.json')['cases'] if c['from'].startswith('charleston-') and c['to'].startswith('charleston-')]
assert len(cases)==40 and all(c['passed'] and c['board'] for c in cases)
public=read('.build-cache/UnityProject/Builds/Windows/MotionPhase/clip-hashes.json')
tested=read('.build-cache/PersonalUnityProject/Builds/Windows/MotionPhase/clip-hashes.json')
digests=lambda review:{c['id']:c['sha256'] for c in review['motions']}
assert digests(public)==digests(tested) and len(digests(public))==8
source=Path('Assets/SongChoreography/Runtime/DancePlayback.cs')
assert (root/source).read_bytes()==(root/'.build-cache/PersonalUnityProject'/source).read_bytes()
assert (root/source).read_bytes()==(root/'.build-cache/UnityProject'/source).read_bytes()
out=root/'Builds/PublicWindows051/CharlestonReview';out.mkdir(exist_ok=True)
parts=['<!doctype html><meta charset="utf-8"><title>0.5.1 Charleston playback review</title><style>body{font:16px system-ui;background:#101923;color:#e5edf3;margin:32px}img{max-width:100%;margin-bottom:32px}h2{font-size:18px}</style><h1>0.5.1 Charleston：20 条连接 × 两个角色</h1><p>实际 DancePlayback 采样、混合和接地；120 Hz 检查全部通过。以下为逐连接六帧渲染板，非前台点击截图。已核对公共构建的 8 个动作摘要及播放器源码与受测版本一致。</p>']
for c in cases:
    assert Path(c['board']).name==c['board'] and 'personal-' not in c['board']
    shutil.copyfile(evidence/c['board'],out/c['board'])
    parts.append('<h2>'+html.escape(c['character']+' / '+c['from']+' → '+c['to'])+'</h2><img src="'+html.escape(c['board'])+'">')
report=dict(schemaVersion=1,version='0.5.1',scope='Public Charleston cases only. Rendered through shipped DancePlayback in the isolated editor; not a foreground click test.',playbackSourceSha256=hashlib.sha256((root/source).read_bytes()).hexdigest(),clipDigests=digests(public),cases=cases)
(out/'results.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
(out/'report.html').write_text('\n'.join(parts),encoding='utf-8')
print('Prepared public-only connection evidence: 40 cases; public and tested clip digests match.')
