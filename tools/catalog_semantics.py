"""Source-reviewed memberships, pinned to the native v0.113.1 definitions.

These are mechanic classifications, not balance changes. Powers are inspected as
well as cards: a hover-tip reference alone does not establish a membership.
"""
from catalog_creation import CREATION_MEMBERS, CREATION_EXCLUSIONS, CREATION_FORGERS, CREATION_TRANSFORMERS
from catalog_combat_flows import COMBAT_FLOW_MEMBERS, COMBAT_FLOW_EXCLUSIONS
from catalog_resources import RESOURCE_MEMBERS, RESOURCE_EXCLUSIONS
from catalog_fool import FOOL_MEMBERS, FOOL_EXCLUSIONS, ETHEREAL_SUPPORT, ETHEREAL_MEMBERS
from catalog_exhaust_discard import EMBER_MEMBERS, EMBER_SUPPORT, SLEIGHT_MEMBERS, SLEIGHT_SUPPORT, ZERO_SUPPORT
from catalog_keyword_special import STRIKE_MEMBERS, GUARD_MEMBERS, GUARD_SUPPORT, FORESIGHT_MEMBERS, FORESIGHT_UPGRADE, PROFIT_MEMBERS, SPECIAL_EXCLUSIONS
from catalog_chain import CHAIN_MEMBERS, CHAIN_EXCLUSIONS

REVIEWED_MEMBERSHIPS = {
    'Fury': {
        'Arsenal': ('ArsenalPower', '生成卡牌后，为自己施加力量。'),
        'Blaze': (None, '为友方施加正数力量。'),
        'Brand': (None, '主动自损并为自己施加力量。'),
        'BulkUp': (None, '为自己施加正数力量。'),
        'Coordinate': ('CoordinatePower', '为友方施加临时力量。'),
        'DemonForm': ('DemonFormPower', '能力在后续回合为自己施加力量。'),
        'Dominate': (None, '根据敌人的易伤为自己施加力量。'),
        'Enrapture': (None, '为友方施加活力。'),
        'FeedingFrenzy': ('FeedingFrenzyPower', '为自己施加临时力量。'),
        'FightMe': (None, '同时强化双方，包含自己获得力量。'),
        'Inflame': (None, '为自己施加正数力量。'),
        'Monologue': ('MonologuePower', '后续出牌为自己积累临时力量，并在回合结束时移除。'),
        'Patter': (None, '为自己施加活力。'),
        'PrepTime': ('PrepTimePower', '能力在回合开始时为自己施加活力。'),
        'Prowess': (None, '为自己施加正数力量。'),
        'Resonance': (None, '自己获得力量，同时降低敌人的力量。'),
        'Rupture': ('RupturePower', '失去生命后为自己施加力量。'),
        'SetupStrike': ('SetupStrikePower', '攻击后为自己施加临时力量。'),
        'Strength': (None, '衍生牌为自己施加力量。'),
        'Terraforming': (None, '为自己施加活力。'),
    },
    'Blood': {
        'BloodWall': (None, '打出时主动对自己造成生命损失。'),
        'Bloodletting': (None, '主动损失生命以获得能量。'),
        'Brand': (None, '打出时主动对自己造成生命损失。'),
        'Breakthrough': (None, '攻击前主动对自己造成生命损失。'),
        'DemonicShield': (None, '为友方提供格挡前主动损失自己的生命。'),
        'Hemokinesis': (None, '打出时主动对自己造成生命损失。'),
        'Offering': (None, '主动损失生命以获得抽牌和能量。'),
        'Rupture': ('RupturePower', '为主动损失生命提供收益。'),
        'TheHangedMan': (None, '主动损失生命以获得额外回合。'),
    },
    'Overload': {
        'ConsumingShadow': ('ConsumingShadowPower', '附加能力在回合结束时激发最后的充能球。'),
        'Dualcast': (None, '直接激发充能球。'),
        'MultiCast': (None, '直接激发充能球。'),
        'Quadcast': (None, '直接激发充能球。'),
        'Shatter': (None, '直接激发充能球。'),
        'Thunder': ('ThunderPower', '附加能力在闪电充能球激发后造成额外伤害。'),
    },
}
REVIEWED_MEMBERSHIPS['Creation']=CREATION_MEMBERS
REVIEWED_MEMBERSHIPS.update(COMBAT_FLOW_MEMBERS)
REVIEWED_MEMBERSHIPS.update(RESOURCE_MEMBERS)
REVIEWED_MEMBERSHIPS.update(FOOL_MEMBERS)
REVIEWED_MEMBERSHIPS['Ethereal']=ETHEREAL_MEMBERS
REVIEWED_MEMBERSHIPS['Ember']=EMBER_MEMBERS
REVIEWED_MEMBERSHIPS['Sleight']=SLEIGHT_MEMBERS
REVIEWED_MEMBERSHIPS['Strike']=STRIKE_MEMBERS
REVIEWED_MEMBERSHIPS['Guard']=GUARD_MEMBERS
REVIEWED_MEMBERSHIPS['Foresight']=FORESIGHT_MEMBERS
REVIEWED_MEMBERSHIPS['Profit']=PROFIT_MEMBERS
REVIEWED_MEMBERSHIPS['Chain']=CHAIN_MEMBERS

