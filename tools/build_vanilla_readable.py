"""Build Claude-readable vanilla card data from an isolated native runtime export."""
from __future__ import annotations
import hashlib
import json
import re
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'outputs/claude-vanilla-cards-0.113.1'
SOURCE = ROOT.parent / 'proj/113.1'
def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))
def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()

POOL = {'IroncladCardPool':'铁甲', 'SilentCardPool':'静默', 'DefectCardPool':'机器人',
        'RegentCardPool':'储君', 'NecrobinderCardPool':'死灵', 'FoolCardPool':'愚者',
        'ColorlessCardPool':'无色', 'EventCardPool':'事件', 'TokenCardPool':'衍生',
        'QuestCardPool':'任务', 'CurseCardPool':'诅咒', 'StatusCardPool':'状态', 'DeprecatedCardPool':'废弃占位'}
COLOUR = {'red':'红色', 'green':'绿色', 'blue':'蓝色', 'orange':'橙色', 'pink':'粉色',
          'yellow':'黄色', 'colorless':'无色', 'curse':'诅咒牌框', 'quest':'任务牌框'}
TYPE = {'None':'尚未配置', 'Attack':'攻击', 'Skill':'技能', 'Power':'能力', 'Status':'状态', 'Curse':'诅咒', 'Quest':'任务'}
RARITY = {'None':'未设定', 'Basic':'基础', 'Common':'普通', 'Uncommon':'罕见', 'Rare':'稀有',
          'Ancient':'先古', 'Event':'事件', 'Token':'衍生', 'Status':'状态', 'Curse':'诅咒', 'Quest':'任务'}

def cost(value):
    return 'X' if value['kind']=='x' else '—' if value['kind']=='none' else str(value['value'])
def kw(state):
    return {k['id']:k['name'] for k in state['keywords']}
def change(before, after):
    result = {}
    for key in ['energy','stars','type','rarity','description','handDescription','tags']:
        if before[key] != after[key]:
            result[key] = {'before':before[key], 'after':after[key]}
    old, new = kw(before), kw(after)
    result['addedKeywords'] = [new[k] for k in new.keys()-old.keys()]
    result['removedKeywords'] = [old[k] for k in old.keys()-new.keys()]
    result['variables'] = {k:{'before':before['variables'].get(k,{}).get('value'), 'after':after['variables'].get(k,{}).get('value')}
                           for k in before['variables'].keys() | after['variables'].keys()
                           if before['variables'].get(k,{}).get('value') != after['variables'].get(k,{}).get('value')}
    return result
def costs_and_keywords(state):
    keywords = '、'.join(k['name'] for k in state['keywords']) or '无'
    return f"能量费用 **{cost(state['energy'])}**；蓝星费用 **{cost(state['stars'])}**；追加关键词：{keywords}。"
def description_lines(text):
    return '\n'.join('  > '+line.strip() for line in text.splitlines() if line.strip())
def state_lines(state, label):
    return f"- **{label}**：{costs_and_keywords(state)}\n\n{description_lines(state['description'])}\n"
def card_markdown(card):
    base = card['base']
    text = f"### {card['name']} / {card['englishName']} · `{card['className']}`\n\n"
    text += f"卡池：{'、'.join(POOL[p] for p in card['pools'])}；实际卡色：{card['colour']}；类型：{TYPE[base['type']]}；稀有度：{RARITY[base['rarity']]}。\n\n"
    text += state_lines(base,'未升级')
    for state in card['upgrades']:
        text += '\n'+state_lines(state, f"升级+{state['level']}（{state['name']}）")
    if not card['upgrades']:
        text += '\n不可升级。\n'
    elif card['maxUpgradeLevel'] == 1:
        changes = card['upgradeChanges'][0]
        details=[]
        for field, label in [('energy','能量费用'),('stars','蓝星费用')]:
            if field in changes:
                details.append(f"{label}：{cost(changes[field]['before'])} → {cost(changes[field]['after'])}")
        if changes['addedKeywords']: details.append('新增：'+'、'.join(changes['addedKeywords']))
        if changes['removedKeywords']: details.append('移除：'+'、'.join(changes['removedKeywords']))
        if 'type' in changes: details.append(f"类型：{TYPE[changes['type']['before']]} → {TYPE[changes['type']['after']]}")
        if details: text += '\n升级另查：'+'；'.join(details)+'。\n'
    else:
        text += '\n可反复升级。上方保留+1至+6及+10实测牌面；第n次升级增加3+n点基础伤害，+3、+6时更换名称。不能把+1当作最终上限。\n'
    if base['handDescription'] != base['description']:
        text += '\n手牌语境（仍无战斗加成）：\n\n'+description_lines(base['handDescription'])+'\n'
    if card['className']=='MadScience':
        text += '\n这是尚未配置的原生模板，不是缺失翻译。实际事件有9种成品组合，另有3种选类型预览，完整费用和升级牌面见[动态卡牌变体](动态卡牌变体.md)。\n'
    return text+'\n'

