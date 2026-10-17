"""Walkable display-surface grid for 3D replay decals.

Derived only from the packaged display mesh, never from game collision or navigation data.
Opaque triangles are sampled into vertical height spans on a 0.5 m XZ grid. A floor is the
gently sloped top of a span with standing clearance above it. Floors connected to the
arena centre by small steps are kept; wall tops, rock tops, canyon floors and decorative
roofs fall out because no small step reaches them.

Runtime format (gzip, little endian):
  'CCW1', int32 width, int32 depth, float32 originX, float32 originZ, float32 cell,
  uint8 layerCount[width*depth]  (cell index = z*width+x),
  float32 height[sum(layerCount)] (ascending per cell, cells in index order).
"""
import gzip,struct
import numpy as np
from scipy import ndimage

CELL=0.5          # grid spacing in game units (yalms)
SPACING=0.15      # surface sample spacing
GAP=0.35          # vertical gap that separates two spans in one column
CLEARANCE=1.8     # free height required above a floor
CLIMB=0.6         # largest step between neighbouring floor cells
SLOPE=0.82        # minimum |normal.y| of a standable surface (about 35 degrees)
# Flood seeds per map: arena centre (crystal start). Bayside players can drop onto the sand.
SEEDS={'bayside':[(100,0,100),(70,-10,100)],'harmonias':[(100,0,100)]}


