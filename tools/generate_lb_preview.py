import re,json,pathlib
root=pathlib.Path(__file__).resolve().parents[1]
s=(root/'PvpStats/Analysis/LimitBreakVisuals.cs').read_text()
translations=dict((int(k),v) for k,v in re.findall(r'\{\s*(\d+)\s*,\s*"([^"]*)"\s*\}',(root/'PvpStats/Localization/ChineseText.g.cs').read_text()))
# The generated dictionary uses index initializers in some versions.
translations.update((int(k),v) for k,v in re.findall(r'\[(\d+)u?\]\s*=\s*"([^"]*)"',(root/'PvpStats/Localization/ChineseText.g.cs').read_text()))
jobs={19:'骑士',21:'战士',32:'暗黑骑士',37:'绝枪战士',24:'白魔法师',28:'学者',33:'占星术士',40:'贤者',20:'武僧',22:'龙骑士',30:'忍者',34:'武士',39:'钐镰客',41:'蝰蛇剑士',23:'吟游诗人',31:'机工士',38:'舞者',25:'黑魔法师',27:'召唤师',35:'赤魔法师',42:'绘灵法师'}
roots={19:[29069],21:[29083],32:[29097],37:[29130,29131],24:[29230],28:[41502],33:[29255],40:[29266,29267],20:[29485],22:[29497,29498],30:[29515],34:[29537],39:[29553],41:[39190],23:[29401],31:[29415],38:[29432],25:[29662],27:[29673,29678],35:[41498],42:[39215]}
rules=[]
for line in s.splitlines():
 for m in re.finditer(r'new\((\d+),(\d+),LbShape\.(\w+),LbAnchor\.(\w+),([^)]*)\)',line):
  aid,jid,shape,anchor,tail=m.groups();parts=[x.strip() for x in tail.split(',')];kwargs={x.split(':')[0]:x.split(':')[1] for x in parts if ':' in x};pos=[x for x in parts if ':' not in x]
  rules.append(dict(id=int(aid),job=int(jid),shape=shape,anchor=anchor,size=float(pos[0]),width=float(pos[1]) if len(pos)>1 else 0,benefit=kwargs.get('Benefit')=='true',duration=float(kwargs.get('Duration','1.25')),reference=kwargs.get('Reference')=='true'))
records=[]
for jid,name in jobs.items():
 grouped={}
 for rule in rules:
  if rule['job']==jid:grouped.setdefault(rule['id'],[]).append(rule)
 records.append(dict(id=jid,name=name,roots=roots[jid],actions=[dict(id=k,name=translations.get(k,'技能 '+str(k)),rules=v) for k,v in grouped.items()]))
(root/'docs/previews/lb-catalog.json').write_text(json.dumps(records,ensure_ascii=False,indent=2)+'\n')
(root/'docs/previews/lb-preview.html').write_text((pathlib.Path(__file__).with_name('lb-preview.template.html').read_text()).replace('__CATALOG__',json.dumps(records,ensure_ascii=False)))
print(len(records),'jobs',len(rules),'rules',len(translations),'translations')