raw = read(OUT / 'native-runtime-export.json')
assert raw['exporterOnly'] and not raw['errors']
zh = {r['className']:r for r in raw['rows'] if r['language']=='zhs'}
en = {r['className']:r for r in raw['rows'] if r['language']=='eng'}
assert len(zh)==len(en)==raw['nativeCardCount']==717
assert len({r['id'] for r in zh.values()})==717
cards=[]
for name, row in zh.items():
    states = row['states']
    assert [s['level'] for s in states] == [s['level'] for s in en[name]['states']]
    for state in states:
        assert state['description'].strip(), name
        assert not re.search(r'\{[A-Za-z]|\[/?(?:gold|green|img|red)',state['description']), name
        for keyword in state['keywords']:
            assert keyword['name'] in state['description'], (name,keyword)
    base = states[0]
    source=SOURCE/'MegaCrit.Sts2.Core.Models.Cards'/f'{name}.cs'
    cards.append({'id':row['id'],'className':name,'name':base['name'],'englishName':en[name]['states'][0]['name'],
        'pools':row['pools'],'poolNames':[POOL[p] for p in row['pools']],
        'colour':COLOUR[base['frame'].removeprefix('card_frame_')], 'visualPool':base['visualPool'],
        'maxUpgradeLevel':row['maxUpgradeLevel'], 'base':base,'upgrades':states[1:],
        'upgradeChanges':[{'fromLevel':before['level'],'toLevel':after['level'],**change(before,after)} for before,after in zip(states,states[1:])],
        'variants':row['variants'],'englishStates':en[name]['states'],
        'nativeSourceSha256':digest(source)})

by_name={c['className']:c for c in cards}
assert 'Exhaust' in kw(by_name['Shiv']['base'])
assert 'Exhaust' in kw(by_name['Hologram']['base']) and 'Exhaust' not in kw(by_name['Hologram']['upgrades'][0])
assert 'Ethereal' in kw(by_name['EchoForm']['base']) and 'Ethereal' not in kw(by_name['EchoForm']['upgrades'][0])
assert by_name['Corruption']['base']['energy']['value']==3 and by_name['Corruption']['upgrades'][0]['energy']['value']==2
assert by_name['NeutronAegis']['base']['stars']['value']==5 and by_name['NeutronAegis']['upgrades'][0]['stars']['value']==5
assert by_name['NeutronAegis']['base']['variables']['PlatingPower']['value']==8 and by_name['NeutronAegis']['upgrades'][0]['variables']['PlatingPower']['value']==11
assert by_name['MultiCast']['base']['energy']['kind']=='x' and by_name['Stardust']['base']['stars']['kind']=='x'

metadata = {'game':raw['game'],'createdUtc':datetime.now(timezone.utc).isoformat(),'language':'简体中文，附英文牌名与英文完整牌面',
    'nativeAssemblySha256':raw['nativeAssemblySha256'],'runtimeExportSha256':digest(OUT/'native-runtime-export.json'),
    'cards':len(cards),'costNotation':{'—':'没有该资源费用标记；不是0费，也不单独代表不能打出','X':'X费用','0':'原版明确的0费用'},
    'descriptionContext':'原版无主卡牌、局外初始状态，无盟约模组、外部能力、遗物、附魔、污秽或临时费用。完整牌面通过原版GetDescriptionForPile读取，包含自身关键词及升级追加／移除的关键词。',
    'scope':'ModelDb.AllCards的全部717个定义，含六角色、无色、先古、事件、衍生、状态、诅咒、任务及废弃占位；不等于717张都可从普通奖励获得。',
    'excludedNativeMockTypes':raw['extraNativeTypesOutsideAllCards']}
