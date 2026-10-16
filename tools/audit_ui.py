#!/usr/bin/env python3
"""Review UI strings from TermExporter JSON without changing gameplay identifiers.
Usage: dotnet run --project tools/TermExporter -- PvpStats /tmp/ui.json
       python3 tools/audit_ui.py /tmp/ui.json
"""
import csv,json,re,sys
from pathlib import Path
root=Path(__file__).resolve().parents[1]
translations=json.loads((root/'translation/ui-layout.zh-CN.json').read_text())
for filename in ['01_game_terms.csv','03_ui_text_candidates.csv']:
    for row in csv.DictReader((root/'translation'/filename).open(encoding='utf-8-sig')):
        if row['zh_CN']:translations[row['original']]=row['zh_CN']
# Reviewed table columns that never call TableHeadersRow; retain their IDs.
internal={'Button','name','homeworld','team1','team2','rankteam1','rankteam2','playerteam1','playerteam2','jobteam1','jobteam2','time','text','player','value','medkit','description','rate','filterName','filters','title','examine','c1','c2','c3','c4'}
issues=[];reviewed=0
for row in json.loads(Path(sys.argv[1]).read_text())['strings']:
    if not(row['file'].startswith('Windows/') or row['file']=='Helpers/ImGuiHelper.cs') or row['file'].endswith('DebugWindow.cs'):continue
    context=row['context']
    if not re.search(r'ImGui\.(?:Text|Button|SmallButton|Checkbox|Selectable|SetTooltip|TableSetupColumn|InputText|InputInt|Slider|MenuItem|Combo)|HelpMarker|WrappedTooltip|ModernTheme\.(?:Button|SmallButton)',context):continue
    reviewed+=1
    value=row['value']
    if row['interpolated']:
        value=re.sub(r'^\$@?"|"$','',value)
        value=re.sub(r'\{[^{}]*\}','',value)
    value=value.split('##')[0]
    if not re.search('[A-Za-z]{2,}',value) or re.search('[\u4e00-\u9fff]',value):continue
    if context=='ImGui.TableSetupColumn' and value in internal:continue
    if value.startswith('ID:') or value in translations or re.fullmatch(r'[NPF]\d|%\.\d[fd]',value):continue
    if re.search(r'FontAwesomeIcon|ToString\(|string.Format|ShowPercentages',value):continue
    issues.append({'source':f"{row['file']}:{row['line']}",'context':context,'text':value})
print(json.dumps({'reviewed':reviewed,'unmapped_candidates':issues},ensure_ascii=False,indent=2))
raise SystemExit(bool(issues))
