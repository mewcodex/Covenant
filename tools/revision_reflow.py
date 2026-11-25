"""Heavy and Reflow memberships and card traits from the split proposal."""

from hashlib import sha256
import re


HEAVY_MEMBERS = set('''
Abrasive BansheesCry Barricade BeatDown Bludgeon Bombardment BulletTime Bury Calamity
ChoChorus Coda Corruption CreativeAi Cruelty DanseMacabre Demesne DemonForm EchoForm
EndOfDays Ensemble EternalArmor ExpectAFight Hegemony Honing HowlFromBeyond IceLance
Jackpot KinglyKick Knockdown KnockoutBlow LostLute Mangle Mayhem Melancholy MementoMori
MeteorStrike Midnight Murder MythicForm Nightmare Outbreak Pinpoint Pounce Pyre Reanimate
Reap ReaperForm Reflex Refract Relax RollingBoulder SerpentForm Simpleton Squeeze
Stampede Stomp Sunder Tactician TheFool Timber Unrelenting Uproar VoidForm Voltaic WraithForm
'''.split())

REFLOW_MEMBERS = set('''
Adrenaline Alignment Automation BigBang Bloodletting BorrowedTime BowSmack ChargeBattery
Convergence Delay Demesne DoubleEnergy DrumOfBattle Encore EpicRetrieval ForgottenRitual
Friendship Hegemony Invoke KiRest KiWhip Neurosurge Offering Orbit Production Pyre RefineBlade
Restlessness Scavenge Sidestep Subroutine Sunder Supercritical Tactician Turbo Wisp Yah
BrightestFlame Fuel Joy Luminesce Outmaneuver Relax TheEmperor TheStar TheSun
'''.split())

# These traits already use the cadence or condition that fits their new covenant.
TRAIT_MOVES = {
    'BorrowedTime': ('Heavy', '重装', 'Reflow', '回流'),
    'Friendship': ('Heavy', '重装', 'Reflow', '回流'),
    'Hegemony': ('Heavy', '重装', 'Reflow', '回流'),
    'Supercritical': ('Heavy', '重装', 'Reflow', '回流'),
    'Fuel': ('Heavy', '重装', 'Reflow', '回流'),
    'Luminesce': ('Heavy', '重装', 'Reflow', '回流'),
    'TheEmperor': ('Heavy', '重装', 'Reflow', '回流'),
    'Automation': ('Rotation', '轮转', 'Reflow', '回流'),
    'ChargeBattery': ('Guard', '坚守', 'Reflow', '回流'),
    'DoubleEnergy': ('Ember', '余烬', 'Reflow', '回流'),
    'Sidestep': ('Zero', '零界', 'Reflow', '回流'),
    'Subroutine': ('Ritual', '仪式', 'Reflow', '回流'),
    'TheStar': ('Rotation', '轮转', 'Reflow', '回流'),
    'Whirlwind': ('Heavy', '重装', 'Channel', '倾注'),
    'TearAsunder': ('Heavy', '重装', 'Blood', '血誓'),
    'Uppercut': ('Heavy', '重装', 'Breach', '破绽'),
    'Hyperbeam': ('Heavy', '重装', 'Resonance', '共鸣'),
    'Catastrophe': ('Heavy', '重装', 'Chain', '连携'),
    'Buffer': ('Heavy', '重装', 'Guard', '坚守'),
    'CelestialMight': ('Heavy', '重装', 'Fury', '狂战'),
    'SleightOfFlesh': ('Heavy', '重装', 'Breach', '破绽'),
    'TagTeam': ('Heavy', '重装', 'Chain', '连携'),
}


def update_traits(name, revision):
    if name not in TRAIT_MOVES and name not in {'Cruelty', 'Pyre', 'WasteAway'}:
        return revision
    for stage in ('base', 'upgraded'):
        program = revision[stage]
        if name == 'WasteAway':
            total = sum(int(amount) for amount in re.findall(r'增加(\d+)层', program['text']))
            program['text'] = f'抽到此牌时，使已激活的【残响】增加{total}层。'
            program['rules'][0]['amount'] = str(total)
            program['rules'] = program['rules'][:1]
        elif name == 'Pyre':
            program['text'] = program['text'].replace('花费至少3点能量打出一张牌', '打出一张原始能量消耗至少为3的牌')
            program['rules'][0]['condition'] = 'subject_cost>=3'
        elif name == 'Cruelty':
            program['text'] = program['text'].replace('手牌中一张随机攻击牌', '手牌中原始能量消耗最高的一张攻击牌')
        else:
            source, source_title, target, target_title = TRAIT_MOVES[name]
            for rule in program['rules']:
                if rule['bond'] == source:
                    rule['bond'] = target
            for field in ('text', 'amountText'):
                if field in program:
                    program[field] = program[field].replace(f'【{source_title}】', f'【{target_title}】')
    revision['sourceSha256'] = sha256(repr((revision['base'], revision['upgraded'])).encode()).hexdigest()
    return revision


def update_membership(card):
    name = card['className']
    card['bonds'] = [bond for bond in card['bonds'] if bond not in {'Heavy', 'Reflow'}]
    evidence = card['evidence']
    evidence.pop('Heavy', None)
    evidence.pop('Reflow', None)
    if name in HEAVY_MEMBERS:
        card['bonds'].append('Heavy')
        evidence['Heavy'] = '高耗能牌或直接支援高耗能牌。'
    if name in REFLOW_MEMBERS:
        card['bonds'].append('Reflow')
        evidence['Reflow'] = '提供能量或配合能量运转。'