write_json(OUT/'原版全卡数据.json',{'metadata':metadata,'cards':cards})
(OUT/'原版全卡数据.jsonl').write_text('\n'.join(json.dumps(c,ensure_ascii=False,separators=(',',':')) for c in cards)+'\n',encoding='utf-8')
intro='# 杀戮尖塔2原版全卡牌数据\n\n版本：v0.113.1 / d09d48e5。共717个原版定义。卡色是实际卡面颜色，卡池与稀有度另列。能量／蓝星费用中“—”表示没有该费用标记，“0”为真实0费，“X”为X费；文本中的【能量】【蓝星】为原版图标的可读转写。\n\n每段效果都是包含追加关键词的完整原版牌面，不是仅提取本地化描述模板。未升级与升级后分别列出费用、蓝星、关键词及全文；没有列出升级状态的牌明确标为不可升级。所有数值为初始局外状态，没有盟约模组或其他外部加成。动态卡牌和反复升级另有说明。\n\n'
(OUT/'原版全卡牌表.md').write_text(intro+'\n'.join(card_markdown(c) for c in cards),encoding='utf-8')
split = OUT/'按卡池阅读'; split.mkdir(exist_ok=True)
index=[]
for pool, label in POOL.items():
    group=[c for c in cards if pool in c['pools']]
    filename=f'{label}.md'
    (split/filename).write_text(intro.replace('# 杀戮尖塔2原版全卡牌数据',f'# {label} · 原版全卡牌数据')+'\n'.join(card_markdown(c) for c in group),encoding='utf-8')
    index.append((label,len(group),filename))

variant_text='# 动态卡牌变体\n\n疯狂科学（MadScience）是事件配置的卡牌。未配置模板显示“？？？？？？”；这是原版行为，不应据此认定没有效果。事件先选攻击／技能／能力，再从该类型对应的三个附加效果中选择。以下9项是成品组合，3项无附加效果的条目仅供事件预览。\n\n'
mad=next(c for c in cards if c['className']=='MadScience')
assert len(mad['variants'])==12 and sum(not v['isEventPreviewOnly'] for v in mad['variants'])==9
for variant in mad['variants']:
    variant_text+=f"## {TYPE[variant['type']]} / {variant['rider']}"+('（仅事件预览）' if variant['isEventPreviewOnly'] else '')+'\n\n'
    for state in variant['states']:
        assert state['description'].strip()
        if not variant['isEventPreviewOnly']: assert '？？' not in state['description']
        for keyword in state['keywords']: assert keyword['name'] in state['description']
        variant_text+=state_lines(state,'未升级' if state['level']==0 else '升级后')+'\n'
(OUT/'动态卡牌变体.md').write_text(variant_text,encoding='utf-8')
keywords={k['id']:k for card in cards for state in [card['base'],*card['upgrades']] for k in state['keywords']}
write_json(OUT/'追加关键词字典.json',keywords)
(OUT/'追加关键词说明.md').write_text('# 原版自动追加关键词\n\n'+ '\n'.join(f"- **{k['name']}（{key}）**：{k['description']}" for key,k in sorted(keywords.items()))+'\n\n这些关键词已经包含在各卡的完整效果描述中。关键词列表记录原生CardKeyword，不把正文中提到的“消耗另一张牌”等效果误认成此牌自身具有消耗。打击、基础防御、发射与旋律等原生CardTag另保存在JSON的tags字段。\n',encoding='utf-8')

summary={'status':'pass','cards':len(cards),'upgradableCards':sum(bool(c['upgrades']) for c in cards),
    'nativeLanguageRows':len(raw['rows']),'nativeExportErrors':len(raw['errors']),
    'upgradedCostChanges':sum(c['base']['energy']!=c['upgrades'][0]['energy'] or c['base']['stars']!=c['upgrades'][0]['stars'] for c in cards if c['upgrades']),
    'cardsWithUpgradeKeywordChanges':sum(bool(c['upgradeChanges'][0]['addedKeywords'] or c['upgradeChanges'][0]['removedKeywords']) for c in cards if c['upgradeChanges']),
    'cardsWithDistinctVisualPool':sum(c['visualPool']!=c['base']['pool'] for c in cards),
    'madScienceVariants':12,'madScienceFinishedVariants':9,
    'multiUpgradeCards':[c['className'] for c in cards if c['maxUpgradeLevel']>1],
    'pools':{label:n for label,n,_ in index},'nativeAssemblySha256':raw['nativeAssemblySha256'],
    'runtimeExportSha256':metadata['runtimeExportSha256']}
