"""Rebuild core arena maps from user GLB exports, preserving arena terrain and rock silhouettes.
Requires numpy, Pillow and fast-simplification. The source exports are not stored in Git.
"""
from pathlib import Path
import argparse,struct,json,hashlib,gzip,re,sys
import numpy as np
from PIL import Image
parser=argparse.ArgumentParser()
parser.add_argument('--map',default='red-sands',choices=['red-sands','palaistra','volcanic-heart','cloud-nine','castletown','bayside','harmonias'])
parser.add_argument('--source',required=True,type=Path)
parser.add_argument('--output',required=True,type=Path)
args=parser.parse_args()
ROOT=args.source
try: import fast_simplification as fs
except ImportError: raise SystemExit('Install fast-simplification to rebuild map resources.')
DT={5120:'i1',5121:'u1',5122:'<i2',5123:'<u2',5125:'<u4',5126:'<f4'}
WIDTH={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}

def load(path):
 raw=path.read_bytes();magic,version,length=struct.unpack_from('<4sII',raw);assert magic==b'glTF' and version==2 and length==len(raw)
 off=12;doc=None;binary=b''
 while off<len(raw):
  size,kind=struct.unpack_from('<II',raw,off);off+=8
  if kind==0x4e4f534a:doc=json.loads(raw[off:off+size])
  elif kind==0x004e4942:binary=raw[off:off+size]
  off+=size
 def accessor(i):
  a=doc['accessors'][i];assert 'sparse' not in a
  bv=doc['bufferViews'][a['bufferView']];dtype=np.dtype(DT[a['componentType']]);width=WIDTH[a['type']]
  stride=bv.get('byteStride',dtype.itemsize*width);offset=bv.get('byteOffset',0)+a.get('byteOffset',0)
  arr=np.ndarray((a['count'],width),dtype=dtype,buffer=binary,offset=offset,strides=(stride,dtype.itemsize)).copy()
  if a.get('normalized') and a['componentType']!=5126:
   info=np.iinfo(dtype);arr=arr.astype(float)/info.max
   if info.min<0:arr=np.maximum(arr,-1)
  return arr
 def matrix(node):
  if 'matrix' in node:return np.array(node['matrix']).reshape(4,4,order='F')
  x,y,z,w=node.get('rotation',[0,0,0,1]);r=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
  m=np.eye(4);m[:3,:3]=r@np.diag(node.get('scale',[1,1,1]));m[:3,3]=node.get('translation',[0,0,0]);return m
 batches=[];nodes=doc.get('nodes',[])
 def walk(i,parent,path,visible):
  node=nodes[i];world=parent@matrix(node);name=node.get('name',str(i));path=path+[name]
  visible=visible and node.get('extras',{}).get('IsVisible',True)
  if 'mesh' in node:
   for prim in doc['meshes'][node['mesh']]['primitives']:
    if prim.get('mode',4)!=4:continue
    pts=accessor(prim['attributes']['POSITION']).astype(float);pts=pts@world[:3,:3].T+world[:3,3]
    ids=accessor(prim['indices']).reshape(-1) if 'indices' in prim else np.arange(len(pts))
    assert len(ids)%3==0 and (len(ids)==0 or ids.max()<len(pts))
    material=doc.get('materials',[])[prim['material']] if 'material' in prim else {}
    batches.append(dict(file=path0.name,name=name,path=path,visible=bool(visible),points=pts,faces=ids.reshape(-1,3),material=material))
  for child in node.get('children',[]):walk(child,world,path,visible)
 path0=path
 for node in doc['scenes'][doc.get('scene',0)]['nodes']:walk(node,np.eye(4),[],True)
 return doc,batches


DETAIL=re.compile(r'_(?:plt|tre|flg|lmp|lig|flw|fsh|ppr|pfl)\d+\.mdl|w_itm_')
CONFIGS={
 'red-sands':('赤土红沙',108,87,-48,38,[0,0,0]),
 'palaistra':('角力学校',102,50,-3,8,[0,0,0]),
 'volcanic-heart':('火山之心',76,46,-4,6,[0,0,0]),
 'cloud-nine':('九霄云上',102,62,-4,8,[0,0,0]),
 'castletown':('机关大殿',88,57,-2.8,6,[0,0,0]),
 'bayside':('海岸斗场',99,69,-25,12,[100,0,100]),
 'harmonias':('休兵书库',103,91,-3.5,10,[100,0,100])}
name,x,z,low,high,origin=CONFIGS[args.map]
origin=np.array(origin)
LIMITS=np.array([[-x,x],[low,high],[-z,z]])
# Clip only at the outer arena margin; never use an inferred walkable-floor mask.
def clip_triangle(triangle):
    polygons=[triangle]
    for axis in range(3):
        for bound,sign in [(LIMITS[axis,0],1),(LIMITS[axis,1],-1)]:
            result=[]
            for poly in polygons:
                out=[]
                for a,b in zip(poly,np.roll(poly,-1,axis=0)):
                    da=(a[axis]-bound)*sign;db=(b[axis]-bound)*sign
                    if da>=0: out.append(a)
                    if (da>=0)!=(db>=0): out.append(a+(b-a)*(da/(da-db)))
                if len(out)>=3:result.append(np.array(out))
            polygons=result
    return [np.array([p[0],p[i],p[i+1]]) for p in polygons for i in range(1,len(p)-1)]

