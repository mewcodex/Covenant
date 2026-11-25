"""Approved star and delayed strike traits layered over the reviewed card catalog."""

import re
from hashlib import sha256


def update(name, revision, instruction):
    original = repr((revision['base'], revision['upgraded']))
    for stage in ('base', 'upgraded'):
        upgraded = stage == 'upgraded'
        program = revision[stage]
        if name == 'FallingStar':
            program['text'] = '额外对目标施加1层虚弱和1层易伤。'
        elif name in {'CloakOfStars', 'ParticleWall'}:
            effects = {
                'CloakOfStars': [('Stars', '星辉', 8 if upgraded else 6)],
                'ParticleWall': [('Stars', '星辉', 5 if upgraded else 4), ('Guard', '坚守', 3 if upgraded else 2)],
            }[name]
            program['text'] = '，并'.join(f'使已激活的【{label}】增加{amount}层' for _, label, amount in effects) + '。'
            program['rules'] = [instruction('play', 'grow', bond, str(amount)) for bond, _, amount in effects]
        elif name in {'Venerate', 'GatherLight', 'SolarStrike', 'LeadingStrike'}:
            action, bond, label, trigger, amount, lifetime = {
                'Venerate': ('queue_star_growth', 'Stars', '星辉', '本回合下次花费{starIcon}时', 5 if upgraded else 4, 'turn'),
                'GatherLight': ('queue_star_growth', 'Stars', '星辉', '本回合下次花费{starIcon}时', 8 if upgraded else 6, 'turn'),
                'SolarStrike': ('queue_star_growth', 'Stars', '星辉', '下次花费{starIcon}时', 5 if upgraded else 4, 'battle'),
                'LeadingStrike': ('queue_shiv_growth', 'Blades', '飞刃', '下次打出小刀时', 5 if upgraded else 4, 'battle'),
            }[name]
            program['text'] = f'{trigger}，使已激活的【{label}】增加{amount}层。'
            program['rules'] = [instruction('play', action, bond, str(amount), lifetime=lifetime, requireActive=False)]
        elif name == 'MeteorShower':
            program['text'] = program['text'].replace('一半', '')
        elif name == 'NeutronAegis':
            layers, threshold = (22, 8) if upgraded else (16, 10)
            program['text'] = f'使已激活的【星辉】增加{layers}层。每累计消耗{threshold}{{starIcon}}，获得1点残影，并使【星辉】增加5层。'
            program['rules'] = [
                instruction('play', 'grow', 'Stars', str(layers)),
                instruction('play', 'neutron_meter', 'Stars', '1', requireActive=False),
            ]
        elif name == 'KnockoutBlow':
            program['text'] = '此牌额外造成{0}点伤害。此牌命中的敌人在同一回合内死亡时，也能触发获得{starIcon}的效果。'
            program['amountText'] = program['text']
            program['rules'] = [instruction('damage_bonus', 'modifier', 'Stars', 'A', requireActive=False)]
        elif name == 'Hellraiser':
            program['text'] = program['text'].replace('自动打出的打击获得', '自动打出的打击牌获得')
        for key in ('text', 'amountText'):
            if not program.get(key):
                continue
            text = re.sub(r'(\d+|\{\d+\})颗?(?:蓝星|星星)', r'\1{starIcon}', program[key])
            program[key] = (text.replace('蓝星', '辉星').replace('星星', '辉星')
                            .replace('具有辉星耗能的', '消耗{starIcon}的'))
    for child in revision.get('variants', {}).values():
        update(name, child, instruction)
    updated = repr((revision['base'], revision['upgraded']))
    if updated != original:
        revision['sourceSha256'] = sha256(updated.encode()).hexdigest()
    return revision
