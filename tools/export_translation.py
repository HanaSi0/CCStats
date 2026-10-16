#!/usr/bin/env python3
"""Generate a translation handoff from TermExporter JSON. Does not edit plugin code."""
import csv, hashlib, json, re, shutil, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'PvpStats'
OUT = ROOT / 'translation'
DATA = Path(sys.argv[2]) if len(sys.argv)>2 else None
parsed = json.loads(Path(sys.argv[1]).read_text())
OUT.mkdir(exist_ok=True)
for existing in OUT.glob('*.csv'):
    with existing.open(encoding='utf-8-sig', newline='') as f:
        if any(row.get('zh_CN') for row in csv.DictReader(f)):
            raise SystemExit('Translation CSVs already contain confirmed values. Refusing to overwrite them. Use tools/generate_localization.py to update runtime translations.')

def write(name, rows, fields):
    with (OUT/name).open('w',encoding='utf-8-sig',newline='') as f:
        w=csv.DictWriter(f,fieldnames=fields);w.writeheader();w.writerows(rows)
def key(kind,*parts):
    return kind+'.'+hashlib.sha256('\x1f'.join(map(str,parts)).encode()).hexdigest()[:16]
def loc(row):return f"PvpStats/{row['file']}:{row['line']}"
terms=[]
for row in parsed['strings']:
    f=row['file']; text=row['value']; kind=''; note='仅翻译显示文本，保留原文列。'
    if f=='Helpers/PlayerJobHelper.cs' and row['context']=='FieldDeclaration':
        kind='职业或职能';note='职业英文全名也被 GetJobFromName 用于识别；回填时另设显示翻译，不能覆盖识别字典。'
    elif f=='Helpers/MatchHelper.cs' and row['member'] in ('GetArenaName','GetTeamName') and not row['raw'].startswith('@'):
        kind='地图或队伍'
    elif f=='Helpers/MatchHelper.cs' and row['context']=='FieldDeclaration' and text in ['bronze','silver','gold','platinum','diamond','crystal']:
        kind='段位识别词';note='这是英文输入识别键；填写中文对应词，回填时保留原键。'
    elif f=='Types/Display/Action/ActionSet.cs' and text:
        kind='技能组合';note='源码自定义组合显示名，可能不同于单个 Action 的游戏名称。'
    elif f=='Types/GamePeriod.cs' and text in ('Endwalker','Dawntrail'):
        kind='资料片'
    elif f.startswith('Windows/') and not row['interpolated'] and re.search(r'Crystalline Conflict|Astra|Umbra|Crystal |Overtime|Medicine Kit|Limit Break|Everlasting Flight|Guard|Purify|Recuperate|Elixir',text,re.I):
        if not re.search(r'GetHashCode|###|##',text):kind='战斗或界面名词'
    if kind:
        terms.append(dict(key=key('term',f,row['raw']),category=kind,original=text,zh_CN='',source=loc(row),notes=note))
# Terms displayed from enum identifiers (do not rename the persisted enum itself).
for row in parsed['enums']:
    if row['type'] not in ('DutyType','CrystallineConflictTeamName','CrystallineConflictMatchType','CrystallineConflictMap','ArenaTier','CrystallineConflictMatchEvent','Job','JobRole','JobSubRole'):continue
    terms.append(dict(key=f"enum.{row['type']}.{row['name']}",category='枚举显示名',original=row['name'],zh_CN='',source=loc(row),notes='填写显示翻译；不得改枚举名称、数字值或数据库字段。地图/职业与完整英文名可能重复，保持一致。'))
# Consolidate repeated literals while retaining every source location.
def merge(rows):
    result={}
    for row in rows:
        k=row['key']
        if k in result:result[k]['source']+='; '+row['source']
        else:result[k]=dict(row)
    return list(result.values())
terms=merge(terms)
write('01_game_terms.csv',terms,['key','category','original','zh_CN','source','notes'])
# Fixed game row IDs; English names come from a separately pinned public datamining snapshot.
refs={}
def add(sheet,id,source,symbol='',note=''):
    k=(sheet,int(id));entry=refs.setdefault(k,dict(key=f'{sheet}.{id}',sheet=sheet,row_id=int(id),original='',zh_CN='',symbols=[],source=[],notes=[]))
    if source not in entry['source']:entry['source'].append(source)
    if symbol and symbol not in entry['symbols']:entry['symbols'].append(symbol)
    if note and note not in entry['notes']:entry['notes'].append(note)
for row in parsed['enums']:
    if row['file']!='Types/Action/ActionEnums.cs' or not (row['assigned'] or '').isdigit():continue
    sheet='Action' if row['type'] in ('LimitBreak','KeyAction') else 'Status'
    add(sheet,row['assigned'],loc(row),f"{row['type']}.{row['name']}",'UselessStatus 是采集排除项，原文仅供参考。' if row['type']=='UselessStatus' else '')
p=SRC/'Types/Display/Action/ActionSet.cs'
for i,line in enumerate(p.read_text(encoding='utf-8-sig').splitlines(),1):
    if line.lstrip().startswith('//'):continue
    for m in re.finditer(r'\{\s*(?:(CrystallineConflictMatchTimeline.StatusIdOffset)\s*\+\s*)?(\d+),\s*new\(',line):
        add('Status' if m[1] else 'Action',m[2],f'PvpStats/Types/Display/Action/ActionSet.cs:{i}',note='技能组合引用；StatusIdOffset 是程序内部偏移，row_id 已去除偏移。' if m[1] else '技能组合中的单个技能。')
