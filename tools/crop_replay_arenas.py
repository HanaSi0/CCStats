"""Crop display meshes to the official arena silhouette plus a wall margin.
Restores previously box-clipped regions from original GLBs before cropping.
Use --rebuild-source-maps when earlier simplification also removed terrain inside the old bounds. Color, height and alpha
are interpolated at cut edges. Display footprints are not game navigation or collision data.
"""
from pathlib import Path
import argparse,gzip,hashlib,json,re,struct,sys,time
import numpy as np
from PIL import Image
from scipy import ndimage
import refine_replay_maps as mesh
import replay_walkable as walk
from glb_scene import load as load_glb

ROOT=Path(__file__).resolve().parents[1]
SOURCE_DIRS={'palaistra':'.','volcanic-heart':'map-185315','cloud-nine':'map-185648','castletown':'map-185833','red-sands':'map-190003','bayside':'map-190404','harmonias':'map-190721'}
DETAIL=re.compile(r'_(?:plt|tre|flg|lmp|lig|flw|fsh|ppr|pfl)\d+\.mdl|w_itm_')
DTYPE=np.dtype([('p','<f4',(3,)),('c','<u4')])

def attributes(p,c,f):
    colors=np.stack([c&255,(c>>8)&255,(c>>16)&255,c>>24],1).astype(np.float32)
    return np.concatenate([p[f].astype(np.float32),colors[f]],axis=2)

def plane(poly,axis,bound,sign):
    out=[]
    for a,b in zip(poly,np.roll(poly,-1,axis=0)):
        da=(a[axis]-bound)*sign;db=(b[axis]-bound)*sign
        if da>=0:out.append(a)
        if (da>=0)!=(db>=0):out.append(a+(b-a)*(da/(da-db)))
    return np.array(out,dtype=np.float32).reshape(-1,7)

def triangles(poly):
    if len(poly)<3:return []
    out=[]
    for j in range(1,len(poly)-1):
        t=poly[[0,j,j+1]]
        if np.linalg.norm(np.cross(t[1,:3]-t[0,:3],t[2,:3]-t[0,:3]))>1e-7:out.append(t)
    return out

class Boundary:
    def __init__(self,key,margin):
        raw=walk.official_silhouette(key)
        if raw is None:raise ValueError('Official map missing: '+key)
        lab,n=ndimage.label(raw);sizes=np.bincount(lab.ravel());raw&=(sizes[lab]>=64)
        # Work on 0.5-unit cells. Each cell conservatively includes either of its 2x2 official pixels.
        allowed=ndimage.distance_transform_edt(~raw,sampling=.25)<=margin
        self.mask=allowed.reshape(1024,2,1024,2).any(axis=(1,3))
        offset=walk.MAP_OFFSET.get(key,0);self.origin=np.array([-256-offset,-256-offset]);self.cell=.5
        self.prefix=np.pad(self.mask.astype(np.int32),((1,0),(1,0))).cumsum(0).cumsum(1)
        self.runs=[]
        for row in self.mask:
            changes=np.flatnonzero(np.diff(np.r_[False,row,False].astype(np.int8)))
            self.runs.append(list(zip(changes[::2],changes[1::2])))
    def classify(self,ts):
        lo=ts[:,:,:3].min(1);hi=ts[:,:,:3].max(1)
        q0=np.floor((lo[:,[0,2]]-self.origin)/self.cell).astype(int);q1=np.floor((hi[:,[0,2]]-self.origin)/self.cell).astype(int)+1
        a=np.clip(q0,0,1024);b=np.clip(q1,0,1024);p=self.prefix
        count=p[b[:,1],b[:,0]]-p[a[:,1],b[:,0]]-p[b[:,1],a[:,0]]+p[a[:,1],a[:,0]]
        all_in=(q0>=0).all(1)&(q1<=1024).all(1)&(count==np.prod(q1-q0,axis=1))
        return count,all_in
    def clip(self,poly):
        lo=poly[:,:3].min(0);hi=poly[:,:3].max(0)
        a=np.clip(np.floor((lo[[0,2]]-self.origin)/self.cell).astype(int),0,1023)
        b=np.clip(np.floor((hi[[0,2]]-self.origin)/self.cell).astype(int)+1,0,1024)
        out=[]
        for z in range(a[1],b[1]):
            for start,end in self.runs[z]:
                if end<a[0] or start>=b[0]:continue
                p=plane(poly,2,self.origin[1]+z*self.cell,1)
                p=plane(p,2,self.origin[1]+(z+1)*self.cell,-1) if len(p)>=3 else p
                p=plane(p,0,self.origin[0]+start*self.cell,1) if len(p)>=3 else p
                p=plane(p,0,self.origin[0]+end*self.cell,-1) if len(p)>=3 else p
                out.extend(triangles(p))
        return out
    def batch(self,ts):
        if not len(ts):return np.empty((0,3,7),np.float32)
        count,inside=self.classify(ts);parts=[]
        if inside.any():parts.append(ts[inside])
        out=[]
        for poly in ts[(count>0)&~inside]:out.extend(self.clip(poly))
        if out:parts.append(np.array(out,dtype=np.float32))
        return np.concatenate(parts) if parts else np.empty((0,3,7),np.float32)