def sample(tris,s=SPACING):
    """Barycentric lattice samples. The corner whose two edges are shortest anchors the lattice."""
    n=len(tris);idx=np.arange(n)
    e=[np.linalg.norm(tris[:,(k+1)%3]-tris[:,k],axis=1) for k in range(3)]
    k=np.stack([e[0]*e[2],e[1]*e[0],e[2]*e[1]],1).argmin(1)
    A=tris[idx,k];B=tris[idx,(k+1)%3];C=tris[idx,(k+2)%3]
    ku=np.minimum(np.ceil(np.linalg.norm(B-A,axis=1)/s).astype(np.int64)+1,4000)
    kv=np.minimum(np.ceil(np.linalg.norm(C-A,axis=1)/s).astype(np.int64)+1,4000)
    cnt=(ku+1)*(kv+1)
    tid=np.repeat(idx,cnt);off=np.arange(cnt.sum())-np.repeat(np.cumsum(cnt)-cnt,cnt)
    u=(off//(kv[tid]+1))/ku[tid];v=(off%(kv[tid]+1))/kv[tid]
    keep=u+v<=1+1e-9;tid,u,v=tid[keep],u[keep],v[keep]
    return A[tid]+(B[tid]-A[tid])*u[:,None]+(C[tid]-A[tid])*v[:,None],tid


class WalkGrid:
    def __init__(self,tris,lo,hi,cell=CELL):
        tris=np.asarray(tris,dtype=np.float64)
        normal=np.cross(tris[:,1]-tris[:,0],tris[:,2]-tris[:,0]);length=np.linalg.norm(normal,axis=1)
        valid=length>1e-9;tris=tris[valid];flat=np.abs(normal[valid,1])>=SLOPE*length[valid]
        self.cell=cell
        self.lo=np.floor(np.asarray(lo,dtype=float)/cell)*cell-cell
        self.W=int(np.ceil((hi[0]-self.lo[0])/cell))+2;self.D=int(np.ceil((hi[1]-self.lo[1])/cell))+2
        cols=[];ys=[];fl=[]
        for s in range(0,len(tris),40000):
            p,t=sample(tris[s:s+40000])
            cx=np.floor((p[:,0]-self.lo[0])/cell).astype(np.int64);cz=np.floor((p[:,2]-self.lo[1])/cell).astype(np.int64)
            ok=(cx>=0)&(cx<self.W)&(cz>=0)&(cz<self.D)
            cols.append(cz[ok]*self.W+cx[ok]);ys.append(p[ok,1]);fl.append(flat[s:s+40000][t[ok]])
        col=np.concatenate(cols);y=np.concatenate(ys);f=np.concatenate(fl)
        o=np.lexsort((y,col));col,y,f=col[o],y[o],f[o]
        brk=np.ones(len(col),bool);brk[1:]=(col[1:]!=col[:-1])|(y[1:]-y[:-1]>GAP)
        first=np.flatnonzero(brk);sid=np.cumsum(brk)-1;n=len(first)
        bottom=y[first];top=np.maximum.reduceat(y,first);scol=col[first]
        flattop=np.maximum.reduceat(np.where(f,y,-1e9),first)>=top-0.12
        near=f&(y>=top[sid]-0.6)
        count=np.bincount(sid,weights=near,minlength=n);total=np.bincount(sid,weights=np.where(near,y,0),minlength=n)
        height=np.where(count>0,total/np.maximum(count,1),top)
        above=np.full(n,np.inf);same=scol[1:]==scol[:-1];above[:-1][same]=bottom[1:][same]
        floor=flattop&(above-top>=CLEARANCE)
        self.occupied=np.zeros(self.W*self.D,bool);self.occupied[scol]=True
        self.col_top=np.full(self.W*self.D,-np.inf);np.maximum.at(self.col_top,scol,top)
        self.f_col=scol[floor];self.f_h=height[floor]
        self.reach_col=np.zeros(0,np.int64);self.reach_h=np.zeros(0)

    def cell_of(self,x,z):return int((z-self.lo[1])//self.cell)*self.W+int((x-self.lo[0])//self.cell)

    def flood(self,seeds,climb=CLIMB):
        W=self.W;o=np.argsort(self.f_col,kind='stable');fc=self.f_col[o];fh=self.f_h[o]
        start=np.searchsorted(fc,np.arange(W*self.D+1))
        reach=np.zeros(len(fc),bool);stack=[]
        for x,y,z in seeds:
            cx=int((x-self.lo[0])//self.cell);cz=int((z-self.lo[1])//self.cell);best=None
            for r in range(24):
                for dz in range(-r,r+1):
                    for dx in range(-r,r+1):
                        if max(abs(dx),abs(dz))!=r:continue
                        c=(cz+dz)*W+cx+dx
                        if c<0 or c>=W*self.D:continue
                        for k in range(start[c],start[c+1]):
                            d=abs(fh[k]-y)+np.hypot(dx,dz)*self.cell
                            if best is None or d<best[0]:best=(d,k)
                if best:break
            if best and not reach[best[1]]:reach[best[1]]=True;stack.append(best[1])
        while stack:
            k=stack.pop();c=fc[k];x=c%W;z=c//W
            for nx,nz in ((x+1,z),(x-1,z),(x,z+1),(x,z-1)):
                if nx<0 or nz<0 or nx>=W or nz>=self.D:continue
                nc=nz*W+nx
                for m in range(start[nc],start[nc+1]):
                    if not reach[m] and abs(fh[m]-fh[k])<=climb:reach[m]=True;stack.append(m)
        self.reach_col=fc[reach];self.reach_h=fh[reach]
        return int(reach.sum())

    def enclosed_holes(self,max_area=900.0):
        """Empty columns fully enclosed by a floor of consistent height (missing display floor)."""
        occ=self.occupied.reshape(self.D,self.W);lab,n=ndimage.label(~occ)
        border=set(np.unique(np.concatenate([lab[0],lab[-1],lab[:,0],lab[:,-1]])).tolist())
        top=np.full(self.W*self.D,np.nan);o=np.argsort(self.f_h);top[self.f_col[o]]=self.f_h[o];top=top.reshape(self.D,self.W)
        sizes=ndimage.sum(np.ones_like(lab),lab,range(n+1));holes=[]
        for k in range(1,n+1):
            area=sizes[k]*self.cell**2
            if k in border or area>max_area or area<0.2:continue
            m=lab==k;ring=ndimage.binary_dilation(m)&~m;h=top[ring]
            if np.isfinite(h).mean()<0.75:continue
            h=h[np.isfinite(h)];base=float(np.percentile(h,25))
            if np.mean(np.abs(h-base)<=0.3)<0.5 or h.max()-h.min()>1.2:continue
            holes.append({'mask':m,'ring':ndimage.binary_dilation(m,iterations=3)&~m,'height':base,'area':float(area)})
        return holes

    def write(self,path):
        o=np.lexsort((self.reach_h,self.reach_col));col=self.reach_col[o];h=self.reach_h[o].astype('<f4')
        counts=np.bincount(col,minlength=self.W*self.D)
        assert counts.max()<=255
        raw=struct.pack('<4siifff',b'CCW1',self.W,self.D,float(self.lo[0]),float(self.lo[1]),self.cell)+counts.astype('u1').tobytes()+h.tobytes()
        data=gzip.compress(raw,compresslevel=9,mtime=0);open(path,'wb').write(data);return data


def build(tris,map_key,lo,hi):
    grid=WalkGrid(tris,lo,hi);grid.flood(SEEDS.get(map_key,[(0,0,0)]));return grid
