"""Compile reviewed 0.5 text to explicit rules; reject every unhandled clause.

The generated file is a staging artifact. --apply is deliberately unavailable until
every native card has a complete program, so a partial migration cannot ship.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import os
import re
from pathlib import Path
import rework27
import approved_traits
from revision_new_bonds import revise_membership, rewrite_revision
from revision_star_traits import update as update_star_traits
from revision_growth_review import update as update_reviewed_growth
from revision_condition_review import update as update_reviewed_conditions
from revision_reflow import update_traits as update_reflow_traits, update_membership as update_reflow_membership
from catalog_exhaust_discard import EMBER_EXCLUDED, ember_category_excluded
from catalog_keyword_special import FORESIGHT_REMOVED, FORESIGHT_UPGRADE, PROFIT_EXCLUDED, ZERO_EXCLUDED
from catalog_creation import CREATION_REMOVED

ROOT = Path(__file__).resolve().parents[1]
PROPOSAL = ROOT / 'proposals' / '0.5'
OUT = ROOT / 'audits' / '0.5'
NATIVE_ROOT = Path(os.environ.get('COVENANT_NATIVE_SOURCE', ROOT.parent / 'proj/114'))
NATIVE_EXPORT = Path(os.environ.get('COVENANT_NATIVE_EXPORT', ROOT / 'outputs/claude-vanilla-cards-0.114.0/native-runtime-export.json'))
BONDS = dict(re.findall(r'D\(Bond\.(\w+),"([^"]+)"', (ROOT / 'Core/BondCatalog.cs').read_text('utf-8')))
BONDS = {cn: en for en, cn in BONDS.items()}
ASSOCIATIONS = {row['card']:row['bond'] for row in json.loads((PROPOSAL/'card-revisions.json').read_text('utf-8'))['cards']}
GROW = r'使(?P<active>已激活的)?【(?P<bond>[^】]+)】(?P<temp>临时)?增加(?P<amount>\d+)层'
MARKER_CARDS = {'Sloth', 'Disintegration', 'MindRot', 'Amnesia'}
ANCIENT_TRAITS = {
    'Abundance': 'Creation', 'Apotheosis': 'Ember', 'BiasedCognition': 'Resonance', 'Break': 'Fury',
    'BrightestFlame': 'Zero', 'DivineShot': 'Arrows', 'ForbiddenGrimoire': 'Ritual', 'LostLute': 'Melody',
    'Maul': 'Fury', 'MeteorShower': 'Stars', 'NeowsFury': 'Ember', 'Protector': 'Bones',
    'Quadcast': 'Overload', 'Relax': 'Guard', 'Suppress': 'Zero', 'TheSealedThrone': 'Stars',
    'Whistle': 'Ember', 'WraithForm': 'Ritual',
}
REASSIGNED_SOLITUDE_TRAITS = {
    'Burst': 'Chain', 'Cascade': 'Chain', 'Conflagration': 'Fury',
    'CrashLanding': 'Status', 'CrushUnder': 'Fury', 'DaggerSpray': 'Blades',
    'Debilitate': 'Doom', 'DislikeYou': 'Arrows', 'Dismantle': 'Fury',
    'EchoingSlash': 'Fury', 'Flechettes': 'Chain', 'ForegoneConclusion': 'Chain',
    'GangUp': 'Chain', 'Hang': 'Doom', 'HiddenGem': 'Profit',
    'KinglyPunch': 'Chain', 'MindBlast': 'Profit', 'OneTwoPunch': 'Chain',
    'Rampage': 'Fury', 'Rend': 'Profit', 'Strangle': 'Chain',
    'SuckerPunch': 'Fury', 'Supermassive': 'Creation', 'SwordBoomerang': 'Fury',
    'TheBall': 'Profit', 'ThrummingHatchet': 'Profit', 'Thunderclap': 'Fury',
}
REASSIGNED_ZERO_TRAITS = {'Adrenaline': 'Chain', 'Compost': 'Ritual'}
TRAIT_BOND_MEMBERS = {
    'CrimsonMantle', 'Inferno', 'Spite',
    'BorrowedTime', 'Cruelty', 'DanseMacabre', 'Friendship',
    'Honing', 'LockedIn', 'HiddenRoots', 'Juggernaut', 'Haunt',
    'Pillage', 'TheHunt', 'ImitationLearning', 'SignalBoost', 'DrainPower',
    'Mayhem', 'Simpleton', 'Stampede',
}
HEAVY_EXCLUDED = {
    'Apotheosis', 'BladeSymphony', 'DarkEmbrace', 'Ecstasy', 'Eidolon', 'Entrench', 'Feral',
    'Flanking', 'ForbiddenGrimoire', 'HammerTime', 'Hellraiser', 'MasterPlanner',
    'MonarchsGaze', 'Motif', 'Orbit', 'SentryMode', 'SevenStars', 'SovereignBlade',
    'SpectrumShift', 'SwordSage', 'Tracking', 'Tune', 'Unmovable', 'WellLaidPlans',
}
REASSIGNED_HEAVY_TRAITS = {'Flanking': 'Chain', 'MonarchsGaze': 'Ritual', 'Orbit': 'Ritual', 'Tracking': 'Ritual'}
NEXT_PLAY_ATTACHMENTS = {
    'Calamity', 'CloakAndDagger', 'Graveblast', 'Jackpot', 'LeadingStrike', 'LockedIn',
    'Nostalgia', 'Rebound', 'SecretTechnique', 'SecretWeapon',
}
PERSISTENT_RESOURCES = {'Archery', 'Genesis', 'HiddenRoots', 'PrepTime', 'SolarStrike', 'Terraforming'}
NEXT_PLAY_DISCOUNTS = {'CollisionCourse', 'ForegoneConclusion', 'MachineLearning', 'Wish'}


def revise_current_rules(name, program, card_type, upgraded):
    if name in MARKER_CARDS:
        return dict(text='', nativePowers=[], rules=[], causalPowers=[])
    translations = {bond: label for label, bond in BONDS.items()}
    if name in REASSIGNED_HEAVY_TRAITS:
        replacement = REASSIGNED_HEAVY_TRAITS[name]
        for rule in program['rules']:
            if rule['bond'] == 'Heavy':
                rule['bond'] = replacement
        for field in ('text', 'amountText'):
            if program.get(field):
                program[field] = program[field].replace('【重装】', f'【{translations[replacement]}】')
    if name in REASSIGNED_ZERO_TRAITS:
        replacement = REASSIGNED_ZERO_TRAITS[name]
        for rule in program['rules']:
            if rule['bond'] == 'Zero':
                rule['bond'] = replacement
        for field in ('text', 'amountText'):
            if program.get(field):
                program[field] = program[field].replace('【零界】', f'【{translations[replacement]}】')
    if name == 'Royalties':
        amount = 8 if upgraded else 6
        return dict(text=f'使已激活的【逐利】增加{amount}层。', nativePowers=[],
                    rules=[instruction('play', 'grow', 'Profit', amount)], causalPowers=[])
    if name in ANCIENT_TRAITS:
        bond = ANCIENT_TRAITS[name]
        old_bond = 'Mastery' if name == 'Apotheosis' else 'Ancient'
        for rule in program['rules']:
            if rule['bond'] == old_bond:
                rule['bond'] = bond
        for field in ('text', 'amountText'):
            if program.get(field):
                program[field] = program[field].replace(f'【{translations[old_bond]}】', f'【{translations[bond]}】')
    if name == 'Abundance':
        program['rules'][1]['bond'] = 'Creation'
        program['text'] = program['text'].replace('【仪式】', '【造物】')
    if name == 'Wish':
        program['rules'][0]['bond'] = 'Retain'
    if any(rule['bond'] == 'Solitude' for rule in program['rules']):
        replacement = REASSIGNED_SOLITUDE_TRAITS.get(name)
        if replacement is None:
            raise Pending(f'No replacement for the Solitude trait of {name}')
        for rule in program['rules']:
            if rule['bond'] == 'Solitude':
                rule['bond'] = replacement
        for field in ('text', 'amountText'):
            if program.get(field):
                program[field] = program[field].replace('【独行】', f'【{translations[replacement]}】')
    for rule in program['rules']:
        if rule['action'] == 'temporary':
            # Former combat-only rates were sized to vanish at victory. Reduce their per-trigger coefficient.
            coefficient = re.match(r'\d+', rule['amount'])
            if coefficient is None:
                raise Pending(f'No growth coefficient for {name}')
            old = int(coefficient[0])
            rate = (old + 1) // 2
            rule['amount'] = str(rate) + rule['amount'][coefficient.end():]
            rule['action'] = 'grow'
            text = program['text']
            if f'临时增加{old}层' in text:
                text = text.replace(f'临时增加{old}层', f'增加{rate}层')
            elif f'的{old}倍' in text:
                text = text.replace('临时增加层数', '增加层数').replace(f'的{old}倍', f'的{rate}倍')
            else:
                raise Pending(f'No matching temporary growth wording for {name}')
            program['text'] = text
        if name in NEXT_PLAY_ATTACHMENTS and rule.get('scope') in ('generated', 'returned', 'top'):
            rule['lifetime'] = 'next'
        if name in NEXT_PLAY_DISCOUNTS and rule['action'] == 'discount_cards':
            rule['lifetime'] = 'next'
        if name in PERSISTENT_RESOURCES and rule.get('scope') == 'resource':
            rule['condition'] = rule['condition'].replace('resource_issued_turn==resource_turn&&', '').replace('&&(resource_issued_turn==resource_turn)', '')
    for field in ('text', 'amountText'):
        text = program.get(field)
        if not text:
            continue
        if name in ('Nostalgia', 'Rebound'):
            text = re.sub(r'^(?:打出后，每当)?被它放(?:回|到)抽牌堆顶的牌在下回合被打出(?:时)?，',
                          '被此牌放到抽牌堆顶的牌下次打出时，', text)
        elif name in NEXT_PLAY_ATTACHMENTS:
            text = text.replace('在同一回合被打出', '下次打出')
            text = text.replace('在本回合被打出', '下次打出').replace('在本回合', '下次打出时')
        if name == 'LeadingStrike':
            text = text.replace('打出时，若由此生成的小刀下次打出，', '由此牌生成的小刀下次打出时，')
        if name == 'SecretTechnique':
            text = re.sub(r'打出时，使被取回的技能牌下次打出时获得的格挡增加(\d+)点，或使其叠层额外增加(\d+)层。',
                          r'取回的技能牌下次打出时，其获得的格挡增加\1点。若该牌不提供格挡，则额外增加盟约层数\2层。', text)
        if name in PERSISTENT_RESOURCES:
            text = text.replace('在同一回合', '').replace('在本回合', '').replace('星星', '蓝星')
            if name == 'SolarStrike':
                text = re.sub(r'^打出时，若由(?:此牌|此|它)获得的蓝星被花费，', '此牌提供的蓝星被花费时，', text)
            if name == 'Terraforming':
                text = text.replace('打出时，若你的活力被一张攻击牌用掉，', '此牌提供的活力被攻击牌消耗时，')
        if name == 'Break':
            text = text.replace('打出时，目标', '打出后，目标')
        if name == 'FeedingFrenzy':
            text = text.replace('打出后的本回合，你打出的每张攻击牌', '本回合，你每打出1张攻击牌，')
        if name == 'SorrowfulStrike':
            text = text.replace('打出时，若由此牌加入手牌的悲伤在本回合结束时仍在手牌，',
                                '本回合结束时，若此牌生成的悲伤仍在手牌，')
        if name == 'Salvo':
            text = text.replace('，并使其中的攻击牌下次打出时伤害增加', '。被此牌保留的攻击牌下次打出时，造成的伤害增加')
        if name == 'CollisionCourse':
            text = text.replace('，并使由此牌生成的碎屑在本回合的能量消耗减少', '。由此牌生成的碎屑下次打出时，能量消耗减少')
        if name in ('ForegoneConclusion', 'MachineLearning'):
            text = text.replace('在那个回合的能量消耗各减少', '下次打出时各少消耗').replace('在本回合的能量消耗减少', '下次打出时，能量消耗减少')
            text = text.replace('各少消耗1。', '各少消耗1点能量。').replace('能量消耗减少1。', '能量消耗减少1点。')
        if name == 'Wish':
            text = text.replace('使被放入手牌的牌在本回合的能量消耗减少1；若此牌在上回合被保留，改为减少2',
                                '此牌放入手牌的牌下次打出时，能量消耗减少1点。若此牌在上回合被保留，改为减少2点')
        if name in ('EchoForm', 'LostLute'):
            text = re.sub(r'(增加)层数，数量为(该牌原始费用加1)的1倍', r'\1等同于\2的层数', text)
        if card_type == 'Power':
            text = re.sub(r'^打出后，', '', text)
        for original, replacement in (('由它', '由此牌'), ('被它', '被此牌'), ('因它', '因此牌'),
                                      ('每当它', '每当此牌'), ('它每次', '此牌每次'), ('它每生成', '此牌每生成'),
                                      ('每回合它', '每回合此牌'), ('；它变化为', '；此牌变化为'), ('星星', '蓝星')):
            text = text.replace(original, replacement)
        if name == 'WraithForm':
            text = text.replace('再增加', '使已激活的【仪式】增加')
        if name=='Reboot' or any(rule['bond']=='Zero' for rule in program['rules']):
            text = text.replace('原始费用', '能量费用')
        if any(rule['bond']=='Heavy' for rule in program['rules']):
            text = text.replace('实际花费', '花费')
        program[field] = text
    if name=='Encore':
        for rule in program['rules']:
            if rule['bond']=='Zero':
                rule['amount']=rule['amount'].replace('subject_cost','subject_current_cost')
    return program


class Pending(ValueError):
    pass


def expand(text: str, upgraded: bool) -> str:
    def choose(m):
        return m[2] if upgraded else m[1]
    text = re.sub(r'([\d.]*A(?:/\d+)?|\d+(?:\.\d+)?)(?:（([^（）]+)）)', choose, text)
    remaining=re.sub(r'（【[^】]+】）','',text)
    if '（' in remaining or '）' in remaining:
        raise Pending('Unexpanded upgrade text: ' + text)
    return text


def instruction(event, action, bond, amount='0', condition='1', **kwargs):
    return dict(event=event, action=action, bond=BONDS.get(bond, bond),
                amount=str(amount), condition=condition, **kwargs)


def formula(text):
    """Normalize the proposal's implicit multiplication for compiled arithmetic."""
    if not re.fullmatch(r'[\d.A/*+ -]+',text):
        raise Pending('Unsupported numeric expression: '+text)
    return re.sub(r'(\d)A',r'\1*A',text)


