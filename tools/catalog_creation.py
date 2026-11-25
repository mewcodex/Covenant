"""Reviewed creation sources: native combat generation of playable cards.

Temporary playable results also count dynamically at runtime. A static factory
helper declared on a token is not that token's effect when played. Native
combat transformation dispatches generation once at the end of its command.
"""

CREATION_MEMBERS = {}

def include(names, reason, power=None):
    for name in names.split():
        if name in CREATION_MEMBERS:raise ValueError('Duplicate creation review: '+name)
        CREATION_MEMBERS[name]=(power,reason)

include('Abundance BlotOutTheSky BundleOfJoy Discovery Distraction InfernalBlade JackOfAllTrades Jackpot Largesse ManifestAuthority Metamorphosis Quasar Splash Stoke TheHierophant TheLovers WhiteNoise',
        '通过原版卡牌工厂生成可打出的战斗临时牌，并加入战斗牌堆。')
include('AdaptiveStrike Anger Outrage Undeath', '打出后生成自己的战斗临时复制品。')
include('BladeDance BladeOfInk BladeSymphony CloakAndDagger FanOfKnives HiddenDaggers LeadingStrike StormOfSteel TheMoon UpMySleeve',
        '实际效果调用小刀生成命令，为自己或友方生成小刀。')
include('CaptureSpirit Dirge GlimpseBeyond GraveWarden Reave Severance', '实际效果生成灵魂并加入战斗牌堆。')
include('DualWield HeirloomHammer', '选取符合条件的牌，生成其战斗临时复制品。')
include('Florescence Growth', '将手牌变化为全力射击或成长的复制。')
include('BrightenUp SelfDeprecate', '除生成悲伤以外，还会实际生成可打出的喜悦。')
CREATION_TRANSFORMERS='Begone Charge Compact Dowsing Guards PrimalForce PunchOfJoy Seance Ecstasy Entropy'.split()
include(' '.join(CREATION_TRANSFORMERS[:-2]), '变化出非状态、非诅咒的战斗临时牌，原版变化命令结算末尾派发生成事件。')

for name,reason in {
    'Calamity':'附加能力在打出攻击牌后生成新的攻击牌。',
    'CallOfTheVoid':'附加能力在抽牌前生成具有虚无的卡牌。',
    'CreativeAi':'附加能力在抽牌前生成能力牌。',
    'HelloWorld':'附加能力在抽牌前生成普通牌。',
    'InfiniteBlades':'附加能力在抽牌前生成小刀。',
    'Juggling':'附加能力在第三张攻击打出后生成它的复制品。',
    'MythicForm':'附加能力在抽牌前生成塔罗牌。',
    'Nightmare':'附加能力在下回合生成所选卡牌的复制品。',
    'SentryMode':'附加能力在抽牌前生成扫荡凝视。',
    'Soulbound':'附加能力在生成灵魂后为目标额外生成灵魂。',
    'SpectrumShift':'附加能力在抽牌前生成无色牌。',
    'Ecstasy':'附加能力把悲伤变化为喜悦，原版变化命令结算末尾派发生成事件。',
    'Entropy':'附加能力变化手牌，原版变化命令结算末尾派发生成事件。',
    'Arsenal':'附加能力在生成卡牌后提供力量收益。',
    'PillarOfCreation':'附加能力在生成卡牌后提供格挡收益。',
}.items():include(name,reason,name+'Power')

CREATION_FORGERS='BeatIntoShape BigBang Bulwark Conqueror RefineBlade SeekingEdge SpoilsOfBattle SummonForth TheSmith WroughtInWar Furnace HammerTime'.split()
include(' '.join(CREATION_FORGERS[:-2]), '调用原版锻造命令；没有可用君王之剑时，实际生成一张君王之剑。')
include('Furnace', '附加能力在回合开始时锻造，没有可用君王之剑时生成一张。', 'FurnacePower')
include('HammerTime', '附加能力在自己锻造后为其他友方锻造，没有可用君王之剑时生成一张。', 'HammerTimePower')

CREATION_EXCLUSIONS = {}
for name in 'BoostAway CollisionCourse CrashLanding FightThrough GunkUp Jest MightyShield MightyWeapon Overclock Rummage SorrowfulStrike Turbo Vitriol Ennui MourningBlow'.split():
    CREATION_EXCLUSIONS[name]='只生成状态牌，不是生成可打出非状态、非诅咒牌的组件。'
for name in 'Shiv Soul'.split():
    CREATION_EXCLUSIONS[name]='源码中的静态生成辅助函数供其他牌调用，本牌打出时不会生成牌；战斗临时实例仍动态计入造物。'
for name in 'AllIsWell Smokestack TrashToTreasure RocketPunch'.split():
    CREATION_EXCLUSIONS[name]='生成收益只针对状态牌，不对造物所用的非状态生成生效。'

KNIFE_CREATION_REMOVED = 'BladeDance BladeOfInk BladeSymphony CloakAndDagger FanOfKnives HiddenDaggers InfiniteBlades LeadingStrike StormOfSteel UpMySleeve'.split()
CREATION_REMOVED = frozenset(KNIFE_CREATION_REMOVED + CREATION_FORGERS)
for name in KNIFE_CREATION_REMOVED:
    CREATION_MEMBERS.pop(name)
    CREATION_EXCLUSIONS[name]='只生成小刀，归入飞刃。'
for name in CREATION_FORGERS:
    CREATION_MEMBERS.pop(name)
    CREATION_EXCLUSIONS[name]='锻造在没有君王之剑时会生成剑，归入铸王。'
