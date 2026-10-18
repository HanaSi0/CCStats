"""Export the actual renderer gallery eye phases and animated preview.
Run generate_mechanic_preview.py first. Requires Playwright, Pillow and Chromium.
"""
from pathlib import Path
from io import BytesIO
from PIL import Image,ImageDraw,ImageFont
from playwright.sync_api import sync_playwright
root=Path(__file__).resolve().parents[1];out=root/'docs/previews/map-mechanics'
with sync_playwright() as p:
 browser=p.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox','--use-angle=swiftshader','--enable-unsafe-swiftshader'])
 page=browser.new_page(viewport={'width':1280,'height':960});page.set_content((root/'docs/previews/map-mechanics.html').read_text());page.wait_for_function('window.__ready || window.__error');assert page.evaluate('window.__ready'),page.evaluate('window.__error');page.evaluate('window.__freeze=true');page.wait_for_timeout(80);page.evaluate("window.__select(window.__items.findIndex(i=>i.kind==='Onmyoji'))")
 frames=[]
 for i in range(32):
  page.evaluate('(t)=>window.__draw(t)',i*125);frames.append(Image.open(BytesIO(page.locator('canvas').screenshot())).convert('RGB'))
 frames[0].save(out/'Onmyoji-closed.png');frames[9].save(out/'Onmyoji-opening.png');frames[12].save(out/'Onmyoji-open.png');frames[0].save(out/'Onmyoji-cycle.gif',save_all=True,append_images=frames[1:],duration=125,loop=0)
 browser.close()
font=ImageFont.truetype('/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc',27)
sheet=Image.new('RGB',(1200,280),'#0b1220');d=ImageDraw.Draw(sheet)
for j,(i,label) in enumerate([(0,'移动 / 待机：闭眼'),(9,'本体读条：睁眼'),(12,'施放：睁开'),(24,'结算 / 中断：闭合')]):
 im=frames[i].copy();im.thumbnail((296,220));sheet.paste(im,(j*300+(300-im.width)//2,50));d.text((j*300+12,10),label,font=font,fill='#dceaff')
sheet.save(out/'Onmyoji-phases.png');print('Eye phases and 4 second animation exported')