def prefix_rule(text):
    """Return event, multiplier, condition, limit for fully recognized wording."""
    event, limit = 'play', ''
    starts = [
        ('每场战斗首次打出时，', 'play', 'battle'),
        ('此牌使箭矢松脱时，', 'play', ''),
        ('此牌每使你抽1张牌，', 'play', ''),
        ('抽到这张牌时，', 'draw', ''), ('这张牌被消耗时，', 'exhaust', ''),
        ('此牌因虚无被消耗时，', 'ethereal', ''),
        ('此牌从你的牌组中移除时，', 'remove', ''),
        ('这张牌在回合结束时对你造成伤害时，', 'self_damage', ''),
        ('这张牌使你失去生命时，', 'self_damage', ''),
        ('打出这张牌时，', 'play', ''), ('打出时，', 'play', ''),
        ('战斗胜利时，', 'victory', ''), ('斩杀时，', 'play', ''),
        ('此牌击杀敌人时，', 'play', ''), ('此牌杀死敌人时，', 'play', ''),
        ('此牌将敌人的生命值变为0时，', 'play', ''),
    ]
    start = next((s for s in starts if text.startswith(s[0])), None)
    if not start:
        raise Pending('Trigger: ' + text)
    text = text[len(start[0]):]
    event, limit = start[1:]
    condition = 'kills>0' if '斩杀' in start[0] or '击杀' in start[0] or '杀死' in start[0] or '变为0' in start[0] else '1'
    if start[0]=='斩杀时，':
        condition='kills>0&&before_fatal_allowed>0'
    if start[0]=='此牌使箭矢松脱时，':
        condition='native_dislodges>0'
    if not text:
        return event, 'drawn_count' if start[0]=='此牌每使你抽1张牌，' else '1', condition, limit
    direct = {
        '若目标在此之前已有易伤，': 'before_target_VulnerablePower>0',
        '若目标在此之前没有易伤，': 'before_target_VulnerablePower==0',
        '若目标在此之前已有虚弱，': 'before_target_WeakPower>0',
        '若目标在此之前没有虚弱，': 'before_target_WeakPower==0',
        '若目标在此之前已有中毒，': 'before_target_PoisonPower>0',
        '若目标在此之前既没有虚弱也没有易伤，': 'before_target_WeakPower==0&&before_target_VulnerablePower==0',
        '若目标的意图是攻击，': 'before_target_attacking>0',
        '若有敌人的意图是攻击，': 'before_enemy_attacking>0',
        '若你在此之前没有格挡，': 'before_block==0',
        '若你当时没有格挡，': 'before_block==0',
        '若你当时拥有叽旋律，': 'before_melody_Ki>0',
        '若你在此之前没有星星，': 'before_stars==0',
        '若你在此之前已有活力，': 'before_owner_VigorPower>0',
        '若此牌的伤害没有被完全格挡，': 'hit_unblocked>0',
        '若两次伤害都没有被完全格挡，': 'hit_unblocked_count>=2',
        '若因此抽了牌，': 'drawn_count>0',
        '若你在本回合失去过生命，': 'before_owner_lost_hp_this_turn>0',
        '若因此获得了能量，': 'gain_energy>0',
        '若因此给予了虚弱，': 'power_WeakPower>0',
        '若成功给予了中毒，': 'power_PoisonPower>0',
        '若被丢弃的是攻击牌，': 'discarded_Attack>0',
        '若被丢弃的牌具有奇巧，': 'discarded_Sly>0',
        '若被消耗的是状态牌或诅咒牌，': 'exhausted_waste>0',
        '若被随机消耗的是攻击牌，': 'exhausted_Attack>0',
        '若由此抽到的是攻击牌，': 'drawn_Attack>0',
        '若由此抽到的是技能牌，': 'drawn_Skill>0',
        '若由此抽到的牌原始费用为0，': 'drawn_zero>0',
        '若此牌当时的能量消耗为0，': 'before_cost==0',
        '若这是你本场战斗的第一个回合，': 'before_turn==1',
        '若这是你本场战斗打出的第一张牌，': 'battle_before_play==0',
        '若这是你本回合打出的第一张攻击牌，': 'turn_before_Attack==0',
        '若这是你本回合打出的第三张或更靠后的牌，': 'turn_before_play>=2',
        '若你在本回合已打出过至少两张原始费用为0的牌，': 'turn_before_zero>=2',
        '若此牌在上回合被保留，': 'native_retained>0',
        '若你的生命值不高于一半，': 'before_hp*2<=before_maxhp',
        '若此牌是战斗中生成的复制品，': 'before_generated>0',
        '若你的弃牌堆中已经有愤怒，': 'before_discard_class_Anger>0',
        '若你的手牌中有君王之剑，': 'before_hand_class_SovereignBlade>0',
        '若奥斯提在场，': 'before_osty_alive>0',
        '若目标处于扎穿状态，': 'before_target_ImpaledPower>0',
        '若你当时的力量不高于0，': 'before_owner_StrengthPower<=0',
        '若你已有至少一个闪电充能球，': 'before_orb_LightningOrb>0',
        '若你已有至少一个冰霜充能球，': 'before_orb_FrostOrb>0',
        '若你已有至少一个黑暗充能球，': 'before_orb_DarkOrb>0',
        '若你已有至少一个玻璃充能球，': 'before_orb_GlassOrb>0',
        '若由此打出的牌原始费用至少为2，': 'played_heavy>0',
        '若由此打出的攻击牌原始费用至少为2，': 'played_heavy_attack>0',
        '若你在本回合已经打出过此牌，': 'turn_before_source>0',
        '若这是你手牌中的最后一张牌，': 'before_hand==0',
        '若奥斯提在此之前已被击倒，': 'before_osty_existed>0&&before_osty_alive==0',
        '若奥斯提在此之前不在场，': 'before_osty_alive==0',
        '若奥斯提因此死去，': 'native_osty_killed>0',
        '若因此取回了一张发射牌，': 'returned_Shoot>0',
        '若因此额外获得了一次格挡，': 'gain_block_count>1',
        '若因此获得了额外的格挡，': 'gain_block_count>1',
        '若你拥有箭矢，': 'before_arrow_count>0',
        '若这使你不再拥有星星，': 'before_stars>0&&after_stars==0',
        '若你的充能球栏位因此被填满，': 'orb_generated>0&&after_orb_count==before_orb_slots',
        '若你的充能球栏位已满，': 'before_orb_count==before_orb_slots',
        '若你拥有全部三种旋律，': 'after_melodies==3',
        '若被激发的是黑暗充能球，': 'orb_evoked_DarkOrb>0',
        '若由此抽到的牌类型各不相同，': 'drawn_count>0&&drawn_types==drawn_count',
        '若抽到的牌与丢弃的牌类型不同，': 'native_draw_discard_different>0',
        '由此打出的牌若类型不同，': 'played_count>1&&played_types==played_count',
        '若生成的充能球种类是你当前没有的，': 'orb_generated_newtype>0',
        '若目标的灾厄因此不低于其生命值，': 'after_target_DoomPower>=after_target_hp&&power_DoomPower>0',
        '若X的数值因此被翻倍，': 'x_value>=4&&hit_count>=2*x_value',
        '若奥斯提的最大生命值因此达到20点，': 'after_osty_maxhp>=20&&after_osty_maxhp>before_osty_maxhp',
        '若此牌击杀了敌人，': 'kills>0',
        '若被消耗的是能力牌或状态牌，': 'exhausted_Power+exhausted_Status>0',
        '若放到抽牌堆顶的是攻击牌，': 'to_top_Attack>0',
        '若因此使箭矢松脱，': 'native_dislodges>0',
        '若这是奥斯提本回合的第一次攻击，': 'turn_before_osty_attack==0&&native_osty_attacks>0',
        '若你在本回合还没有发射过箭矢，': 'turn_before_shot==0',
        '若这是你本回合的第二次或更靠后的射击，': 'turn_before_shot>=1&&native_shots>0',
        '若你因此同时拥有三种不同的充能球，': 'after_orb_types>=3&&orb_generated>0',
        '若此牌在本场战斗被打出过，': 'battle_played>0',
        '若君王之剑是从消耗牌堆被取回的，': 'returned_from_Exhaust_class_SovereignBlade>0',
        '若因此命中了两次，': 'hit_count>=2',
        '若你在此之前已拥有哇旋律或啾旋律，': 'before_melody_Wah+before_melody_Cho>0',
        '若因此造成了双倍伤害，': 'before_target_ImpaledPower>0&&hit_count>0',
        '若这支箭矢扎穿了一名此前没有被扎穿的敌人，': 'native_impales>0&&before_target_ImpaledPower==0',
        '若目标因此被眩晕，': 'native_stunned>0',
        '若命中了至少两名敌人，': 'hits_targets>=2',
        '若由此抽到的牌能够生成充能球，': 'drawn_orbs>0',
        '若由此抽到的牌能够给予灾厄，': 'drawn_doom>0',
        '若被丢弃的是诅咒牌，': 'discarded_Curse>0',
        '若因此保留了手牌，': 'retained_count>0',
        '若奥斯提因此回复了生命，': 'native_osty_healed>0',
        '若被打出3次的技能牌原始费用至少为2，': 'played_heavy_skill>=3',
    }
    if text in direct:
        return event, '1', direct[text], limit
    metrics = {
        '每命中一名敌人，': 'hits_targets', '每因此杀死一名敌人，': 'kills',
        '每击杀一名敌人，': 'kills', '每消耗一张手牌，': 'exhausted_count',
        '每因此消耗一张牌，': 'exhausted_count',
        '每消耗一张状态牌或诅咒牌，': 'exhausted_waste',
        '每丢弃一张牌，': 'discarded_count', '每丢弃一张攻击牌，': 'discarded_Attack',
        '被丢弃的牌中每有一张具有奇巧，': 'discarded_Sly',
        '因此被丢弃的牌中每有一张具有奇巧，': 'discarded_Sly',
        '由此每抽到一张牌，': 'drawn_count', '由此每抽到一张攻击牌，': 'drawn_Attack',
        '由此每抽到一张技能牌，': 'drawn_Skill', '由此每抽到一张原始费用为0的牌，': 'drawn_zero',
        '由此每抽到一张具有虚无的牌，': 'drawn_Ethereal',
        '由此每抽到一张具有星星耗能的牌，': 'drawn_star',
        '由此抽到的牌中每有一种不同的类型，': 'drawn_types',
        '由此每取回一张牌，': 'returned_count', '由此每取回一张稀有牌，': 'returned_rare',
        '由此每取回一张具有保留的牌，': 'returned_Retain',
        '由此每打出一张牌，': 'played_count', '由此每打出一张小刀，': 'played_class_Shiv',
        '由此每打出一张虚无牌，': 'played_Ethereal',
        '由此每打出一张哇牌或叽牌，': 'played_wah_ki',
        '你的手牌中每有一张攻击牌，': 'before_hand_Attack',
        '每有一名意图攻击的敌人，': 'before_enemy_attacking',
        '每有一名具有中毒的敌人，': 'before_enemy_PoisonPower_targets',
        '每有一名敌人因此首次中毒，': 'first_poison_targets',
        '每次弹跳命中一名此前没有中毒的敌人，': 'first_poison_targets',
        '每有一名敌人因此首次获得灾厄，': 'first_doom_targets',
        '每有一名敌人因此首次获得易伤，': 'first_vulnerable_targets',
        '目标每有1层易伤，': 'after_target_VulnerablePower',
        '目标每有1层虚弱，': 'after_target_WeakPower',
        '你每有1点力量，': 'after_owner_StrengthPower',
        '你每有1点敏捷，': 'after_owner_DexterityPower',
        '你每有1点集中，': 'after_owner_FocusPower',
        '你每有一个充能球，': 'before_orb_count',
        '每因此升级一张牌，': 'upgraded_count',
        '每保留一张手牌，': 'retained_count',
        '每保留一张，': 'retained_count',
        '每保留一张具有星星耗能的手牌，': 'retained_star',
        '每因此获得1点力量，': 'power_owner_StrengthPower',
        '每花费1颗星星，': 'spent_stars',
        '由此每获得1点能量，': 'gain_energy',
        'X每有1点，': 'x_value',
        '每生成一个充能球，': 'orb_generated', '每生成一个冰霜充能球，': 'orb_generated_FrostOrb',
        '每激发一次，': 'orb_evoked',
        '每生成一张小刀，': 'generated_class_Shiv', '每生成一张灵魂，': 'generated_class_Soul',
        '每生成一张悲伤，': 'generated_class_Sadness',
        '每变化出一张巨石，': 'transformed_class_GiantRock',
        '每变化出一张燃料，': 'transformed_class_Fuel',
        '每变化出一张仆从捐躯，': 'transformed_class_MinionSacrifice',
        '每变化出一张全力射击，': 'transformed_class_TheShot',
        '若此牌变化了悲伤，': 'transformed_original_class_Sadness',
        '每失去1点，': 'hit_self_hp',
        '你的弃牌堆中每有一张不死，': 'before_discard_class_Undeath',
        '每消耗一张发射牌，': 'exhausted_Shoot',
        '每消耗一张悲伤，': 'exhausted_class_Sadness',
        '由此每获得一张随机牌，': 'generated_count',
        '由此每抽到一张喜悦或悲伤，': 'drawn_emotion',
        '每有一名其他敌人因此受到伤害，': 'other_hit_targets',
        '你在本回合每打出过一张技能牌，': 'turn_before_Skill',
        '每有一名敌人因此失去力量，': 'strength_lost_targets',
        '每有一名敌人因此同时首次获得虚弱和易伤，': 'first_weak_vulnerable_targets',
        '每有一名其他敌人因此获得负面效果，': 'other_debuff_targets',
        '每有一名敌人的灾厄因此达到10层，': 'doom_crossed_10_targets',
        '每触发一个黑暗充能球的被动，': 'orb_passive_DarkOrb',
        '每因旋律额外命中一次，': 'max(0,hit_count-1)',
        '每因此升级一张攻击牌，': 'upgraded_Attack',
        '由此每抽到一张能够获得格挡或拉伸的牌，': 'drawn_guard_or_stretch',
        '由此每抽到一张能够铸造的牌，': 'drawn_forge',
    }
    if text in metrics:
        return event, metrics[text], condition, limit
    patterns = [
        (r'若你的格挡不少于(\d+)点，', lambda m: ('1', f'before_block>={m[1]}')),
        (r'若此牌当时的能量消耗不高于(\d+)，', lambda m: ('1', f'before_cost<={m[1]}')),
        (r'若因此消耗了至少(\d+)张牌，', lambda m: ('1', f'exhausted_count>={m[1]}')),
        (r'若命中了至少(\d+)名敌人，', lambda m: ('1', f'hits_targets>={m[1]}')),
        (r'每(?:因此)?升级(\d+)张牌，', lambda m: (f'floor(upgraded_count/{m[1]})', '1')),
        (r'(?:由此每抽到|每丢弃)(\d+)张牌，', lambda m: (f'floor({"drawn" if "抽到" in m[0] else "discarded"}_count/{m[1]})', '1')),
        (r'由此每获得(\d+)点格挡，', lambda m: (f'floor(gain_block/{m[1]})', '1')),
        (r'每因此多获得(\d+)点格挡，', lambda m: (f'floor(gain_block/{m[1]})', '1')),
        (r'每因此增加(\d+)层易伤，', lambda m: (f'floor(power_VulnerablePower/{m[1]})', '1')),
        (r'每因此获得(\d+)点集中，', lambda m: (f'floor(power_owner_FocusPower/{m[1]})', '1')),
        (r'你的消耗牌堆中每有(\d+)张牌，', lambda m: (f'floor(before_exhaust/{m[1]})', '1')),
        (r'你每有(\d+)点格挡，', lambda m: (f'floor(before_block/{m[1]})', '1')),
        (r'(?:奥斯提的|他的)最大生命值每有(\d+)点，', lambda m: (f'floor(before_osty_maxhp/{m[1]})', '1')),
        (r'奥斯提的当前生命值每有(\d+)点，', lambda m: (f'floor(before_osty_hp/{m[1]})', '1')),
        (r'每获得(\d+)点拉伸，', lambda m: (f'floor(power_owner_StretchPower/{m[1]})', '1')),
        (r'若有敌人的意图伤害总和不低于(\d+)点，', lambda m: ('1', f'before_enemy_intent_total>={m[1]}')),
        (r'每(?:因此给予|因此额外给予)(\d+)层灾厄，', lambda m: (f'floor({"max(0,power_DoomPower-native_var_CalculationBase)" if "额外" in m[0] else "power_DoomPower"}/{m[1]})', '1')),
        (r'每因此重新获得(\d+)点拉伸，', lambda m: (f'floor(max(0,power_owner_StretchPower)/{m[1]})', '1')),
        (r'每生成(\d+)个闪电充能球，', lambda m: (f'floor(orb_generated_LightningOrb/{m[1]})', '1')),
        (r'每有一名敌人因立即触发的中毒失去至少(\d+)点生命，', lambda m: (f'poison_damage_targets_{m[1]}', '1')),
        (r'每使其失去(\d+)点力量，', lambda m: (f'floor(max(0,-power_StrengthPower)/{m[1]})','1')),
        (r'每移除(\d+)点格挡或1层人工制品，', lambda m: (f'floor(native_block_removed/{m[1]})+max(0,before_target_ArtifactPower-after_target_ArtifactPower)','1')),
    ]
    for pattern, result in patterns:
        if (match := re.fullmatch(pattern, text)):
            multiplier, condition = result(match)
            return event, multiplier, condition, limit
    # Combine a recognized condition with a recognized per-result count. Both
    # fragments must be consumed; unknown wording still fails closed.
    if text.startswith('若') and '，' in text[:-1]:
        first, rest = text.split('，', 1)
        _, _, left, _ = prefix_rule('打出时，' + first + '，')
        _, multiplier, right, _ = prefix_rule('打出时，' + rest)
        return event, multiplier, f'({left})&&({right})', limit
    raise Pending('Condition: ' + text)


