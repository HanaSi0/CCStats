#!/usr/bin/env python3
"""Validate the built install ZIP and produce a matching Dalamud repository index."""
import argparse
import hashlib
import json
import shutil
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--version', required=True)
parser.add_argument('--check-index', action='store_true')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
package = root / 'PvpStats/bin/x64/Release/PvpStats/latest.zip'
with zipfile.ZipFile(package) as archive:
    assert archive.testzip() is None, 'Corrupt ZIP'
    names = set(archive.namelist())
    required = {'PvpStats.dll', 'PvpStats.json', 'LiteDB.dll', 'HtmlAgilityPack.dll', 'icon.png'}
    assert required <= names, f'Missing files: {required - names}'
    assert not any('..' in Path(name).parts or name.startswith('/') for name in names)
    assert not any(Path(name).name in {'Dalamud.dll', 'FFXIVClientStructs.dll', 'Dalamud.Bindings.ImGui.dll'} for name in names), 'Host assemblies must not be shipped'
    assert archive.read('icon.png') == (root / 'images/icon.png').read_bytes(), 'Logo mismatch'
    manifest = json.loads(archive.read('PvpStats.json'))
assert manifest['InternalName'] == 'PvpStats'
assert manifest['Name'] == 'CCStats'
assert manifest['AssemblyVersion'] == args.version + '.0', manifest['AssemblyVersion']
assert manifest['DalamudApiLevel'] == 15
assert manifest['RepoUrl'] == 'https://github.com/HanaSi0/CCStats'
url = f'https://github.com/HanaSi0/CCStats/releases/download/v{args.version}/latest.zip'
manifest.update(DownloadLinkInstall=url, DownloadLinkUpdate=url, DownloadLinkTesting=url)
index = json.dumps([manifest], ensure_ascii=False, indent=2) + '\n'
index_path = root / 'repo.json'
if args.check_index:
    assert json.loads(index_path.read_text()) == [manifest], 'Committed repo.json does not match build'
else:
    index_path.write_text(index)
output = root / 'artifacts' / f'v{args.version}'
output.mkdir(parents=True, exist_ok=True)
shutil.copy2(package, output / 'latest.zip')
shutil.copy2(index_path, output / 'repo.json')
shutil.copy2(root / 'LICENSE.txt', output / 'LICENSE.txt')
checksum = hashlib.sha256(package.read_bytes()).hexdigest()
(output / 'SHA256SUMS.txt').write_text(f'{checksum}  latest.zip\n')
print(f'Validated {len(names)} package files, API {manifest["DalamudApiLevel"]}, version {manifest["AssemblyVersion"]}')
print(f'Release assets: {output}')
