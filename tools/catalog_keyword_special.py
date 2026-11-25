"""Explicit keyword/support and selection/economy decisions for v0.113.1."""

# Authored Zero candidates whose innate Exhaust survives their upgrade. Keep this
# independent of combat keywords so discounts or temporary keyword changes cannot re-add them.
ZERO_SELF_EXHAUST = set('''
Abundance Adrenaline Alchemize Assassinate Backstab BigBang BlotOutTheSky BootSequence
CalculatedGamble DarkShackles Death DecisionsDecisions Dirge Distraction DoubleEnergy
DramaticEntrance Expose Florescence Fuel InfernalBlade JackOfAllTrades Joy Judgement Justice
Luminesce Malaise MasterOfStrategy MinionDiveBomb MinionSacrifice MinionStrike Offering
PanicButton Production Purity Reboot RoyalGamble SharedFate SignalBoost Soul Strength
Supercritical SweepingGaze Temperance TheChariot TheDevil TheEmperor TheEmpress TheFool
TheHangedMan TheHermit TheHierophant TheHighPriestess TheLovers TheMagician TheMoon
TheStar TheSun TheTower TheWorld WheelOfFortune WhiteNoise Wish Wisp
'''.split())

# Original card data, including upgrades. Star X costs also consume blue stars.
ZERO_COST_POWERS = set('''
Automation Compost Lullaby Neurosurge Nostalgia Pagestorm Panache Stratagem Subroutine
Tank TheSealedThrone ToolsOfTheTrade TrashToTreasure
'''.split())
ZERO_STAR_COST = set('''
Alignment AstralPulse CloakOfStars Comet Constellation CrescentSpear DecisionsDecisions
Devastate DyingStar FallingStar GammaBlast GuidingStar MeteorShower NeutronAegis ParticleWall
Quasar Reflect Resonance RoyalGamble SevenStars Stardust TheSealedThrone TheSmith
'''.split())
ZERO_EXCLUDED = ZERO_SELF_EXHAUST | ZERO_COST_POWERS | ZERO_STAR_COST

STRIKE_MEMBERS = {name: (None, '原版具有打击标签；实际归属读取当前实例标签。') for name in '''
AdaptiveStrike AshenStrike BlightStrike FocusedStrike LeadingStrike MeteorStrike MinionStrike
MomentumStrike PerfectedStrike PommelStrike RhythmStrike SculptingStrike SeekerStrike SetupStrike
ShiningStrike SolarStrike SorrowfulStrike StrikeDefect StrikeFool StrikeIronclad StrikeNecrobinder
StrikeRegent StrikeSilent TwinStrike UltimateStrike
'''.split()}

