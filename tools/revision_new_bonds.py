"""Reviewed covenant memberships and trait revisions used by the catalog compiler."""
from copy import deepcopy
from hashlib import sha256
import re

from catalog_chain import CHAIN_MEMBERS


BREACH_MEMBERS = set("""
Assassinate Bash BeamCell Break Bully Colossus Comet Cruelty Deathbringer Debilitate Defy
Dismantle Dominate Expose FallingStar Fear GammaBlast GoForTheEyes Haze HighFive HobblingShot
KnowThyPlace LegSweep Malaise MeteorShower MoltenFist Nail Neutralize Null Putrefy Shockwave
Squash SuckerPunch Suppress Taunt TheEmpress Thunderclap Tracking Tremble Uppercut Uproot
Vicious Vitriol WahTouch
""".split())

DRAW_TRAIT_ONLY = {'Automation', 'Cacophony', 'PaleBlueDot', 'Speedster'}
MAD_SCIENCE_BONDS = {
    'Sapping': (('Breach', '破绽'),),
    'Violence': (('Fury', '狂战'),),
    'Choking': (('Chain', '连携'),),
    'Energized': (('Heavy', '重装'),),
    'Wisdom': (('Rotation', '轮转'),),
    'Chaos': (('Creation', '造物'),),
    'Expertise': (('Fury', '狂战'), ('Guard', '坚守')),
    'Curious': (('Ritual', '仪式'),),
    'Improvement': (('Foresight', '远见'),),
}
ROTATION_MEMBERS = {
    card for card, (_, reason) in CHAIN_MEMBERS.items()
    if '抽牌' in reason or '抽空牌堆' in reason
} | {'Stratagem'} | DRAW_TRAIT_ONLY

IMMEDIATE_CHAIN = {
    'EvilEye': '本回合先消耗卡牌，才能获得追加格挡。',
    'ForgottenRitual': '本回合先消耗卡牌，才能获得能量。',
    'BeatIntoShape': '读取本回合此前命中同一目标的次数。',
    'Spite': '本回合先失去生命，才会追加攻击。',
    'DeathsDoor': '本回合先施加灾厄，才会额外获得两次格挡。',
    'Flatten': '本回合先让奥斯提攻击，才能免除耗能。',
    'Rattle': '读取本回合此前打出的奥斯提攻击牌。',
    'Rage': '为本回合随后打出的攻击牌提供格挡。',
    'Oblivion': '为本回合随后打出的牌追加灾厄。',
    'Monologue': '本回合越早打出，后续出牌获得的临时力量越多。',
    'SicEm': '先标记目标，再让奥斯提于本回合命中目标。',
    'SignalBoost': '由下一张能力牌使用这次重放。',
    'KiVerse': '由本回合接下来的一张旋律牌使用这次重放。',
    'Quietus': '强化本回合下一次发射的伤害。',
    'Pounce': '免除下一张技能牌的耗能，先打出哪张技能牌会改变收益。',
    'Unrelenting': '免除下一张攻击牌的耗能，先打出哪张攻击牌会改变收益。',
    'Synthesis': '免除下一张能力牌的耗能，先打出哪张能力牌会改变收益。',
    'Veilpiercer': '免除下一张虚无牌的耗能，先打出哪张虚无牌会改变收益。',
    'BulletTime': '封锁后续抽牌并为当前手牌减费，需要先处理抽牌和手牌构成。',
    'Encore': '本回合打出后只剩一次出牌机会。',
    'BattleTrance': '封锁本回合剩余的抽牌，需要在其他抽牌之后使用。',
    'PreciseCut': '根据当前手牌数减少伤害，先打出或丢弃其他牌可直接提高伤害。',
    'IAmInvincible': '需要在同一回合把牌留在抽牌堆顶，才会自动打出。',
    'Simpleton': '保留到回合结束的手牌决定自动打出的目标。',
    'Stampede': '保留到回合结束的攻击牌决定自动打出的候选。',
}

