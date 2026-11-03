import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


spec = importlib.util.spec_from_file_location("mixamo_registration", Path(__file__).resolve().parents[1] / "Tools/prepare_mixamo_library.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class MixamoRegistrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root / ".generation").mkdir()
        (self.root / "Generation").mkdir()
        self.catalog_path = self.root / "Generation/library.json"
        self.catalog_path.write_text(json.dumps({"schemaVersion": 2, "characters": [{"id": "dancer", "signature": "keep"}], "motions": []}), encoding="utf-8")
        relative = "Assets/Generated/Motions/mixamo-sample/mixamo-sample.fbx"
        path = self.root / relative
        path.parent.mkdir(parents=True)
        contents = b"Kaydara FBX Binary  \x00\x1a\x00fixture"
        path.write_bytes(contents)
        self.entry = dict(id="123", name="Jazz Dancing", description="Jazz Dancing", file=relative, sha256=hashlib.sha256(contents).hexdigest())
        self.save_manifest([self.entry])

    def tearDown(self):
        self.temp.cleanup()

    def save_manifest(self, entries):
        (self.root / ".generation/mixamo-batch.json").write_text(json.dumps(dict(status="complete", remaining=0, downloaded=entries)), encoding="utf-8")

    def test_repeat_preserves_existing_review_and_character(self):
        module.prepare(self.root)
        data = json.loads(self.catalog_path.read_text(encoding="utf-8"))
        data["motions"][0]["reviewedCharacters"] = ["dancer"]
        self.catalog_path.write_text(json.dumps(data), encoding="utf-8")
        result = module.prepare(self.root)
        again = json.loads(self.catalog_path.read_text(encoding="utf-8"))
        self.assertEqual(result["added"], 0)
        self.assertEqual(again, data)

    def test_hash_failure_does_not_change_catalog(self):
        before = self.catalog_path.read_bytes()
        self.entry["sha256"] = "wrong"
        self.save_manifest([self.entry])
        with self.assertRaises(ValueError):
            module.prepare(self.root)
        self.assertEqual(self.catalog_path.read_bytes(), before)
        self.assertFalse((self.root / ".generation/library-before-mixamo-registration.json").exists())

    def test_source_outside_motion_folder_is_rejected(self):
        self.entry["file"] = "Assets/Generated/Motions/../../../outside.fbx"
        self.save_manifest([self.entry])
        with self.assertRaisesRegex(ValueError, "source path"):
            module.prepare(self.root)

    def test_pack_ids_and_draft_tags(self):
        pack = dict(id="free_test", name="Free Test Pack", kind="animation-pack", members=[self.entry])
        self.save_manifest([pack])
        module.prepare(self.root)
        motion = json.loads(self.catalog_path.read_text(encoding="utf-8"))["motions"][0]
        self.assertRegex(motion["id"], r"^[a-z][a-z0-9-]{1,63}$")
        self.assertEqual(motion["reviewedCharacters"], [])
        self.assertIn("draft", motion["tags"])

    def test_real_source_categories_are_not_new_jazz(self):
        self.assertEqual(module.category("Female Dance Pose"), "pose")
        self.assertEqual(module.category("Jazz Dancing"), "jazz")
        self.assertEqual(module.category("Salsa Dancing"), "salsa")


if __name__ == "__main__":
    unittest.main()
