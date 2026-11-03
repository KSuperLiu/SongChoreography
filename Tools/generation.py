"""Tripo characters and fal motion. Durable jobs; never automatically retry POSTs."""
from __future__ import annotations
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse, quote
from urllib.request import Request, urlopen
import zipfile
from contextlib import contextmanager

ROOT = Path(__file__).resolve().parents[1]
TRIPO = 'https://openapi.tripo3d.ai/v3'
FAL = 'https://queue.fal.run'


class PipelineError(RuntimeError):
    pass


@contextmanager
def workspace_lock(root):
    """OS lock is released on crash; only one local writer submits paid jobs."""
    path = Path(root) / '.generation/pipeline.lock'
    path.parent.mkdir(parents=True, exist_ok=True)
    stream = path.open('a+b')
    try:
        stream.seek(0)
        if not stream.read(1):
            stream.write(b'0'); stream.flush()
        stream.seek(0)
        if os.name == 'nt':
            import msvcrt
            msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(stream.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        stream.close()
        raise PipelineError('另一个生成工具正在操作此项目，请稍后续查。') from None
    try:
        yield
    finally:
        stream.close()


def read_json(path, default=None):
    return json.loads(Path(path).read_text(encoding='utf-8-sig')) if Path(path).exists() else default


def write_json(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(data, ensure_ascii=False, indent=2, allow_nan=False), encoding='utf-8')
    for attempt in range(8):
        try:
            temporary.replace(path)
            break
        except PermissionError:
            if attempt == 7:
                raise
            time.sleep(.025 * (attempt + 1))


def sha256(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def asset_id(value):
    if not re.fullmatch(r'[a-z][a-z0-9-]{1,63}', value):
        raise PipelineError('素材 ID 只允许小写字母、数字和连字符，长度 2～64。')
    return value


def within(root, relative):
    root = Path(root).resolve()
    path = (root / relative).resolve()
    if root not in path.parents:
        raise PipelineError('文件路径必须位于指定目录内。')
    return path


def load_env(root):
    path = Path(root) / '.env'
    for line in path.read_text(encoding='utf-8-sig').splitlines() if path.exists() else []:
        key, separator, value = line.strip().partition('=')
        if separator and key in ('TRIPO_API_KEY', 'FAL_KEY'):
            os.environ.setdefault(key, value.strip().strip('"').strip("'"))


class Transport:
    def json(self, method, url, headers, payload=None):
        body = None if payload is None else json.dumps(payload).encode('utf-8')
        try:
            with urlopen(Request(url, body, {**headers, 'Content-Type': 'application/json'}, method=method), timeout=45) as response:
                return json.load(response)
        except HTTPError as error:
            raise PipelineError(f'服务返回 HTTP {error.code}；请检查平台控制台。') from None
        except (URLError, TimeoutError, OSError, ValueError):
            raise PipelineError('网络或响应异常；任务已保留，请续查，勿重复提交。') from None

    def download(self, url, path):
        parsed = urlparse(url)
        if parsed.scheme != 'https' or not parsed.hostname or parsed.username or parsed.password:
            raise PipelineError('素材必须使用 HTTPS 下载地址。')
        try:
            # CDN downloads intentionally receive no API credentials.
            with urlopen(Request(url, headers={'User-Agent': 'ChoreoStudio/1.0'}), timeout=60) as response, Path(path).open('wb') as out:
                total = 0
                while block := response.read(1024 * 1024):
                    total += len(block)
                    if total > 512 * 1024 * 1024:
                        raise PipelineError('素材超过 512 MB。')
                    out.write(block)
        except (URLError, TimeoutError, OSError):
            raise PipelineError('下载失败；可再次 sync，不会重新生成。') from None


class Pipeline:
    def __init__(self, root=ROOT, transport=None):
        self.root = Path(root).resolve()
        self.transport = transport or Transport()
        self.plan = read_json(self.root / 'Generation/plan.json')
        if not self.plan or self.plan.get('schemaVersion') != 2:
            raise PipelineError('缺少 Generation/plan.json 或版本错误。')
        ids = [asset_id(a['id']) for a in self.plan['characters'] + self.plan['motions']]
        if len(set(ids)) != len(ids):
            raise PipelineError('生成计划素材 ID 必须唯一。')
        load_env(self.root)

    def path(self, identifier):
        return within(self.root, '.generation/jobs/' + asset_id(identifier) + '.json')

    def save(self, job):
        write_json(self.path(job['id']), job)

    def credential(self, provider):
        name = 'TRIPO_API_KEY' if provider == 'tripo' else 'FAL_KEY'
        key = os.environ.get(name, '').strip()
        if not key:
            raise PipelineError(f'请先在本机 .env 或环境变量中配置 {name}。')
        return {'Authorization': ('Bearer ' if provider == 'tripo' else 'Key ') + key}

    def call(self, provider, method, url, payload=None):
        parsed = urlparse(url)
        expected = 'openapi.tripo3d.ai' if provider == 'tripo' else 'queue.fal.run'
        if parsed.scheme != 'https' or parsed.netloc != expected:
            raise PipelineError('拒绝向非平台地址发送凭据。')
        value = self.transport.json(method, url, self.credential(provider), payload)
        if provider == 'tripo':
            if value.get('code') != 0 or not isinstance(value.get('data'), dict):
                raise PipelineError('Tripo 返回错误；请检查控制台，未自动重提。')
            return value['data']
        return value

    def payload(self, kind, spec):
        if kind == 'character':
            return dict(prompt=spec['prompt'], model=self.plan['tripo']['model'], model_seed=spec['seed'],
                        texture=True, pbr=True, face_limit=spec.get('faceLimit', 40000), auto_size=True)
        bpm, beats = float(spec['bpm']), int(spec['beats'])
        if not math.isfinite(bpm) or not 60 <= bpm <= 180 or beats not in (4, 8):
            raise PipelineError('动作 BPM 应为 60～180，拍数为 4 或 8。')
        duration = 60 * beats / bpm
        if not .5 <= duration <= 12:
            raise PipelineError('动作时长超出模型范围。')
        return dict(prompt=spec['prompt'], duration=duration, seed=int(spec['seed']), output_format='fbx')

    def start(self, kind, identifier):
        asset_id(identifier)
        spec = next((a for a in self.plan[kind + 's'] if a['id'] == identifier), None)
        if not spec:
            raise PipelineError('生成计划没有此素材 ID。')
        if self.path(identifier).exists():
            job = read_json(self.path(identifier))
            model = self.plan['tripo']['model'] if kind == 'character' else self.plan['fal']['model']
            if job['spec'] != spec or job['model'] != model or (kind == 'character' and job.get('rigModel') != self.plan['tripo']['rigModel']):
                raise PipelineError('此 ID 已有不同方案；请使用新的 ID。')
            return self.sync(identifier)
        provider = 'tripo' if kind == 'character' else 'fal'
        self.credential(provider)
        payload = self.payload(kind, spec)
        job = dict(schemaVersion=2, id=identifier, kind=kind, provider=provider, spec=spec,
                   model=self.plan['tripo']['model'] if kind == 'character' else self.plan['fal']['model'],
                   rigModel=self.plan['tripo']['rigModel'] if kind == 'character' else '', stages={}, state='new')
        self.save(job)
        endpoint = TRIPO + '/generation/text-to-model' if kind == 'character' else FAL + '/' + job['model']
        self.submit(job, 'generate', endpoint, payload)
        return job

    def submit(self, job, name, endpoint, payload):
        stage = dict(state='submitting', payload=payload)
        job['stages'][name] = stage
        job['state'] = name
        self.save(job)  # Crash or unknown POST outcome must never cause an automatic retry.
        result = self.call(job['provider'], 'POST', endpoint, payload)
        task_id = result.get('task_id') if job['provider'] == 'tripo' else result.get('request_id')
        if not task_id:
            raise PipelineError('缺少任务编号；请在平台控制台找回编号后 recover。')
        stage.update(state='queued', taskId=task_id)
        if job['provider'] == 'fal':
            base = FAL + '/' + job['model'] + '/requests/' + quote(task_id, safe='')
            stage['statusUrl'] = result.get('status_url') or base + '/status'
            stage['responseUrl'] = result.get('response_url') or base
        self.save(job)

    def query(self, job, name):
        stage = job['stages'][name]
        if stage['state'] == 'complete':
            return True
        if not stage.get('taskId'):
            raise PipelineError('上次提交结果不确定，禁止自动重复付费。请从控制台找回编号后 recover。')
        if stage['state'] == 'failed':
            raise PipelineError('任务已失败；请检查控制台，以新 ID 创建新候选。')
        if job['provider'] == 'tripo':
            result = self.call('tripo', 'GET', TRIPO + '/tasks/' + quote(stage['taskId'], safe=''))
            status = result.get('status', 'unknown')
            if status in ('failed', 'cancelled', 'banned', 'expired'):
                stage['state'] = 'failed'
            elif status == 'success':
                stage.update(state='complete', output=result.get('output', {}), credits=result.get('credits_consumed'))
            else:
                stage['state'] = status
        else:
            result = self.call('fal', 'GET', stage['statusUrl'])
            status = result.get('status')
            if status in ('FAILED', 'CANCELLED'):
                stage['state'] = 'failed'
            elif status == 'COMPLETED':
                output = self.call('fal', 'GET', stage['responseUrl'])
                if output.get('error') or not output.get('fbx_file', {}).get('url'):
                    stage['state'] = 'failed'
                else:
                    stage.update(state='complete', output=output)
            else:
                stage['state'] = status or 'unknown'
        self.save(job)
        if stage['state'] == 'failed':
            raise PipelineError('生成失败；任务编号已保留，请检查控制台。')
        return stage['state'] == 'complete'

    def sync(self, identifier):
        job = read_json(self.path(identifier))
        if not job:
            raise PipelineError('尚无此素材任务，请先生成。')
        if job['state'] == 'downloaded':
            source = within(self.root, job['sourceFile'])
            if not source.exists() or sha256(source) != job['sha256']:
                raise PipelineError('本地文件缺失或变更，请检查生成文件。')
            return job
        if not self.query(job, 'generate'):
            return job
        if job['kind'] == 'character':
            if 'rig-check' not in job['stages']:
                self.submit(job, 'rig-check', TRIPO + '/animations/rig-check', {'input': job['stages']['generate']['taskId']})
                return job
            if not self.query(job, 'rig-check'):
                return job
            check = job['stages']['rig-check']['output']
            if not check.get('riggable') or check.get('rig_type') != 'biped':
                raise PipelineError('模型未通过双足骨骼检查；请在 Tripo 查看。')
            if 'rig' not in job['stages']:
                self.submit(job, 'rig', TRIPO + '/animations/rig', dict(input=job['stages']['generate']['taskId'],
                    model=job['rigModel'], rig_type='biped', spec='unity', out_format='fbx'))
                return job
            if not self.query(job, 'rig'):
                return job
        self.download(job)
        return job

    def recover(self, identifier, name, task_id):
        job = read_json(self.path(identifier))
        if not job or name not in job['stages']:
            raise PipelineError('没有待恢复的提交阶段。')
        stage = job['stages'][name]
        if stage.get('taskId') or stage['state'] != 'submitting':
            raise PipelineError('该阶段已有编号或不处于未知提交状态。')
        if not re.fullmatch(r'[A-Za-z0-9_-]{1,128}', task_id):
            raise PipelineError('任务编号格式无效。')
        stage.update(state='queued', taskId=task_id)
        if job['provider'] == 'fal':
            stage['responseUrl'] = FAL + '/' + job['model'] + '/requests/' + task_id
            stage['statusUrl'] = stage['responseUrl'] + '/status'
        self.save(job)
        return job

    def download(self, job):
        output = job['stages']['rig' if job['kind'] == 'character' else 'generate']['output']
        url = output.get('model_url') if job['kind'] == 'character' else output.get('fbx_file', {}).get('url')
        if not url:
            raise PipelineError('平台结果没有 FBX 下载地址。')
        folder = within(self.root, 'Assets/Generated/' + ('Characters/' if job['kind'] == 'character' else 'Motions/') + job['id'])
        folder.mkdir(parents=True, exist_ok=True)
        temporary = folder / 'artifact.download'
        self.transport.download(url, temporary)
        with temporary.open('rb') as f:
            header = f.read(64)
        if zipfile.is_zipfile(temporary):
            staging = folder / 'unpacked'
            staging.mkdir(exist_ok=True)
            with zipfile.ZipFile(temporary) as archive:
                if sum(i.file_size for i in archive.infolist()) > 1024 * 1024 * 1024:
                    raise PipelineError('素材解压大小超限。')
                for item in archive.infolist():
                    within(staging, item.filename)
                    if (item.external_attr >> 16) & 0o170000 == 0o120000:
                        raise PipelineError('素材压缩包包含符号链接。')
                archive.extractall(staging)
            fbxs = [p for p in staging.rglob('*') if p.suffix.lower() == '.fbx']
            if len(fbxs) != 1:
                raise PipelineError('压缩包需包含一个明确的 FBX。')
            source = fbxs[0]
        elif header.startswith(b'Kaydara FBX Binary') or b'FBX' in header:
            source = folder / (job['id'] + '.fbx')
            temporary.replace(source)
        else:
            raise PipelineError('下载结果不是 FBX 或 FBX 压缩包。')
        job.update(state='downloaded', sourceFile=source.relative_to(self.root).as_posix(), sha256=sha256(source))
        # Commit catalog first, job last: interrupted catalog writes can be resumed.
        library_path = self.root / 'Generation/library.json'
        library = read_json(library_path, dict(schemaVersion=2, characters=[], motions=[]))
        entry = dict(id=job['id'], name=job['spec']['name'], provider=job['provider'], model=job['model'],
            requestId=job['stages']['generate']['taskId'], sourceFile=job['sourceFile'], sha256=job['sha256'],
            prompt=job['spec']['prompt'], termsUrl='https://www.tripo3d.ai/terms' if job['kind'] == 'character' else 'https://fal.ai/terms-of-service')
        if job['kind'] == 'motion':
            entry.update(beats=job['spec']['beats'], sourceBpm=job['spec']['bpm'], energy=job['spec']['energy'], tags=job['spec'].get('tags', []), seed=job['spec']['seed'])
        collection = library[job['kind'] + 's']
        collection[:] = [a for a in collection if a['id'] != job['id']]
        collection.append(entry)
        write_json(library_path, library)
        self.save(job)
        if temporary.exists():
            temporary.unlink()


def run(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    commands.add_parser('doctor'); commands.add_parser('plan')
    for command in ('character', 'motion'):
        commands.add_parser(command).add_argument('id')
    p = commands.add_parser('sync')
    p.add_argument('id', help='素材 ID 或 all'); p.add_argument('--wait', type=int, default=0)
    p = commands.add_parser('recover')
    p.add_argument('id'); p.add_argument('stage', choices=['generate', 'rig-check', 'rig']); p.add_argument('task_id')
    args = parser.parse_args(argv)
    try:
        pipeline = Pipeline()
        if args.command == 'doctor':
            print(json.dumps(dict(tripoConfigured=bool(os.environ.get('TRIPO_API_KEY')), falConfigured=bool(os.environ.get('FAL_KEY')),
                characters=len(pipeline.plan['characters']), motions=len(pipeline.plan['motions'])), ensure_ascii=False))
        elif args.command == 'plan':
            print(json.dumps(pipeline.plan, ensure_ascii=False, indent=2))
        elif args.command in ('character', 'motion'):
            job = pipeline.start(args.command, args.id)
            print(json.dumps(dict(id=job['id'], state=job['state']), ensure_ascii=False))
        elif args.command == 'recover':
            job = pipeline.recover(args.id, args.stage, args.task_id)
            print(json.dumps(dict(id=job['id'], state=job['state']), ensure_ascii=False))
        else:
            deadline = time.monotonic() + max(0, min(args.wait, 3600))
            while True:
                ids = [p.stem for p in (ROOT / '.generation/jobs').glob('*.json')] if args.id == 'all' else [args.id]
                jobs = [pipeline.sync(identifier) for identifier in ids]
                print(json.dumps([dict(id=j['id'], state=j['state']) for j in jobs], ensure_ascii=False), flush=True)
                if all(j['state'] == 'downloaded' for j in jobs) or time.monotonic() >= deadline:
                    break
                time.sleep(3)
        return 0
    except (PipelineError, KeyError, ValueError, OSError) as error:
        print(str(error), file=sys.stderr)
        return 1


def main(argv=None):
    try:
        with workspace_lock(ROOT):
            return run(argv)
    except PipelineError as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
