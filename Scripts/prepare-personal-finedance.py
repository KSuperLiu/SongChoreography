"""Convert locally acquired FineDance poses for personal, non-commercial learning.

This tool does not download or redistribute the dataset, music, SMPL models or
upstream code. Read the author's LICENSE before use. Output stays in ignored
PersonalAssets. A Street/Jazz source tag is preserved; style approval is separate.
"""
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
from scipy.spatial.transform import Rotation

ROOT = Path(__file__).resolve().parents[1]


def rotations_6d(values):
    values = np.asarray(values, dtype=np.float64)
    if values.ndim != 3 or values.shape[1:] != (52, 6) or not np.isfinite(values).all():
        raise ValueError('Expected finite (frames, 52, 6) rotation data.')
    a, b = values[..., :3], values[..., 3:]
    length = np.linalg.norm(a, axis=-1, keepdims=True)
    if (length < 1e-6).any():
        raise ValueError('Degenerate first rotation axis.')
    x = a / length
    y = b - (x * b).sum(axis=-1, keepdims=True) * x
    length = np.linalg.norm(y, axis=-1, keepdims=True)
    if (length < 1e-6).any():
        raise ValueError('Degenerate second rotation axis.')
    y /= length
    matrix = np.stack((x, y, np.cross(x, y)), axis=-2)
    return Rotation.from_matrix(matrix.reshape(-1, 3, 3)).as_quat().reshape(-1, 52, 4)


def convert(source, label, bpm, start, accept, skeleton=None):
    if not accept:
        raise ValueError('Use --personal-use after reviewing the upstream license; output must not be redistributed.')
    if not np.isfinite(bpm) or bpm < 60 or bpm > 180 or not np.isfinite(start) or start < 0:
        raise ValueError('Invalid source beat grid.')
    data = np.load(source, allow_pickle=False)
    if data.ndim != 2 or data.shape[1] != 315 or data.shape[0] < 240 or not np.isfinite(data).all():
        raise ValueError('Expected finite FineDance (frames, 315) data.')
    original_label = json.loads(Path(label).read_text(encoding='utf-8-sig'))
    if str(original_label.get('style2', '')).lower() != 'jazz':
        raise ValueError('This workflow only accepts an upstream Jazz label; it does not infer a dance genre.')
    period = 60 / bpm
    if (start + period * 64) * 30 >= len(data):
        raise ValueError('Source does not cover eight eight-count phrases.')
    skeleton=Path(skeleton) if skeleton else ROOT/'PersonalAssets/FineDance/source/smplx_neu_J_1.npy'
    if not skeleton.is_file():raise ValueError('Missing source neutral skeleton; rerun fetch-personal-finedance.py. A target T pose alone cannot calibrate joint axes.')
    neutral=np.load(skeleton,allow_pickle=False)
    if neutral.shape!=(55,3) or not np.isfinite(neutral).all():raise ValueError('Expected finite (55, 3) neutral source joints.')
    neutral=np.concatenate((neutral[:22],neutral[25:]))
    quaternions = rotations_6d(data[:, 3:].reshape(-1, 52, 6))
    # Adjacent quaternions q/-q encode the same rotation. Choose a continuous sign.
    for frame in range(1, len(quaternions)):
        flip = (quaternions[frame] * quaternions[frame-1]).sum(axis=-1) < 0
        quaternions[frame, flip] *= -1
    output = ROOT / 'PersonalAssets/FineDance'
    output.mkdir(parents=True, exist_ok=True)
    source_id = Path(source).stem
    first = start * 30
    last = (start + period * 64) * 30
    sample = lambda at: (data[int(at),:3]*(1-at%1) + data[int(at)+1,:3]*(at%1))
    drift = sample(last)-sample(first)
    drift[1] = 0
    result = dict(schemaVersion=1, personalUseOnly=True, sourceId=source_id, fps=30,
        source='https://github.com/li-ronghui/FineDance',
        license='FineDance: non-commercial research, education or artistic projects; no redistribution',
        sourceLabel=original_label, sourceSha256=hashlib.sha256(Path(source).read_bytes()).hexdigest(),
        sourceBpm=bpm, startSeconds=start, rootDrift=drift.round(7).tolist(),neutralJoints=neutral.ravel().tolist(),neutralSkeletonSha256=hashlib.sha256(skeleton.read_bytes()).hexdigest(), frames=[dict(root=p[:3].round(7).tolist(),
            rotations=q.round(7).ravel().tolist()) for p,q in zip(data,quaternions)])
    destination = output / 'poses.json'
    destination.write_text(json.dumps(result, ensure_ascii=False, separators=(',', ':')), encoding='utf-8')
    print(json.dumps(dict(output=str(destination),frames=len(data),sourceLabel=original_label,
        personalUseOnly=True,approved=False),ensure_ascii=False))
    return destination


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True)
    parser.add_argument('--label', required=True)
    parser.add_argument('--bpm', type=float, required=True)
    parser.add_argument('--start', type=float, default=8)
    parser.add_argument('--personal-use', action='store_true')
    parser.add_argument('--skeleton',help='Author-provided neutral SMPL-X joint NPY; stays local.')
    args = parser.parse_args()
    convert(args.source,args.label,args.bpm,args.start,args.personal_use,args.skeleton)