write_json(OUT/'导出核对.json',summary)
readme=f'''# 给Claude的原版卡牌数据

游戏版本：**{raw['game']}**。共**717**个原版定义，中英文各导出一次，导出错误为0。此目录不含盟约卡牌特性；正式游戏目录和正式存档未改动。

## 建议阅读顺序

1. 本说明：先确认版本与字段边界。
2. [按卡池阅读](按卡池阅读/)：每次读取所需角色或共享分类，避免一次读完整表占满上下文。
3. [完整中文牌表](原版全卡牌表.md)：全部卡牌，未升级／升级费用、蓝星、完整效果、关键词、卡色、类型和稀有度。
4. [结构化JSON](原版全卡数据.json)或[逐卡JSONL](原版全卡数据.jsonl)：按原生类名或ID准确查找，含升级差异、原生变量、中英文牌面及来源哈希。
5. [动态卡牌变体](动态卡牌变体.md)、[追加关键词说明](追加关键词说明.md)：处理疯狂科学、自动追加关键词等例外。

## 费用和描述的含义

- 能量和蓝星分别存储，升级后分别重新读取。**“—”是没有该费用标记，“0”是真实0费用，“X”是X费用，三者不可混用。**
- 卡色取VisualCardPool实际卡面材质；所属卡池另列。事件牌可能显示为角色颜色，但仍属于事件卡池；卡色不等于稀有度。
- 描述通过原版`GetDescriptionForPile`生成，已包含运行时在正文前后追加的消耗、虚无、固有、不能被打出、保留、奇巧、永恒等自身关键词。升级后移除的关键词也按实际结果记录。正文提到消耗其他卡牌，并不代表本卡自身有消耗。
- 中文效果中的【能量】和【蓝星】是原版图标的可读转写。原始BBCode和图标资源路径保留在JSON的descriptionFormatted及native-runtime-export.json中，方便审计。
- 图标按原版逐个保留：“获得【能量】【能量】”表示获得2点能量；“1【能量】”中的图标是能量单位，不表示再加1点。
- 数值是新建卡牌的局外初始状态，不含外部战斗状态、遗物、附魔、污秽或临时费用。手牌语境若与局外描述不同另列；不穷举其他卡施加的所有临时改动。
- 疯狂科学的未配置模板确实显示问号；另导出了9个合法成品和3个事件预览的升级前后完整描述。不要把模板问号当成实际效果。
- 黯淡匕首（DingyDagger）可反复升级，保留+1至+6及+10的原版实测结果；第n次升级增加3+n点基础伤害，+3和+6更换名称。此项对照本版本原版OnUpgrade及逐次UpgradeInternal结果。
- 本表包含衍生、事件、任务、状态、诅咒和废弃占位。它们属于原版定义，不因此具有普通奖励获取资格。原版7个Mock测试类型不属于ModelDb.AllCards，名单留在JSON metadata中，未当作正常卡牌混入。

## 分类索引

| 卡池 | 数量 | 文件 |
| --- | --- | --- |
'''+ '\n'.join(f'| {label} | {n} | [{filename}](按卡池阅读/{filename}) |' for label,n,filename in index)+f'''

## 核对结果

可升级定义{summary['upgradableCards']}个；升级改变能量或蓝星费用的定义{summary['upgradedCostChanges']}个；升级改变追加关键词的定义{summary['cardsWithUpgradeKeywordChanges']}个。全部状态均检查了效果非空、无未展开本地化变量、自身关键词出现在完整牌面中，以及中英文升级层级一致。

原版程序集SHA256：`{raw['nativeAssemblySha256']}`。原始运行时导出SHA256：`{metadata['runtimeExportSha256']}`。进一步证据见[导出核对](导出核对.json)。原始引擎日志另留本地导出目录，没有纳入资料压缩包；其中保留兼容渲染器的原版诊断，不代表导出字段失败。
'''
(OUT/'给Claude先读我.md').write_text(readme,encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False,indent=2))