SUPPORT_MEMBERS = {
    'Voltaic': ('Overload', '连续填入闪电充能球，特性奖励由此触发的激发。'),
    'Compact': ('Status', '把手牌中的状态牌转成燃料。'),
    'FlakCannon': ('Status', '消耗状态牌并按消耗张数攻击。'),
    'RocketPunch': ('Status', '生成状态牌会减少费用，特性还可处理抽到的状态牌。'),
    'Dredge': ('Retain', '特性奖励从弃牌堆取回具有保留的牌。'),
    'WellLaidPlans': ('Retain', '让手牌留到下回合，特性按保留的牌数叠层。'),
    'TearAsunder': ('Blood', '本场战斗中每次失去生命都会增加此牌的攻击段数。'),
    'Aggression': ('Mastery', '取回并升级弃牌堆中的攻击牌。'),
    'Apotheosis': ('Mastery', '升级现有的战斗牌。'),
    'Armaments': ('Mastery', '升级现有手牌。'),
    'DrainPower': ('Mastery', '升级弃牌堆中的牌。'),
    'Tune': ('Mastery', '获得叽旋律时升级手牌。'),
}

# The cards keep their distinct native triggers and amounts when the original
# effect already fits the new covenant. Only the layer source or recipient moves.
TRAIT_RETARGETS = {
    'DramaticEntrance': ('Ember', 'Initiative', '余烬', '先机'),
    'ForgottenRitual': ('Ember', 'Chain', '余烬', '连携'),
    'Encore': ('Zero', 'Chain', '零界', '连携'),
    'Monologue': ('Fury', 'Chain', '狂战', '连携'),
    'BulletTime': ('Heavy', 'Chain', '重装', '连携'),
    'Bash': ('Heavy', 'Breach', '重装', '破绽'),
    'HobblingShot': ('Arrows', 'Breach', '游弋', '破绽'),
    'Shockwave': ('Ember', 'Breach', '余烬', '破绽'),
    'Expose': ('Ember', 'Breach', '余烬', '破绽'),
    'Thunderclap': ('Fury', 'Breach', '狂战', '破绽'),
    'Bully': ('Zero', 'Breach', '零界', '破绽'),
    'Dismantle': ('Fury', 'Breach', '狂战', '破绽'),
    'HeavenlyDrill': ('Zero', 'Channel', '零界', '倾注'),
    'Skewer': ('Zero', 'Channel', '零界', '倾注'),
    'Volley': ('Zero', 'Channel', '零界', '倾注'),
    'Eradicate': ('Zero', 'Channel', '零界', '倾注'),
    'Tempest': ('Resonance', 'Channel', '共鸣', '倾注'),
    'Brace': ('Arrows', 'Channel', '游弋', '倾注'),
    'Malaise': ('Ember', 'Channel', '余烬', '倾注'),
    'Transfigure': ('Ember', 'Foresight', '余烬', '远见'),
    'Compact': ('Creation', 'Status', '造物', '残响'),
    'FlakCannon': ('Ember', 'Status', '余烬', '残响'),
    'BattleTrance': ('Zero', 'Rotation', '零界', '轮转'),
    'Backflip': ('Guard', 'Rotation', '坚守', '轮转'),
    'MasterOfStrategy': ('Ember', 'Rotation', '余烬', '轮转'),
}


