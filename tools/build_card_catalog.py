"""Compile explicit card metadata from the pinned native source; never parse localized descriptions at runtime."""
import collections, hashlib, json, pathlib, re
from catalog_semantics import REVIEWED_MEMBERSHIPS, RETAIN_SUPPORT, ETHEREAL_SUPPORT, EMBER_SUPPORT, SLEIGHT_SUPPORT, ZERO_SUPPORT, GUARD_SUPPORT, FORESIGHT_UPGRADE, semantic_review_report

ROOT=pathlib.Path(__file__).resolve().parents[1]
SOURCE=ROOT.parent/'proj/113.1'
if (ROOT/'Data/cards.json').exists() and any(c['trait'].get('revision') for c in json.loads((ROOT/'Data/cards.json').read_text('utf-8'))):
    raise SystemExit('The 0.5 revision catalog is active. Use compile_revision05.py --apply; this legacy generator would overwrite reviewed traits.')
POOL_NAMES={'Ironclad':'铁甲','Silent':'静默','Defect':'机器人','Regent':'储君','Necrobinder':'死灵','Fool':'愚者'}
MEMBERS={}
for file in sorted((SOURCE/'MegaCrit.Sts2.Core.Models.CardPools').glob('*.cs')):
    if file.stem=='TestCardPool':continue
    for name in re.findall(r'ModelDb\.Card<(\w+)>',file.read_text('utf-8-sig')):
        if name=='T':continue
        MEMBERS.setdefault(name,[]).append(file.stem)

