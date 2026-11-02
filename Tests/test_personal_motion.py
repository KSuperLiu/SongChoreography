"""Synthetic fixtures only: no restricted source motions are used in these tests."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import numpy as np
from scipy.spatial.transform import Rotation

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('personal_converter', ROOT/'Scripts/prepare-personal-finedance.py')
converter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(converter)


class PersonalMotionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        (ROOT/'TestResults').mkdir(exist_ok=True)

    def test_rotation_convention_matches_known_matrices(self):
        original = Rotation.from_euler('xyz', np.arange(2*52*3).reshape(-1,3)%90, degrees=True)
        six = original.as_matrix()[:,:2,:].reshape(2,52,6)
        actual = Rotation.from_quat(converter.rotations_6d(six).reshape(-1,4))
        self.assertLess(np.max((original.inv()*actual).magnitude()), 1e-12)

    def test_corrupt_axes_are_rejected(self):
        for six in (np.zeros((1,52,6)), np.ones((1,52,6)), np.full((1,52,6), np.nan)):
            with self.subTest(value=str(six[0,0])):
                with self.assertRaises(ValueError): converter.rotations_6d(six)

    def test_personal_output_and_provenance(self):
        with tempfile.TemporaryDirectory(dir=ROOT/'TestResults') as folder:
            base=Path(folder)
            data=np.zeros((1024,315),dtype=np.float32)
            data[:,3:]=np.tile([1,0,0,0,1,0],52)
            np.save(base/'fixture.npy',data)
            (base/'label.json').write_text(json.dumps(dict(name='Original synthetic fixture',style1='Street',style2='Jazz')))
            saved_root=converter.ROOT
            try:
                converter.ROOT=base
                with self.assertRaises(ValueError): converter.convert(base/'fixture.npy',base/'label.json',120,0,False)
                path=converter.convert(base/'fixture.npy',base/'label.json',120,0,True)
                result=json.loads(path.read_text(encoding='utf-8'))
                self.assertTrue(result['personalUseOnly'])
                self.assertNotIn('approved',result)
                self.assertEqual(result['sourceLabel']['style2'],'Jazz')
                self.assertEqual(len(result['sourceSha256']),64)
                self.assertEqual(len(result['frames']),1024)
                with self.assertRaises(ValueError): converter.convert(base/'fixture.npy',base/'label.json',120,20,True)
                (base/'label.json').write_text(json.dumps(dict(style2='Ballet')))
                with self.assertRaises(ValueError): converter.convert(base/'fixture.npy',base/'label.json',120,0,True)
            finally:
                converter.ROOT=saved_root

    def test_pickle_payload_is_not_loaded(self):
        with tempfile.TemporaryDirectory(dir=ROOT/'TestResults') as folder:
            path=Path(folder)/'object.npy'
            np.save(path,np.array([dict(test=True)],dtype=object))
            with self.assertRaises(ValueError): converter.convert(path,path,120,0,True)


if __name__=='__main__':unittest.main()
