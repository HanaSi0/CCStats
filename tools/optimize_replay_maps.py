"""Optimize rebuilt arena geometry without removing core surfaces.
Requires numpy, Pillow and fast-simplification.
"""
from pathlib import Path
import argparse,json,gzip,hashlib,struct
import numpy as np
from PIL import Image,ImageDraw
import fast_simplification as fs
parser=argparse.ArgumentParser()
parser.add_argument('--input',type=Path,required=True)
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args()
source=json.loads((args.input/'report.json').read_text())
source.setdefault('map',source.get('slug'))
data=np.load(args.input/'geometry.npz');tri=data['triangles'];colors=data['colors']
alpha=data['alphas'] if 'alphas' in data else np.full(len(tri),255,dtype='u1')
if 'configuration' in source:tri=tri+np.array(source['configuration']['origin'],dtype='f4')
# Quantize only display colors (4 bits/channel), not world-space geometry.
colors=np.rint(np.clip(colors,0,1)*15).astype('u1')
keys=colors[:,0].astype('int32')*256+colors[:,1].astype('int32')*16+colors[:,2]+alpha.astype('int32')*4096
out=[];out_colors=[];out_alpha=[];duplicates=0
for key in np.unique(keys):
    group=tri[keys==key];p,inv=np.unique(group.reshape(-1,3),axis=0,return_inverse=True);faces=inv.reshape(-1,3)
    _,unique=np.unique(np.sort(faces,axis=1),axis=0,return_index=True);duplicates+=len(faces)-len(unique);faces=faces[np.sort(unique)]
    before=p[faces]
    if len(faces)>8:
        sp,sf=fs.simplify(p,faces.astype('int32'),lossless=True)
        if len(sf):
            after=sp[sf]
            def area(t):return np.linalg.norm(np.cross(t[:,1]-t[:,0],t[:,2]-t[:,0]),axis=1).sum()
            # No lossy fallback: preserve extent and total surface area in each color region.
            if np.max(abs(before.reshape(-1,3).min(0)-after.reshape(-1,3).min(0)))<.0001 and np.max(abs(before.reshape(-1,3).max(0)-after.reshape(-1,3).max(0)))<.0001 and abs(area(before)-area(after))<max(.0001,area(before)*.000001):p,faces=sp,sf
    out.append(p[faces]);rgb=key%4096;out_colors.append(np.tile([rgb//256,(rgb//16)%16,rgb%16],(len(faces),1)));out_alpha.append(np.full(len(faces),key//4096,dtype='u1'))
new_alpha=np.concatenate(out_alpha)
new_tri=np.concatenate(out).astype('<f4');new_colors=np.concatenate(out_colors).astype('f4')/15
# Verify horizontal/sloping surface coverage against the pre-optimization model.
lo=tri.reshape(-1,3).min(0);hi=tri.reshape(-1,3).max(0);step=.25
size=np.ceil((hi[[0,2]]-lo[[0,2]])/step).astype(int)+3

def coverage(ts):
    normal=np.cross(ts[:,1]-ts[:,0],ts[:,2]-ts[:,0]);length=np.linalg.norm(normal,axis=1)
    ts=ts[(abs(normal[:,1])>.65*length)&(length>1e-8)]
    im=Image.new('1',tuple(size));d=ImageDraw.Draw(im)
    for t in ts:
        uv=(t[:,[0,2]]-lo[[0,2]])/step
        d.polygon([tuple(p) for p in uv],fill=1)
    return np.array(im,dtype=bool)
a=coverage(tri[alpha==255]);b=coverage(new_tri[new_alpha==255]);lost=int((a&~b).sum())
# Rasterization can expose boundary-pixel differences after changing triangulation.
# Keep the unmodified source topology if any covered sample would be lost.
if lost:
    new_tri=tri;new_colors=colors.astype('f4')/15;new_alpha=alpha
    duplicates=0
final_lost=int((a&~coverage(new_tri[new_alpha==255])).sum());assert final_lost==0
packed=np.empty(new_tri.size//3,dtype=[('p','<f4',(3,)),('color','u1',(4,))]);packed['p']=new_tri.reshape(-1,3)
packed['color'][:,:3]=np.rint(np.repeat(new_colors,3,axis=0)*255).astype('u1');packed['color'][:,3]=np.repeat(new_alpha,3)
vertices,inv=np.unique(packed,return_inverse=True);indices=inv.astype('<u4')
compressed=gzip.compress(struct.pack('<4sII',b'CCM4',len(vertices),len(indices))+vertices.tobytes()+indices.tobytes(),compresslevel=9,mtime=0)
args.output.mkdir(parents=True,exist_ok=True)
(args.output/(source['map']+'.ccmap.gz')).write_bytes(compressed)
np.savez_compressed(args.output/'geometry.npz',triangles=new_tri,colors=new_colors,alphas=new_alpha)
report=source|{'triangles':len(indices)//3,'vertices':len(vertices),'compressed_bytes':len(compressed),'sha256':hashlib.sha256(compressed).hexdigest(),'before_optimization_triangles':len(tri),'duplicate_faces_removed':duplicates,'floor_coverage':{'spacing':step,'source_covered_samples':int(a.sum()),'lost_samples':final_lost,'topology_fallback':bool(lost)}}
(args.output/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(source['map'],len(tri),'->',report['triangles'],'faces,',len(vertices),'vertices,',len(compressed),'bytes; lost floor samples:',final_lost,flush=True)