ROLE_RULES=[
('Blood',r'LoseHp\(|LoseHpVar|Bloodletting|BloodWall|BloodPrice|Offering|Hemokinesis|RupturePower'),
('Ember',r'CardCmd\.Exhaust|CardKeyword\.Exhaust|CorruptionPower|FeelNoPainPower|DarkEmbracePower'),
('Fury',r'StrengthPower|VigorPower|HeavyBlade|DemonFormPower'),
('Poison',r'PoisonPower|PoisonVar|Envenom|AccelerantPower'),
('Blades',r'Card<Shiv>|ShivPower|AccuracyPower|HiddenDaggers|FanOfKnivesPower|PhantomBladesPower'),
('Sleight',r'CardCmd\.Discard|FromHandForDiscard|Sly|Discarded'),
('Overload',r'OrbCmd\.Evoke|EvokeOrb|EvokeAll|Dualcast|Multicast|Fission'),
('Resonance',r'OrbCmd\.Channel|FocusPower|OrbSlot|OrbPassive|OrbModel|OrbCmd\.'),
('Stars',r'StarCost|GainStars|StarsSpent|StarsPower|StarsVar'),
('Forge',r'ForgeCmd|ForgeVar|\.Forge\(|SovereignBlade|CardTag\.Forge'),
('Creation',r'CreateCard|AddGeneratedCard|\.CreateInHand\(|TransformCard|CardCmd\.Transform|CardFactory\.'),
('Doom',r'DoomPower|DoomVar|CountdownPower'),
('Bones',r'Osty|SummonCmd|\.Summon\(|SummonVar'),
('Ethereal',r'CardKeyword\.Ethereal|Eidolon|GhostSeed'),
('Arrows',r'ArrowCmd|AimPower|ImpaledPower|CardTag\.CanShoot'),
('Melody',r'MelodyCmd|MelodyType|CardTag\.(Wah|Cho|Ki)|Melod|MotifPower|LullabyPower'),
('Emotion',r'Card<Sadness>|Card<Joy>|SadnessPower|EcstasyPower|AllIsWellPower|Sadness|Joy'),
]
TRAIT_TRIGGERS={'Blood':'self_loss','Ember':'exhaust','Poison':'poison','Blades':'generated_attack','Sleight':'discard','Overload':'evoke','Resonance':'channel','Zero':'zero_play','Stars':'stars','Forge':'forge','Creation':'generate','Doom':'doom','Bones':'summon','Ethereal':'ethereal','Arrows':'arrow','Melody':'melody','Emotion':'emotion'}
rows=[]
for name,pools in sorted(MEMBERS.items()):
    file=SOURCE/'MegaCrit.Sts2.Core.Models.Cards'/f'{name}.cs'
    if not file.exists():raise RuntimeError(f'Unknown source {name}')
    source=file.read_text('utf-8-sig')
    ctor=re.search(r': base\((-?\d+), CardType\.(\w+), CardRarity\.(\w+), TargetType\.(\w+)(?:, [^\n]+)?\)',source)
    if not ctor:raise RuntimeError(f'Unreviewed constructor {name}')
    cost,kind,rarity,target=ctor.groups();cost=int(cost)
    role=next((p.removesuffix('CardPool') for p in pools if p.removesuffix('CardPool') in POOL_NAMES),'Shared')
    tags=[];evidence={}
    def tag(value,why):
        if value not in tags:tags.append(value);evidence[value]=why
    # Flow memberships are explicit in the output and include semantic source evidence for review.
    if not name.startswith(('Strike','Defend')):
        for value,pattern in ROLE_RULES:
            if value in REVIEWED_MEMBERSHIPS:
                if name in REVIEWED_MEMBERSHIPS[value]:tag(value,REVIEWED_MEMBERSHIPS[value][name][1])
                continue
            hit=re.search(pattern,source)
            if hit:tag(value,hit.group())
    for value in ('Strike','Guard'):
        if name in REVIEWED_MEMBERSHIPS[value]:tag(value,REVIEWED_MEMBERSHIPS[value][name][1])
    if name in REVIEWED_MEMBERSHIPS['Chain']:tag('Chain',REVIEWED_MEMBERSHIPS['Chain'][name][1])
    if cost>=2:tag('Heavy','Printed cost >= 2')
    if cost==0 and kind in ('Attack','Skill','Power'):tag('Zero','Printed energy cost is zero')
    if name in ZERO_SUPPORT:tag('Zero',ZERO_SUPPORT[name][1])
    if name in RETAIN_SUPPORT:tag('Retain',RETAIN_SUPPORT[name][1])
    elif 'CardKeyword.Retain' in source and name!='SummonForth':tag('Retain','Native self-retention; runtime checks current keyword/turn state')
    if kind=='Power':tag('Ritual','Power card')
    if name=='Entropy':tag('Harmony','Entropy only')
    if rarity=='Ancient':tag('Ancient','Native Ancient rarity')
    if kind=='Curse':tag('Curse','Curse card')
    if kind=='Status':tag('Status','Status card')
    for value in ('Profit','Foresight'):
        if name in REVIEWED_MEMBERSHIPS[value]:tag(value,REVIEWED_MEMBERSHIPS[value][name][1])
    if 'CardKeyword.Ethereal' in source:tag('Ethereal','Native ethereal keyword')
    if kind=='Attack' and 'CardTag.OstyAttack' in source:tag('Bones','Native Osty attack')
    # A few native starters and plain cards have no role mechanic: give their actual broad function.
    if not tags:tag('Solitude','Independent utility card without a native flow requirement')
    if name in {'MasterOfStrategy','SecretTechnique','SecretWeapon','JackOfAllTrades','Discovery','ThinkingAhead','Finesse','FlashOfSteel','Panache','Magnetism'}:
        tag('Solitude','Authored singleton utility bridge')
    primary=next((t for t in tags if t in TRAIT_TRIGGERS),tags[0])
    # Authored source roles for global/special covenants; none depend on the player's chosen character.
    if kind=='Curse':primary='Curse'
    if kind=='Status':primary='Status'
    if 'Profit' in tags:primary='Profit'
    if name in {'Armaments','Apotheosis','Anointed','HiddenDaggers'}:primary='Mastery'
    if name=='Entropy':primary='Harmony'
    if rarity=='Ancient':primary='Ancient'
    if pools and all(p in {'EventCardPool','QuestCardPool','DeprecatedCardPool'} for p in pools):primary='Encounter'
    trigger='draw' if kind in ('Curse','Status') else TRAIT_TRIGGERS.get(primary,'play')
    if kind=='Power':trigger='play'
    if kind=='Quest':trigger='victory'
    magnitude=2 if rarity in ('Rare','Ancient','Event') else 1
    rows.append(dict(className=name,pools=pools,role=role,type=kind,dynamicType='override CardType Type' in source,retainSupport=name in RETAIN_SUPPORT,etherealSupport=name in ETHEREAL_SUPPORT,emberSupport=name in EMBER_SUPPORT,sleightSupport=name in SLEIGHT_SUPPORT,zeroSupport=name in ZERO_SUPPORT,guardSupport=name in GUARD_SUPPORT,foresightRequiresUpgrade=FORESIGHT_UPGRADE.get(name),rarity=rarity,cost=cost,bonds=tags,
                     trait=dict(trigger=trigger,bond=primary,amount=magnitude,limit=1 if kind=='Quest' else 3),evidence=evidence,
                     sourceSha256=hashlib.sha256(file.read_bytes()).hexdigest()))
# When several role mechanics coexist, support the more specific flow first.
# Broad keyword/special memberships never displace an actual role mechanic.
# Powers still award on play, so consuming the card cannot strand its trait.
role_flows=[name for name,_ in ROLE_RULES]+['Zero']
role_sizes=collections.Counter(t for row in rows for t in row['bonds'] if t in role_flows)
for row in rows:
    special=row['trait']['bond'] in {'Curse','Status','Profit','Mastery','Harmony','Ancient','Encounter'}
    candidates=[t for t in row['bonds'] if t in role_flows]
    if candidates and not special:
        primary=min(candidates,key=lambda t:(role_sizes[t],role_flows.index(t)))
        row['trait']['bond']=primary
        row['trait']['trigger']='play' if row['type']=='Power' else TRAIT_TRIGGERS.get(primary,'play')
    # Colorless acquisitions are an authored investment component, not a claim
    # that their native effects generate gold. Keep their existing trait targets.
    if 'ColorlessCardPool' in row['pools'] and 'Profit' not in row['bonds']:
        row['bonds'].append('Profit')
        row['evidence']['Profit']='Authored investment component: native colorless pool; its own mechanic and trait target remain unchanged.'
