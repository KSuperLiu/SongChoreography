"""Rebuild the original synthesized 40-second demo, without downloading music."""
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / 'Analyzer'))
from analyzer import make_demo, analyze

source = root / 'Samples/demo-source.wav'
make_demo(source)
analyze(dict(schemaVersion=1, sourcePath=str(source), outputDir=str(root/'Samples/Demo'), startSeconds=0, durationSeconds=40))
print('Demo ready. Rebuild the Unity player to include it.')
