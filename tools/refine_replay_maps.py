"""Refine packaged replay maps and build their walkable decal grids.

Input must be the flat-shaded 1.0.5 resources: one 4-bit colour per face, equal to a material
colour times the old per-face light term 0.5+0.5*|n.L|. This tool
  1. welds vertices and drops degenerate/duplicate faces,
  2. recovers each material region's colour from the quantised face colours,
  3. removes interior vertices of coplanar single-material fans (no visible change),
  4. fills empty columns fully enclosed by one floor height (missing display floor),
  5. relights with smoothed normals (40 degree crease) and 8-bit colour,
  6. writes <map>.ccmap.gz, <map>.ccwalk.gz and manifest.json.
Display geometry only; never game collision or navigation data.
Usage: python3 tools/refine_replay_maps.py --input <1.0.5 ReplayMaps dir> --output PvpStats/Resources/ReplayMaps
"""
from pathlib import Path
import argparse,gzip,hashlib,json,struct,sys,time
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
import replay_walkable

MAPS={'bayside':'海岸斗场','castletown':'机关大殿','cloud-nine':'九霄云上','harmonias':'休兵书库','palaistra':'角力学校','red-sands':'赤土红沙','volcanic-heart':'火山之心'}
LIGHT=np.array([.3,.88,.36])
CREASE=np.cos(np.radians(40))
VERTEX=np.dtype([('p','<f4',(3,)),('c','<u4')])


def read(path):
    raw=gzip.open(path).read();assert raw[:4]==b'CCM4'
    vc,ic=struct.unpack_from('<ii',raw,4)
    v=np.frombuffer(raw,VERTEX,vc,12);i=np.frombuffer(raw,'<u4',ic,12+16*vc).reshape(-1,3)
    return v['p'].astype(np.float64),v['c'].copy(),i.astype(np.int64)


def write(path,positions,colors,faces):
    packed=np.zeros(len(faces)*3,VERTEX);packed['p']=positions[faces.ravel()];packed['c']=colors.ravel()
    vertices,inverse=np.unique(packed,return_inverse=True)
    data=gzip.compress(struct.pack('<4sii',b'CCM4',len(vertices),len(faces)*3)+vertices.tobytes()+inverse.astype('<u4').tobytes(),compresslevel=9,mtime=0)
    path.write_bytes(data);return data,len(vertices)


def face_normals(p,f):
    n=np.cross(p[f[:,1]]-p[f[:,0]],p[f[:,2]]-p[f[:,0]]);a=np.linalg.norm(n,axis=1)
    return n/np.maximum(a,1e-30)[:,None],a/2


class Regions:
    """Union-find over faces; a region keeps the intersection of its members' colour intervals."""
    def __init__(self,lo,hi):self.parent=np.arange(len(lo));self.lo=lo.copy();self.hi=hi.copy()
    def find(self,a):
        root=a
        while self.parent[root]!=root:root=self.parent[root]
        while self.parent[a]!=root:self.parent[a],a=root,self.parent[a]
        return root
    def merge(self,a,b):
        a=self.find(a);b=self.find(b)
        if a==b:return
        lo=np.maximum(self.lo[a],self.lo[b]);hi=np.minimum(self.hi[a],self.hi[b])
        if (lo>hi+1e-9).any():return
        if a>b:a,b=b,a
        self.parent[b]=a;self.lo[a]=lo;self.hi[a]=hi


def recover_regions(p,f,colors):
    rgb=np.stack([colors&255,(colors>>8)&255,(colors>>16)&255],1).astype(float)
    if (rgb%17).any():raise SystemExit('Input is not the flat-shaded 4-bit resource set; use the 1.0.5 ReplayMaps files.')
    q=rgb/17;n,_=face_normals(p,f);light=.5+.5*np.abs(n@LIGHT)
    lo=np.maximum((q-.5)/(15*light[:,None]),.2);hi=np.where(q>=15,.88,np.minimum((q+.5)/(15*light[:,None]),.88))
    regions=Regions(lo,hi)
    e=np.concatenate([f[:,[0,1]],f[:,[1,2]],f[:,[2,0]]]);e.sort(1);owner=np.tile(np.arange(len(f)),3)
    o=np.lexsort((e[:,1],e[:,0]));e=e[o];owner=owner[o]
    same=(e[1:]==e[:-1]).all(1)
    for a,b in zip(owner[:-1][same],owner[1:][same]):regions.merge(int(a),int(b))
    root=np.array([regions.find(i) for i in range(len(f))])
    base=(regions.lo[root]+regions.hi[root])/2
    _,region=np.unique(root,return_inverse=True)
    return region,base