for group in [*REVIEWED_MEMBERSHIPS.values(),RETAIN_SUPPORT,ZERO_SUPPORT]:
    unknown=set(group)-set(MEMBERS)
    if unknown:raise RuntimeError(f'Reviewed cards missing from pinned pools: {sorted(unknown)}')

# Continuous traits have authored cadence, never a shared or per-battle growth ceiling.
# A played power grants an attached support effect; its instance remains registered after leaving the piles.
ongoing={
    'Accuracy':('generated_attack',1), 'InfiniteBlades':('generated_attack',1),
    'PhantomBlades':('generated_attack',1), 'NoxiousFumes':('poison',1),
    'Envenom':('poison',1), 'FeelNoPain':('exhaust',1), 'DarkEmbrace':('exhaust',1),
    'CreativeAi':('generate',1), 'CallOfTheVoid':('generate',1), 'Soulbound':('generate',1),
    'Furnace':('forge',1), 'Parry':('sword_play',1), 'SpiritOfAsh':('ethereal',1),
    'Afterimage':('any_play',1),
}
layer_units=json.loads((ROOT/'Data/layer-units.json').read_text('utf-8'))
for row in rows:
    trait=row['trait'];name=row['className']
    trait['limit']=1 if row['type']=='Quest' else 0
    trait['every']=1
    if row['type']=='Power':
        trait['amount']+=max(1,row['cost'])
        event,period=ongoing.get(name,('power_turn',1))
        trait.update(ongoingTrigger=event,ongoingEvery=period,ongoingAmount=1)
    if 'TokenCardPool' in row['pools'] and row['type'] in ('Attack','Skill'):
        trait.update(trigger='play',every=1)
    if name=='Shiv':trait.update(bond='Blades',amount=1)
    if name=='Soul':trait.update(trigger='self_exhaust',bond='Ember',amount=1,every=1)
    if name=='NeutronAegis':trait.update(trigger='play',bond='Stars',amount=6,every=1,extra='aegis')
    if name=='KnockoutBlow':trait.update(trigger='play',bond='Stars',amount=3,every=1,extra='stars')
    # Event components need not produce their own support event. These two grants deliberately
    # pay on play, rather than leaving a high-cost attack dependent on another card's action.
    if name in {'Glimmer','SolarStrike','KinglyPunch'}:trait.update(trigger='play',every=1)
    # Keep one-off play/draw rewards in the same denomination as their target bond.
    # Repeated support events always pay a smooth integer amount on every event.
    if trait['trigger'] in ('play','draw','self_exhaust','victory'):
        factor=layer_units.get(trait['bond'],10)//10
        trait['amount']*=factor
        trait['upgradeAmount']=factor
    row['evidence']['Trait']='Authored continuous growth: own play/draw/exhaust, support cadence, or attached power support; no generated-card penalty.'
from trait_design import design
trait_design=design(rows,SOURCE)
for row in rows:row['trait']['effects']=trait_design[row['className']]['effects']
reviews=semantic_review_report(SOURCE)
out=ROOT/'Data';out.mkdir(exist_ok=True)
(out/'cards.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2)+'\n','utf-8')
(out/'trait-design.json').write_text(json.dumps(trait_design,ensure_ascii=False,indent=2)+'\n','utf-8')
report={'sourceVersion':'v0.113.1/d09d48e5','cards':len(rows),'byRole':dict(collections.Counter(r['role'] for r in rows)),
        'memberships':dict(sorted(collections.Counter(t for r in rows for t in r['bonds']).items())),
        'traitTargets':dict(sorted(collections.Counter(r['trait']['bond'] for r in rows).items())),
        'traitRouting':'Authored programs replace the legacy generic trait. Most cards have one role; two named weak-card exceptions have two paired effects. Played powers attach their one declared support ability. Generated copies retain the same program.',
        'authoredTraitCounts':dict(sorted(collections.Counter(len(r['trait']['effects']) for r in rows).items())),
        'referenceStudy':'audits/arknights-trait-study/native-traits.json: act2autochess native operator traits, read before the single-role redesign.',
        'regularGrowth':'Every ordinary declared event grants integer layers immediately. Attached powers grant on every declared event or every player turn. Multi-action thresholds remain only on special covenant rewards.',
        'layerUnits':layer_units,
        'authoredInvestmentRule':'All native colorless cards also count Profit; membership does not redirect their own traits.',
        'reviewedWholeGroups':list(REVIEWED_MEMBERSHIPS),
        'retainMembership':'Native current keywords/turn retention, or explicitly reviewed support card.',
        'unreviewedThirdPartyCards':'No automatic gameplay traits; safe generic/dynamic memberships only.'}
(out/'catalog-audit.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf-8')
(out/'semantic-review.json').write_text(json.dumps(reviews,ensure_ascii=False,indent=2)+'\n','utf-8')
print(json.dumps(report,ensure_ascii=False))