def compile_growth(text):
    matches = list(re.finditer(GROW, text))
    if not matches:
        raise Pending('No recognized growth clause')
    prefix = text[:matches[0].start()]
    event, factor, condition, limit = prefix_rule(prefix or '打出时，')
    rules = []
    end = matches[0].start()
    for match in matches:
        between = text[end:match.start()]
        alternative = between == '；否则'
        if between not in ('', '，并', '，', '并') and not alternative:
            raise Pending('Separate growth condition: ' + between)
        if not match['active']:
            raise Pending('Non-activated growth requires an explicit exception')
        rules.append(instruction(event, 'temporary' if match['temp'] else 'grow', match['bond'],
                                 f"{match['amount']}*({factor})", f'!({condition})' if alternative else condition, limit=limit))
        end = match.end()
    tail = text[end:]
    alternative = re.fullmatch(r'；否则增加(\d+)层。', tail)
    if alternative:
        if condition == '1':
            raise Pending('An alternative requires an actual condition')
        match = matches[0]
        rules.append(instruction(event, 'temporary' if match['temp'] else 'grow', match['bond'], alternative[1], f'!({condition})', limit=limit))
        tail = ''
    if tail not in ('。', ''):
        simple = re.fullmatch(r'，并(?:额外)?(获得(\d+)点格挡|获得(\d+)颗星星|抽(\d+)张牌|对随机敌人施加(\d+)层易伤|给予(\d+)层易伤)。', tail)
        if not simple:
            raise Pending('Additional effect: ' + tail)
        amounts = simple.groups()[1:]
        action = ['block', 'stars', 'draw', 'vulnerable_random','vulnerable_target'][next(i for i, x in enumerate(amounts) if x)]
        rules.append(instruction(event, action, matches[0]['bond'], next(x for x in amounts if x),
                                 condition, limit=limit, requireActive=False))
    if limit=='battle':
        for rule in rules:
            rule['condition']=f"battle_before_source==0&&({rule['condition']})"
    return rules


def compile_special(text):
    def program(rules, amount_text=None):
        return dict(text=text,nativePowers=[],rules=rules,amountText=amount_text)
    match=re.fullmatch(r'打出时，你每有1点力量，使已激活的【狂战】增加(\d+)层；每有1点敏捷，使已激活的【坚守】增加(\d+)层。',text)
    if match:
        return program([instruction('play','grow','狂战',f'{match[1]}*max(0,after_owner_StrengthPower)'),instruction('play','grow','坚守',f'{match[2]}*max(0,after_owner_DexterityPower)')])
    match=re.fullmatch(r'打出时，每因此升级一张攻击牌，使已激活的【绝技】增加(\d+)层；升级其他牌时增加(\d+)层。',text)
    if match:
        return program([instruction('play','grow','绝技',f'{match[1]}*upgraded_Attack'),instruction('play','grow','绝技',f'{match[2]}*(upgraded_count-upgraded_Attack)')])
    match=re.fullmatch(r'打出时，使已激活的【余烬】增加(\d+)层；此牌当前的格挡值不低于10点时改为增加(\d+)层。',text)
    if match:
        return program([instruction('play','grow','余烬',match[1],'before_var_Block<10'),instruction('play','grow','余烬',match[2],'before_var_Block>=10')])
    match=re.fullmatch(r'每场战斗首次打出时，使已激活的【余烬】增加(\d+)层；你在此之前没有格挡时改为增加(\d+)层。',text)
    if match:
        return program([instruction('play','grow','余烬',match[1],'battle_before_source==0&&before_block>0'),instruction('play','grow','余烬',match[2],'battle_before_source==0&&before_block==0')])
    match=re.fullmatch(r'打出时，因此被丢弃的牌中每有一张具有奇巧，使已激活的【巧变】增加(\d+)层；否则每张使已激活的【合奏】增加(\d+)层。',text)
    if match:
        return program([instruction('play','grow','巧变',f'{match[1]}*discarded_Sly'),instruction('play','grow','合奏',f'{match[2]}*(discarded_count-discarded_Sly)')])
    match=re.fullmatch(r'打出时，每丢弃一张具有奇巧的牌，使已激活的【巧变】增加(\d+)层；丢弃其他牌时增加(\d+)层。',text)
    if match:
        return program([instruction('play','grow','巧变',f'{match[1]}*discarded_Sly+{match[2]}*(discarded_count-discarded_Sly)')])
    match=re.fullmatch(r'打出时，使已激活的【星辉】增加(\d+)层，并获得(\d+)点格挡；【星辉】每1档使这个格挡再增加(\d+)点。',text)
    if match:
        return program([instruction('play','grow','星辉',match[1]),instruction('play','block','星辉',f'{match[2]}+{match[3]}*A',requireActive=False)],f'打出时，使已激活的【星辉】增加{match[1]}层，并获得{{1}}点格挡。')
    match=re.fullmatch(r'打出时，使已激活的【先古】增加(\d+)层；它每次使你失去敏捷时，再增加(\d+)层。',text)
    if match:
        return program([instruction('play','grow','先古',match[1]),instruction('native','grow','先古',match[2],'power_owner_DexterityPower<0')])
    match=re.fullmatch(r'抽到这张牌时，使已激活的【咒缚】增加(\d+)层；战斗胜利时，若此牌在消耗牌堆中，获得(\d+)金币。',text)
    if match:
        return program([instruction('draw','grow','咒缚',match[1]),instruction('victory','gold','咒缚',match[2],'battle_exhausted>0',requireActive=False)])
    match=re.fullmatch(r'战斗胜利时，使已激活的【远见】增加(\d+)层；它变化为富足时，使已激活的【奇遇】增加(\d+)层。',text)
    if match:
        return program([instruction('victory','grow','远见',match[1]),instruction('transform','grow','奇遇',match[2],'transformed_into_Abundance>0')])
    return None


