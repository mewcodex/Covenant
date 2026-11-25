"""Reviewed exhaust/discard components; self keywords remain native instance state."""

# These are sources that consume another card or explicitly benefit from exhaustion.
# They can count without their own Exhaust keyword.
EMBER_SUPPORT = {
    'AshenStrike': (None, '伤害随消耗堆中的卡牌数量提高。'),
    'Bombardment': (None, '位于消耗堆时会在自动出牌阶段再次打出。'),
    'Brand': (None, '主动消耗手牌并获得力量。'),
    'BurningPact': (None, '消耗选中的手牌并抽牌。'),
    'Cinder': (None, '攻击后随机消耗一张手牌。'),
    'Cleanse': (None, '从抽牌堆选择卡牌并消耗。'),
    'Corruption': ('CorruptionPower', '附加能力使技能免费，并改变其打出后去向为消耗堆。'),
    'DarkEmbrace': ('DarkEmbracePower', '附加能力在自己的卡牌被消耗后抽牌。'),
    'DrumOfBattle': (None, '自身被消耗时获得能量。'),
    'EvilEye': (None, '根据本回合是否消耗过自己的卡牌获得额外格挡。'),
    'FeelNoPain': ('FeelNoPainPower', '附加能力在自己的卡牌被消耗后获得格挡。'),
    'FiendFire': (None, '消耗手牌并按消耗数量攻击。'),
    'FlakCannon': (None, '消耗战斗中尚未被消耗的状态牌并造成伤害。'),
    'ForgottenRitual': (None, '本回合消耗过自己的卡牌后提供能量。'),
    'Havoc': (None, '自动打出抽牌堆顶部的卡牌，并强制消耗它。'),
    'HowlFromBeyond': (None, '位于消耗堆时会在自动出牌阶段再次打出。'),
    'KnifeTrap': (None, '从消耗堆中再次打出小刀。'),
    'Midnight': (None, '根据本场战斗已经消耗的卡牌数量减少能量消耗。'),
    'PactsEnd': (None, '消耗堆达到指定卡牌数量后可以造成伤害。'),
    'Scavenge': (None, '消耗选中的手牌并提供下回合能量。'),
    'SecondWind': (None, '消耗非攻击手牌并按数量获得格挡。'),
    'SoulStorm': (None, '伤害随消耗堆中的灵魂数量提高。'),
    'Stoke': (None, '消耗手牌并生成等量的新牌。'),
    'TheDevil': (None, '消耗手牌并按数量获得能量。'),
    'TheHermit': (None, '选择手牌；诅咒被消耗，其他牌被丢弃。'),
    'Optimism': (None, '消耗手牌中的悲伤，并按消耗数量抽牌。'),
    'Thrash': (None, '选择并消耗攻击手牌，读取其伤害造成攻击。'),
    'TrueGrit': (None, '消耗手牌；升级后可以指定目标。'),
    'Tyranny': ('TyrannyPower', '附加能力在回合开始时选择并消耗手牌。'),
}

# Upgrading these native cards removes their own Exhaust keyword. They do not count at either level.
EMBER_EXCLUDED = set('''
Chill DemonicShield Discovery Fusion Graveblast Hologram Hotfix Ignition KnowThyPlace
Mimic Mirage Prolong Rainbow Recall SecretTechnique SecretWeapon ThinkingAhead Transfigure
Underworld Voltaic
'''.split())

def ember_category_excluded(card):
    return card['className'] != 'TheDevil' and (
        card['type'] in ('Status', 'Curse') or 'TokenCardPool' in card['pools']
    )

