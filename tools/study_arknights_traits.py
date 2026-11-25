"""Export the previous Stronghold season's native unit traits for an actual design review."""
import collections, hashlib, json, re, statistics
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
SOURCE=Path('F:/codex/2026-10-25/kan/work/pc-inspection/decoded-tables/activity_table858cbc.json')
NAMES=Path('F:/codex/2026-10-25/kan/work/stronghold-20261109/voice-research/character_tablec913b6.json')
def indexed(rows):return {r['key']:r['value'] for r in rows}
def plain(text):
    text=re.sub(r'<@[^>]+>|</>', '', text)
    return re.sub(r'<([^>]+)>',r'〈\1〉',text).replace('\\n','\n')
native=json.loads(SOURCE.read_bytes())
season=indexed(native['activity']['AUTOCHESS_SEASON'])['act2autochess']
characters=indexed(json.loads(NAMES.read_bytes())['characters'])
chess=indexed(season['charChessDataDict']);traits=indexed(season['garrisonDataDict']);bonds=indexed(season['bondInfoDict'])
rows=[];missing=[]
for card_id,shop in sorted(indexed(season['charShopChessDatas']).items(),key=lambda p:(p[1].get('chessLevel',0),p[1].get('shopLevelSortId',0),p[0])):
    char_id=shop.get('charId');name=characters.get(char_id,{}).get('name',char_id or card_id)
    if char_id not in characters:missing.append(char_id)
    row=dict(chessId=card_id,charId=char_id,name=name,tier=shop.get('chessLevel'),normal={},elite={})
    for key,variant in [('normal',card_id),('elite',shop['goldenChessId'])]:
        unit=chess[variant];ids=unit.get('garrisonIds',[])
        entries=[dict(id=i,text=plain(traits[i].get('garrisonDesc',traits[i].get('description',''))),raw=traits[i]) for i in ids]
        row[key]=dict(chessId=variant,bonds=[bonds.get(b,{}).get('name',b) for b in unit.get('bondIds',[])],traitCount=len(ids),traits=entries,cardDescription=plain(unit.get('desc','')),textSource='garrisonDataDict.garrisonDesc')
    rows.append(row)
counts=collections.Counter(r['normal']['traitCount'] for r in rows)
lengths=[len(t['text']) for r in rows for t in r['normal']['traits']]
stats=dict(presetEntries=len(rows),internalGarrisonCountDistribution=dict(sorted(counts.items())),internalEliteGarrisonCountDistribution=dict(sorted(collections.Counter(r['elite']['traitCount'] for r in rows).items())),individualDescriptionLength=dict(min=min(lengths),median=statistics.median(lengths),max=max(lengths)),events=dict(collections.Counter(t['raw'].get('eventType','') for r in rows for t in r['normal']['traits'])),effects=dict(collections.Counter(t['raw'].get('effectType','') for r in rows for t in r['normal']['traits'])),unresolvedCharacterIds=missing)
output=ROOT/'audits/arknights-trait-study';output.mkdir(exist_ok=True)
report=dict(source=str(SOURCE),sourceSha256=hashlib.sha256(SOURCE.read_bytes()).hexdigest(),nameSource=str(NAMES),nameSourceSha256=hashlib.sha256(NAMES.read_bytes()).hexdigest(),activity='act2autochess',versions=[v['value'] for v in native['autoChessData']['versionInfoDict'] if v['value'].get('activityId')=='act2autochess'],note='Native previous Covenant season, not the current V3_1 Rulebook. Text markup stripped for reading; raw native records retained. No game processes or neighboring project files modified.',statistics=stats,operators=rows)
(output/'native-traits.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf-8')
lines=['# 上一期卫戍协议「盟约」原始特性对照表','',f'来源：`{SOURCE}`。活动：act2autochess。共{len(rows)}个预设条目。原始charChessData没有独立desc字段，下文逐项导出所引用garrison记录的描述；多条时用原始ID标明，重复或调试标记保留，不等同于原版界面排版。末列是内部记录数，不将其冒充可见特性数量。普通、精锐及原始事件和黑板参数见同目录native-traits.json。','', '| # | 干员 | 等阶 | 盟约 | 普通特性原文 | 精锐特性原文 | 内部记录（普／精） |','| --- | --- | --- | --- | --- | --- | --- |']
for index,row in enumerate(rows,1):
    def cell(variant):
        entries=row[variant]['traits'];return '<br>'.join((f"〔{t['id']}〕" if len(entries)>1 else '')+t['text'].replace('|','｜').replace('\n','<br>') for t in entries) or '无（自选槽位）'
    lines.append(f"| {index} | {row['name']} | {row['tier']} | {'、'.join(row['normal']['bonds'])} | {cell('normal')} | {cell('elite')} | {row['normal']['traitCount']}／{row['elite']['traitCount']} |")
(output/'干员特性全表.md').write_text('\n'.join(lines)+'\n','utf-8')
print(json.dumps(stats,ensure_ascii=False,indent=2))