REVIEWED_EXCLUSIONS = {
    'Fury': {
        'CrushUnder': '施加降低敌人力量的能力，不强化友方。',
        'DarkShackles': '临时降低敌人的力量。',
        'DyingStar': '施加降低敌人力量的能力。',
        'EnfeeblingTouch': '降低敌人的力量。',
        'Friendship': '自己失去力量以获得能量上限，不属于获得力量或活力。',
        'Malaise': '降低敌人的力量。',
        'Mangle': '施加降低敌人力量的能力。',
        'MonarchsGaze': '能力在攻击后降低敌人的力量。',
        'PiercingWail': '临时降低全体敌人的力量。',
        'SharedFate': '自己和敌人均失去力量，不包含正向力量收益。',
    },
    'Blood': {
        'BadLuck': '诅咒在回合结束时造成被动损失，不是主动自损组件。',
        'Beckon': '状态在回合结束时造成被动损失，不是主动自损组件。',
        'Haunt': 'HpLossVar 在附加能力中表示对敌人的伤害，不会损失自己的生命。',
    },
}
REVIEWED_EXCLUSIONS['Creation']=CREATION_EXCLUSIONS
REVIEWED_EXCLUSIONS.update(COMBAT_FLOW_EXCLUSIONS)
REVIEWED_EXCLUSIONS.update(RESOURCE_EXCLUSIONS)
REVIEWED_EXCLUSIONS.update(FOOL_EXCLUSIONS)
REVIEWED_EXCLUSIONS.update(SPECIAL_EXCLUSIONS)
REVIEWED_EXCLUSIONS['Chain']=CHAIN_EXCLUSIONS

# These cards grant retention to other cards. Self-retention is always queried
# from the native card instance, including upgrades and temporary keyword changes.
RETAIN_SUPPORT = {
    'Convergence': (None, '附加保留整手牌的能力。'),
    'Equilibrium': (None, '附加保留整手牌的能力。'),
    'Expertise': (None, '抽牌后附加保留整手牌的能力。'),
    'KiRest': (None, '满足旋律条件时保留整手牌。'),
    'PhantomBlades': ('PhantomBladesPower', '附加能力为自己的小刀赋予保留。'),
    'Salvo': (None, '附加保留整手牌的能力。'),
    'Snap': (None, '为选中的手牌赋予保留。'),
}

def semantic_review_report(source_root):
    import hashlib
    result=[]
    for bond,members in {**REVIEWED_MEMBERSHIPS,'RetainSupport':RETAIN_SUPPORT,'ZeroSupport':ZERO_SUPPORT}.items():
        for name,(power,reason) in members.items():
            paths=[source_root/'MegaCrit.Sts2.Core.Models.Cards'/(name+'.cs')]
            if power:paths.append(source_root/'MegaCrit.Sts2.Core.Models.Powers'/(power+'.cs'))
            if bond=='Creation' and name in CREATION_FORGERS:paths.append(source_root/'MegaCrit.Sts2.Core.Commands'/'ForgeCmd.cs')
            if bond=='Creation' and name in CREATION_TRANSFORMERS:paths.append(source_root/'MegaCrit.Sts2.Core.Commands'/'CardCmd.cs')
            if bond=='Ember' and name=='TheShot':paths.append(source_root/'MegaCrit.Sts2.Core.Models.Cards'/'Florescence.cs')
            if bond=='Guard' and name in ('Anticipate','Fade'):paths.append(source_root/'MegaCrit.Sts2.Core.Models.Powers'/'TemporaryDexterityPower.cs')
            if bond=='Guard' and name=='Parry':paths.append(source_root/'MegaCrit.Sts2.Core.Models.Cards'/'SovereignBlade.cs')
            result.append(dict(card=name,bond=bond,included=True,reason=reason,sources=[dict(path=str(p.relative_to(source_root)),sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in paths]))
    for bond,members in REVIEWED_EXCLUSIONS.items():
        for name,reason in members.items():
            p=source_root/'MegaCrit.Sts2.Core.Models.Cards'/(name+'.cs')
            result.append(dict(card=name,bond=bond,included=False,reason=reason,sources=[dict(path=str(p.relative_to(source_root)),sha256=hashlib.sha256(p.read_bytes()).hexdigest())]))
    return result
