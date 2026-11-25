"""Growth adjustments from the card-wide trigger and frequency review."""

from hashlib import sha256
import re


# Keep the growth-per-draw and growth-per-application conditions tied to
# what the card actually does. Adjust low-information preconditions separately.
PLAY_GROWTH = {
    'Afterlife': ('Bones', '役骨', (4, 6)),
    'Bash': ('Breach', '破绽', (6, 8)),
    'Bodyguard': ('Bones', '役骨', (2, 3)),
    'DeadlyPoison': ('Poison', '毒棘', (4, 6)),
    'Leap': ('Guard', '坚守', (4, 5)),
    'LegSweep': ('Guard', '坚守', (4, 6)),
    'Ignition': ('Resonance', '共鸣', (4, 5)),
    'KnowThyPlace': ('Breach', '破绽', (4, 6)),
    'Mimic': ('Guard', '坚守', (4, 5)),
    'Rally': ('Guard', '坚守', (6, 8)),
    'Shiv': ('Blades', '飞刃', (1, 1)),
    'Squash': ('Encounter', '奇遇', (5, 7)),
    'TheEmpress': ('Breach', '破绽', (2, 3)),
}

EMBER_RETARGETS = {
    'Graveblast': 'Chain',
    'SecretTechnique': 'Chain',
    'SecretWeapon': 'Chain',
    'ThinkingAhead': 'Rotation',
}

TOKEN_EMBER_RETARGETS = {
    'Slimed': ('Rotation', '轮转'),
    'TheFool': ('Heavy', '重装'),
    'TheChariot': ('Encounter', '奇遇'),
    'Judgement': ('Encounter', '奇遇'),
    'Temperance': ('Guard', '坚守'),
    'Luminesce': ('Heavy', '重装'),
    'Fuel': ('Heavy', '重装'),
    'TheTower': ('Encounter', '奇遇'),
    'TheWorld': ('Aid', '助力'),
}