def revise_membership(row):
    name = row['className']
    bonds = row['bonds']
    evidence = row.setdefault('evidence', {})
    if name == 'MadScience':
        bonds.clear()
        for bond in ('Creation', 'Fury', 'Guard', 'Chain', 'Rotation'):
            evidence.pop(bond, None)
        evidence['Variants'] = '事件附加效果决定特性盟约。各技能形态额外计入坚守，各能力形态额外计入仪式。'
        return
    if name in ROTATION_MEMBERS and ('Chain' in bonds or 'Rotation' in bonds or name in DRAW_TRAIT_ONLY or name == 'Stratagem'):
        if name != 'DrumOfBattle' and name not in IMMEDIATE_CHAIN and 'Chain' in bonds:
            bonds.remove('Chain')
            evidence.pop('Chain', None)
        if 'Rotation' not in bonds:
            bonds.append('Rotation')
        reason = '原版效果按抽牌触发，或提供额外抽牌。' if name in DRAW_TRAIT_ONLY else '原版效果在洗牌后取牌。'
        evidence['Rotation'] = CHAIN_MEMBERS.get(name, (None, reason))[1]
    if name in BREACH_MEMBERS and 'Breach' not in bonds:
        bonds.append('Breach')
        evidence['Breach'] = '原版明确给予敌人虚弱或易伤，或依赖这两种状态。'
    if name in IMMEDIATE_CHAIN:
        if 'Chain' not in bonds:
            bonds.append('Chain')
        evidence['Chain'] = IMMEDIATE_CHAIN[name]
    if name in SUPPORT_MEMBERS:
        bond, reason = SUPPORT_MEMBERS[name]
        if bond not in bonds:
            bonds.append(bond)
        if bond == 'Retain':
            row['retainSupport'] = True
        evidence[bond] = reason


def rewrite_revision(name, revision):
    if name == 'MadScience':
        return mad_science_revision()
    revision = deepcopy(revision)
    if name in ROTATION_MEMBERS and name != 'DrumOfBattle':
        for stage in ('base', 'upgraded'):
            program = revision[stage]
            if not any(rule['bond'] == 'Chain' for rule in program['rules']):
                continue
            for rule in program['rules']:
                if rule['bond'] == 'Chain':
                    rule['bond'] = 'Rotation'
            for key in ('text', 'amountText'):
                if key in program:
                    program[key] = re.sub(r'^打出时，', '', program[key].replace('连携', '轮转'))
    if name in TRAIT_RETARGETS:
        old, new, old_name, new_name = TRAIT_RETARGETS[name]
        for stage in ('base', 'upgraded'):
            program = revision[stage]
            for rule in program['rules']:
                if rule['bond'] == old:
                    rule['bond'] = new
            for field in ('text', 'amountText'):
                if program.get(field):
                    program[field] = program[field].replace(f'【{old_name}】', f'【{new_name}】')
    if name in TRAITS:
        for stage in ('base', 'upgraded'):
            text, bond, event, action, amount, condition, powers = TRAITS[name](stage == 'upgraded')
            program = {
                'text': text, 'nativePowers': powers,
                'rules': [{'event': event, 'action': action, 'bond': bond, 'amount': amount,
                           'condition': condition, 'requireActive': action == 'grow'}],
            }
            if '{0}' in text:
                program['amountText'] = text
            revision[stage] = program
    if name in SUPPORT_TRAITS:
        for stage in ('base', 'upgraded'):
            SUPPORT_TRAITS[name](revision[stage], stage == 'upgraded')
    if name in TRAITS or name in SUPPORT_TRAITS or name in TRAIT_RETARGETS:
        revision['sourceSha256'] = sha256(repr((revision['base'], revision['upgraded'])).encode()).hexdigest()
    return revision


def mad_science_revision():
    def program(bonds, amount):
        targets = '和'.join(f'【{label}】' for _, label in bonds)
        each = '各' if len(bonds) > 1 else ''
        return {'text': f'使已激活的{targets}{each}增加{amount}层。', 'nativePowers': [],
                'rules': [{'event': 'play', 'action': 'grow', 'bond': bond, 'amount': str(amount)}
                          for bond, _ in bonds]}

    variants = {}
    for rider, bonds in MAD_SCIENCE_BONDS.items():
        base, upgraded = program(bonds, 6), program(bonds, 8)
        variants[rider] = {'base': base, 'upgraded': upgraded,
                           'sourceSha256': sha256(repr((base, upgraded)).encode()).hexdigest()}
    template = {'text': '特性由事件中选定的附加效果决定。', 'nativePowers': [], 'rules': []}
    return {'base': template, 'upgraded': template, 'variants': variants,
            'sourceSha256': sha256(repr((template, variants)).encode()).hexdigest()}