def weld(p,colors,i):
    key=np.round(p/1e-4).astype(np.int64);_,first,inv=np.unique(key,axis=0,return_index=True,return_inverse=True)
    f=inv.ravel()[i];c=colors[i[:,0]]
    keep=(f[:,0]!=f[:,1])&(f[:,1]!=f[:,2])&(f[:,0]!=f[:,2])
    f,c=f[keep],c[keep];_,area=face_normals(p[first],f);keep=area>1e-7;f,c=f[keep],c[keep]
    _,unique=np.unique(np.sort(f,1),axis=0,return_index=True);unique.sort()
    return p[first],f[unique],c[unique]


def decimate(p,f,region):
    """Half-edge collapses inside coplanar single-region fans; outlines and colours are unchanged."""
    n,_=face_normals(p,f);alive=np.ones(len(f),bool);faces=f.copy()
    vf=[set() for _ in range(len(p))]
    for k,(a,b,c) in enumerate(faces):vf[a].add(k);vf[b].add(k);vf[c].add(k)
    total=0
    for _ in range(6):
        changed=0
        for v in range(len(p)):
            fs=vf[v]
            if len(fs)<2:continue
            fl=list(fs);r=region[fl[0]];n0=n[fl[0]]
            if any(region[k]!=r or n[k]@n0<1-1e-6 for k in fl):continue
            edges={}
            for k in fl:
                for u in faces[k]:
                    if u!=v:edges[u]=edges.get(u,0)+1
            if any(c>2 for c in edges.values()):continue
            boundary=[u for u,c in edges.items() if c==1]
            if boundary:
                if len(boundary)!=2:continue
                d1=p[boundary[0]]-p[v];d2=p[boundary[1]]-p[v]
                if np.linalg.norm(np.cross(d1,d2))>1e-6*np.linalg.norm(d1)*np.linalg.norm(d2) or d1@d2>=0:continue
                targets=boundary
            else:targets=sorted(edges,key=lambda u:np.sum((p[u]-p[v])**2))
            for u in targets:
                shared=[k for k in fl if u in faces[k]]
                if len(shared)!=(1 if boundary else 2):continue
                # Link condition: v and u share exactly the neighbours of the collapsed faces.
                nv=set(edges);nu=set(x for k in vf[u] for x in faces[k] if x!=u)
                if len(nv&nu)!=len(shared):continue
                ok=True;new={}
                for k in fl:
                    if k in shared:continue
                    t=[u if x==v else x for x in faces[k]]
                    m=np.cross(p[t[1]]-p[t[0]],p[t[2]]-p[t[0]]);l=np.linalg.norm(m)
                    if l<1e-7 or m@n0<=0.999*l:ok=False;break
                    new[k]=t
                if not ok:continue
                if any(set(t)==set(faces[j]) for k,t in new.items() for j in vf[u]):continue
                for k in shared:
                    alive[k]=False
                    for x in faces[k]:vf[x].discard(k)
                for k,t in new.items():faces[k]=t;vf[u].add(k)
                vf[v]=set();changed+=1;break
        total+=changed
        if not changed:break
    return faces[alive],np.flatnonzero(alive),total


def fill_holes(p,f,base,grid):
    """Flat display patches for enclosed holes, one cell beyond the rim and slightly below it.
    Colour: area-weighted median material of the upward rim faces at the hole's floor level."""
    from scipy import ndimage
    patches=[];colors=[];report=[]
    n,area=face_normals(p,f);centre=p[f].mean(1)
    cx=np.floor((centre[:,0]-grid.lo[0])/grid.cell).astype(np.int64);cz=np.floor((centre[:,2]-grid.lo[1])/grid.cell).astype(np.int64)
    inside=(cx>=0)&(cx<grid.W)&(cz>=0)&(cz<grid.D);cell=np.where(inside,cz*grid.W+cx,0)
    level=inside&(np.abs(n[:,1])>.9)
    for hole in grid.enclosed_holes():
        mask=ndimage.binary_dilation(hole['mask']);y=hole['height']-.03
        near=level&hole['ring'].ravel()[cell]&(np.abs(centre[:,1]-hole['height'])<=.06)
        if not near.any():continue
        o=np.argsort(base[near].sum(1));w=np.cumsum(area[near][o]);colour=base[near][o][np.searchsorted(w,w[-1]/2)]
        for z in range(mask.shape[0]):
            row=np.flatnonzero(mask[z])
            if not len(row):continue
            for run in np.split(row,np.flatnonzero(np.diff(row)>1)+1):
                a=grid.lo[0]+run[0]*grid.cell;b=grid.lo[0]+(run[-1]+1)*grid.cell;c=grid.lo[1]+z*grid.cell;d=c+grid.cell
                patches+=[[[a,y,c],[b,y,d],[b,y,c]],[[a,y,c],[a,y,d],[b,y,d]]];colors+=[colour,colour]
        zz,xx=np.nonzero(hole['mask'])
        report.append({'centre':[round(float(grid.lo[0]+(xx.mean()+.5)*grid.cell),2),round(float(grid.lo[1]+(zz.mean()+.5)*grid.cell),2)],
                       'area':round(hole['area'],2),'height':round(y,3)})
    return np.array(patches).reshape(-1,3,3),np.array(colors).reshape(-1,3),report


