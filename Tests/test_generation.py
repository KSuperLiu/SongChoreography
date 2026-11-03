import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('generation', ROOT / 'Tools/generation.py')
generation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generation)


class FakeTransport:
    def __init__(self):
        self.posts = []
        self.queries = []
        self.outputs = {}
        self.fail_post = False
        self.download_kind = 'fbx'

    def json(self, method, url, headers, payload=None):
        if method == 'POST':
            self.posts.append((url, payload))
            if self.fail_post:
                raise generation.PipelineError('ambiguous network error')
            identifier = 'task-' + str(len(self.posts))
            if 'openapi.tripo3d.ai' in url:
                if url.endswith('rig-check'):
                    output = {'riggable': True, 'rig_type': 'biped'}
                else:
                    output = {'model_url': 'https://cdn.test/character.fbx'}
                self.outputs[identifier] = output
                return {'code': 0, 'data': {'task_id': identifier}}
            return {'request_id': identifier, 'status_url': 'https://queue.fal.run/status/' + identifier,
                    'response_url': 'https://queue.fal.run/result/' + identifier}
        self.queries.append(url)
        if 'openapi.tripo3d.ai' in url:
            identifier = url.rsplit('/', 1)[1]
            return {'code': 0, 'data': {'status': 'success', 'output': self.outputs[identifier]}}
        if '/status/' in url:
            return {'status': 'COMPLETED'}
        return {'fbx_file': {'url': 'https://cdn.test/motion.fbx'}}

    def download(self, url, path):
        if self.download_kind == 'zip-traversal':
            with zipfile.ZipFile(path, 'w') as z:
                z.writestr('../../escape.fbx', b'Kaydara FBX Binary test')
        else:
            Path(path).write_bytes(b'Kaydara FBX Binary test animation data')


class GenerationTests(unittest.TestCase):
    def setUp(self):
        (ROOT / 'TestResults').mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=ROOT / 'TestResults')
        self.root = Path(self.temp.name)
        (self.root / 'Generation').mkdir()
        self.plan = json.loads((ROOT / 'Generation/plan.json').read_text(encoding='utf-8'))
        generation.write_json(self.root / 'Generation/plan.json', self.plan)
        self.env = patch.dict(os.environ, {'TRIPO_API_KEY': 'fake-tripo', 'FAL_KEY': 'fake-fal'})
        self.env.start()
        self.transport = FakeTransport()
        self.pipeline = generation.Pipeline(self.root, self.transport)

    def tearDown(self):
        self.env.stop()
        self.temp.cleanup()

    def motion(self):
        return self.plan['motions'][0]['id']

    def test_fal_job_download_and_repeat_does_not_rebill(self):
        identifier = self.motion()
        self.pipeline.start('motion', identifier)
        job = self.pipeline.sync(identifier)
        self.assertEqual(job['state'], 'downloaded')
        self.pipeline.start('motion', identifier)
        self.assertEqual(len(self.transport.posts), 1)
        self.assertEqual(self.transport.posts[0][1]['duration'], 4)
        library = generation.read_json(self.root / 'Generation/library.json')
        self.assertEqual(len(library['motions']), 1)
        self.assertEqual(library['motions'][0]['sha256'], generation.sha256(self.root / job['sourceFile']))
        self.assertNotIn('approved', library['motions'][0])
        self.assertNotIn('fake-fal', self.pipeline.path(identifier).read_text())

    def test_tripo_generation_check_rig_then_download(self):
        identifier = self.plan['characters'][0]['id']
        self.pipeline.start('character', identifier)
        for _ in range(3):
            job = self.pipeline.sync(identifier)
        self.assertEqual(job['state'], 'downloaded')
        self.assertEqual(len(self.transport.posts), 3)
        self.assertEqual(self.transport.posts[2][1]['spec'], 'unity')
        self.assertEqual(self.transport.posts[2][1]['out_format'], 'fbx')
        self.assertNotIn('fake-tripo', self.pipeline.path(identifier).read_text())

    def test_ambiguous_post_never_retried(self):
        self.transport.fail_post = True
        with self.assertRaises(generation.PipelineError):
            self.pipeline.start('motion', self.motion())
        self.transport.fail_post = False
        with self.assertRaisesRegex(generation.PipelineError, '不确定'):
            self.pipeline.start('motion', self.motion())
        self.assertEqual(len(self.transport.posts), 1)

    def test_recover_only_associates_existing_remote_task(self):
        self.transport.fail_post = True
        with self.assertRaises(generation.PipelineError):
            self.pipeline.start('motion', self.motion())
        self.transport.fail_post = False
        self.pipeline.recover(self.motion(), 'generate', 'console-task-id')
        self.pipeline.sync(self.motion())
        self.assertEqual(len(self.transport.posts), 1)
        with self.assertRaises(generation.PipelineError):
            self.pipeline.recover(self.motion(), 'generate', 'another-id')

    def test_keys_not_sent_to_arbitrary_polling_hosts(self):
        with self.assertRaisesRegex(generation.PipelineError, '非平台'):
            self.pipeline.call('fal', 'GET', 'https://evil.test/status')
        self.assertEqual(self.transport.queries, [])

    def test_changed_plan_requires_new_id(self):
        self.pipeline.start('motion', self.motion())
        self.pipeline.plan['motions'][0]['prompt'] = 'different motion'
        with self.assertRaisesRegex(generation.PipelineError, '新的 ID'):
            self.pipeline.start('motion', self.motion())
        self.assertEqual(len(self.transport.posts), 1)

    def test_missing_credentials_create_no_unknown_submission(self):
        with patch.dict(os.environ, {'FAL_KEY': ''}):
            with self.assertRaisesRegex(generation.PipelineError, 'FAL_KEY'):
                self.pipeline.start('motion', self.motion())
        self.assertFalse(self.pipeline.path(self.motion()).exists())

    def test_archive_cannot_escape_generated_asset_folder(self):
        self.transport.download_kind = 'zip-traversal'
        self.pipeline.start('motion', self.motion())
        with self.assertRaises(generation.PipelineError):
            self.pipeline.sync(self.motion())
        self.assertFalse(list(self.root.glob('**/escape.fbx')))

    def test_modified_download_is_not_trusted(self):
        self.pipeline.start('motion', self.motion())
        job = self.pipeline.sync(self.motion())
        (self.root / job['sourceFile']).write_bytes(b'changed')
        with self.assertRaisesRegex(generation.PipelineError, '变更'):
            self.pipeline.sync(self.motion())

    def test_invalid_bpm_never_posts(self):
        self.pipeline.plan['motions'][0]['bpm'] = float('nan')
        with self.assertRaises(generation.PipelineError):
            self.pipeline.start('motion', self.motion())
        self.assertFalse(self.transport.posts)

    def test_concurrent_writer_is_blocked_and_lock_released(self):
        with generation.workspace_lock(self.root):
            with self.assertRaises(generation.PipelineError):
                with generation.workspace_lock(self.root):
                    self.fail('second writer acquired lock')
        with generation.workspace_lock(self.root):
            pass


if __name__ == '__main__':
    unittest.main()