# Native canonical Exhaust sources, pinned to the reviewed source version.
EMBER_SELF = '''
Abundance Adrenaline Afterlife Alchemize Anointed Apotheosis Apparition Assassinate
Backstab BigBang BladeDance BlotOutTheSky Bombardment BootSequence BrightenUp BundleOfJoy
CalculatedGamble Chill DarkShackles Death Debris DecisionsDecisions DemonicShield
DeprecatedCard Dirge Discovery Distraction Dominate DoubleEnergy DramaticEntrance Dredge
Eidolon EnergySurge Ensemble Expose Feed FiendFire ForgottenRitual Fuel Fusion
GeneticAlgorithm GlimpseBeyond Graveblast Guards Hologram Hotfix HuddleUp Ignition
ImitationLearning Impervious InfernalBlade JackOfAllTrades Joy Judgement Justice KnowThyPlace
Luminesce Malaise MasterOfStrategy Metamorphosis Mimic MinionDiveBomb MinionSacrifice
MinionStrike Mirage MoltenFist NeowsFury Nightmare NotYet Offering PanicButton PiercingWail
Production Prolong Purity Putrefy Quietus Rainbow Reanimate Reboot Relax RoyalGamble Scrawl
SecretTechnique SecretWeapon SharedFate Shatter Shiv Shockwave ShowOff SignalBoost SkyShot
Slimed Soul SporeMind Strength Supercritical SweepingGaze Temperance TheChariot TheDevil
TheEmperor TheEmpress TheFool TheHangedMan TheHermit TheHierophant TheHighPriestess TheHunt
TheLovers TheMagician TheMoon TheScythe TheStar TheSun TheTower TheWorld ThinkingAhead
Toxic Transfigure Tremble Underworld Voltaic WheelOfFortune Whistle WhiteNoise Wish Wisp
'''.split()
EMBER_MEMBERS = {name: (None, '原版定义具有消耗关键词；实际计入以当前实例关键词为准。') for name in EMBER_SELF}
EMBER_MEMBERS.update(EMBER_SUPPORT)
EMBER_MEMBERS = {name: source for name, source in EMBER_MEMBERS.items() if name not in EMBER_EXCLUDED}

SLEIGHT_MEMBERS = {
    'Abrasive': (None, '具有奇巧，可在主动弃牌时自动打出。'),
    'Acrobatics': (None, '抽牌后主动丢弃手牌。'),
    'CalculatedGamble': (None, '主动丢弃整手牌并抽取相应数量。'),
    'DaggerThrow': (None, '攻击并抽牌后主动丢弃手牌。'),
    'FlickFlack': (None, '具有奇巧，可在主动弃牌时自动打出。'),
    'HandTrick': (None, '为其他技能手牌赋予本回合奇巧。'),
    'HiddenDaggers': (None, '主动丢弃手牌并生成小刀。'),
    'KiChant': (None, '查看抽牌堆顶部的卡牌，将其中一张放入手牌，主动丢弃其余卡牌。'),
    'MasterPlanner': ('MasterPlannerPower', '附加能力为打出的技能牌赋予奇巧。'),
    'MementoMori': (None, '具有奇巧，可在主动弃牌时自动打出。'),
    'Prepared': (None, '抽牌后主动丢弃手牌。'),
    'Reflex': (None, '具有奇巧，可在主动弃牌时自动打出并抽牌。'),
    'Ricochet': (None, '具有奇巧，可在主动弃牌时自动打出。'),
    'Scrape': (None, '抽牌后主动丢弃其中费用不为零的牌和X费牌。'),
    'ShadowStep': ('ShadowStepPower', '主动丢弃整手牌，并附加原版暗影步能力。'),
    'Sneaky': (None, '具有奇巧，可在主动弃牌时自动打出。'),
    'StormOfSteel': (None, '主动丢弃整手牌并生成小刀。'),
    'Survivor': (None, '获得格挡后主动丢弃手牌。'),
    'Tactician': (None, '具有奇巧，可在主动弃牌时自动打出并获得能量。'),
    'TheHermit': (None, '选择手牌；非诅咒牌被主动丢弃。'),
    'ToolsOfTheTrade': ('ToolsOfTheTradePower', '附加能力增加回合抽牌，并在回合开始时主动丢弃手牌。'),
    'Untouchable': (None, '具有奇巧，可在主动弃牌时自动打出。'),
    'WheelOfFortune': (None, '主动丢弃整手牌并抽牌。'),
}
SLEIGHT_SELF = {'Abrasive','FlickFlack','MementoMori','Reflex','Ricochet','Sneaky','Tactician','Untouchable'}
SLEIGHT_SUPPORT = {name: value for name,value in SLEIGHT_MEMBERS.items() if name not in SLEIGHT_SELF}

ZERO_SUPPORT = {
    'AllForOne': (None, '将弃牌堆中当前费用为零的可打出牌放入手牌，不取回X费牌。'),
    'Feral': ('FeralPower', '附加能力使本回合前几张零费攻击打出后回到手牌。'),
    'Jackpot': (None, '从角色卡池生成原始费用为零的牌。'),
    'OneForAll': ('OneForAllPower', '附加能力为零能量打出的非X费攻击提供伤害。'),
    'Scrape': (None, '抽牌后保留其中当前费用为零的牌，丢弃非零费和X费牌。'),
}