def outside_box(poly,lo,hi):
    # Disjoint complement of the old XZ box; prevents duplicate faces where the current model already exists.
    out=[]
    for axis,bound,sign in [(0,lo[0],-1),(0,hi[0],1)]:out.extend(triangles(plane(poly,axis,bound,sign)))
    middle=plane(plane(poly,0,lo[0],1),0,hi[0],-1)
    if len(middle)>=3:
        for bound,sign in [(lo[2],-1),(hi[2],1)]:out.extend(triangles(plane(middle,2,bound,sign)))
    return out

def additions(key,source_root,boundary,oldlo,oldhi,full_source=False):
    directory=source_root/SOURCE_DIRS[key]/'extracted';folders=list(directory.glob('InstanceExport-*'))
    if len(folders)!=1:raise ValueError('Need original GLBs in '+str(directory))
    folder=folders[0];out=[];texture_cache={};used=[]
    for src in sorted(folder.glob('*.glb')):
        for batch in load_glb(src)[1]:
            path='/'.join(batch['path']);material=batch['material'];extras=material.get('extras',{});shader=extras.get('ShaderPackage','').lower()
            if not batch['visible'] or DETAIL.search(path) or any(word in shader for word in ['fog','water','sky','lightshaft']):continue
            ts=batch['points'][batch['faces']].astype(np.float32);lo=ts.min(1);hi=ts.max(1)
            keep=(lo[:,0]<oldlo[0]-1e-4)|(hi[:,0]>oldhi[0]+1e-4)|(lo[:,2]<oldlo[2]-1e-4)|(hi[:,2]>oldhi[2]+1e-4)
            if full_source:keep=np.ones(len(ts),bool)
            keep&=(hi[:,1]>=oldlo[1]-1)&(lo[:,1]<=oldhi[1]+1)
            ts=ts[keep]
            if not len(ts):continue
            texture=extras.get('g_SamplerColorMap0_PngCachePath','')
            if texture not in texture_cache:
                file=folder/'cache'/texture.replace('\\','/')
                if texture and file.is_file():
                    im=Image.open(file).convert('RGB');im.thumbnail((32,32));base=np.asarray(im).reshape(-1,3).mean(0)/255
                else:base=np.array([.55,.60,.64])
                texture_cache[texture]=np.clip(base,.2,.88)
            n=np.cross(ts[:,1]-ts[:,0],ts[:,2]-ts[:,0]);length=np.linalg.norm(n,axis=1);valid=length>1e-8;ts=ts[valid];n=n[valid]/length[valid,None]
            rgb=np.clip(texture_cache[texture][None,:]*(.5+.5*abs(n@np.array([.3,.88,.36])))[:,None],0,1)*255
            colors=np.concatenate([rgb,np.full((len(ts),1),255)],1)
            attr=np.concatenate([ts,np.repeat(colors[:,None,:],3,axis=1)],axis=2).astype(np.float32)
            count,_=boundary.classify(attr);attr=attr[count>0]
            new=[]
            for poly in attr:
                poly=plane(poly,1,oldlo[1]-1,1)
                if len(poly)>=3:poly=plane(poly,1,oldhi[1]+1,-1)
                if len(poly)>=3:new.extend(triangles(poly) if full_source else outside_box(poly,oldlo,oldhi))
            cropped=boundary.batch(np.array(new,np.float32)) if new else np.empty((0,3,7),np.float32)
            if len(cropped):out.append(cropped);used.append({'source':src.name,'resource':path,'triangles':len(cropped)})
    if full_source:
        summary={}
        for row in used:
            k=(row['source'],row['resource'])
            if k not in summary:summary[k]=dict(source=k[0],resource=k[1],triangles=0,instances=0)
            summary[k]['triangles']+=row['triangles'];summary[k]['instances']+=1
        used=list(summary.values())
    return (np.concatenate(out) if out else np.empty((0,3,7),np.float32)),used