def trait(text, bond, amount='1', *, event='play', action='covenant_trait', condition='1', powers=()):
    return text, bond, event, action, str(amount), condition, list(powers)


def marker(text, bond, powers=(), amount='1'):
    return trait(text, bond, amount, event='redesign', action='redesign', powers=powers)


TRAITS = {
    'MeteorShower': lambda u: trait(
        '打出后，分别合计敌人的虚弱和易伤层数。使已激活的【星辉】和【破绽】各增加'
        + ('等同于两者平均值一半的层数。' if u else '等同于两者中较小值一半的层数。'), 'Stars'),
    'BeamCell': lambda u: trait(f'若目标已有易伤，额外给予{2 if u else 1}层虚弱。', 'Breach', 2 if u else 1),
    'Capacitor': lambda u: trait('若扩容前充能球栏位已满，在新栏位生成一个最右侧充能球的复制。', 'Resonance'),
    'BootSequence': lambda u: marker('此牌在第一回合被保留。', 'Initiative'),
    'Assassinate': lambda u: trait(
        f'若在战斗的第一回合使尚未易伤的目标获得易伤，使已激活的【先机】增加{8 if u else 6}层。',
        'Initiative', 8 if u else 6, action='grow',
        condition='before_turn==1&&first_vulnerable_targets>0'),
    'Suppress': lambda u: trait(
        f'在战斗的第一回合，此牌每施加1层虚弱，使已激活的【先机】增加{3 if u else 2}层。',
        'Initiative', f'{3 if u else 2}*power_WeakPower', action='grow', condition='before_turn==1'),
    'Voltaic': lambda u: trait(
        f'此牌生成闪电充能球时，每因此激发一个充能球，使已激活的【过载】增加{3 if u else 2}层。',
        'Overload', f'{3 if u else 2}*orb_evoked', action='grow'),
    'Reanimate': lambda u: marker('若奥斯提本场战斗曾被击倒过，此牌少消耗{energyPrefix:energyIcons(2)}。', 'Bones'),
    'Bloodletting': lambda u: marker('若本回合失去过生命，此牌的生命损失从3点降至1点。', 'Blood'),
    'Neutralize': lambda u: trait('若目标当前有攻击意图，获得{0}点格挡。', 'Breach', '2+floor(A/2)',
                                 action='block', condition='before_target_attacking>0'),
    'FallingStar': lambda u: trait('若打出此牌耗尽了{starIcon}，额外给予目标1层虚弱和1层易伤。', 'Stars'),
    'Barricade': lambda u: marker('壁垒生效期间，【坚守】进阶改为在你的回合开始时获得{0}点格挡。', 'Guard',
                                  ('BarricadePower',), f'{2 if u else 1}+A'),
    'Backstab': lambda u: trait('对满血的单位额外造成{0}点伤害。', 'Ember',
                                f'{4 if u else 3}+floor(A/2)', event='damage_bonus', action='modifier',
                                condition='before_target_hp>0&&before_target_hp==before_target_maxhp'),
    'Tremble': lambda u: trait('若目标当前的意图中没有攻击，此牌额外给予1层易伤。', 'Breach',
                               action='vulnerable_target', condition='before_target_attacking==0'),
    'Stratagem': lambda u: trait('通过此牌置入手牌的牌获得保留。' + ('该牌下次打出时少消耗{energyPrefix:energyIcons(1)}。' if u else ''),
                                 'Rotation', event='native', condition='native_method_AfterShuffle>0', powers=('StratagemPower',)),
    'Reboot': lambda u: trait(f'抽牌后，从刚洗回且仍在抽牌堆的原手牌中随机取回{2 if u else 1}张。', 'Rotation', 2 if u else 1),
    'Expertise': lambda u: trait(f'此牌抽到的牌在被保留后打出时，使已激活的【轮转】增加{3 if u else 2}层。', 'Rotation', 3 if u else 2),
    'PhotonCut': lambda u: trait(f'由此放回牌堆顶的牌下次抽到时获得保留，并使已激活的【轮转】增加{6 if u else 4}层。', 'Rotation', 6 if u else 4),
    'RocketPunch': lambda u: trait('若抽到的是状态牌，可以将它丢弃。' + ('若如此做，再抽1张牌。' if u else ''), 'Rotation'),
    'SweepingBeam': lambda u: trait(f'若此牌命中至少两名敌人，抽到的攻击牌下次打出时每次额外造成{3 if u else 2}点伤害。', 'Rotation', 3 if u else 2),
    'Vicious': lambda u: trait(f'每当此牌使你抽牌，使已激活的【破绽】增加{3 if u else 2}层。',
                               'Breach', 3 if u else 2, event='native', action='grow',
                               condition='drawn_count>0', powers=('ViciousPower',)),
}


