"""Render all actual mechanic exports in Chromium and regenerate their screenshots.
Requires playwright, numpy, Pillow, Chromium and Noto Sans CJK.
"""
from pathlib import Path
import json,hashlib
import numpy as np
from PIL import Image,ImageDraw,ImageFont
from playwright.sync_api import sync_playwright
root=Path(__file__).resolve().parents[1];out=root/'docs/previews/map-mechanics';out.mkdir(exist_ok=True)
results=[]
with sync_playwright() as p:
 browser=p.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox','--use-angle=swiftshader','--enable-unsafe-swiftshader'])
 page=browser.new_page(viewport={'width':1280,'height':960});errors=[];page.on('pageerror',lambda e:errors.append(str(e)));page.set_content((root/'docs/previews/map-mechanics.html').read_text());page.wait_for_function('window.__ready || window.__error');assert page.evaluate('window.__ready'),page.evaluate('window.__error');page.evaluate('window.__freeze=true');page.wait_for_timeout(80)
 items=page.evaluate('window.__items.map(i=>({kind:i.kind,name:i.name}))')
 for i,item in enumerate(items):
  page.evaluate('(i)=>{window.__select(i);window.__draw(window.__items[i].kind==="Onmyoji"?1500:375)}',i);p1=out/(item['kind']+'.png');page.locator('canvas').screenshot(path=str(p1));im=np.asarray(Image.open(p1).convert('RGB'));assert len(np.unique(im.reshape(-1,3),axis=0))>5,item
  a=hashlib.sha256(p1.read_bytes()).hexdigest();page.evaluate('window.__draw(window.__items['+str(i)+'].kind==="Onmyoji"?3500:875)');p2=out/(item['kind']+'-motion.png');page.locator('canvas').screenshot(path=str(p2));b=hashlib.sha256(p2.read_bytes()).hexdigest();p2.unlink();results.append(item|{'render':True,'motion':a!=b,'sha256':a});print(item['kind'],item['name'],a!=b,flush=True)
 assert not errors,errors;browser.close()
font=ImageFont.truetype('/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc',23)
cardw,cardh=360,245;sheet=Image.new('RGB',(cardw*4,cardh*6),'#0b1220');draw=ImageDraw.Draw(sheet)
for i,item in enumerate(items):
 im=Image.open(out/(item['kind']+'.png')).convert('RGB');im.thumbnail((350,203));x=(i%4)*cardw+(cardw-im.width)//2;y=(i//4)*cardh;sheet.paste(im,(x,y+35));draw.text(((i%4)*cardw+15,y+3),item['name'],font=font,fill='#e6efff')
sheet.save(out/'all-mechanics.png');(root/'docs/research/mechanic-gallery-verification.json').write_text(json.dumps(results,ensure_ascii=False,indent=2)+'\n');print('Verified',len(results),'mechanic styles')
