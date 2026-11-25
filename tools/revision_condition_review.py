"""Reviewed changes that move trait payoffs onto observable card effects."""

from hashlib import sha256


# Entries give the covenant, visible trigger, base/upgraded growth, and the native event or count.
REVIEW = {
    'Anger': ('Creation', '造物', '', (1, 2), 'play', '1'),
    'Archery': ('Arrows', '游弋', '此牌使你获得瞄准时', (2, 3), 'native', 'power_owner_AimPower>0'),
    'ChoFirm': ('Melody', '合奏', '此牌使你第二次获得格挡时', (4, 5), 'play', 'gain_block_count>1'),
    'Fetch': ('Bones', '役骨', '此牌使你抽牌时', (4, 5), 'play', 'drawn_count>0'),
    'FoolishShot': ('Arrows', '游弋', '此牌使箭矢扎穿敌人时', (3, 4), 'play', 'native_impales>0'),
    'Friendship': ('Heavy', '重装', '', (2, 3), 'play', '1'),
    'Fusion': ('Resonance', '共鸣', '', (3, 4), 'play', '1'),
    'HiddenRoots': ('Guard', '坚守', '此牌使你获得拉伸时', (2, 3), 'native', 'power_owner_StretchPower>0'),
    'HobblingShot': ('Breach', '破绽', '', (4, 6), 'play', '1'),
    'KiHop': ('Melody', '合奏', '此牌使你抽牌时', (4, 5), 'play', 'drawn_count>0'),
    'PrepTime': ('Fury', '狂战', '此牌使你获得活力时', (2, 3), 'native', 'power_owner_VigorPower>0'),
    'Putrefy': ('Ember', '余烬', '', (4, 6), 'play', '1'),
    'RefineBlade': ('Forge', '铸王', '下回合开始时', (5, 7), 'native', 'gain_energy>0'),
    'TrickShot': ('Arrows', '游弋', '', (3, 4), 'play', '1'),
    'Underworld': ('Doom', '厄兆', '', (4, 6), 'play', '1'),
    'WahTouch': ('Melody', '合奏', '', (3, 4), 'play', '1'),
    # Fixed native output or an incidental check such as a hit getting past block.
    'BladeSymphony': ('Blades', '飞刃', '', (4, 6), 'play', '1'),
    'BoneShards': ('Bones', '役骨', '', (4, 5), 'play', '1'),
    'BubbleBubble': ('Poison', '毒棘', '', (6, 8), 'play', '1'),
    'CaptureSpirit': ('Creation', '造物', '', (6, 12), 'play', '1'),
    'IronWave': ('Guard', '坚守', '', (4, 5), 'play', '1'),
    'Justice': ('Encounter', '奇遇', '', (2, 3), 'play', '1'),
    'PoisonedStab': ('Poison', '毒棘', '', (4, 5), 'play', '1'),
    'Rainbow': ('Resonance', '共鸣', '', (6, 9), 'play', '1'),
    'Reave': ('Creation', '造物', '', (4, 5), 'play', '1'),
    'Restlessness': ('Zero', '零界', '', (4, 5), 'play', '1'),
    'SelfDeprecate': ('Emotion', '悲欢', '', (2, 3), 'play', '1'),
    'ShiningStrike': ('Stars', '星辉', '', (4, 6), 'play', '1'),
    'SorrowfulStrike': ('Emotion', '悲欢', '', (6, 8), 'play', '1'),
    'Spur': ('Bones', '役骨', '', (4, 5), 'play', '1'),
    'TwinStrike': ('Strike', '打击', '', (4, 5), 'play', '1'),
    'Whistle': ('Ember', '余烬', '', (8, 11), 'play', '1'),
    'WroughtInWar': ('Forge', '铸王', '', (4, 5), 'play', '1'),
}

NATIVE_TRIGGER_WORDING = {
    'ForgottenRitual': ('打出时，若因此获得了能量，', '此牌提供能量时，'),
    'Poke': ('打出时，若这是奥斯提本回合的第一次攻击，', '此牌使奥斯提发动本回合的第一次攻击时，'),
    'Sacrifice': ('打出时，若奥斯提因此死去，他的最大生命值每有10点，', '牺牲奥斯提时，他的最大生命值每有10点，'),
    'Uproot': ('打出时，若因此使箭矢松脱，', '此牌使箭矢松脱时，'),
}

DELAYED_DRAW = {'GuidingStar', 'Plot', 'Predator'}

AID_TRAITS = {
    'DrainPower': ('每因此升级一张牌，使该牌所属的已激活盟约中层数最高的一个增加{0}层。',
                   (3, 4), 'play', 'grow_upgraded_highest_bond', {}),
    'Begone': ('使被此牌变化的牌原先所属的已激活盟约各增加{0}层。',
               (2, 3), 'play', 'grow_transformed_from_bonds', {}),
    'Terraforming': ('你下次打出攻击牌时，使该牌所属的已激活盟约各增加{0}层。',
                     (2, 3), 'play', 'terraforming_power', {}),
    'Headbutt': ('被此牌放回抽牌堆顶的牌下次打出时，使该牌所属的已激活盟约各增加{0}层。',
                 (2, 3), 'subject_play', 'grow_subject_bonds', {'scope': 'top', 'lifetime': 'next'}),
}

