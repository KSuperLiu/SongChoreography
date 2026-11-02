"""Check the delivered ZIP, rather than just its uncompressed build inputs."""
import hashlib
import json
from pathlib import Path
import sys
import zipfile

archive = Path(sys.argv[1]).resolve()
with zipfile.ZipFile(archive) as bundle:
    bad = bundle.testzip()
    if bad:
        raise RuntimeError(f'ZIP CRC failure: {bad}')
    manifests = [name for name in bundle.namelist() if name.count('/') == 1 and name.endswith('/PACKAGE.json')]
    if len(manifests) != 1:
        raise RuntimeError('Expected one package manifest')
    prefix = manifests[0].split('/')[0] + '/'
    manifest = json.loads(bundle.read(manifests[0]).decode('utf-8-sig'))
    total = 0
    for entry in manifest['files']:
        name = prefix + entry['path'].replace('\\', '/')
        info = bundle.getinfo(name)
        if info.file_size != entry['bytes']:
            raise RuntimeError(f'Size mismatch: {name}')
        digest = hashlib.sha256()
        with bundle.open(name) as stream:
            while chunk := stream.read(1024 * 1024):
                digest.update(chunk)
        if digest.hexdigest() != entry['sha256']:
            raise RuntimeError(f'SHA-256 mismatch: {name}')
        total += info.file_size
result = dict(allPassed=True, version=manifest['version'], zipCrcPassed=True,
              filesVerified=len(manifest['files']), uncompressedBytes=total,
              runtimeAssemblySha256=manifest['runtimeAssemblySha256'])
archive.with_suffix('.verification.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result))
