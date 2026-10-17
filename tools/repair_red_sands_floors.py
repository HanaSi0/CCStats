"""Reconstruct the two missing display floors from surrounding exported surface footprints.
This is visual reconstruction, not recovered game collision/navigation data.
Requires numpy, Pillow, scipy. Input/output are geometry.npz plus report.json.
"""
from pathlib import Path
import argparse,json
import numpy as np
from PIL import Image,ImageDraw
from scipy.ndimage import binary_closing,binary_fill_holes,binary_dilation,label
p=argparse.ArgumentParser();p.add_argument('--input',type=Path,required=True);p.add_argument('--output',type=Path,required=True);args=p.parse_args()
d={k:v for k,v in np.load(args.input/'geometry.npz').items()};tri=d['triangles'];colors=d['colors'];alpha=d.get('alphas',np.full(len(tri),255,dtype='u1'))
n=np.cross(tri[:,1]-tri[:,0],tri[:,2]-tri[:,0]);length=np.linalg.norm(n,axis=1);cy=tri[:,:,1].mean(1)
ground=(alpha==255)&(abs(n[:,1])>.7*length)&(length>1e-8)&(cy>-4)&(cy<4)
step=.2;lo=np.array([-110.,-90.]);size=(1101,901)
im=Image.new('1',size);draw=ImageDraw.Draw(im)
for t in tri[ground]:draw.polygon([tuple(q) for q in (t[:,[0,2]]-lo)/step],fill=1)
raw_mask=np.array(im,dtype=bool);mask=binary_closing(raw_mask,iterations=3);holes=binary_fill_holes(mask)&~mask;labels,count=label(holes)
patches=[];patch_colors=[];repairs=[]
for cx in [-60.,60.]:
    iz,ix=np.rint((np.array([cx,0])-lo)/step).astype(int)[::-1]
    selected=labels[iz,ix];assert selected!=0,'Missing circle must be an enclosed source-floor hole, not open map exterior.'
    zz,xx=np.indices(mask.shape);radius2=(lo[0]+xx*step-cx)**2+(lo[1]+zz*step)**2
    # Include smaller disconnected gaps divided off by rails inside the same circular floor.
    hole=(~raw_mask)&(radius2<=30**2);area=float(hole.sum()*step*step);assert 500<area<2500
    expanded=binary_dilation(hole,iterations=3)&(radius2<=30.6**2)
    points=tri[ground].reshape(-1,3);r=np.linalg.norm(points[:,[0,2]]-[cx,0],axis=1)
    rim=points[(r>24)&(r<31)&(points[:,1]>-2)&(points[:,1]<.3)]
    assert len(rim)>10
    y=float(np.median(rim[:,1])-.08)
    ct=tri.mean(1);distance=np.linalg.norm(ct[:,[0,2]]-[cx,0],axis=1)
    ring=ground&(distance>24)&(distance<32)&(cy<.3)
    nearby=colors[ring];earth=nearby[(nearby[:,0]>nearby[:,1]*1.05)&(nearby[:,1]>nearby[:,2]*1.05)]
    color=np.median(earth if len(earth)>10 else nearby,axis=0)
    zs,xs=np.where(expanded)
    # Coarse 1m cells overlap the existing rim slightly, preventing tiny raster seams.
    cells=np.unique(np.column_stack([xs//5,zs//5]),axis=0)
    out=[]
    for x,z in cells:
        x0=lo[0]+x;x1=x0+1;z0=lo[1]+z;z1=z0+1
        a=np.array([x0,y,z0]);b=np.array([x1,y,z0]);c=np.array([x1,y,z1]);e=np.array([x0,y,z1])
        out.extend([[a,c,b],[a,e,c]])
    patch=np.array(out,dtype='f4');patches.append(patch);patch_colors.append(np.tile(color,(len(patch),1)))
    repairs.append({'center':[cx,0],'source_hole_area':round(area,2),'display_radius':30,'display_height':y,'triangles':len(patch),'method':'Existing near-ground surface footprint, enclosed-hole detection, rim-height median and adjacent source colors. Display reconstruction only.'})
all_patches=np.concatenate(patches);all_colors=np.concatenate(patch_colors)
args.output.mkdir(parents=True,exist_ok=True)
np.savez_compressed(args.output/'geometry.npz',triangles=np.concatenate([tri,all_patches]),colors=np.concatenate([colors,all_colors]),alphas=np.concatenate([alpha,np.full(len(all_patches),255,dtype='u1')]))
report=json.loads((args.input/'report.json').read_text());report['display_floor_reconstructions']=repairs
(args.output/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(repairs,ensure_ascii=False,indent=2))
