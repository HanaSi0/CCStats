#!/usr/bin/env python3
"""Download original game map textures decoded as PNG by XIVAPI; never render substitute images."""
from pathlib import Path
import argparse,hashlib,json,struct,sys,urllib.request,zipfile
ROOT=Path(__file__).resolve().parents[1]
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--output',type=Path,default=ROOT/'docs/exports/official-maps-2d/images')
p.add_argument('--timeout',type=float,default=30)
p.add_argument('--dry-run',action='store_true',help='Print resource paths only; does not prove image availability.')
a=p.parse_args()
catalog=json.loads((ROOT/'docs/exports/official-maps-2d/assets.json').read_text())
if a.dry_run:
    for m in catalog['maps']: print(m['name'],m['client_texture'],m['png_url'])
    sys.exit(0)
a.output.mkdir(parents=True,exist_ok=True)
results=[]
for m in catalog['maps']:
    row={'map':m['key'],'name':m['name'],'url':m['png_url'],'client_texture':m['client_texture']}
    try:
        request=urllib.request.Request(m['png_url'],headers={'User-Agent':'CCStats-map-resource-export/1.0','Accept':'image/png'})
        with urllib.request.urlopen(request,timeout=a.timeout) as response:
            data=response.read(32*1024*1024+1)
        if len(data)>32*1024*1024:raise ValueError('Image exceeds 32 MiB')
        if not data.startswith(b'\x89PNG\r\n\x1a\n') or data[12:16]!=b'IHDR' or len(data)<33:raise ValueError('Response is not a PNG')
        width,height=struct.unpack('>II',data[16:24])
        if not (128<=width<=8192 and 128<=height<=8192):raise ValueError('Unexpected PNG dimensions')
        name=m['key']+'.png';target=a.output/name;temporary=target.with_suffix('.png.tmp');temporary.write_bytes(data);temporary.replace(target)
        row.update(status='downloaded',file=name,width=width,height=height,bytes=len(data),sha256=hashlib.sha256(data).hexdigest())
        print(f"Downloaded {m['name']}: {width}x{height}, {len(data)} bytes")
    except Exception as e:
        row.update(status='failed',error=f'{type(e).__name__}: {e}')
        print(f"FAILED {m['name']}: {row['error']}",file=sys.stderr)
    results.append(row)
(a.output/'downloads.json').write_text(json.dumps(results,ensure_ascii=False,indent=2)+'\n')
if any(r['status']!='downloaded' for r in results):
    sys.exit('Not all seven maps downloaded; no complete ZIP produced.')
archive=a.output.parent/'official-maps-2d.zip'
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
    for r in results:z.write(a.output/r['file'],r['file'])
    z.write(a.output/'downloads.json','downloads.json')
    z.write(ROOT/'docs/exports/official-maps-2d/assets.json','assets.json')
print('All seven original textures downloaded:',archive)