def compile_program(text,card_name=''):
    if card_name=='Entropy' and text=='由此变化出的牌，其所属的每个已激活盟约各增加等同于当前已激活盟约数量的层数。':
        return dict(text=text,nativePowers=[],rules=[
            instruction('entropy_transform','grow_entropy_bonds','Harmony',1,scope='live',requireActive=False)
        ])
    if card_name=='Unrelenting' and (next_attack:=re.fullmatch(r'你打出下一张攻击牌时，使已激活的【重装】增加等同于该牌原始费用(\d+)倍的层数。',text)):
        return dict(text=text,nativePowers=[],rules=[instruction('play','unrelenting_power','重装',next_attack[1],requireActive=False)])
    if card_name=='Spite' and text.startswith('若你在本回合失去过生命，'):
        return dict(text=text,nativePowers=[],rules=compile_growth('打出时，'+text))
    if (special:=compile_special(text)) is not None:
        return special
    resource_prefixes={
        '打出后，每当它提供的能量被用于打出原始费用至少为2的牌，':('energy','subject_heavy>0',False),
        '打出后，每当它提供的星星在同一回合被花费，':('stars','resource_issued_turn==resource_turn',False),
        '打出后，每当它提供的活力在同一回合被一张攻击牌用掉，':('VigorPower','resource_issued_turn==resource_turn&&subject_Attack>0',False),
        '打出后，每当它提供的瞄准在同一回合被一次射击用掉，':('AimPower','resource_issued_turn==resource_turn&&subject_Shoot>0',False),
        '打出后，每当它提供的拉伸在本回合被花费，':('StretchPower','resource_issued_turn==resource_turn',False),
        '打出时，若由它获得的星星在本回合被花费，':('stars','resource_issued_turn==resource_turn',True),
        '打出时，若你的活力在本回合被一张攻击牌用掉，':('VigorPower','resource_issued_turn==resource_turn&&subject_Attack>0',True),
        '此牌在下个回合提供的能量被用于打出君王之剑时，':('energy','subject_class_SovereignBlade>0',True),
    }
    for prefix,(resource,condition,once) in resource_prefixes.items():
        if text.startswith(prefix) and (match:=re.fullmatch(GROW+'。',text[len(prefix):])):
            return dict(text=text,nativePowers=[],rules=[instruction('resource_spent','temporary' if match['temp'] else 'grow',match['bond'],match['amount']+('*resource_new_grants' if once else ''),f'resource_{resource}>0&&({condition})',scope='resource',parameter='once_grant' if once else None)])
    free=re.fullmatch(r'因它而免费的那张(攻击牌|技能牌|能力牌)被打出时，使已激活的【重装】增加层数，数量为该牌原始费用(加1)?的(\d+)倍。',text)
    if free:
        return dict(text=text,nativePowers=[],rules=[instruction('subject_play','grow','重装',f'{free[3]}*(subject_cost+{1 if free[2] else 0})',scope='free',parameter={'攻击牌':'Attack','技能牌':'Skill','能力牌':'Power'}[free[1]],lifetime='next')])
    free_live=re.fullmatch(r'打出后，每当它使一张原始费用至少为2的牌免费，'+GROW+'。',text)
    if free_live:
        return dict(text=text,nativePowers=[],rules=[instruction('subject_play','temporary',free_live['bond'],free_live['amount'],'subject_heavy>0',scope='free',lifetime='next')])
    if card_name=='Veilpiercer' and text.endswith('因它而免费的虚无牌同样获得这个加成。'):
        tail='因它而免费的虚无牌同样获得这个加成。'
        program=compile_program(text[:-len(tail)],card_name)
        rule=program['rules'][0]
        program['rules'].append(instruction('damage_bonus','modifier',rule['bond'],rule['amount'],scope='free',parameter='Ethereal',lifetime='next',requireActive=False))
        program['text']=text;program['amountText']+=tail
        return program
    accelerant=re.fullmatch(r'打出后，中毒额外触发时造成的伤害增加([\d.A/]+)点（【([^】]+)】）。',text)
    if accelerant:
        return dict(text=text,nativePowers=[],rules=[instruction('power_extra_poison_bonus','modifier',accelerant[2],formula(accelerant[1]),requireActive=False)],amountText='打出后，中毒额外触发时造成的伤害增加{0}点。')
    honing=re.fullmatch(r'打出后，箭矢额外造成等于([\d.A/]+)的固定伤害（【([^】]+)】），这个伤害不受其百分比加成影响。',text)
    if honing:
        return dict(text=text,nativePowers=[],rules=[instruction('power_arrow_flat_bonus','modifier',honing[2],formula(honing[1]),requireActive=False)],amountText='打出后，箭矢额外造成{0}点固定伤害，这个伤害不受百分比加成影响。')
    counter_tails={
        'CrashLanding':('由它生成的每张碎屑使这个数值再增加1点。','play','generated_class_Debris','1',None),
        'ThrummingHatchet':('它每次回到手牌，本场战斗中再增加1点。','return_hand','1','battle_played>0',None),
        'Severance':('由它生成的灵魂每被打出一张，本场战斗中再增加1点。','subject_play','1','1','generated'),
    }
    if card_name in counter_tails and text.endswith(counter_tails[card_name][0]):
        tail,event,amount,condition,scope=counter_tails[card_name]
        program=compile_program(text[:-len(tail)],card_name)
        base=program['rules'][0];base['amount']+=' +source_bonus'
        program['rules'].append(instruction(event,'source_bonus',base['bond'],amount,condition,scope=scope or 'own',parameter='class_Soul' if scope else None,requireActive=False))
        program['text']=text;program['amountText']+=tail
        return program
    if card_name=='Jackpot' and text.endswith('由它生成的牌在本回合也获得这个伤害加成。'):
        tail='由它生成的牌在本回合也获得这个伤害加成。'
        program=compile_program(text[:-len(tail)],card_name);base=program['rules'][0]
        program['rules'].append(instruction('damage_bonus','modifier',base['bond'],base['amount'],scope='generated',lifetime='turn',requireActive=False))
        program['text']=text;program['amountText']+=tail
        return program
    if card_name=='PunchOfJoy' and text.endswith('若因此变化出了喜悦，再增加等量伤害。'):
        tail='若因此变化出了喜悦，再增加等量伤害。'
        program=compile_program(text[:-len(tail)],card_name);base=program['rules'][0]
        program['rules'].append(instruction('play','damage_target',base['bond'],base['amount'],'transformed_class_Joy>0',requireActive=False))
        program['text']=text;program['amountText']+='若因此变化出了喜悦，对目标再造成{1}点伤害。'
        return program
    spinner=re.fullmatch(r'打出后，每当它生成的玻璃充能球被激发，'+GROW+'。',text)
    if spinner:
        return dict(text=text,nativePowers=[],rules=[instruction('orb_origin_evoked','temporary',spinner['bond'],spinner['amount'],scope='orb')])
    hibernate=re.fullmatch(r'打出后的本回合，每当冰霜充能球因它提供格挡，'+GROW+'。',text)
    if hibernate:
        return dict(text=text,nativePowers=[],rules=[instruction('orb_origin_block','grow',hibernate['bond'],hibernate['amount'],'orb_created_turn==orb_turn',scope='orb')])
    tesla=re.fullmatch(r'此牌触发的每个闪电充能球被动额外造成([\d.A/]+)点伤害（【([^】]+)】）。',text)
    if tesla:
        return dict(text=text,nativePowers=[],rules=[instruction('orb_passive_bonus','modifier',tesla[2],formula(tesla[1]),requireActive=False)],amountText='此牌触发的每个闪电充能球被动额外造成{0}点伤害。')
    shatter=re.fullmatch(r'此牌额外造成(\d+)点伤害；每激发一个充能球再额外造成([\d.A/]+)点（【([^】]+)】）。',text)
    if shatter:
        return dict(text=text,nativePowers=[],rules=[instruction('damage_bonus','modifier',shatter[3],shatter[1],requireActive=False),instruction('own_orb_evoked','damage',shatter[3],formula(shatter[2]),requireActive=False)],amountText='此牌额外造成{0}点伤害。每当它激发一个充能球时，对所有敌人造成{1}点伤害。')
    quietus=re.fullmatch(r'被此牌强化的射击结算后，'+GROW+'。',text)
    if quietus:
        return dict(text=text,nativePowers=[],rules=[instruction('native','grow',quietus['bond'],quietus['amount'],'native_quietus_consumed>0')])
    blur=re.fullmatch(r'打出时，若你上回合的格挡保留到了本回合，'+GROW+'。',text)
    if blur:
        return dict(text=text,nativePowers=[],rules=[instruction('play','grow',blur['bond'],blur['amount'],'turn_carried_block>0')])
    abundance=re.fullmatch(r'获得此牌时，使【先古】增加(\d+)层；由它加入手牌的能力牌被打出时，使已激活的【仪式】增加(\d+)层。',text)
    if abundance:
        return dict(text=text,nativePowers=[],rules=[instruction('acquire','grow','先古',abundance[1],requireActive=False),instruction('subject_play','grow','仪式',abundance[2],scope='generated',parameter='Power')])
    blade=re.fullmatch(r'打出时，使由此生成的每张小刀下次打出时额外'+GROW+'。',text)
    if blade:
        return dict(text=text,nativePowers=[],rules=[instruction('subject_play','grow',blade['bond'],blade['amount'],scope='generated',parameter='class_Shiv',lifetime='next')])
    borrowed=re.fullmatch(r'打出时，使(被放回抽牌堆顶的牌|被放到抽牌堆顶的牌|被取回的牌|被取回的攻击牌|被选入手牌的牌)(下次打出时|在本回合)(造成的伤害和获得的格挡|造成的伤害|获得的格挡)增加(\d+)点(?:；若它是打击牌，改为增加(\d+)点)?。',text)
    if borrowed:
        scope='top' if '抽牌堆顶' in borrowed[1] else 'returned'
        lifetime='next' if borrowed[2]=='下次打出时' else 'turn'
        amount=borrowed[4] if not borrowed[5] else f'{borrowed[4]}+(subject_Strike>0)*({borrowed[5]}-{borrowed[4]})'
        events=(['damage_bonus'] if '伤害' in borrowed[3] else [])+(['block_bonus'] if '格挡' in borrowed[3] else [])
        return dict(text=text,nativePowers=[],rules=[instruction(event,'modifier',ASSOCIATIONS[card_name],amount,scope=scope,lifetime=lifetime,requireActive=False) for event in events])
    cloak=re.fullmatch(r'打出时，使由此生成的小刀在本回合额外造成(\d+)点伤害；【([^】]+)】每(\d+)档再增加(\d+)点。',text)
    if cloak:
        return dict(text=text,nativePowers=[],rules=[instruction('damage_bonus','modifier',cloak[2],f'{cloak[1]}+floor(A/{cloak[3]})*{cloak[4]}',scope='generated',parameter='class_Shiv',lifetime='turn',requireActive=False)],amountText='打出时，使由此生成的小刀在本回合额外造成{0}点伤害。')
    keyword=re.fullmatch(r'被它添加(奇巧|虚无|保留)的那张牌(在本回合被丢弃时|被打出时|在之后的回合被打出时)，'+GROW+'。',text)
    planner=re.fullmatch(r'打出后，每当一张因它获得奇巧的牌被丢弃，'+GROW+'。',text)
    if keyword or planner:
        match=keyword or planner
        event='subject_discard' if planner or '被丢弃' in keyword[2] else 'subject_play'
        scope='keyword_'+('Sly' if planner else {'奇巧':'Sly','虚无':'Ethereal','保留':'Retain'}[keyword[1]])
        lifetime='turn' if keyword and keyword[2].startswith('在本回合') else 'battle'
        condition='subject_before_turn>attachment_turn' if keyword and keyword[2].startswith('在之后') else '1'
        return dict(text=text,nativePowers=[],rules=[instruction(event,'temporary' if match['temp'] else 'grow',match['bond'],match['amount'],condition,scope=scope,lifetime=lifetime)])
    sorrow=re.fullmatch(r'打出时，若由它加入手牌的悲伤在本回合结束时仍在手牌，'+GROW+'。',text)
    if sorrow:
        return dict(text=text,nativePowers=[],rules=[instruction('subject_endturn','grow',sorrow['bond'],sorrow['amount'],scope='generated',parameter='class_Sadness',lifetime='turn',limit='turn')])
    thinking=re.fullmatch(r'打出时，使被放到抽牌堆顶的牌下次打出时的能量消耗减少(\d+)。',text)
    if thinking:
        return dict(text=text,nativePowers=[],rules=[instruction('play','discount_cards',ASSOCIATIONS[card_name],thinking[1],parameter='to_top',lifetime='next',requireActive=False)])
    wish=re.fullmatch(r'打出时，使被放入手牌的牌在本回合的能量消耗减少(\d+)；若此牌在上回合被保留，改为减少(\d+)。',text)
    if wish:
        return dict(text=text,nativePowers=[],rules=[instruction('play','discount_cards',ASSOCIATIONS[card_name],f'{wish[1]}+(native_retained>0)*({wish[2]}-{wish[1]})',parameter='returned',requireActive=False)])
    foregone=re.fullmatch(r'由它放入手牌的两张牌在那个回合的能量消耗各减少(\d+)。',text)
    if foregone:
        return dict(text=text,nativePowers=[],rules=[instruction('native','discount_cards',ASSOCIATIONS[card_name],foregone[1],parameter='returned',requireActive=False)])
    learning=re.fullmatch(r'打出后，每回合由它额外抽到的牌在本回合的能量消耗减少(\d+)。',text)
    if learning:
        return dict(text=text,nativePowers=[],rules=[instruction('native','discount_cards',ASSOCIATIONS[card_name],learning[1],parameter='drawn',requireActive=False)])
    collision=re.fullmatch(r'打出时，'+GROW+r'，并使由它生成的碎屑在本回合的能量消耗减少(\d+)。',text)
    if collision:
        return dict(text=text,nativePowers=[],rules=[instruction('play','grow',collision['bond'],collision['amount']),instruction('play','discount_cards',collision['bond'],collision[5],parameter='generated',requireActive=False)])
    parry=re.fullmatch(r'打出后，君王之剑因它获得的格挡增加([\d.A/]+)点（【([^】]+)】）。',text)
    if parry:
        return dict(text=text,nativePowers=[],rules=[instruction('power_sword_block_bonus','modifier',parry[2],formula(parry[1]),requireActive=False)],amountText='打出后，君王之剑因它获得的格挡增加{0}点。')
    sovereign=re.fullmatch(r'打出时，此牌因锻造获得的伤害每有(\d+)点，使已激活的【铸王】增加(\d+)层。',text)
    if sovereign:
        return dict(text=text,nativePowers=[],rules=[instruction('play','grow','铸王',f'{sovereign[2]}*floor(native_forge_damage/{sovereign[1]})')])
    murder=re.fullmatch(r'此牌从本场战斗每次抽牌获得的额外伤害，每(\d+)张牌再多计(\d+)点；【([^】]+)】每(\d+)档使这个数值再加(\d+)。',text)
    if murder:
        return dict(text=text,nativePowers=[],rules=[instruction('damage_bonus','modifier',murder[3],f'floor(native_battle_draws/{murder[1]})*({murder[2]}+floor(A/{murder[4]})*{murder[5]})',requireActive=False)],amountText='此牌额外造成{0}点伤害。')
    wither=re.fullmatch(r'这张牌在回合结束时对你造成伤害时，使已激活的【残响】增加层数，数量为所受伤害的(\d+)倍。',text)
    if wither:
        return dict(text=text,nativePowers=[],rules=[instruction('self_damage','grow','残响',f'{wither[1]}*hit_self_hp')])
    entropy=re.fullmatch(r'打出后，每回合首次变化卡牌时，获得(\d+)点格挡，并使变化后的牌在本回合的能量消耗减少(\d+)；【调和】每(\d+)档使格挡再增加(\d+)点。',text)
    if entropy:
        return dict(text=text,nativePowers=[],rules=[instruction('observe_transform','block','调和',f'{entropy[1]}+floor(A/{entropy[3]})*{entropy[4]}',scope='live',limit='turn',requireActive=False),instruction('observe_transform','discount_subject','调和',entropy[2],scope='live',limit='turn',requireActive=False)],amountText='打出后，每回合首次变化卡牌时，获得{0}点格挡，并使变化后的牌在本回合的能量消耗减少1。')
    evil=re.fullmatch(r'若你在本回合消耗过卡牌，此牌额外获得(\d+)点格挡；【([^】]+)】每(\d+)档再增加(\d+)点。',text)
    if evil:
        return dict(text=text,nativePowers=[],rules=[instruction('block_bonus','modifier',evil[2],f'{evil[1]}+floor(A/{evil[3]})*{evil[4]}','turn_exhausted>0',requireActive=False)],amountText='若你在本回合消耗过卡牌，此牌额外获得{0}点格挡。')
    ecstatic=re.fullmatch(r'打出后，每当它将一张悲伤变化为喜悦，'+GROW+'。',text)
    if ecstatic:
        return dict(text=text,nativePowers=[],rules=[instruction('native','temporary',ecstatic['bond'],f"{ecstatic['amount']}*transformed_class_Joy")])
    gaze=re.fullmatch(r'打出后，每回合首次使一名敌人失去力量时，'+GROW+'。',text)
    if gaze:
        return dict(text=text,nativePowers=[],rules=[instruction('native','temporary',gaze['bond'],gaze['amount'],'strength_lost_targets>0',limit='turn')])
    reaper=re.fullmatch(r'打出后，每回合首次由它给予灾厄时，每给予(\d+)层，使已激活的【厄兆】临时增加(\d+)层。',text)
    if reaper:
        return dict(text=text,nativePowers=[],rules=[instruction('native','temporary','厄兆',f'{reaper[2]}*floor(power_DoomPower/{reaper[1]})','power_DoomPower>0',limit='turn')])
    throne=re.fullmatch(r'打出后，每当它在一回合内提供第三颗星星，'+GROW+'。',text)
    if throne:
        return dict(text=text,nativePowers=[],rules=[instruction('native','temporary',throne['bond'],throne['amount'],'sum_before<3&&sum_after>=3',accumulate='gain_stars',lifetime='turn')])
    enlighten=re.fullmatch(r'打出时，每有一张手牌的耗能因此降低，'+GROW+'。',text)
    if enlighten:
        return dict(text=text,nativePowers=[],rules=[instruction('play','grow',enlighten['bond'],f"{enlighten['amount']}*discounted_count")])
    if card_name=='EpicRetrieval' and (match:=re.fullmatch(r'此牌使一支箭矢松脱时，'+GROW+'。',text)):
        return dict(text=text,nativePowers=[],rules=[instruction('native_card','grow',match['bond'],match['amount'],'native_method_AfterArrowDislodged>0&&gain_energy>0')])
    if card_name=='Thrash' and (match:=re.fullmatch(r'此牌从被消耗的攻击牌获得的伤害额外增加([\d.A/]+)点（【([^】]+)】）。',text)):
        return dict(text=text,nativePowers=[],rules=[instruction('play','absorb_damage_bonus',match[2],formula(match[1]),'exhausted_Attack>0',requireActive=False)],amountText='此牌从被消耗的攻击牌获得的伤害额外增加{0}点。')
    unmovable=re.fullmatch(r'打出后，每当它翻倍你的格挡，每翻倍出(\d+)点格挡，使已激活的【坚守】临时增加(\d+)层。',text)
    if unmovable:
        return dict(text=text,nativePowers=[],rules=[instruction('native','temporary','坚守',f'{unmovable[2]}*floor(native_extra_block/{unmovable[1]})','native_method_AfterModifyingBlockAmount>0')])
    shadow=re.fullmatch(r'打出后的本回合，被它翻倍出的格挡每满(\d+)点，额外获得(\d+)点格挡；【坚守】每(\d+)档再增加(\d+)点。',text)
    if shadow:
        return dict(text=text,nativePowers=[],rules=[instruction('native','block','坚守',f'floor(native_extra_block/{shadow[1]})*({shadow[2]}+floor(A/{shadow[3]})*{shadow[4]})','native_method_AfterModifyingBlockAmount>0',requireActive=False)],amountText=f'打出后的本回合，被它翻倍出的格挡每满{shadow[1]}点，额外获得{{0}}点格挡。',displayAmounts=[f'{shadow[2]}+floor(A/{shadow[3]})*{shadow[4]}'])
    corruption=re.fullmatch(r'打出后，每当一张技能牌因它被消耗，'+GROW+'。',text)
    if corruption:
        return dict(text=text,nativePowers=[],rules=[instruction('subject_exhaust','temporary',corruption['bond'],corruption['amount'],scope='forced_exhaust',parameter='Skill')])
    topgrowth=re.fullmatch(r'(?:被它放到抽牌堆顶的牌在下回合被打出时，|打出后，每当被它放回抽牌堆顶的牌在下回合被打出，|被此牌放到抽牌堆顶的牌下次打出时，)'+GROW+'。',text)
    if topgrowth:
        lifetime='next' if text.startswith('被此牌') else 'next_turn'
        return dict(text=text,nativePowers=[],rules=[instruction('subject_play','temporary' if topgrowth['temp'] else 'grow',topgrowth['bond'],topgrowth['amount'],scope='top',lifetime=lifetime)])
    alchemy=re.fullmatch(r'打出时，若你的药水栏已满，改为使已激活的【逐利】增加(\d+)层并获得(\d+)金币。',text)
    if alchemy:
        return dict(text=text,nativePowers=[],rules=[instruction('play_override','grow','逐利',alchemy[1]),instruction('play_override','gold','逐利',alchemy[2],requireActive=False)])
    covering=re.fullmatch(r'打出后，每当你发射时，你自己也获得(\d+)点格挡；【游弋】每(\d+)档再增加(\d+)点。',text)
    if covering:
        return dict(text=text,nativePowers=[],rules=[instruction('observe_shot','block','游弋',f'{covering[1]}+floor(A/{covering[2]})*{covering[3]}',scope='live',requireActive=False)],amountText='打出后，每当你发射时，你自己也获得{0}点格挡。')
    intercept=re.fullmatch(r'打出后的本回合，每格挡掉一次攻击，'+GROW+'。',text)
    if intercept:
        return dict(text=text,nativePowers=[],rules=[instruction('observe_blocked','grow',intercept['bond'],intercept['amount'],scope='live')])
    locked=re.fullmatch(r'打出时，使被放入手牌的发射牌在本回合的箭矢额外造成(\d+)点伤害。',text)
    if locked:
        return dict(text=text,nativePowers=[],rules=[instruction('damage_bonus','modifier','游弋',locked[1],scope='returned',parameter='Shoot',lifetime='turn',requireActive=False)])
    all_lineage=re.fullmatch(r'由它加入手牌的(攻击牌|牌)被打出时，使该牌所属的已激活盟约各增加(\d+)层。',text)
    if all_lineage:
        return dict(text=text,nativePowers=[],rules=[instruction('subject_play','grow_subject_bonds',ASSOCIATIONS[card_name],all_lineage[2],scope='generated',parameter='Attack' if all_lineage[1]=='攻击牌' else None,requireActive=False)])
    all_returned=re.fullmatch(r'打出时，由此每取回一张牌，使该牌所属的已激活盟约各增加(\d+)层。',text)
    if all_returned:
        return dict(text=text,nativePowers=[],rules=[instruction('play','grow_returned_bonds',ASSOCIATIONS[card_name],all_returned[1],requireActive=False)])
    technique=re.fullmatch(r'打出时，使被取回的技能牌在本回合获得的格挡增加(\d+)点，或使其叠层额外增加(\d+)层。',text)
    if technique:
        return dict(text=text,nativePowers=[],rules=[instruction('block_bonus','modifier',ASSOCIATIONS[card_name],technique[1],'subject_gains_block>0',scope='returned',parameter='Skill',lifetime='turn',requireActive=False),instruction('growth_bonus','modifier',ASSOCIATIONS[card_name],technique[2],'subject_gains_block==0',scope='returned',parameter='Skill',lifetime='turn',requireActive=False)])
    salvo=re.fullmatch(r'打出时，每保留一张手牌，使已激活的【延续】增加(\d+)层，并使其中的攻击牌下次打出时伤害增加(\d+)点。',text)
    if salvo:
        return dict(text=text,nativePowers=[],rules=[instruction('play','grow','延续',f'{salvo[1]}*retained_count'),instruction('damage_bonus','modifier','延续',salvo[2],scope='retained',parameter='Attack',lifetime='next',requireActive=False)])
    base_replay=re.fullmatch(r'(被它添加重放的那张牌首次重放时，|每场战斗中，被它添加重放的那张牌每次重放时，)'+GROW+'。',text)
    if base_replay:
        return dict(text=text,nativePowers=[],rules=[instruction('subject_play','grow',base_replay['bond'],base_replay['amount'],scope='base_replay',lifetime='next_replay' if base_replay[1].startswith('被它') else 'battle')])
    grimoire=re.fullmatch(r'每当它在战斗结束时移除一张牌，'+GROW+'。',text)
    if grimoire:
        return dict(text=text,nativePowers=[],rules=[instruction('native','grow',grimoire['bond'],f"{grimoire['amount']}*removed_count")])
    encore=re.fullmatch(r'打出后的本回合，你打出的最后一张被允许的牌使已激活的【零界】增加层数，数量为其原始费用加1的(\d+)倍。',text)
    if encore:
        return dict(text=text,nativePowers=[],rules=[instruction('native','grow','零界',f'{encore[1]}*(subject_cost+1)','native_method_AfterCardPlayed>0&&before_power_var_PlaysRemaining==1&&native_power_var_PlaysRemaining==0')])
    target_support={'DislikeYou':('本回合对同一目标的射击也获得这个加成。','power_target_arrow_bonus'),'SicEm':('此牌及本回合奥斯提对同一目标的攻击','power_target_osty_bonus')}
    if card_name=='DislikeYou' and text.endswith(target_support['DislikeYou'][0]):
        program=compile_program(text[:-len(target_support['DislikeYou'][0])],card_name)
        rule=program['rules'][0]
        program['rules'].append(instruction('power_target_arrow_bonus','modifier',rule['bond'],rule['amount'],requireActive=False))
        program['text']=text;program['amountText']+=target_support['DislikeYou'][0]
        return program
    if card_name=='SicEm' and text.startswith(target_support['SicEm'][0]):
        program=compile_program(text.replace(target_support['SicEm'][0],'此牌',1),card_name)
        rule=program['rules'][0]
        program['rules'].append(instruction('power_target_osty_bonus','modifier',rule['bond'],rule['amount'],requireActive=False))
        program['text']=text;program['amountText']=program['amountText'].replace('此牌',target_support['SicEm'][0],1)
        return program
    mad=re.fullmatch(r'打出时，按此牌的类型使对应的已激活盟约增加(\d+)层：攻击为【狂战】，技能为【坚守】，能力为【仪式】。',text)
    if mad:
        return dict(text=text,nativePowers=[],rules=[instruction('play','grow',bond,mad[1],f'native_type_{kind}>0') for kind,bond in [('Attack','狂战'),('Skill','坚守'),('Power','仪式')]])
    poor=re.fullmatch(r'此牌在回合结束时被保留在手牌中时，'+GROW+r'，并'+GROW.replace('?P<','?P<second_')+'。',text)
    if poor:
        return dict(text=text,nativePowers=[],rules=[instruction('retained','grow',poor['bond'],poor['amount']),instruction('retained','grow',poor['second_bond'],poor['second_amount'])])
    support={
        '打出后，每当一张小刀同时命中至少两名敌人，':('after_play','subject_class_Shiv>0&&subject_hits_targets>=2',''),
        '打出后，每当君王之剑同时命中至少两名敌人，':('after_play','subject_class_SovereignBlade>0&&subject_hits_targets>=2',''),
        '打出后的本回合，每当君王之剑造成双倍伤害，':('after_play','subject_class_SovereignBlade>0&&subject_hit_conquered_targets>0',''),
        '打出后，每回合首次攻击具有虚弱的敌人时，':('after_play','subject_Attack>0&&subject_hit_weak_targets>0','turn'),
        '打出后，每回合你首次用非射击的攻击命中被扎穿的敌人时，':('after_play','subject_Attack>0&&subject_Shoot==0&&subject_hit_impaled_targets>0','turn'),
        '打出后，每当被它强化的攻击牌击杀敌人，':('after_play','subject_Attack>0&&subject_turn_before_Attack==0&&subject_native_play_index==0&&subject_kills>0',''),
        '打出后的本回合，每免费打出一张原始费用至少为2的牌，':('observe_play','subject_heavy>0&&subject_spent_energy==0',''),
        '打出后，每当君王之剑重放，':('after_play','subject_class_SovereignBlade>0&&subject_native_play_index>0',''),
    }
    for prefix,(event,condition,limit) in support.items():
        if text.startswith(prefix) and (match:=re.fullmatch(GROW+'。',text[len(prefix):])):
            return dict(text=text,nativePowers=[],rules=[instruction(event,'temporary' if match['temp'] else 'grow',match['bond'],match['amount'],condition,scope='live',limit=limit)])
    sneaky=re.fullmatch(r'打出后，每当一张具有奇巧的牌被丢弃，获得(\d+)点格挡。',text)
    if sneaky:
        return dict(text=text,nativePowers=[],rules=[instruction('observe_discard','block','巧变',sneaky[1],'subject_native_was_sly>0',scope='live',requireActive=False)])
    dark=re.fullmatch(r'打出后，每回合首次有牌被消耗时，额外抽(\d+)张牌。',text)
    if dark:
        return dict(text=text,nativePowers=[],rules=[instruction('observe_exhaust','draw','余烬',dark[1],scope='live',limit='turn',requireActive=False)])
    cruelty=re.fullmatch(r'打出后，每回合首次给予易伤时，使手牌中一张随机攻击牌在本回合的能量消耗减少(\d+)。',text)
    if cruelty:
        return dict(text=text,nativePowers=[],rules=[instruction('observe_power','discount_attack','重装',cruelty[1],'observed_VulnerablePower>0',scope='live',limit='turn',requireActive=False)])
    plans=re.fullmatch(r'打出后，在你的回合结束时，你每保留一张手牌，使已激活的【延续】临时增加(\d+)层。',text)
    if plans:
        return dict(text=text,nativePowers=[],rules=[instruction('observe_retained','temporary','延续',f'{plans[1]}*retained_count',scope='live')])
    beacon=re.fullmatch(r'打出后，每回合你通过卡牌获得的格挡首次达到(\d+)点时，使已激活的【坚守】临时增加(\d+)层。',text)
    if beacon:
        return dict(text=text,nativePowers=[],rules=[instruction('observe_card_block','temporary','坚守',beacon[2],f'sum_before<{beacon[1]}&&sum_after>={beacon[1]}',scope='live',accumulate='observed_block',lifetime='turn')])
    replay=re.fullmatch(r'被它额外打出的那张(能力牌)打出时，'+GROW+'。',text)
    if replay:
        return dict(text=text,nativePowers=[],rules=[instruction('subject_play','grow',replay['bond'],replay['amount'],scope='replay',lifetime='next_replay',parameter='Power')])
    verse=re.fullmatch(r'每当一张牌因它被额外打出一次，'+GROW+'。',text)
    if verse:
        return dict(text=text,nativePowers=[],rules=[instruction('subject_play','grow',verse['bond'],verse['amount'],scope='replay',lifetime='next_replay')])
    echo=re.fullmatch(r'打出后，每当它使一张牌被打出两次，使已激活的【([^】]+)】临时增加层数，数量为该牌原始费用加1的(\d+)倍。',text)
    if echo:
        return dict(text=text,nativePowers=[],rules=[instruction('subject_play','temporary',echo[1],f'{echo[2]}*(subject_cost+1)',scope='replay',lifetime='next_replay')])
    replay_bonus=re.fullmatch(r'被它额外打出的那张(攻击牌|技能牌)(造成的伤害|获得的格挡)增加(\d+)点；【([^】]+)】每(\d+)档再增加(\d+)点。',text)
    if replay_bonus:
        event='damage_bonus' if replay_bonus[1]=='攻击牌' else 'block_bonus'
        return dict(text=text,nativePowers=[],rules=[instruction(event,'modifier',replay_bonus[4],f'{replay_bonus[3]}+floor(A/{replay_bonus[5]})*{replay_bonus[6]}',scope='replay',lifetime='next_replay',requireActive=False)],amountText=f'被它额外打出的那张{replay_bonus[1]}{replay_bonus[2]}增加{{0}}点。')
    native_notes={'Hang':'这个数值同样被吊杀翻倍。','Fisticuffs':'由此获得的格挡同样增加。','BlossomShot':'由此获得的拉伸同样增加。'}
    if card_name in native_notes and text.endswith(native_notes[card_name]):
        program=compile_program(text[:-len(native_notes[card_name])],card_name)
        program['text']=text;program['amountText']+=native_notes[card_name]
        return program
    copy_prefix={'AdaptiveStrike':'此牌及其复制品','HeirloomHammer':'此牌及由它复制出的牌'}
    if card_name in copy_prefix and text.startswith(copy_prefix[card_name]):
        prefix=copy_prefix[card_name]
        program=compile_program(text.replace(prefix,'此牌',1),card_name)
        program['text']=text;program['amountText']=program['amountText'].replace('此牌',prefix,1)
        return program
    conditional_tails={
        'Conflagration': ('命中具有易伤的敌人时再增加1点。','1','after_target_VulnerablePower>0'),
        'DaggerSpray': ('命中至少两名敌人时再增加1点。','1','before_enemy_count>=2'),
        'Hyperbeam': ('你每有一个充能球再增加1点。','before_orb_count','1'),
        'Null': ('你每有一个黑暗充能球再增加2点。','2*before_orb_DarkOrb','1'),
        'MightyWeapon': ('你的抽牌堆中每有一张晕眩再增加1点。','before_draw_class_Dazed','1'),
        'TagTeam': ('目标的意图是攻击时再增加2点。','2','before_target_attacking>0'),
        'HelixDrill': ('你在本回合花费过至少4点能量时再增加1点。','1','turn_energy_spent>=4'),
        'Radiate': ('你在本回合获得过至少5颗星星时再增加1点。','1','turn_stars_gained>=5'),
    }
    if card_name in conditional_tails and text.endswith(conditional_tails[card_name][0]):
        tail,amount,condition=conditional_tails[card_name]
        if not text.endswith(tail):raise Pending('Unexpected conditional passive: '+text)
        program=compile_program(text[:-len(tail)],card_name)
        program['text']=text;program['amountText']+=tail
        program['rules'].append(instruction('damage_bonus','modifier',program['rules'][0]['bond'],amount,condition,requireActive=False))
        return program
    double_tails={
        'Debilitate': ('目标已有易伤或虚弱时，这个数值翻倍。','after_target_VulnerablePower>0||after_target_WeakPower>0'),
        'MindBlast': ('你的抽牌堆不少于10张时这个数值翻倍。','before_draw>=10'),
        'SkyShot': ('目标没有被扎穿时这个数值翻倍。','before_target_ImpaledPower==0'),
    }
    if card_name in double_tails and text.endswith(double_tails[card_name][0]):
        tail,condition=double_tails[card_name]
        if not text.endswith(tail):raise Pending('Unexpected doubled passive: '+text)
        program=compile_program(text[:-len(tail)],card_name)
        base=program['rules'][0]['amount'];program['displayAmounts']=[base]
        program['rules'][0]['amount']=f'({base})*(1+({condition}))'
        program['text']=text;program['amountText']+=tail
        return program
    fight=re.fullmatch(r'打出时，获得(\d+)点格挡；【([^】]+)】每(\d+)档再增加(\d+)点。',text)
    if fight:
        return dict(text=text,nativePowers=[],rules=[instruction('play','block',fight[2],f'{fight[1]}+floor(A/{fight[3]})*{fight[4]}',requireActive=False)],amountText='打出时，获得{0}点格挡。')
    whirlwind=re.fullmatch(r'打出时，X每有1点，获得(\d+)点格挡。',text)
    if whirlwind:
        return dict(text=text,nativePowers=[],rules=[instruction('play','block','重装',f'{whirlwind[1]}*x_value',requireActive=False)])
    colossus=re.fullmatch(r'打出时，每有一名具有易伤的敌人，额外获得(\d+)点格挡。',text)
    if colossus:
        return dict(text=text,nativePowers=[],rules=[instruction('play','block','坚守',f'{colossus[1]}*before_enemy_VulnerablePower_targets',requireActive=False)])
    dagger=re.fullmatch(r'此牌额外造成等于其升级次数的伤害；【([^】]+)】每(\d+)档再增加(\d+)点。',text)
    if dagger:
        return dict(text=text,nativePowers=[],rules=[instruction('damage_bonus','modifier',dagger[1],f'upgrade_level+floor(A/{dagger[2]})*{dagger[3]}',requireActive=False)],amountText='此牌额外造成{0}点伤害。')
    tower=re.fullmatch(r'此牌额外造成等于([\d.A/]+)乘以当前幕数的伤害（【([^】]+)】）。',text)
    if tower:
        return dict(text=text,nativePowers=[],rules=[instruction('damage_bonus','modifier',tower[2],f'({formula(tower[1])})*act',requireActive=False)],amountText='此牌额外造成{0}点伤害。')
    times=re.fullmatch(r'此牌额外造成等于目标灾厄层数乘以A再除以(\d+)的伤害（【([^】]+)】）。',text)
    if times:
        return dict(text=text,nativePowers=[],rules=[instruction('damage_bonus','modifier',times[2],f'target_doom*A/{times[1]}',requireActive=False)],amountText='此牌额外造成约等于目标灾厄层数{0}%的伤害。',displayAmounts=[f'100*A/{times[1]}'],displayFormats=['0.##'])
    victory=re.fullmatch(r'战斗胜利时，(回复|获得)(\d+)(点生命值|金币)；【([^】]+)】每(\d+)档再增加(\d+)(点|金币)。',text)
    if victory:
        action='heal' if victory[1]=='回复' else 'gold'
        return dict(text=text,nativePowers=[],rules=[instruction('victory',action,victory[4],f'{victory[2]}+floor(A/{victory[5]})*{victory[6]}',requireActive=False)],amountText=f'战斗胜利时，{victory[1]}{{0}}{victory[3]}。')
    shinies=re.fullmatch(r'战斗胜利时，若此牌在本场战斗被打出过，获得(\d+)金币；【([^】]+)】每(\d+)档再增加(\d+)金币。',text)
    if shinies:
        return dict(text=text,nativePowers=[],rules=[instruction('victory','gold',shinies[2],f'{shinies[1]}+floor(A/{shinies[3]})*{shinies[4]}','battle_played>0',requireActive=False)],amountText='战斗胜利时，若此牌在本场战斗被打出过，获得{0}金币。')
    axe=re.fullmatch(r'战斗胜利时，若此牌在本场战斗造成过至少(\d+)点伤害，获得(\d+)金币。',text)
    if axe:
        return dict(text=text,nativePowers=[],rules=[instruction('victory','gold','逐利',axe[2],f'battle_damage>={axe[1]}',requireActive=False)])
    lantern=re.fullmatch(r'商店商品的价格降低(\d+)金币；【([^】]+)】每(\d+)档再降低(\d+)金币。',text)
    if lantern:
        return dict(text=text,nativePowers=[],rules=[instruction('shop_discount','modifier',lantern[2],f'{lantern[1]}+floor(A/{lantern[3]})*{lantern[4]}',requireActive=False)],amountText='商店商品的价格降低{0}金币。')
    royalties=re.fullmatch(r'打出后，它在战斗结束时提供的金币增加([\d.A/]+)（【([^】]+)】）。',text)
    if royalties:
        return dict(text=text,nativePowers=[],rules=[instruction('power_gold_bonus','modifier',royalties[2],formula(royalties[1]),requireActive=False)],amountText='打出后，它在战斗结束时提供的金币增加{0}。')
    pillage=re.fullmatch(r'打出时，由此每抽到一张攻击牌，本场战斗胜利时额外获得(\d+)金币。',text)
    if pillage:
        return dict(text=text,nativePowers=[],rules=[instruction('play','bank_gold','逐利',f'{pillage[1]}*drawn_Attack',requireActive=False)])
    costgrowth=re.fullmatch(r'打出(?:这张牌)?时，使已激活的【([^】]+)】增加层数，数量为(?:此牌|其)(当前能量消耗(?:加2)?|原始费用(?:加1)?)的(\d+)倍。',text)
    if costgrowth:
        cost='before_cost' if costgrowth[2].startswith('当前') else 'before_printed_cost'
        if '加2' in costgrowth[2]:cost+=' +2'
        if '加1' in costgrowth[2]:cost+=' +1'
        return dict(text=text,nativePowers=[],rules=[instruction('play','grow',costgrowth[1],f'{costgrowth[3]}*({cost})')])
    counted=re.fullmatch(r'打出时，(?:由此每打出一张(?:攻击牌|牌)|)(?:，)?使已激活的【([^】]+)】增加层数，数量为(?:该牌|被消耗牌)原始费用(加1)?的(\d+)倍。',text)
    if counted:
        metric=('exhausted' if '被消耗牌' in text else 'played')+('_costplus_sum' if counted[2] else '_cost_sum')
        return dict(text=text,nativePowers=[],rules=[instruction('play','grow',counted[1],f'{counted[3]}*{metric}')])
    endturn_count=re.fullmatch(r'此牌在回合结束时每打出1张牌，使已激活的【([^】]+)】增加等同于该牌原始费用加1的层数。',text)
    if endturn_count:
        return dict(text=text,nativePowers=[],rules=[instruction('native','grow',endturn_count[1],'played_costplus_sum')])
    native_count=re.fullmatch(r'打出后，每当它(?:在回合结束时)?打出(?:抽牌堆顶的牌|一张攻击牌|一张打击牌|一张牌)，使已激活的【([^】]+)】临时增加层数，数量为该牌原始费用(加1)?的(\d+)倍。',text)
    if native_count:
        return dict(text=text,nativePowers=[],rules=[instruction('native','temporary',native_count[1],f'{native_count[3]}*played_cost{"plus" if native_count[2] else ""}_sum')])
    power_bonus=re.fullmatch(r'(?:打出后，它每(?:次|回合)|此牌)(造成的伤害|提供的格挡|使敌人每次失去的生命|使敌人失去的生命)增加([\d.A/]+)点（【([^】]+)】）。',text)
    if power_bonus:
        event='power_block_bonus' if power_bonus[1]=='提供的格挡' else 'power_damage_bonus'
        return dict(text=text,nativePowers=[],rules=[instruction(event,'modifier',power_bonus[3],formula(power_bonus[2]),requireActive=False)],amountText=text.replace(power_bonus[2]+'点','{0}点',1))
    modifiers={
        'Accuracy': ('打出后，小刀额外造成的伤害再增加','ModifyDamageAdditive','1'),
        'Calcify': ('打出后，它使奥斯提攻击额外造成的伤害增加','ModifyDamageAdditive','1'),
        'OneForAll': ('打出后，它使原始费用为0的攻击牌额外造成的伤害增加','ModifyDamageAdditive','subject_zero>0&&subject_Attack>0'),
        'Fasten': ('打出后，它使防御牌额外获得的格挡增加','ModifyBlockAdditive','1'),
    }
    if card_name in modifiers:
        prefix,method,condition=modifiers[card_name]
        match=re.fullmatch(re.escape(prefix)+r'([\d.A/]+)点（【([^】]+)】）。',text)
        if not match:
            raise Pending('Unexpected native modifier text: '+text)
        return dict(text=text,nativePowers=[],rules=[instruction('power_modifier_bonus','modifier',match[2],formula(match[1]),condition,parameter=method,requireActive=False)],amountText=text.replace(match[1]+'点','{0}点',1))
    child=re.fullmatch(r'打出后，它每花费1颗星星提供的格挡增加([\d.A/]+)点（【([^】]+)】）。',text)
    if child:
        return dict(text=text,nativePowers=[],rules=[instruction('power_block_bonus','modifier',child[2],f'floor({formula(child[1])})*native_arg_amount',requireActive=False)],amountText=text.replace(child[1]+'点','{0}点',1),displayAmounts=[formula(child[1])])
    dual=re.fullmatch(r'此牌额外(获得|造成)(\d+)点(格挡|伤害)并额外(造成|获得)(\d+)点(伤害|格挡)；【([^】]+)】每(\d+)档各再增加(\d+)点。',text)
    if dual:
        return dict(text=text,nativePowers=[],rules=[instruction('block_bonus' if kind=='格挡' else 'damage_bonus','modifier',dual[7],f'{amount}+floor(A/{dual[8]})*{dual[9]}',requireActive=False) for amount,kind in [(dual[2],dual[3]),(dual[5],dual[6])]],amountText=f'此牌额外{dual[1]}{{0}}点{dual[3]}并额外{dual[4]}{{1}}点{dual[6]}。')
    variable_patterns=[
        r'此牌从.+?获得的(?:额外)?伤害(?:增加值)?提高([\d.A/]+)点（【([^】]+)】）。',
        r'此牌使所有(?:爪击|撕咬)增加的伤害提高([\d.A/]+)点（【([^】]+)】）。',
        r'此牌每次打出后的伤害增量提高([\d.A/]+)点（【([^】]+)】）。',
        r'此牌每次打出后(?:永久)?增加的伤害提高([\d.A/]+)点（【([^】]+)】）。',
        r'此牌每次被抽到时增加的伤害提高([\d.A/]+)点（【([^】]+)】）。',
        r'打出后，它每回合增加的伤害提高([\d.A/]+)点（【([^】]+)】）。',
        r'此牌使敌人失去的力量增加([\d.A/]+)点（【([^】]+)】）。',
        r'此牌因本回合此前命中而额外铸造的数值提高([\d.A/]+)（【([^】]+)】）。',
        r'此牌从被消耗的攻击牌获得的伤害额外增加([\d.A/]+)点（【([^】]+)】）。',
    ]
    for pattern in variable_patterns:
        if (match:=re.fullmatch(pattern,text)):
            if not card_name:
                raise Pending('Native variable rule requires a card identity')
            source=(NATIVE_ROOT/'MegaCrit.Sts2.Core.Models.Cards'/(card_name+'.cs')).read_text('utf-8')
            fixed={'Claw':'Increase','Maul':'Increase','Rampage':'Increase','TheBall':'Increase','TheScythe':'Increase','KinglyPunch':'Increase','RollingBoulder':'IncrementAmount','Mangle':'StrengthLoss','BeatIntoShape':'CalculationExtra'}
            variable=fixed.get(card_name)
            if variable is None:
                variable='ExtraDamage' if 'new ExtraDamageVar(' in source else 'CalculationExtra' if 'new CalculationExtraVar(' in source else None
            if variable is None:
                raise Pending('Unresolved native coefficient variable: '+card_name)
            return dict(text=text,nativePowers=[],rules=[instruction('variable','modifier',match[2],formula(match[1]),parameter=variable,requireActive=False)],amountText=text.replace(match[1]+'点','{0}点',1).replace(match[1]+'（','{0}（',1))
    changed=re.fullmatch(r'每当此牌变化为悲伤，'+GROW+'。',text)
    if changed:
        return dict(text=text,nativePowers=[],rules=[instruction('transform','grow',changed['bond'],changed['amount'],'transformed_into_Sadness>0')])
    source_transform=re.fullmatch(r'由它变化出的(仆从打击|仆从俯冲|灵魂)被打出时，使已激活的【([^】]+)】增加(\d+)层。',text)
    if source_transform:
        target={'仆从打击':'MinionStrike','仆从俯冲':'MinionDiveBomb','灵魂':'Soul'}[source_transform[1]]
        return dict(text=text,nativePowers=[],rules=[instruction('subject_play','grow',source_transform[2],source_transform[3],scope='transformed',parameter='class_'+target)])
    jest=re.fullmatch(r'由它加入手牌的悲伤被变化为喜悦时，'+GROW+'。',text)
    if jest:
        return dict(text=text,nativePowers=[],rules=[instruction('subject_transform','grow',jest['bond'],jest['amount'],'transformed_into_Joy>0',scope='generated',parameter='class_Sadness')])
    self_events = {
        '这张牌使你获得虚弱时，': ('self_power','power_owner_WeakPower>0'),
        '这张牌使你获得脆弱时，': ('self_power','power_owner_FrailPower>0'),
        '每当此牌在回合开始时回到你的手牌，': ('return_hand','native_return_at_start>0'),
        '每当此牌从弃牌堆回到手牌，': ('return_hand','native_return_from_Discard>0'),
        '每当此牌因你打出技能牌而回到手牌，': ('return_hand','native_method_AfterCardPlayedLate>0'),
        '每当此牌因你发射箭矢而回到手牌，': ('return_hand','native_method_AfterArrowShot>0'),
        '每当此牌从消耗牌堆被打出，': ('play','native_from_Exhaust>0'),
        '每当此牌在回合结束时从抽牌堆顶被自动打出，': ('play','native_autoplay>0&&native_from_Draw>0&&native_parent_endturn>0'),
        '每当此牌因奇巧被自动打出，': ('play','native_sly>0'),
    }
    for prefix,(event,condition) in self_events.items():
        if text.startswith(prefix):
            tail=text[len(prefix):]
            bonus=re.fullmatch(r'(.+?)；若因此击杀了敌人，再增加(\d+)层。',tail)
            rules=compile_growth('打出时，'+(bonus[1]+'。' if bonus else tail))
            for rule in rules:
                rule['event']=event;rule['condition']=f"({condition})&&({rule['condition']})"
            if bonus:
                rules.append(instruction(event,'grow',rules[0]['bond'],bonus[2],f'({condition})&&kills>0'))
            return dict(text=text,nativePowers=[],rules=rules)
    echo=re.fullmatch(r'每当此牌因击杀而重复，'+GROW+'。',text)
    if echo:
        return dict(text=text,nativePowers=[],rules=[instruction('play','grow',echo['bond'],f"{echo['amount']}*kills")])
    show=re.fullmatch(r'每当此牌因敌人死亡而永久增加瞄准，'+GROW+'。',text)
    if show:
        return dict(text=text,nativePowers=[],rules=[instruction('native','grow',show['bond'],show['amount'],'native_var_AimPower>before_var_AimPower')])
    observed = {
        '打出后的本回合，每打出一张实际花费至少2点能量的牌，': ('observe_play', 'subject_spent_energy>=2', ''),
        '打出后的本回合，你打出的每张攻击牌': ('observe_play', 'subject_Attack>0', ''),
        '打出后，每回合首次实际花费至少3点能量打出一张牌时，': ('observe_play', 'subject_spent_energy>=3', 'turn'),
        '打出后，你每回合打出的第一张小刀': ('observe_play', 'subject_class_Shiv>0', 'turn'),
        '打出后，每回合你首次生成灵魂时，': ('observe_generate', 'subject_class_Soul>0', 'turn'),
    }
    for prefix, (event, condition, limit) in observed.items():
        if text.startswith(prefix) and (match := re.fullmatch(GROW+'。', text[len(prefix):])):
            rule=instruction(event,'temporary' if match['temp'] else 'grow',match['bond'],match['amount'],condition,scope='live',limit=limit)
            return dict(text=text,nativePowers=[],rules=[rule])
    hand = re.fullmatch(r'你在持有这张牌的回合打出第三张牌时，'+GROW+'。', text)
    if hand:
        return dict(text=text,nativePowers=[],rules=[instruction('observe_play','grow',hand['bond'],hand['amount'],'subject_turn_before_play==2',scope='hand')])
    barricade = re.fullmatch(r'打出后，在你的回合开始时，你每有(\d+)点格挡，使已激活的【([^】]+)】临时增加(\d+)层。',text)
    if barricade:
        return dict(text=text,nativePowers=[],rules=[instruction('turn_start','temporary',barricade[2],f'{barricade[3]}*floor(before_block/{barricade[1]})',scope='live')])
    accumulation = re.fullmatch(r'打出后，每回合它提供的格挡每满(\d+)点，使已激活的【([^】]+)】临时增加(\d+)层。',text)
    if accumulation:
        return dict(text=text,nativePowers=[],rules=[instruction('native','temporary',accumulation[2],f'{accumulation[3]}*(floor(sum_after/{accumulation[1]})-floor(sum_before/{accumulation[1]}))',accumulate='gain_block',lifetime='turn')])
    delayed_benefit = re.fullmatch(r'此牌在下个回合提供收益时，'+GROW+'。',text)
    if delayed_benefit:
        return dict(text=text,nativePowers=[],rules=[instruction('native','grow',delayed_benefit['bond'],delayed_benefit['amount'],'gain_energy>0||drawn_count>0',limit='turn')])
    lineage_prefixes = {
        '由它生成的墨影小刀被打出时，': ('class_Shiv', 'battle'),
        '由它生成的复制品被打出时，': (None, 'battle'),
        '由它生成的无色牌被打出时，': ('colorless', 'battle'),
        '由它加入手牌的牌被打出时，': (None, 'battle'),
        '由它加入手牌的无色牌被打出时，': ('colorless', 'battle'),
        '由它加入手牌的能力牌被打出时，': ('Power', 'battle'),
        '由它加入手牌的技能牌被打出时，': ('Skill', 'battle'),
        '由它加入手牌的发射牌被打出时，': ('Shoot', 'battle'),
        '由它加入抽牌堆的攻击牌被打出时，': ('Attack', 'battle'),
        '由此获得的攻击牌被打出时，': ('Attack', 'battle'),
        '由它生成的能力牌被打出时，': ('Power', 'battle'),
        '由它生成的灵魂被打出时，': ('class_Soul', 'battle'),
        '打出后，每当由它生成的牌被打出，': (None, 'battle'),
        '打出后，每当由它生成的能力牌被打出，': ('Power', 'battle'),
        '打出后，每当由它生成的无色牌被打出，': ('colorless', 'battle'),
        '打出后，每当由它生成的普通牌被打出，': ('common', 'battle'),
        '打出后，每当由它生成的塔罗牌被打出，': ('tarot', 'battle'),
        '打出后，每当由它生成的扫荡凝视被打出，': ('class_SweepingGaze', 'battle'),
        '打出后，每当由它生成的攻击牌在同一回合被打出，': ('Attack', 'turn'),
        '打出时，若由此生成的小刀在本回合被打出，': ('class_Shiv', 'turn'),
    }
    for prefix, (filter_name, lifetime) in lineage_prefixes.items():
        if text.startswith(prefix):
            rest = text[len(prefix):]
            extra=re.fullmatch(r'(.+)，并获得(\d+)点格挡。',rest)
            growth=extra[1]+'。' if extra else rest
            simple = re.fullmatch(GROW + '。', growth)
            scaled = re.fullmatch(r'使已激活的【([^】]+)】增加层数，数量为其原始费用加1的(\d+)倍。', rest)
            if not simple and not scaled:
                raise Pending('Additional lineage clause: ' + rest)
            if simple:
                if not simple['active']:
                    raise Pending('Ungated lineage growth')
                rule = instruction('subject_play', 'temporary' if simple['temp'] else 'grow', simple['bond'], simple['amount'])
            else:
                rule = instruction('subject_play', 'grow', scaled[1], f'{scaled[2]}*(subject_cost+1)')
            rule.update(scope='generated', lifetime=lifetime, parameter=filter_name)
            rules=[rule]
            if extra:
                rules.append(instruction('subject_play','block',rule['bond'],extra[2],scope='generated',lifetime=lifetime,parameter=filter_name,requireActive=False))
            return dict(text=text, nativePowers=[], rules=rules)
    passive = re.fullmatch(r'此牌(?P<repeat>每次)?额外(?P<verb>造成|获得)(?P<base>\d+)点(?P<kind>伤害|格挡)；【(?P<bond>[^】]+)】每(?P<unit>\d+)档再增加(?P<step>\d+)点。', text)
    if passive:
        expected_verb = '造成' if passive['kind'] == '伤害' else '获得'
        if passive['verb'] != expected_verb:
            raise Pending('Invalid passive wording: ' + text)
        event = 'damage_bonus' if passive['kind'] == '伤害' else 'block_bonus'
        rule = instruction(event, 'modifier', passive['bond'],
                           f"{passive['base']}+floor(A/{passive['unit']})*{passive['step']}", requireActive=False)
        return dict(text=text, nativePowers=[], rules=[rule],
                    amountText=f"此牌{passive['repeat'] or ''}额外{passive['verb']}{{0}}点{passive['kind']}。")
    native = {
        '打出后，每当它因敌人死亡而提供收益，': ('1','native_method_AfterDeath>0&&gain_energy>0'),
        '打出后，每当它因箭矢松脱而提供收益，': ('1','native_method_AfterArrowDislodged>0&&gain_energy>0'),
        '打出后，每当它在洗牌时将一张牌放入你的手牌，': ('returned_count','native_method_AfterShuffle>0'),
        '打出后，每当它打出一张打击牌，': ('played_Strike','1'),
        '打出后，每回合首次由它给予中毒时，': ('1','power_PoisonPower>0'),
        '打出后，每当它使一名敌人的灾厄不低于其生命值，': ('doom_reached_hp_targets','1'),
        '打出后，每当它使一名敌人的中毒达到10层的倍数，': ('poison_crossed_10','1'),
        '打出后的本回合，每当荆棘对攻击者造成伤害，': ('hit_count','1'),
        '打出后，每当荆棘对攻击者造成伤害，': ('hit_count','1'),
        '打出后，在你的回合开始时，': ('1', 'power_owner_StrengthPower>0'),
        '打出后，每当它提供能量，': ('1', 'gain_energy>0'),
        '打出后，每当它使你获得力量，': ('1', 'power_owner_StrengthPower>0'),
        '打出后的本回合，每当它使你获得力量，': ('1', 'power_owner_StrengthPower>0'),
        '打出后，每当它使你失去生命，': ('1', 'hit_self_hp>0'),
        '打出后，每当它使你抽牌，': ('1', 'drawn_count>0'),
        '打出后，每当它使你在回合开始时抽牌，': ('1', 'drawn_count>0'),
        '打出后，每当它铸造，': ('native_forges', '1'),
        '打出后，每当它召唤，': ('native_summons', '1'),
        '打出后，每当它生成闪电充能球，': ('orb_generated_LightningOrb', '1'),
        '打出后，每当它生成充能球，': ('orb_generated', '1'),
        '打出后，它每生成一张小刀，': ('generated_class_Shiv', '1'),
        '打出后，每当它使敌人失去生命，': ('1', 'hit_unblocked>0'),
        '打出后，每当它对敌人造成伤害，': ('hit_count', '1'),
        '打出后，每当由它额外抽到的牌具有虚无，': ('drawn_Ethereal', '1'),
        '打出后，每当由它额外抽到的牌是攻击牌，': ('drawn_Attack', '1'),
        '打出后，每当由它抽到的牌是哇、啾或叽牌，': ('drawn_melody', '1'),
        '打出后，每当它升级一张牌，': ('upgraded_count', '1'),
        '打出后，每当它复制一张攻击牌，': ('generated_Attack', '1'),
        '打出后，每当它将一张攻击牌放回手牌，': ('returned_Attack', '1'),
        '打出后，每当它丢弃的牌具有奇巧，': ('discarded_Sly', '1'),
        '打出后的本回合，每当它使你获得格挡，': ('1', 'gain_block>0'),
        '打出后的本回合，每当它对攻击者造成伤害，': ('hit_count', '1'),
        '打出后的本回合，每反弹一次攻击伤害，': ('hit_count', '1'),
        '打出后的本回合，每当它给予中毒，': ('1', 'power_PoisonPower>0'),
        '打出后的本回合，每当它给予灾厄，': ('1', 'power_DoomPower>0'),
        '打出后，每当它给予你灾厄，': ('1', 'power_owner_DoomPower>0'),
        '打出后，每当它消耗的是状态牌或诅咒牌，': ('exhausted_waste', '1'),
        '打出后，每当它在回合结束时激发充能球，': ('1', 'orb_evoked>0'),
        '打出后，每当它触发的是黑暗或等离子充能球的被动，': ('orb_passive_DarkOrb+orb_passive_PlasmaOrb', '1'),
        '打出后，每当它因你集齐所有旋律而提供格挡，': ('1', 'gain_block>0'),
        '此牌在下个回合提供能量时，': ('1', 'gain_energy>0'),
        '此牌在下个回合提供星星时，': ('1', 'gain_stars>0'),
        '此牌在下个回合提供格挡时，': ('1', 'gain_block>0'),
        '此牌在之后的回合提供格挡时，': ('1', 'gain_block>0'),
        '此牌在之后的回合生成充能球时，': ('1', 'orb_generated>0'),
        '此牌在下个回合召唤时，': ('1', 'native_summons>0'),
        '此牌在下个回合生效时，': ('1', 'gain_energy>0'),
        '每当它阻止一次生命值损伤，': ('1', 'native_method_AfterModifyingHpLostAfterOsty>0'),
        '此牌使一支箭矢松脱时，': ('native_dislodges', '1'),
        '它在3回合后造成伤害时，每击杀一名敌人，': ('kills', '1'),
    }
    for prefix, (factor, condition) in native.items():
        if text.startswith(prefix) and (match := re.fullmatch(GROW + '。', text[len(prefix):])):
            if not match['active']:
                raise Pending('Native growth must require activation')
            return dict(text=text, nativePowers=[], rules=[instruction('native', 'temporary' if match['temp'] else 'grow',
                        match['bond'], f"{match['amount']}*({factor})", condition,limit='turn' if '每回合首次' in prefix else '')])
    delayed = {
        '此牌在下个回合提供收益时，': 'gain_energy>0||drawn_count>0',
        '此牌在下个回合使你抽牌时，': 'drawn_count>0',
        '此牌在下个回合造成伤害时，': 'hit_count>0',
        '此牌在下个回合提供格挡时，': 'gain_block>0',
    }
    for prefix, gate in delayed.items():
        if text.startswith(prefix):
            rules=compile_growth('打出时，'+text[len(prefix):])
            for rule in rules:
                rule['event']='native'
                rule['condition']=f"({gate})&&({rule['condition']})"
            return dict(text=text,nativePowers=[],rules=rules)
    rules = compile_growth(text)
    return dict(text=text, nativePowers=[], rules=rules)