def write(path,ts):
    p=np.ascontiguousarray(ts[:,:,:3].reshape(-1,3),dtype='<f4');c=np.clip(np.rint(ts[:,:,3:].reshape(-1,4)),0,255).astype(np.uint32)
    colors=c[:,0]|(c[:,1]<<8)|(c[:,2]<<16)|(c[:,3]<<24)
    packed=np.empty(len(p),DTYPE);packed['p']=p;packed['c']=colors
    vertices,inv=np.unique(packed,return_inverse=True);faces=inv.reshape(-1,3).astype('<u4')
    # Remove only exact duplicated geometry+color faces, including original/export seam duplicates.
    _,first=np.unique(np.sort(faces,1),axis=0,return_index=True);faces=faces[np.sort(first)]
    data=gzip.compress(struct.pack('<4sii',b'CCM4',len(vertices),faces.size)+vertices.tobytes()+faces.tobytes(),compresslevel=9,mtime=0);path.write_bytes(data)
    return data,vertices,faces

def main():
    ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--input',type=Path,required=True);ap.add_argument('--output',type=Path,required=True);ap.add_argument('--originals-root',type=Path,required=True);ap.add_argument('--margin',type=float,default=3);ap.add_argument('--rebuild-source-maps',nargs='*',default=[],help='Replace whole maps from original geometry, including holes inside previous bounds');ap.add_argument('--maps',nargs='*',default=sorted(mesh.MAPS));args=ap.parse_args()
    args.output.mkdir(parents=True,exist_ok=True);reports=[];report_path=args.output/'crop-report.json'
    saved_reports={r['map']:r for r in json.loads(report_path.read_text())} if report_path.exists() else {}
    existing={r['map']:r for r in json.loads((args.input/'manifest.json').read_text())}
    for key in args.maps:
        start=time.time();print(key+': cropping current model...',flush=True);p,c,f=mesh.read(args.input/(key+'.ccmap.gz'));oldlo=p.min(0);oldhi=p.max(0);boundary=Boundary(key,args.margin)
        attr=attributes(p,c,f)
        # The low desert fog plane is scenery, not a walkable/interactive surface.
        fog=((c[f]>>24)==224).all(1) if key=='red-sands' else np.zeros(len(f),bool)
        full_source=key in args.rebuild_source_maps
        cropped=np.empty((0,3,7),np.float32) if full_source else boundary.batch(attr[~fog]);print(key+(': rebuilding all core source geometry...' if full_source else ': restoring source regions beyond old bounds...'),flush=True)
        extra,sources=additions(key,args.originals_root,boundary,oldlo,oldhi,full_source=full_source);ts=np.concatenate([cropped,extra]);data,v,faces=write(args.output/(key+'.ccmap.gz'),ts)
        opaque=(v['c'][faces]>>24).min(1)==255;tris=v['p'][faces[opaque]].astype(np.float64);lo=v['p'].min(0)[[0,2]];hi=v['p'].max(0)[[0,2]]
        print(key+': rebuilding standable display grid...',flush=True);grid=walk.build(tris,key,lo,hi);walkdata=grid.write(args.output/(key+'.ccwalk.gz'))
        report=existing[key]|{'triangles':len(faces),'vertices':len(v),'compressed_bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'walk_cells':len(grid.reach_col),'walk_cells_removed':grid.clipped,'walk_gap_cells':grid.filled,'walk_bytes':len(walkdata),'walk_sha256':hashlib.sha256(walkdata).hexdigest(),'crop_margin':args.margin,'crop_cell':.5,'before_triangles':len(f),'restored_source_triangles':len(extra),'source_restorations':sources,'bounds':[v['p'].min(0).tolist(),v['p'].max(0).tolist()]}
        report['full_source_rebuild']=full_source
        if full_source:report['filled_holes']=[] # Previous generated patches were replaced by original source surfaces.
        report['removed_fog_triangles']=int(fog.sum());report['official_png_sha256']=hashlib.sha256((walk.OFFICIAL_MAPS/(key+'.png')).read_bytes()).hexdigest()
        reports.append(report);saved_reports[key]=report;report_path.write_text(json.dumps([saved_reports[k] for k in sorted(saved_reports)],ensure_ascii=False,indent=2)+'\n')
        print(f'{key}: {len(f):,} -> {len(faces):,} faces; restored {len(extra):,}; {len(data)+len(walkdata):,} bytes; {time.time()-start:.1f}s',flush=True)
    rows={r['map']:r for r in json.loads((args.output/'manifest.json').read_text())} if (args.output/'manifest.json').exists() else {}
    for r in reports:rows[r['map']]={k:v for k,v in r.items() if k!='source_restorations'}
    (args.output/'manifest.json').write_text(json.dumps([rows[k] for k in sorted(rows)],ensure_ascii=False,indent=2)+'\n')
if __name__=='__main__':main()