PER_ACTION = {
    'BouncingFlask': ('Poison', '毒棘', '此牌每对敌人施加1次中毒', (2, 3), 'power_applications_PoisonPower'),
    'Chill': ('Resonance', '共鸣', '此牌每生成1个冰霜充能球', (2, 3), 'orb_generated_FrostOrb'),
    'KiRest': ('Retain', '延续', '此牌每保留1张手牌', (2, 3), 'retained_count'),
    'SecondWind': ('Ember', '余烬', '此牌每消耗1张牌', (2, 3), 'exhausted_count'),
    'Wrest': ('Arrows', '游弋', '此牌每使你抽1张牌', (2, 3), 'drawn_count'),
}

# Fixed combat payoffs that make sense as rewards for building the associated covenant.
ACTIVATED_PAYOFFS = {
    'BorrowedTime': '重装',
    'Capacitor': '共鸣',
    'Cruelty': '重装',
    'DarkEmbrace': '余烬',
    'DemonForm': '狂战',
    'ForegoneConclusion': '连携',
    'Glimmer': '轮转',
    'Juggling': '造物',
    'LockedIn': '游弋',
    'MachineLearning': '轮转',
    'Pillage': '逐利',
    'Reboot': '轮转',
    'SeekerStrike': '打击',
    'Sneaky': '巧变',
    'StormOfSteel': '巧变',
    'Stratagem': '轮转',
    'SweepingBeam': '轮转',
    'ThinkingAhead': '轮转',
    'Turbo': '残响',
    'WahChoKi': '合奏',
    'Wish': '延续',
}

PAYOFF_WORDING = {
    'Cruelty': ('在本回合的能量消耗减少1', '在本回合少消耗{energyPrefix:energyIcons(1)}'),
    'Pillage': ('由此每抽到一张攻击牌，本场战斗胜利时', '此牌每抽到一张攻击牌，战斗胜利时便'),
    'SeekerStrike': ('若它是打击牌', '若选入的是打击牌'),
    'ThinkingAhead': ('的能量消耗减少1', '少消耗{energyPrefix:energyIcons(1)}'),
    'WahChoKi': ('且下次打出时的能量费用为0{energyPrefix:energyIcons(1)}', '并可免费打出一次'),
}


def update(name, revision, instruction):
    if not any(name in group for group in (REVIEW, PER_ACTION, ACTIVATED_PAYOFFS, AID_TRAITS,
                                          NATIVE_TRIGGER_WORDING, DELAYED_DRAW, {'GoForTheEyes'})):
        return revision
    for index, stage in enumerate(('base', 'upgraded')):
        program = revision[stage]
        if name in AID_TRAITS:
            text, amounts, event, action, options = AID_TRAITS[name]
            program.pop('amountText', None)
            program['text'] = text.format(amounts[index])
            program['rules'] = [instruction(event, action, 'Aid', amounts[index], requireActive=False, **options)]
            continue
        if name in NATIVE_TRIGGER_WORDING:
            for field in ('text', 'amountText'):
                if field in program:
                    program[field] = program[field].replace(*NATIVE_TRIGGER_WORDING[name])
            continue
        if name in DELAYED_DRAW:
            program['text'] = program['text'].replace('此牌在下个回合使你抽牌时，由此每抽到', '在下个回合，此牌每抽到')
            for rule in program['rules']:
                rule['condition'] = rule['condition'].replace('(drawn_count>0)&&(1)', '1')
            continue
        if name == 'GoForTheEyes':
            program['text'] = program['text'].replace('打出时，若因此给予了虚弱，', '目标有攻击意图时，')
            program['rules'][0]['condition'] = 'before_target_attacking>0'
            continue
        if name == 'Juggling':
            program['text'] = f'打出此牌生成的复制品后，使你下一张打出的技能牌获得重放{1 + index}。'
        if name in ACTIVATED_PAYOFFS:
            prefix = f'若【{ACTIVATED_PAYOFFS[name]}】已激活，'
            for field in ('text', 'amountText'):
                if field in program:
                    text = program[field].removeprefix('打出时，').removeprefix('打出此牌后，')
                    if name in PAYOFF_WORDING:
                        text = text.replace(*PAYOFF_WORDING[name])
                    if text.startswith('若'):
                        text = '且' + text[1:]
                        program[field] = prefix[:-1] + text
                    else:
                        program[field] = prefix + text
            for rule in program['rules']:
                rule['requireActive'] = True
            continue
        if name in REVIEW:
            bond, label, trigger, amounts, event, condition = REVIEW[name]
            amount = str(amounts[index])
        else:
            bond, label, trigger, amounts, count = PER_ACTION[name]
            event, condition = 'play', '1'
            amount = f'{amounts[index]}*{count}'
        growth = f'使已激活的【{label}】增加{amounts[index]}层。'
        program['text'] = f'{trigger}，{growth}' if trigger else growth
        program['rules'] = [instruction(event, 'grow', bond, amount, condition)]
    revision['sourceSha256'] = sha256(repr((revision['base'], revision['upgraded'])).encode()).hexdigest()
    return revision
