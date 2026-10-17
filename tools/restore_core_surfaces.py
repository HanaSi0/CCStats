"""Restore selected source surfaces onto an existing core mesh without reintroducing props."""
from pathlib import Path
import argparse,json,re
import numpy as np
p=argparse.ArgumentParser()
p.add_argument('--base',type=Path,required=True)
p.add_argument('--restored',type=Path,required=True)
p.add_argument('--output',type=Path,required=True)
p.add_argument('--surfaces',required=True)
a=p.parse_args();pattern=re.compile(a.surfaces)
base={k:v for k,v in np.load(a.base/'geometry.npz').items()};restored={k:v for k,v in np.load(a.restored/'geometry.npz').items()}
report=json.loads((a.restored/'report.json').read_text());base_report=json.loads((a.base/'report.json').read_text())
parts=[];colors=[]
for folder,data,field,select,origin in [(a.base,base,'kept',False,np.array(base_report['configuration']['origin'])),(a.restored,restored,'retained_triangles',True,np.zeros(3))]:
    audit=json.loads((folder/('removal-audit.json' if folder==a.base else 'audit.json')).read_text());offset=0
    for row in audit:
        n=row.get(field,0)
        if bool(pattern.search(row['resource']))==select and n:
            parts.append(data['triangles'][offset:offset+n]+origin);colors.append(data['colors'][offset:offset+n])
        offset+=n
    assert offset==len(data['triangles'])
a.output.mkdir(parents=True,exist_ok=True)
tri=np.concatenate(parts).astype('f4');color=np.concatenate(colors).astype('f4')
np.savez_compressed(a.output/'geometry.npz',triangles=tri,colors=color)
report['triangles']=len(tri);report['retention']='Existing core structures retained. Restore only source terrain and complete canal markers; do not reintroduce surrounding props or architecture.'
(a.output/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(report['map'],len(tri),'source faces')