def clipped(triangles):
    lo=triangles.min(1);hi=triangles.max(1)
    inside=((lo>=LIMITS[:,0])&(hi<=LIMITS[:,1])).all(1)
    intersect=((hi>=LIMITS[:,0])&(lo<=LIMITS[:,1])).all(1)
    partial=[t for original in triangles[intersect&~inside] for t in clip_triangle(original)]
    return np.concatenate([triangles[inside],np.array(partial).reshape(-1,3,3)])

texture_colors={}
def material_color(material):
    extras=material.get('extras',{})
    path=extras.get('g_SamplerColorMap0_PngCachePath','')
    if path not in texture_colors:
        file=ROOT/'cache'/path.replace('\\','/')
        if path and file.is_file():
            im=Image.open(file).convert('RGB');im.thumbnail((32,32));color=np.asarray(im).reshape(-1,3).mean(0)/255
        else:color=np.array([.55,.60,.64])
        texture_colors[path]=np.clip(color,.2,.88)
    return texture_colors[path]

triangles=[];colors=[];audit=[];source_hashes={};original_count=0
for source in sorted(ROOT.glob('*.glb')):
    source_hashes[source.name]=hashlib.sha256(source.read_bytes()).hexdigest()
    for batch in load(source)[1]:
        path='/'.join(batch['path']);material=batch['material'];original=(batch['points']-origin)[batch['faces']];original_count+=len(original)
        shader=material.get('extras',{}).get('ShaderPackage','').lower()
        if not batch['visible'] or DETAIL.search(path) or any(word in shader for word in ['fog','water','sky']):continue
        tri=clipped(original)
        if not len(tri):continue
        p,inv=np.unique(np.round(tri.reshape(-1,3),7),axis=0,return_inverse=True);faces=inv.reshape(-1,3).astype('int32')
        normal=np.cross(tri[:,1]-tri[:,0],tri[:,2]-tri[:,0]);areas=np.linalg.norm(normal,axis=1)
        floor=((abs(normal[:,1])>.85*areas)&(areas>1e-8)).any() and not re.search(r'_rck\d+',path)
        # Preserve source topology unless a lossless operation also preserves area and bounds.
        before=len(faces)
        if len(faces)>40:
            before_tri=p[faces].copy()
            sp,sf=fs.simplify(p.copy(),faces.copy(),lossless=True)
            if len(sf):
                after_tri=sp[sf]
                def area(ts):return np.linalg.norm(np.cross(ts[:,1]-ts[:,0],ts[:,2]-ts[:,0]),axis=1).sum()
                old=before_tri.reshape(-1,3);new=after_tri.reshape(-1,3)
                drift=max(abs(old.min(0)-new.min(0)).max(),abs(old.max(0)-new.max(0)).max())
                if drift<.000001 and abs(area(before_tri)-area(after_tri))<max(.000001,area(before_tri)*.0000001):p,faces=sp,sf
        tri=p[faces];normal=np.cross(tri[:,1]-tri[:,0],tri[:,2]-tri[:,0]);length=np.linalg.norm(normal,axis=1);valid=length>1e-8
        tri=tri[valid];normal=normal[valid]/length[valid,None]
        light=.5+.5*abs(normal@np.array([.3,.88,.36]));color=np.clip(material_color(material)[None,:]*light[:,None],0,1)
        triangles.append(tri);colors.append(color)
        audit.append({'resource':path,'source':source.name,'clipped_triangles':before,'retained_triangles':len(tri),'ground_or_ramp':bool(floor)})
tri=(np.concatenate(triangles)+origin).astype('<f4');color=np.concatenate(colors)
args.output.mkdir(parents=True,exist_ok=True)
np.savez_compressed(args.output/'geometry.npz',triangles=tri,colors=color)
packed=np.empty(tri.size//3,dtype=[('p','<f4',(3,)),('color','u1',(4,))]);packed['p']=tri.reshape(-1,3)
packed['color'][:,:3]=np.rint(np.repeat(color,3,axis=0)*255).astype('u1');packed['color'][:,3]=255
vertices,inv=np.unique(packed,return_inverse=True);indices=inv.astype('<u4')
raw=struct.pack('<4sII',b'CCM4',len(vertices),len(indices))+vertices.tobytes()+indices.tobytes()
compressed=gzip.compress(raw,compresslevel=9,mtime=0)
(args.output/(args.map+'.ccmap.gz')).write_bytes(compressed)
report={'map':args.map,'name':name,'triangles':len(tri),'vertices':len(vertices),'compressed_bytes':len(compressed),'sha256':hashlib.sha256(compressed).hexdigest(),'source_triangles':original_count,'source_sha256':source_hashes,'bounds':[tri.reshape(-1,3).min(0).tolist(),tri.reshape(-1,3).max(0).tolist()],'origin':origin.tolist(),'retention':'Restore source ground, canyon walls, ramps and nearby rocks; no floor-mask clipping. Only outer-margin clipping, area/extent-validated lossless simplification. Fog, sky and tiny props remain excluded.'}
(args.output/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
(args.output/'audit.json').write_text(json.dumps(audit,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(report,ensure_ascii=False,indent=2))
