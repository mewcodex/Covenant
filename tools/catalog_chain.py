"""Draw, delayed draw, and explicit play/draw-count payoffs, not CardsVar names."""
CHAIN_MEMBERS = {}

def include(names, reason, power=None):
    for name in names.split():
        if name in CHAIN_MEMBERS:raise ValueError('Duplicate Chain review: '+name)
        CHAIN_MEMBERS[name]=(power,reason)

include('''Acrobatics Adrenaline Backflip BattleTrance BigBang BrightestFlame BurningPact
CompileDriver Constellation Coolheaded DaggerThrow DecisionsDecisions DeepStretch DeprecatedCard
DrumOfBattle Encore EscapePlan Expertise Fetch Finesse FlashOfSteel Ftl Glimmer Glow HuddleUp
Impatience Joy KiHop MasterOfStrategy MinionStrike Modded Neurosurge Offering
Overclock Parse PhotonCut Pillage PommelStrike Prepared Prophesize Reboot Reflex Restlessness
RocketPunch Rummage Scourge Scrape Scrawl ShrugItOff Skim Slimed Soul SpoilsOfBattle
SteadyCadence SweepingBeam TheHermit TheHighPriestess TheSun ThinkingAhead WahChoKi
WheelOfFortune Wrest''', '原版效果直接抽牌，包含有条件、随机分支或奇巧自动打出的抽牌。')
include('DarkEmbrace', '附加能力在消耗卡牌后抽牌。', 'DarkEmbracePower')
include('Iteration', '附加能力在满足原版条件的抽牌后继续抽牌。', 'IterationPower')
include('Motif', '附加能力在获得旋律后抽牌。', 'MotifPower')
include('Pagestorm', '附加能力在抽到虚无牌后继续抽牌。', 'PagestormPower')
include('Vicious', '附加能力在施加易伤后抽牌。', 'ViciousPower')
include('Demesne', '附加能力增加后续回合抽牌。', 'DemesnePower')
include('Ennui', '附加能力增加后续回合抽牌。', 'EnnuiPower')
include('MachineLearning', '附加能力增加后续回合抽牌。', 'MachineLearningPower')
include('ToolsOfTheTrade', '附加能力增加后续回合抽牌，再主动弃牌。', 'ToolsOfTheTradePower')
include('Tyranny', '附加能力增加后续回合抽牌，再消耗手牌。', 'TyrannyPower')
include('GuidingStar WahKi Plot Predator Relax TheStar', '附加下回合抽牌能力。', 'DrawCardsNextTurnPower')
include('DeathMarch Murder', '伤害读取原版抽牌记录，属于抽牌收益。')
include('GrandFinale', '抽牌堆为空时才能打出，属于抽空牌堆的收益。')
include('Finisher LunarBlast GoldAxe', '攻击次数或伤害读取原版出牌数量记录。')
include('MakeItSo', '累计打出指定数量的技能牌后自动回到手牌。')
include('Pinpoint Stomp', '费用读取本回合已经打出的指定类型卡牌数量。')
include('Panache', '附加能力在累计打出指定数量的牌后造成伤害。', 'PanachePower')
include('Feral', '附加能力记录本回合零费攻击出牌次数，并让前几张回到手牌。', 'FeralPower')

CHAIN_EXCLUSIONS = {
    'DrainPower': 'CardsVar表示随机升级的弃牌数量，没有抽牌或连续出牌收益。',
    'Jackpot': 'CardsVar表示生成的零费牌数量，没有抽牌或连续出牌收益。',
    'LeadingStrike': 'CardsVar表示生成的小刀数量，没有抽牌或连续出牌收益。',
    'MightyWeapon': 'CardsVar表示生成的攻击牌数量，没有抽牌或连续出牌收益。',
    'NeowsFury': '从弃牌堆取回指定数量的牌，不是抽牌或连续出牌收益。',
    'PactsEnd': 'CardsVar表示消耗堆的启用门槛，属于消耗收益。',
    'Reave': 'CardsVar表示生成的灵魂数量，没有抽牌或连续出牌收益。',
    'SeekerStrike': '从抽牌堆候选中选择一张加入手牌，不派发抽牌事件。',
    'SorrowfulStrike': 'CardsVar表示生成的悲伤数量，没有抽牌或连续出牌收益。',
    'Normality': '限制出牌数量的诅咒，不提供连续出牌收益。',
}