# Direct block uses the game's GainsBlock query. The Blade's conditional block is
# deliberately not a support: without Parry, it cannot currently provide block.
GUARD_DIRECT = '''
Armaments Backflip BloodWall Blur BoneShards BoostAway BootSequence Bulwark ChargeBattery
ChoBarbs ChoChorus ChoFirm ChoKi ChoLimbs ChoReady CloakAndDagger CloakOfStars Colossus Compact
Constellation CosmicIndifference Dash DeathsDoor DefendDefect DefendFool DefendIronclad
DefendNecrobinder DefendRegent DefendSilent Deflect Defy Delay DemonicShield DodgeAndRoll
Entrench Equilibrium EscapePlan EvilEye ExpectAFight FightThrough Finesse Fisticuffs
FlameBarrier GatherLight GeneticAlgorithm Glacier Glasswork Glitterstream GraveWarden
HandTrick HarmonicShield Hologram IAmInvincible Impervious Intercept IronWave Jest Leap LegSweep
Lift LightningRod ManifestAuthority Melancholy MightyShield Mimic MinionSacrifice
Mirage NegativePulse PanicButton ParticleWall Patter PullAggro Rally Reflect Relax Sacrifice
SecondWind ShadowShield ShrugItOff SovereignBlade Stack Survivor Taunt Temperance TheGambit
ToricToughness TrueGrit UltimateDefend Undeath Untouchable WahCho WahChoKi WhistlingArrows
'''.split()
GUARD_SUPPORT = {
    'Abrasive': ('DexterityPower', '自己获得正数敏捷。'),
    'Afterimage': ('AfterimagePower', '附加能力在出牌后获得格挡。'),
    'AllIsWell': ('AllIsWellPower', '附加能力在生成悲伤后获得格挡。'),
    'Anticipate': ('AnticipatePower', '附加能力按原版条件提供敏捷。'),
    'Barricade': ('BarricadePower', '附加能力保留已有格挡。'),
    'BeaconOfHope': ('BeaconOfHopePower', '附加能力向友方分享获得的格挡。'),
    'BlossomShot': ('StretchPower', '通过原版成长能力提高后续格挡。'),
    'BodySlam': (None, '攻击伤害读取自己的格挡数值。'),
    'Bounce': ('StretchPower', '通过原版成长能力提高后续格挡。'),
    'BulkUp': ('DexterityPower', '牺牲充能球栏位后获得正数敏捷。'),
    'ChildOfTheStars': ('ChildOfTheStarsPower', '附加能力在花费星星后获得格挡。'),
    'Coolant': ('CoolantPower', '附加能力根据冰霜充能球数量获得格挡。'),
    'CoveringFire': ('CoveringFirePower', '附加能力在射箭后提供友方格挡。'),
    'CrimsonMantle': ('CrimsonMantlePower', '附加能力在后续回合获得格挡。'),
    'DanseMacabre': ('DanseMacabrePower', '附加能力在打出符合条件的牌前获得格挡。'),
    'DeepStretch': ('StretchPower', '获得成长，提高后续格挡。'),
    'Enrapture': ('StretchPower', '向友方提供成长，提高后续格挡。'),
    'EternalArmor': ('PlatingPower', '附加覆甲能力，在后续回合提供格挡。'),
    'Fade': ('FadePower', '向友方提供原版条件敏捷能力。'),
    'Fasten': ('FastenPower', '附加能力提高符合条件的格挡。'),
    'FeelNoPain': ('FeelNoPainPower', '附加能力在消耗自己的卡牌后获得格挡。'),
    'Footwork': ('DexterityPower', '自己获得正数敏捷。'),
    'Growth': ('StretchPower', '消耗悲伤并获得成长，提高后续格挡。'),
    'Lullaby': ('LullabyPower', '附加能力在集齐旋律后获得格挡。'),
    'NeutronAegis': ('PlatingPower', '附加覆甲能力，在后续回合提供格挡。'),
    'Parry': ('ParryPower', '附加能力使君王之剑打出时获得格挡。'),
    'PillarOfCreation': ('PillarOfCreationPower', '附加能力在生成卡牌后获得格挡。'),
    'Prolong': ('BlockNextTurnPower', '提供下回合格挡。'),
    'Prowess': ('DexterityPower', '自己获得正数敏捷。'),
    'Rage': ('RagePower', '附加能力在打出攻击牌后获得格挡。'),
    'Shadowmeld': ('ShadowmeldPower', '附加能力提高本回合的格挡倍率。'),
    'Shroud': ('ShroudPower', '附加能力在施加灾厄后获得格挡。'),
    'Sneaky': ('SneakyPower', '附加能力在符合条件的出牌后获得格挡。'),
    'SongPath': ('SongPathPower', '附加能力在获得相应旋律后获得格挡。'),
    'SpiritOfAsh': ('SpiritOfAshPower', '附加能力在打出虚无牌前获得格挡。'),
    'StoneArmor': ('PlatingPower', '附加覆甲能力，在后续回合提供格挡。'),
    'Unmovable': ('UnmovablePower', '附加能力提高符合条件的格挡倍率。'),
    'Yoga': ('StretchPower', '获得成长，提高后续格挡。'),
}
GUARD_MEMBERS = {name: (None, '原版直接格挡牌；实际归属读取当前实例的GainsBlock。') for name in GUARD_DIRECT}
GUARD_MEMBERS.update(GUARD_SUPPORT)

