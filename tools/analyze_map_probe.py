#!/usr/bin/env python3
"""Validate and summarize a user's local map-probe ZIP, without extracting files."""
import argparse
import hashlib
import json
from pathlib import PurePosixPath
from zipfile import ZipFile


def analyze(path):
    with ZipFile(path) as archive:
        infos = archive.infolist()
        if len(infos) > 801 or len({i.filename for i in infos}) != len(infos):
            raise ValueError('Unexpected or duplicate ZIP entries')
        info = archive.getinfo('manifest.json')
        if info.file_size > 1024 * 1024:
            raise ValueError('Manifest too large')
        manifest = json.loads(archive.read(info).decode('utf-8-sig'))
        if manifest.get('Format') != 1 or len(manifest.get('Maps', [])) > 100:
            raise ValueError('Unsupported manifest')
        output = []
        for entry in manifest['Maps']:
            if entry['State'] != 'Complete':
                raise ValueError('Archive contains incomplete capture')
            documents = {}
            for file in entry['Files']:
                name = entry['Id'] + '/' + file['Name']
                if any(p in ('..', '.') for p in PurePosixPath(name).parts) or '\\' in name:
                    raise ValueError('Unsafe document path')
                info = archive.getinfo(name)
                if not 0 <= info.file_size <= 8 * 1024 * 1024 or info.file_size != file['Length']:
                    raise ValueError('Document size mismatch')
                raw = archive.read(info)
                if hashlib.sha256(raw).hexdigest().upper() != file['Sha256'].upper():
                    raise ValueError('Document hash mismatch')
                documents[file['Name']] = json.loads(raw.decode('utf-8-sig'))
            capture = documents.get('capture.json', {})
            metadata = capture.get('Capture', {})
            scene = documents.get('scene.json', {})
            output.append({
                'label': entry['Label'], 'territory': entry['Territory'], 'map': entry['Map'],
                'reported_territory': metadata.get('ReportedTerritory'),
                'background': metadata.get('Background'), 'objects': len(metadata.get('Objects', [])),
                'models': scene.get('Models'), 'triangles': scene.get('TriangleCount'),
                'missing_models': scene.get('MissingModels'), 'terrain_plates': scene.get('TerrainPlates'),
                'geometry_limited': scene.get('Limited'), 'scene_status': scene.get('Status'),
                'radius_samples': len(capture.get('RadiusSamples', [])),
                'reference': capture.get('Reference'), 'inside_bound': capture.get('InsideBound'),
                'outside_bound': capture.get('OutsideBound'), 'provisional_radius': capture.get('ReferenceRadius'),
                'asset_paths': scene.get('AssetPaths', []),
                'model_diagnostics': scene.get('ModelDiagnostics', []),
            })
        return {'validated_maps': len(output), 'maps': output}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('zip')
    args = parser.parse_args()
    print(json.dumps(analyze(args.zip), ensure_ascii=False, indent=2))