def shade(p,f,base):
    """Per-corner light from area-weighted normals of faces within the crease angle."""
    n,area=face_normals(p,f);w=n*area[:,None]
    corner_v=f.ravel();corner_f=np.repeat(np.arange(len(f)),3)
    o=np.argsort(corner_v,kind='stable');cv=corner_v[o];cf=corner_f[o]
    start=np.flatnonzero(np.r_[True,cv[1:]!=cv[:-1]]);size=np.diff(np.r_[start,len(cv)])
    group=np.repeat(np.arange(len(start)),size)
    # all ordered pairs inside each vertex group
    pairs=np.repeat(size,size);first=np.repeat(start,size*size)
    local=np.arange(pairs.sum())-np.repeat(np.cumsum(size*size)-size*size,size*size)
    a=first+local//np.repeat(size,size*size);b=first+local%np.repeat(size,size*size)
    na=n[cf[a]];nb=n[cf[b]];d=(na*nb).sum(1);sign=np.where(d<0,-1.0,1.0)
    use=np.abs(d)>=CREASE
    acc=np.zeros((len(cv),3));np.add.at(acc,a[use],w[cf[b[use]]]*sign[use,None])
    acc/=np.maximum(np.linalg.norm(acc,axis=1),1e-30)[:,None]
    light=np.empty(len(cv));light[o]=.5+.5*np.abs(acc@LIGHT)
    light=light.reshape(-1,3)
    rgb=np.clip(np.rint(base[:,None,:]*light[:,:,None]*255),0,255).astype(np.uint32)
    return rgb[...,0]|(rgb[...,1]<<8)|(rgb[...,2]<<16)|(np.uint32(255)<<24)


def refine(name,src,out):
    t0=time.time();p,c,i=read(src)
    alpha=c>>24;opaque=np.minimum.reduce([alpha[i[:,0]],alpha[i[:,1]],alpha[i[:,2]]])==255
    tp,ti=p,i[~opaque];tc=c[ti]
    wp,wf,wc=weld(p,c,i[opaque]);before=len(wf)
    region,base=recover_regions(wp,wf,wc)
    wf,kept,collapsed=decimate(wp,wf,region);region=region[kept];base=base[kept]
    lo=p.min(0)[[0,2]];hi=p.max(0)[[0,2]]
    grid=replay_walkable.WalkGrid(wp[wf],lo,hi)
    patch,patch_colour,holes=fill_holes(wp,wf,base,grid)
    if len(patch):
        offset=len(wp);wp=np.concatenate([wp,patch.reshape(-1,3)]);wf=np.concatenate([wf,offset+np.arange(len(patch)*3).reshape(-1,3)])
        base=np.concatenate([base,patch_colour])
    colors=shade(wp,wf,base)
    positions=np.concatenate([wp,tp]);faces=np.concatenate([wf,len(wp)+ti]);colors=np.concatenate([colors,tc])
    data,vertices=write(out/f'{name}.ccmap.gz',positions,colors,faces)
    grid=replay_walkable.build(wp[wf],name,lo,hi)
    walk=grid.write(out/f'{name}.ccwalk.gz')
    print(f'{name}: {len(i)} -> {len(faces)} faces ({before} welded opaque, {collapsed} coplanar collapses), {vertices} vertices, '
          f'{len(holes)} holes filled, {len(grid.reach_col)} walkable cells ({grid.clipped} outside official map or islands, {grid.filled} gap cells), {time.time()-t0:.0f}s',flush=True)
    return {'map':name,'name':MAPS[name],'triangles':int(len(faces)),'vertices':int(vertices),'compressed_bytes':len(data),
            'sha256':hashlib.sha256(data).hexdigest(),'walk_cells':int(len(grid.reach_col)),'walk_cells_removed':int(grid.clipped),'walk_gap_cells':int(grid.filled),'walk_bytes':len(walk),
            'walk_sha256':hashlib.sha256(walk).hexdigest(),'filled_holes':holes}


if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--input',type=Path,required=True);ap.add_argument('--output',type=Path,required=True)
    ap.add_argument('--maps',nargs='*',default=sorted(MAPS));args=ap.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    rows=[refine(m,args.input/f'{m}.ccmap.gz',args.output) for m in args.maps]
    manifest=args.output/'manifest.json'
    old={r['map']:r for r in json.loads(manifest.read_text())} if manifest.exists() else {}
    for r in rows:old[r['map']]=r
    manifest.write_text(json.dumps([old[k] for k in sorted(old)],ensure_ascii=False,indent=2)+'\n')