def build(*,write_artifacts=True):
    proposal = json.loads((PROPOSAL / 'card-revisions.json').read_text('utf-8'))
    expected = {card['card']: card for card in proposal['cards']}
    authored = {}
    for file in sorted((PROPOSAL / 'authored').glob('*.txt')):
        for line in file.read_text('utf-8-sig').splitlines():
            if not line or line.startswith('#'):
                continue
            name, layer, text = line.split('|', 2)
            if name in authored:
                raise ValueError('Duplicate card ' + name)
            if expected[name]['proposed'] != text:
                raise ValueError('Proposal/authored mismatch ' + name)
            authored[name] = (layer, text)
    if set(authored) != set(expected):
        raise ValueError('The authored card list must match the reviewed proposal')
    native_root=NATIVE_ROOT
    power_cache={}
    def power_source(name,seen):
        if name in seen:return ''
        path=native_root/'MegaCrit.Sts2.Core.Models.Powers'/(name+'.cs')
        if not path.exists():return ''
        if name not in power_cache:power_cache[name]=path.read_text('utf-8')
        code=power_cache[name]
        return code+'\n'+'\n'.join(power_source(child,seen|{name}) for child in set(re.findall(r'PowerCmd\.Apply<([A-Za-z0-9_]+)>',code)))
    native_snapshot=json.loads(NATIVE_EXPORT.read_text('utf-8'))
    native_rows=native_snapshot['rows']
    chinese_names={row['className']:row['states'][0]['name'] for row in native_rows if row['language']=='zhs'}
    if set(chinese_names)!=set(expected) or len(expected)!=native_snapshot['nativeCardCount']:
        raise ValueError(f'Reviewed/native catalog mismatch: {sorted(set(chinese_names)^set(expected))}')
    capabilities={}
    for name in expected:
        code=(native_root/'MegaCrit.Sts2.Core.Models.Cards'/(name+'.cs')).read_text('utf-8')
        code+='\n'+'\n'.join(power_source(power,set()) for power in set(re.findall(r'PowerCmd\.Apply<([A-Za-z0-9_]+)>',code)))
        capabilities[name]=dict(orbs='OrbCmd.Channel' in code,doom='PowerCmd.Apply<DoomPower>' in code,forge='ForgeCmd.Forge' in code,stretch='PowerCmd.Apply<StretchPower>' in code,
                                gainStars='PlayerCmd.GainStars(' in code,strikeName='打击' in chinese_names[name])
    if write_artifacts:
        (ROOT/'Data/native-capabilities.json').write_text(json.dumps(capabilities,ensure_ascii=False,indent=2)+'\n','utf-8')
    compiled, pending = {}, []
    for name, (layer, text) in sorted(authored.items()):
        try:
            native_source = (native_root / 'MegaCrit.Sts2.Core.Models.Cards' / (name + '.cs')).read_text('utf-8')
            direct_powers = set(re.findall(r'PowerCmd\.Apply<([A-Za-z0-9_]+)>', native_source))
            nested_power_code='\n'.join(power_source(power,set()) for power in direct_powers)
            powers = sorted(direct_powers)
            if name in approved_traits.SPECS:
                base, upgraded = approved_traits.programs(name, instruction, powers)
                compiled[name] = dict(base=base, upgraded=upgraded, sourceSha256=approved_traits.source_hash(name,text,rework27))
                continue
            if name in rework27.SPECS:
                base, upgraded = rework27.programs(name, instruction, powers)
                compiled[name] = dict(base=base, upgraded=upgraded, sourceSha256=rework27.source_hash(name,text))
                continue
            base = compile_program(expand(text, False),name)
            upgraded = compile_program(expand(text, True),name)
            causal_powers=sorted(set(re.findall(r'PowerCmd\.Apply<([A-Za-z0-9_]+)>', nested_power_code))-direct_powers)
            for program in (base, upgraded):
                program['causalPowers']=causal_powers
                if any(rule['event'] in ('native','resource_spent','orb_origin_evoked') or rule['event'].startswith(('subject_','power_')) for rule in program['rules']):
                    if not powers and any(rule['event'] == 'native' for rule in program['rules']):
                        raise Pending('Native callback has no audited source power')
                    program['nativePowers'] = powers
                if any(rule.get('scope')=='live' for rule in program['rules']):
                    if not powers:
                        raise Pending('A live support rule requires a native applied power')
                    program['nativePowers']=powers
                if any(rule.get('scope')=='replay' for rule in program['rules']):
                    if not powers:raise Pending('A replay origin requires a native applied power')
                    program['nativePowers']=powers
                if any(rule.get('scope') in ('free','arrow') for rule in program['rules']):
                    program['nativePowers']=powers
            base = revise_current_rules(name, base, expected[name]['type'], False)
            upgraded = revise_current_rules(name, upgraded, expected[name]['type'], True)
            compiled[name] = dict(base=base, upgraded=upgraded, sourceSha256=hashlib.sha256(text.encode()).hexdigest())
        except Pending as error:
            pending.append(dict(card=name, layer=layer, reason=str(error), text=text))
    compiled = {name: update_reflow_traits(name, update_reviewed_conditions(name,
                update_reviewed_growth(name, update_star_traits(name, rewrite_revision(name, revision), instruction), instruction), instruction))
                for name, revision in compiled.items()}
    status = dict(status='complete' if not pending else 'incomplete-not-applied', expected=len(expected),
                  compiled=len(compiled), pending=len(pending), cardsPending=pending,
                  note='Compiled means metadata only; native behavior verification is a separate gate.')
    if write_artifacts:
        OUT.mkdir(parents=True, exist_ok=True)
        (OUT / 'compiled-revisions.json').write_text(json.dumps(compiled, ensure_ascii=False, indent=2) + '\n', 'utf-8')
        (OUT / 'revision-coverage.json').write_text(json.dumps(status, ensure_ascii=False, indent=2) + '\n', 'utf-8')
    return compiled, status