p=SRC/'Services/LocalizationService.cs'
for i,line in enumerate(p.read_text(encoding='utf-8-sig').splitlines(),1):
    m=re.search(r'AddAddonString\(StringCache, "([^"]+)", (\d+)\)',line)
    if m:add('Addon',m[2],f'PvpStats/Services/LocalizationService.cs:{i}',m[1],'原始游戏 Addon 文本可能包含控制标记，勿修改原文或标记。')
for sheet in ['Action','Status','Addon']:
    path=DATA/f'{sheet}.csv' if DATA else None
    if path and path.exists():
        with path.open(encoding='utf-8-sig',newline='') as f:
            for row in csv.DictReader(f):
                entry=refs.get((sheet,int(row['#'])))
                if entry:entry['original']=row.get('Name',row.get('Text',''))
rows=[]
for _,entry in sorted(refs.items()):
    entry['symbols']='; '.join(entry['symbols']);entry['source']='; '.join(entry['source']);entry['notes']=' '.join(entry['notes'])
    if not entry['original']:entry['notes']+=' 当前参考表没有原文，请按游戏表 ID 核对。'
    rows.append(entry)
write('02_game_data_ids.csv',rows,['key','sheet','row_id','original','zh_CN','symbols','source','notes'])
# UI literals as supplementary material for the coming UI work.
ui=[];audit=[]
for row in parsed['strings']:
    text=row['value'];notes='参考：开发日志、格式串、路径、正则或内部键可能不可翻译。'
    if row['interpolated']:notes='插值表达式：保留 {表达式}、格式说明及 ##/### 后的标识；稍后由代码回填。'
    item=dict(key=key('text',row['file'],row['raw']),original=text,zh_CN='',source=loc(row),context=row['context'],raw_csharp=row['raw'],notes=notes)
    audit.append(item.copy())
    if row['file'].startswith('Windows/') and row['file']!='Windows/DebugWindow.cs' and re.search('[A-Za-z]',text):
        if row['context'] in ('ImRaii.Table','ImRaii.Child','ImRaii.TabBar','ImGui.PushID','Regex.IsMatch'):continue
        item['notes']='界面候选文本；纯内部标识、格式串可留空。'+(' 保留插值表达式和格式说明。' if row['interpolated'] else '')
        ui.append(item)
write('03_ui_text_candidates.csv',merge(ui),['key','original','zh_CN','source','context','raw_csharp','notes'])
write('06_all_string_literals_audit.csv',merge(audit),['key','original','zh_CN','source','context','raw_csharp','notes'])
# All sites that read game sheet names or string buffers, including identity-sensitive strings.
reads=[];snapshot=set()
pattern=re.compile(r'GetExcelSheet|\.NameEnglish|\.Name\??\.(?:ToString|ExtractText)|\.PlaceName|ReadString\(|TranslateDataTableEntry|TranslateRankString|\.HomeWorld\.Value|\.DataCenter\.Value|row\.Singular')
for p in sorted(SRC.rglob('*.cs')):
    if any(part in ('bin','obj') for part in p.relative_to(SRC).parts):continue
    lines=p.read_text(encoding='utf-8-sig').splitlines()
    for i,line in enumerate(lines,1):
        if line.lstrip().startswith('//') or not pattern.search(line):continue
        relative=str(p.relative_to(ROOT));snapshot.add(relative)
        reads.append(dict(key=key('read',relative,i,line.strip()),source=f'{relative}:{i}',code=line.strip(),context='\n'.join(lines[max(0,i-3):i+2]),notes='检查语言参数；角色名/服务器名可能参与身份关联和数据库键，不得直接用中文替换存储值。'))
write('04_runtime_name_reads.csv',reads,['key','source','code','context','notes'])
for relative in ['PvpStats/Helpers/MatchHelper.cs','PvpStats/Helpers/PlayerJobHelper.cs','PvpStats/Types/Action/ActionEnums.cs','PvpStats/Types/Display/Action/ActionSet.cs','PvpStats/Types/Match/MatchEnums.cs','PvpStats/Types/Match/PlayerRank.cs','PvpStats/Types/Player/PlayerJobEnums.cs','PvpStats/Types/Player/PlayerAlias.cs','PvpStats/Types/GamePeriod.cs','PvpStats/Types/Match/Timeline/CrystallineConflictMatchTimeline.cs','PvpStats/Types/Match/CrystallineConflictMatch.cs','PvpStats/Types/Event/ActionEvent.cs','PvpStats/Types/Player/BattleCharaSnapshot.cs','PvpStats/Types/Player/StatusSnapshot.cs']:
    snapshot.add(relative)
for relative in sorted(snapshot):
    dest=OUT/'source_reference'/f'{relative}.txt';dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(ROOT/relative,dest)
write('05_enum_identifiers_reference.csv',[dict(type=r['type'],identifier=r['name'],assigned_value=r['assigned'] or '',source=loc(r),notes='内部标识，仅作参考；不得直接改名或重排数值。') for r in parsed['enums']],['type','identifier','assigned_value','source','notes'])
manifest=dict(upstream_commit='ba41904e9cdd2d3c4cb77800945a1a30c8f88715',game_data_reference_commit='76d22160d687b0e0318071a098ab9eb9d531605f' if DATA else None,game_data_reference_url='https://github.com/xivapi/ffxiv-datamining',counts=dict(game_terms=len(terms),fixed_game_rows=len(rows),ui_candidates=len(merge(ui)),runtime_read_sites=len(reads),source_reference_files=len(snapshot),all_literal_rows=len(merge(audit))),files={})
for p in sorted(OUT.rglob('*')):
    if p.is_file() and p.name!='manifest.json':manifest['files'][str(p.relative_to(OUT))]=hashlib.sha256(p.read_bytes()).hexdigest()
(OUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(manifest['counts'],ensure_ascii=False))
