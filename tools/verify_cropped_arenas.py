"""Check the arena-only crop against the previous display floors and official map silhouette."""
from pathlib import Path
import argparse,gzip,struct,json,sys,numpy as np
from scipy import ndimage
sys.path.insert(0,str(Path(__file__).resolve().parent));import replay_walkable as w;import refine_replay_maps as m
ap=argparse.ArgumentParser(description='Verify cropped vertices and retention of old floor layers.');ap.add_argument('--before',type=Path,required=True);ap.add_argument('--after',type=Path,required=True);ap.add_argument('--output',type=Path,required=True);args=ap.parse_args();root=args.before;new=args.after;reports=[]
def readgrid(path):
 b=gzip.decompress(path.read_bytes());_,W,D,x,z,cell=struct.unpack_from('<4sIIfff',b);counts=np.frombuffer(b,'u1',W*D,24);heights=np.frombuffer(b,'<f4',offset=24+W*D);col=np.repeat(np.arange(W*D),counts)
 return W,D,x,z,cell,col,heights
for item in json.loads((new/'manifest.json').read_text()):
 key=item['map'];p,_,f=m.read(new/(key+'.ccmap.gz'));mask=w.official_silhouette(key);dist=ndimage.distance_transform_edt(~mask)*.25
 px,pz=w.to_pixels(key,p[:,0],p[:,2],2048);inside=(px>=0)&(pz>=0)&(px<2048)&(pz<2048);assert inside.all();maxdistance=float(dist[pz,px].max());assert maxdistance<=4
 old=readgrid(root/(key+'.ccwalk.gz'));ng=readgrid(new/(key+'.ccwalk.gz'));W,D,ox,oz,s,col,heights=old;NW,ND,nx,nz,ns,ncol,nh=ng
 lost=0;maxdrift=0
 z=np.floor((oz+(col//W+.5)*s-nz)/ns).astype(int);x=np.floor((ox+(col%W+.5)*s-nx)/ns).astype(int)
 # Neighbour tolerance accounts for surface lattice changes, never accepts a different height layer.
 starts=np.searchsorted(ncol,np.arange(NW*ND+1));found=np.zeros(len(col),bool)
 for dz in [-1,0,1]:
  for dx in [-1,0,1]:
   xx=x+dx;zz=z+dz;ok=(xx>=0)&(zz>=0)&(xx<NW)&(zz<ND);query=np.clip(zz*NW+xx,0,NW*ND-1)
   first=starts[query];length=starts[query+1]-first
   for layer in range(int(length.max())):
    valid=ok&(length>layer);idx=np.minimum(first+layer,len(nh)-1);found|=valid&(abs(nh[idx]-heights)<=.65)
 lost=int((~found).sum());row={'map':key,'max_vertex_distance_from_official_floor':round(maxdistance,3),'previous_walk_layers':len(col),'walk_layers_without_nearby_same_height_floor':lost,'same_height_tolerance':.65,'neighbor_radius':.5,'new_walk_layers':len(ncol)};reports.append(row);print(json.dumps(row),flush=True)
args.output.parent.mkdir(parents=True,exist_ok=True);args.output.write_text(json.dumps(reports,indent=2)+'\n')
assert all(r['walk_layers_without_nearby_same_height_floor']==0 for r in reports),'Missing floor layers after cropping'
