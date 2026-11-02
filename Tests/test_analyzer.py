import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('analyzer', ROOT / 'Analyzer/analyzer.py')
analyzer = importlib.util.module_from_spec(spec)
spec.loader.exec_module(analyzer)


class AnalyzerTests(unittest.TestCase):
    def setUp(self):
        (ROOT/'TestResults').mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=ROOT/'TestResults')
        self.folder = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def request(self, source, name='output'):
        return dict(schemaVersion=1, sourcePath=str(source), outputDir=str(self.folder/name), startSeconds=0, durationSeconds=30)

    def test_three_tempos(self):
        for bpm in (90,120,140):
            with self.subTest(bpm=bpm):
                source=self.folder/f'{bpm}.wav'
                analyzer.make_demo(source,bpm,30)
                request=self.request(source,str(bpm))
                result=analyzer.analyze(request)
                self.assertFalse(result['needsManualGrid'])
                # Half/double-time ambiguity is exposed for user correction, but these fixtures should track the intended tempo.
                self.assertLess(abs(result['bpm']-bpm),4)
                beats=np.array(result['beats'])
                phase=((beats-.25+30/bpm)%(60/bpm))-30/bpm
                self.assertLess(np.median(abs(phase)),.05)
                for rate,key in ((1,'normalAudio'),(.75,'slow075Audio'),(.5,'slow050Audio')):
                    info=sf.info(str(Path(request['outputDir'])/result[key]))
                    self.assertLess(abs(info.duration-30/rate),.002)
                self.assertEqual(json.loads((Path(request['outputDir'])/'progress.json').read_text())['state'],'complete')

    def test_silence_and_short_clip(self):
        source=self.folder/'silent.wav';sf.write(str(source),np.zeros(44100*30),44100)
        with self.assertRaisesRegex(ValueError,'静音'):analyzer.analyze(self.request(source))
        sf.write(str(source),np.ones(44100),44100)
        with self.assertRaisesRegex(ValueError,'30 秒'):analyzer.analyze(self.request(source,'short'))

    def test_cancel_and_invalid_request(self):
        source=self.folder/'source.wav';analyzer.make_demo(source,120,30)
        request=self.request(source);out=Path(request['outputDir']);out.mkdir();(out/'cancel.flag').touch()
        with self.assertRaises(analyzer.Cancelled):analyzer.analyze(request)
        request['schemaVersion']=99
        with self.assertRaises(ValueError):analyzer.analyze(request)

    def test_mp3_and_pitch_preservation(self):
        source=self.folder/'source.mp3'
        sr=44100;t=np.arange(sr*30)/sr;y=.15*np.sin(2*np.pi*440*t)
        sf.write(str(source),y,sr,format='MP3')
        request=self.request(source);result=analyzer.analyze(request)
        for name in ('normalAudio','slow075Audio','slow050Audio'):
            audio,actual_sr=sf.read(str(Path(request['outputDir'])/result[name]),start=sr*2,frames=sr)
            peak=np.argmax(abs(np.fft.rfft(audio*np.hanning(len(audio)))))
            self.assertLess(abs(peak-440),3)


if __name__=='__main__':unittest.main(verbosity=2)