def append_growth(program, text, bond, amount, condition='1', event='play'):
    program['text'] += text
    if program.get('amountText'):
        program['amountText'] += text
    program['rules'].append({'event': event, 'action': 'grow', 'bond': bond, 'amount': str(amount),
                             'condition': condition, 'requireActive': True})


def scale_selected_card(program, upgraded):
    expression = f'{4 if upgraded else 3}+floor(A/3)'
    for rule in program['rules']:
        rule['bond'] = 'Foresight'
        rule['amount'] = expression
    amount = 5 if upgraded else 4
    program['text'] = program['amountText'] = (
        '被此牌取回的牌下次打出时，造成的伤害和获得的格挡增加{0}点，'
        f'并使已激活的【远见】增加{amount}层。'
    )
    program['rules'].append({'event': 'subject_play', 'action': 'grow', 'bond': 'Foresight',
                             'amount': str(amount), 'condition': '1', 'scope': 'returned', 'lifetime': 'next'})


def simplify_recall(program, upgraded):
    amount = 5 if upgraded else 4
    program['text'] = program['text'].replace('打出时，若因此使箭矢松脱，', '此牌使箭矢松脱时，')
    program['text'] = program['text'].rstrip('。') + f'，并使已激活的【远见】增加{amount}层。'
    program['rules'].append({'event': 'play', 'action': 'grow', 'bond': 'Foresight',
                             'amount': str(amount), 'condition': 'native_dislodges>0'})


def scale_innate_mind_blast(program, upgraded):
    for rule in program['rules']:
        rule['bond'] = 'Initiative'
    if program.get('amountText'):
        program['amountText'] = program['amountText'].replace('【逐利】', '【先机】')
    program['text'] = program['text'].replace('【逐利】', '【先机】')


SUPPORT_TRAITS = {
    'BootSequence': lambda p, u: append_growth(
        p, f'因此被保留时，使已激活的【先机】增加{6 if u else 4}层。',
        'Initiative', 6 if u else 4, 'before_turn==1', 'retained'),
    'MindBlast': scale_innate_mind_blast,
    'Rattle': lambda p, u: append_growth(
        p, f'若此牌造成了至少两段伤害，使已激活的【连携】增加{5 if u else 4}层。',
        'Chain', 5 if u else 4, 'hit_count>=2'),
    'Stardust': lambda p, u: append_growth(
        p, f'每实际花费1点蓝星，使已激活的【倾注】增加{2 if u else 1}层。',
        'Channel', f'{2 if u else 1}*spent_stars', 'native_play_index==0'),
    'Hologram': scale_selected_card,
    'Recall': simplify_recall,
    'HeavenlyDrill': lambda p, u: p['rules'][0].update(condition='x_value>=4'),
}