FORESIGHT_MEMBERS = {
    'Abundance': (None, '从生成的能力牌候选中选择一张。'),
    'Acrobatics': (None, '抽牌后选择丢弃的手牌。'),
    'Armaments': (None, '升级手牌，基础版和升级版都计入远见。'),
    'Begone': (None, '选择变化的手牌。'),
    'Brand': (None, '选择消耗的手牌。'),
    'BurningPact': (None, '选择消耗的手牌。'),
    'Charge': (None, '从抽牌堆选择变化的牌。'),
    'Cleanse': (None, '从抽牌堆选择消耗的牌。'),
    'CosmicIndifference': (None, '从弃牌堆选择取回的牌。'),
    'DaggerThrow': (None, '抽牌后选择丢弃的手牌。'),
    'DecisionsDecisions': (None, '选择手牌中的技能牌。'),
    'Discovery': (None, '从生成的卡牌候选中选择一张。'),
    'Dredge': (None, '从弃牌堆选择取回的牌。'),
    'DualWield': (None, '选择复制的手牌。'),
    'Glimmer': (None, '选择放回抽牌堆的手牌。'),
    'Graveblast': (None, '从弃牌堆选择取回的牌。'),
    'Guards': (None, '选择要变化的手牌。'),
    'HandTrick': (None, '选择获得本回合奇巧的技能手牌。'),
    'Headbutt': (None, '选择放到抽牌堆顶部的弃牌。'),
    'HeirloomHammer': (None, '选择无色手牌。'),
    'HiddenDaggers': (None, '选择丢弃的手牌。'),
    'Hologram': (None, '从弃牌堆选择取回的牌。'),
    'KiChant': (None, '查看抽牌堆顶部的卡牌并选择其中一张。'),
    'LockedIn': (None, '从抽牌堆选择射击牌。'),
    'NeowsFury': (None, '从弃牌堆选择取回的牌。'),
    'Nightmare': ('NightmarePower', '选择要在下回合复制的手牌。'),
    'PhotonCut': (None, '选择放回抽牌堆的手牌。'),
    'Prepared': (None, '抽牌后选择丢弃的手牌。'),
    'Purity': (None, '选择要消耗的手牌。'),
    'Quasar': (None, '从生成的无色卡牌候选中选择一张。'),
    'WahKi': (None, '选择弃牌堆中的一张牌置于牌堆顶。'),
    'Scavenge': (None, '选择消耗的手牌。'),
    'SculptingStrike': (None, '选择获得虚无的手牌。'),
    'Seance': (None, '从抽牌堆选择变化的牌。'),
    'SecretTechnique': (None, '从抽牌堆选择技能牌。'),
    'SecretWeapon': (None, '从抽牌堆选择攻击牌。'),
    'SeekerStrike': (None, '从抽牌堆的随机候选中选择一张。'),
    'Snap': (None, '选择获得保留的手牌。'),
    'Splash': (None, '从其他角色的攻击牌候选中选择一张。'),
    'Survivor': (None, '选择丢弃的手牌。'),
    'TheHermit': (None, '选择丢弃或消耗的手牌。'),
    'ThinkingAhead': (None, '选择放回抽牌堆顶部的手牌。'),
    'Transfigure': (None, '选择变化的手牌。'),
    'TrueGrit': (None, '消耗手牌，基础版和升级版都计入远见。'),
    'Tutor': (None, '从目标玩家的抽牌堆选择卡牌。'),
    'Wish': (None, '从抽牌堆选择卡牌。'),
}
FORESIGHT_REMOVED = frozenset('Brand BurningPact Purity HiddenDaggers Prepared Acrobatics DaggerThrow TheHermit Survivor Guards Begone Scavenge ArrowStab Recall'.split())
for name in FORESIGHT_REMOVED:
    FORESIGHT_MEMBERS.pop(name,None)
FORESIGHT_UPGRADE = {}

PROFIT_MEMBERS = {
    'HandOfGreed': (None, '致命一击可以获得金币。'),
    'MyShinies': (None, '持有的金币数量增加攻击次数。'),
    'Royalties': ('RoyaltiesPower', '附加能力在战斗后提供金币奖励。'),
    'SpoilsMap': (None, '完成藏宝图任务后获得金币。'),
}
PROFIT_EXCLUDED = frozenset('Calamity Equilibrium EternalArmor Finesse Jackpot Prowess SecretTechnique SecretWeapon Shockwave ThinkingAhead Entropy'.split())
SPECIAL_EXCLUSIONS = {
    'Profit': {'Debt': '回合结束时被动失去金币，不提供经济收益或金币持有收益。'},
    'Foresight': {
        'SeekingEdge': '名称含Seek，但实际只锻造并强化君王之剑，没有选牌效果。',
        'SovereignBlade': '只引用SeekingEdge的范围攻击能力，没有选牌效果。',
    },
}