def write_catalog(compiled):
    cards_path=ROOT/'Data/cards.json'
    cards=json.loads(cards_path.read_text('utf-8'))
    if {c['className'] for c in cards}!=set(compiled):
        raise SystemExit('Revision/native catalog mismatch')
    for card in cards:
        name=card['className']
        revision=compiled[name]
        rules=revision['base']['rules']
        trait_bond=rules[0]['bond'] if rules else 'Encounter' if name=='MadScience' else 'Status'
        card['bonds']=[bond for bond in card['bonds'] if bond not in {'Solitude','Aid'}]
        card['foresightRequiresUpgrade']=FORESIGHT_UPGRADE.get(name)
        if name in HEAVY_EXCLUDED:
            card['heavyExcluded']=True
            card['bonds']=[bond for bond in card['bonds'] if bond!='Heavy']
            card.get('evidence',{}).pop('Heavy',None)
        else:
            card.pop('heavyExcluded',None)
        if name in REASSIGNED_HEAVY_TRAITS:
            card['bonds']=list(dict.fromkeys([*card['bonds'], REASSIGNED_HEAVY_TRAITS[name]]))
        if name in ZERO_EXCLUDED:
            card['zeroExcluded']=True
            card['bonds']=[bond for bond in card['bonds'] if bond!='Zero']
            card.get('evidence',{}).pop('Zero',None)
        else:
            card.pop('zeroExcluded',None)
        ember_category = ember_category_excluded(card)
        if name in EMBER_EXCLUDED or ember_category:
            card['emberExcluded']=True
            card['bonds']=[bond for bond in card['bonds'] if bond!='Ember']
            card.get('evidence',{}).pop('Ember',None)
            if ember_category:
                card['emberSupport']=False
        else:
            card.pop('emberExcluded',None)
        if name in PROFIT_EXCLUDED:
            card['profitExcluded']=True
            card['bonds']=[bond for bond in card['bonds'] if bond!='Profit']
            card.get('evidence',{}).pop('Profit',None)
        else:
            card.pop('profitExcluded',None)
        if name in approved_traits.AID_MEMBERS:
            card['bonds'].append('Aid')
        if name in REASSIGNED_SOLITUDE_TRAITS:
            card['bonds']=list(dict.fromkeys([*card['bonds'], REASSIGNED_SOLITUDE_TRAITS[name]]))
        if name in MARKER_CARDS:
            card['bonds']=[]
        elif name in {'Break', 'Maul'}:
            card['bonds']=list(dict.fromkeys([*card['bonds'], 'Fury']))
        if name not in MARKER_CARDS and any(rule['bond']=='Status' for program in (revision['base'], revision['upgraded']) for rule in program['rules']):
            card['bonds']=list(dict.fromkeys([*card['bonds'], 'Status']))
        if name in TRAIT_BOND_MEMBERS:
            card['traitBondMember']=True
            card['bonds']=list(dict.fromkeys([*card['bonds'], trait_bond]))
        else:
            card.pop('traitBondMember',None)
        revise_membership(card)
        update_reflow_membership(card)
        for bond, removed in (('Creation', CREATION_REMOVED), ('Foresight', FORESIGHT_REMOVED)):
            if name in removed:
                card['bonds']=[existing for existing in card['bonds'] if existing!=bond]
                card.get('evidence',{}).pop(bond,None)
        if name=='Hellraiser' and 'Strike' not in card['bonds']:
            card['bonds'].append('Strike')
        if name in approved_traits.CURSE_EXTRA_MEMBERS:
            bond, reason=approved_traits.CURSE_EXTRA_MEMBERS[name]
            if bond not in card['bonds']:
                card['bonds'].append(bond)
            card['evidence'][bond]=reason
            if bond=='Ember':
                card['emberSupport']=True
        card['trait']=dict(trigger='revision',bond=trait_bond,amount=0,limit=0,
                           every=1,upgradeAmount=0,effects=[],revision=revision)
    cards_path.write_text(json.dumps(cards,ensure_ascii=False,indent=2)+'\n','utf-8')
    (ROOT/'Data/trait-design.json').write_text(json.dumps({name:dict(revision=program) for name,program in compiled.items()},ensure_ascii=False,indent=2)+'\n','utf-8')


if __name__ == '__main__':
    args = argparse.ArgumentParser()
    args.add_argument('--apply', action='store_true')
    opts = args.parse_args()
    compiled, status = build()
    print(json.dumps({k: v for k, v in status.items() if k != 'cardsPending'}, ensure_ascii=False))
    if opts.apply:
        if status['pending']:
            raise SystemExit('Refusing to apply an incomplete revision catalog')
        proof=json.loads((OUT/'revision-description-check.json').read_text('utf-8'))
        staged_sha=hashlib.sha256((OUT/'compiled-revisions.json').read_bytes()).hexdigest().upper()
        if proof.get('status')!='pass' or proof.get('stagedSha256')!=staged_sha or len(proof.get('cards',[]))!=len(compiled):
            raise SystemExit('All current programs need a completed native description/expression check before integration')
        write_catalog(compiled)
        print(f'Integrated all {len(compiled)} revision programs.')
