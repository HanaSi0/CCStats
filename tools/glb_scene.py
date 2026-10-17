"""Read triangle instances from the original user GLB exports."""
from pathlib import Path
import struct,json
import numpy as np
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
