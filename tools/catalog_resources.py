"""Reviewed star, forge, hostile doom and Osty resource components."""

RESOURCE_MEMBERS = {'Stars':{},'Forge':{},'Doom':{},'Bones':{}}

def include(bond,names,reason,power=None):
    for name in names.split():
        if name in RESOURCE_MEMBERS[bond]:raise ValueError('Duplicate resource review: '+name)
        RESOURCE_MEMBERS[bond][name]=(power,reason)

include('Stars','Alignment AstralPulse CloakOfStars Comet Constellation CrescentSpear DecisionsDecisions Devastate DyingStar FallingStar GammaBlast GuidingStar MeteorShower NeutronAegis ParticleWall Quasar Reflect Resonance RoyalGamble SevenStars Stardust TheSmith',
        '原版效果具有固定或X星星消耗；部分卡牌同时提供星星体系收益。')
include('Stars','BigBang GatherLight Glow KnockoutBlow Radiate ShiningStrike SolarStrike Venerate','实际效果为自己提供星星。')
include('Stars','Convergence','通过原版附加能力在下回合提供星星。','StarNextTurnPower')
include('Stars','HiddenCache','立即提供星星，并通过附加能力在下回合提供星星。','StarNextTurnPower')
for name,reason in {
    'BlackHole':'附加能力在获得星星或花费星星打出卡牌后对敌人造成伤害。',
    'ChildOfTheStars':'附加能力在花费星星后提供格挡。',
    'Genesis':'附加能力在能量重置后提供星星。',
    'TheSealedThrone':'原版具有星星消耗，并附加星星体系能力。',
    'VoidForm':'附加能力使每回合限定数量的牌可以免除星星消耗。',
}.items():include('Stars',name,reason,name+'Power')

include('Forge','BeatIntoShape BigBang Bulwark RefineBlade SpoilsOfBattle SummonForth TheSmith WroughtInWar','实际效果调用原版锻造命令。')
include('Forge','SovereignBlade','原版君王之剑本身，可以接受锻造并保留。')
for name,reason in {
    'Conqueror':'锻造并为君王之剑提供针对目标的伤害收益。',
    'Furnace':'附加能力在回合开始时锻造。',
    'HammerTime':'附加能力在自己锻造后为其他友方锻造。',
    'Parry':'附加能力让君王之剑根据原版规则提供格挡。',
    'SeekingEdge':'锻造并修改君王之剑的攻击规则。',
    'SwordSage':'附加能力为君王之剑提供原版关键词收益。',
}.items():include('Forge',name,reason,name+'Power')

include('Doom','BlightStrike Death Deathbringer EndOfDays NegativePulse NoEscape Scourge','实际效果向敌人施加灾厄。')
include('Doom','DeathsDoor','根据本回合是否向敌人施加灾厄获得额外格挡。')
include('Doom','TimesUp','根据目标的灾厄层数决定攻击伤害。')
for name,reason in {
    'Countdown':'附加能力在回合开始时向敌人施加灾厄。',
    'Oblivion':'附加能力在后续出牌后向目标施加灾厄。',
    'ReaperForm':'附加能力在造成伤害后施加灾厄。',
    'Shroud':'附加能力在施加灾厄后提供格挡。',
    'Underworld':'附加能力在其他友方造成攻击伤害后施加灾厄。',
}.items():include('Doom',name,reason,name+'Power')

include('Bones','Afterlife Bodyguard Cleanse Dirge LegionOfBone PullAggro Reanimate Spur','实际效果召唤奥斯提，部分效果同时提供其他召唤收益。')
include('Bones','BoneShards Fetch Flatten HighFive Poke Protector Rattle RightHandHand Snap Squeeze SweepingGaze Unleash','实际效果让自己的奥斯提攻击，具有原版奥斯提攻击标签。')
include('Bones','Sacrifice','以自己的奥斯提生命为资源提供格挡。')
include('Bones','Invoke','附加能力在下回合召唤奥斯提。','SummonNextTurnPower')
for name,reason in {
    'Calcify':'附加能力增加自己的奥斯提造成的攻击伤害。',
    'DevourLife':'附加能力在打出灵魂后召唤奥斯提。',
    'NecroMastery':'召唤奥斯提，并在奥斯提损失生命时对敌人造成伤害。',
    'SicEm':'让奥斯提攻击，并为奥斯提提供针对目标的后续收益。',
}.items():include('Bones',name,reason,name+'Power')

RESOURCE_EXCLUSIONS = {
    'Doom':{'Neurosurge':'灾厄仅是施加给自己的代价，不向敌人施加灾厄，也不提供灾厄体系收益。'},
    'Bones':{
        'Thrash':'消耗攻击牌并读取其伤害值；对奥斯提伤害变量的兼容不等于奥斯提攻击或召唤。',
        'Apparition':'自身的无实体通用防御兼容宠物，不是奥斯提专用组件。',
        'Buffer':'自身的缓冲通用防御兼容宠物，不是奥斯提专用组件。',
        'WraithForm':'自身的无实体通用防御兼容宠物，不是奥斯提专用组件。',
        'SummonForth':'英文类名中的召唤实际指君王之剑，与召唤奥斯提无关。',
    },
}
