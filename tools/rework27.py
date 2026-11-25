"""Compile the approved card rework table into the existing revision catalog."""
import hashlib
import re
from pathlib import Path

TABLE = Path(__file__).resolve().parents[1] / 'proposals/0.5/27张卡牌特性重做稿.md'
ROWS = {}
for line in TABLE.read_text('utf-8').splitlines():
    if line.startswith('| ') and not line.startswith(('| ---', '| 卡牌 |')):
        name, bond, base, upgraded, notes = (cell.strip() for cell in line.strip('|').split('|'))
        ROWS[name] = (bond, base, upgraded, notes)

# Each row uses one native class. The optional number is the value shown by the card
# or the per-effect amount used by its specialized native callback.
SPECS = {
    'DemonForm': ('恶魔形态', '1', '2', True),
    'Bludgeon': ('重锤', '1', '2', True),
    'Uppercut': ('上勾拳', '1', '2', True),
    'Hellraiser': ('地狱狂徒', '1', '2', False),
    'Juggling': ('杂耍', '1', '2', False),
    'PerfectedStrike': ('完美打击', '1', '1', True),
    'Envenom': ('涂毒', '1', '2', False),
    'PhantomBlades': ('幻影之刃', '1+floor(L/100)', '2+floor(L/100)', False),
    'StormOfSteel': ('钢铁风暴', '1', '2', True),
    'NoxiousFumes': ('毒雾', '1', '2', False),
    'MeteorStrike': ('陨石打击', 'min(40,floor(L/6))', 'min(50,floor(L/4))', False),
    'Claw': ('爪击', 'min(50,floor(L/4))', 'min(50,floor(L/3))', True),
    'Turbo': ('内核加速', '1', '1', False),
    'Loop': ('循环', '1', '2', False),
    'SeekingEdge': ('追踪之刃', '100', '125', False),
    'MakeItSo': ('如此甚好', 'min(70,floor(L/3))', 'min(70,floor(L/2))', True),
    'SwordSage': ('剑圣', '100', '125', False),
    'MeteorShower': ('陨星', '1', '2', True),
    'BorrowedTime': ('预借时间', '1', '2', True),
    'DeathsDoor': ('死亡之门', 'min(75,floor(L/3))', 'min(75,floor(L/3))', True),
    'ReaperForm': ('死神形态', 'min(100,floor(L/6))', 'min(125,floor(L/5))', False),
    'CallOfTheVoid': ('虚空之唤', 'min(75,floor(L/4))', 'min(75,floor(L/3))', False),
    'DivineShot': ('神力射击', 'min(100,33+L/3)', 'min(100,33+L/3)', True),
    'WahChoKi': ('哇 啾 叽', '1', '1', True),
    'Ennui': ('倦怠', 'min(75,floor(L/4))', 'min(75,floor(L/3))', False),
    'Ecstasy': ('狂喜', '1', '2', False),
    'Clash': ('交锋', '1', '2', True),
    'Calamity': ('劫难', 'min(40,floor(L/10))', 'min(45,floor(L/8))', False),
    'HandOfGreed': ('贪婪之手', 'max(1,20-floor(L/20))', 'max(1,20-floor(L/20))', False),
}
assert {spec[0] for spec in SPECS.values()} == set(ROWS)

def source_hash(name, legacy):
    source = legacy if name not in SPECS else repr((ROWS[SPECS[name][0]], SPECS[name]))
    return hashlib.sha256(source.encode()).hexdigest()

def programs(name, instruction, native_powers):
    label, base_amount, upgraded_amount, direct = SPECS[name]
    bond, base_text, upgrade_text, _ = ROWS[label]
    def program(text, amount, upgraded):
        text = text.replace('[E]', '{energyPrefix:energyIcons(1)}')
        text = re.sub(r'(?:每\d+层(?:过载|零界|连携|幽逝|悲欢|造物)增加1个百分点，最高\d+%|初始增伤提高至2点)。$', '', text)
        result = dict(text=text, nativePowers=native_powers,
                      rules=[instruction('redesign', 'redesign', bond, amount,
                                         scope='hand' if direct else 'own', requireActive=False)])
        if '{概率}' in text or '{数值}' in text:
            result['amountText'] = text.replace('{概率}', '{0}').replace('{数值}', '{0}')
        if name == 'DivineShot':
            result['displayAmounts'] = [amount.replace('L', f'(L+{16 if upgraded else 12})')]
        return result
    return program(base_text, base_amount, False), program(upgrade_text, upgraded_amount, True)