def update(name, revision, instruction):
    original = repr((revision['base'], revision['upgraded']))
    for index, stage in enumerate(('base', 'upgraded')):
        program = revision[stage]
        if name in PLAY_GROWTH:
            bond, label, amounts = PLAY_GROWTH[name]
            program['text'] = f'使已激活的【{label}】增加{amounts[index]}层。'
            program['rules'] = [instruction('play', 'grow', bond, amounts[index])]
        elif name in TOKEN_EMBER_RETARGETS:
            bond, label = TOKEN_EMBER_RETARGETS[name]
            for rule in program['rules']:
                if rule['bond'] == 'Ember':
                    rule['bond'] = bond
            for field in ('text', 'amountText'):
                if field in program:
                    program[field] = program[field].replace('【余烬】', f'【{label}】')
        elif name == 'Soul':
            amount = (2, 4)[index]
            program['text'] = f'消耗此牌时，使已激活的【造物】增加{amount}层。'
            program['rules'] = [instruction('exhaust', 'grow', 'Creation', amount)]
        elif name == 'TheEmperor':
            amount = (3, 5)[index]
            program['text'] = f'使已激活的【重装】增加{amount}层。'
            program['rules'] = [instruction('play', 'grow', 'Heavy', amount)]
        elif name == 'Shockwave':
            amount = 3 + index
            program['text'] = f'场上每有一名敌人，使已激活的【破绽】增加{amount}层。'
            program['rules'] = [instruction('play', 'grow', 'Breach', f'{amount}*before_enemy_count')]
        elif name == 'Synthesis':
            amount = 2 + index
            program['text'] = f'你打出下一张能力牌时，使已激活的【仪式】增加层数，数量为该牌原始耗能加1的{amount}倍。'
            program['nativePowers'] = []
            program['rules'] = [instruction('play', 'synthesis_power', 'Ritual', amount, requireActive=False)]
        elif name in ('Storm', 'DodgeAndRoll'):
            amount = (3, 4)[index] if name == 'Storm' else (5, 7)[index]
            program['text'] = re.sub(r'增加\d+层', f'增加{amount}层', program['text'])
            next(rule for rule in program['rules'] if rule['action']=='grow')['amount'] = str(amount)
        elif name == 'Impatience':
            program['text'] = f'此牌每抽到1张攻击牌，使已激活的【零界】增加{3 + index}层。'
            program['rules'] = [instruction('play', 'grow', 'Zero', f'{3 + index}*drawn_Attack')]
        elif name == 'MomentumStrike':
            amount = (3, 4)[index]
            program['text'] = f'若此牌的能量费用为0，使【打击】和【零界】中已激活的盟约各增加{amount}层。'
            program['rules'] = [instruction('play', 'grow', bond, amount, 'before_cost==0')
                                for bond in ('Strike', 'Zero')]
        elif name == 'PommelStrike':
            amount = (4, 5)[index]
            program['text'] = f'若此牌抽到了攻击牌，使已激活的【轮转】增加{amount}层。'
            program['rules'] = [instruction('play', 'grow', 'Rotation', amount, 'drawn_Attack>0')]
        elif name == 'Assassinate':
            amount = (8, 11)[index]
            program['text'] = f'在战斗的第一回合打出此牌时，使已激活的【先机】增加{amount}层。'
            program['rules'] = [instruction('play', 'grow', 'Initiative', amount, 'before_turn==1')]
        elif name == 'BootSequence':
            amount = (5, 7)[index]
            program['text'] = f'此牌在第一回合被保留。因此被保留时，使已激活的【先机】增加{amount}层。'
            next(rule for rule in program['rules'] if rule['event'] == 'retained')['amount'] = str(amount)
        elif name == 'Dualcast':
            per_orb, dark_bonus = (2, 3) if index == 0 else (3, 4)
            program['text'] = f'此牌每激发1个充能球，使已激活的【过载】增加{per_orb}层。若激发了黑暗充能球，再增加{dark_bonus}层。'
            program['rules'] = [
                instruction('play', 'grow', 'Overload', f'{per_orb}*orb_evoked'),
                instruction('play', 'grow', 'Overload', dark_bonus, 'orb_evoked_DarkOrb>0'),
            ]
        elif name in EMBER_RETARGETS:
            for rule in program['rules']:
                if rule['bond'] == 'Ember':
                    rule['bond'] = EMBER_RETARGETS[name]
        elif name == 'Prolong':
            block = (8, 6)[index]
            program['text'] = f'你每拥有{block}点格挡，使已激活的【坚守】增加1层。'
            program['rules'] = [instruction('play', 'grow', 'Guard', f'floor(before_block/{block})')]
        elif name == 'Scrawl':
            divisor = (2, 1)[index]
            draw = '每抽到1张牌' if divisor == 1 else f'每抽到{divisor}张牌'
            program['text'] = f'此牌{draw}，使已激活的【余烬】增加1层。'
            program['rules'] = [instruction('play', 'grow', 'Ember', f'floor(drawn_count/{divisor})' if divisor > 1 else 'drawn_count')]
        elif name == 'Genesis':
            program['text'] = f'此牌使你获得{{starIcon}}时，使已激活的【星辉】增加{2 + index}层。'
            program['nativePowers'] = ['GenesisPower']
            program['rules'] = [instruction('native', 'grow', 'Stars', 2 + index, 'gain_stars>0')]
        elif name == 'TheSealedThrone':
            program['text'] = f'每回合此牌首次使你获得{{starIcon}}时，使已激活的【星辉】增加{4 + index}层。'
            program['rules'] = [instruction('native', 'grow', 'Stars', 4 + index, 'gain_stars>0', limit='turn')]
        elif name == 'Stardust':
            for key in ('text', 'amountText'):
                program[key] = program[key].replace('每实际花费1点辉星', '此牌每消耗1{starIcon}')
    updated = repr((revision['base'], revision['upgraded']))
    if updated != original:
        revision['sourceSha256'] = sha256(updated.encode()).hexdigest()
    return revision
