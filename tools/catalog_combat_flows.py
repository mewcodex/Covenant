"""Reviewed poison, Shiv and orb mechanics; inspect attached powers as well as cards."""

COMBAT_FLOW_MEMBERS = {'Poison':{},'Blades':{},'Resonance':{}}

def include(bond,names,reason,power=None):
    for name in names.split():
        if name in COMBAT_FLOW_MEMBERS[bond]:raise ValueError('Duplicate flow review: '+name)
        COMBAT_FLOW_MEMBERS[bond][name]=(power,reason)

include('Poison','BouncingFlask BubbleBubble DeadlyPoison Haze Outbreak PoisonedStab Snakebite', '实际效果向敌人施加中毒。')
include('Poison','Mirage','根据存活敌人的中毒层数获得格挡。')
for name,reason in {
    'Accelerant':'附加能力让敌人的中毒额外触发。',
    'Concoct':'附加能力让目标的攻击施加中毒。',
    'CorrosiveWave':'附加能力在抽牌后向敌人施加中毒。',
    'Envenom':'附加能力在攻击造成未被格挡的伤害后施加中毒。',
    'NoxiousFumes':'附加能力在回合开始时向敌人施加中毒。',
}.items():include('Poison',name,reason,name+'Power')

include('Blades','BladeDance BladeOfInk BladeSymphony CloakAndDagger HiddenDaggers LeadingStrike StormOfSteel TheMoon UpMySleeve',
        '实际效果生成小刀。')
include('Blades','Shiv','原版小刀本身，拥有小刀标签。')
include('Blades','KnifeTrap','从消耗堆重新打出具有小刀标签的牌。')
for name,reason in {
    'Accuracy':'附加能力增加小刀造成的伤害。',
    'FanOfKnives':'生成小刀，并让小刀攻击全体敌人。',
    'InfiniteBlades':'附加能力在抽牌前生成小刀。',
    'PhantomBlades':'附加能力为小刀赋予保留和首次攻击增益。',
}.items():include('Blades',name,reason,name+'Power')

include('Resonance','BallLightning Chaos Chill ColdSnap Coolheaded Darkness Fusion Glacier Glasswork Hibernate IceLance Ignition MeteorStrike Null Rainbow Refract ShadowShield Tempest TheMagician Voltaic Zap',
        '实际效果为自己或友方生成充能球；部分效果还主动触发充能球被动。')
include('Resonance','Capacitor Modded','增加充能球栏位。')
include('Resonance','Dualcast MultiCast Quadcast Shatter','实际效果激发自己的充能球。')
include('Resonance','TeslaCoil','主动触发自己的闪电充能球被动。')
include('Resonance','Barrage','根据自己的充能球数量决定攻击次数。')
include('Resonance','CompileDriver','根据自己的充能球种类数决定抽牌数量。')
include('Resonance','Defragment','为自己提供正数集中。')
for name,reason in {
    'BiasedCognition':'提供正数集中，并附加后续逐回合失去集中的能力。',
    'ConsumingShadow':'生成黑暗充能球，并附加回合结束时激发充能球的能力。',
    'Coolant':'附加能力根据自己的充能球种类数提供格挡。',
    'FocusedStrike':'附加原版临时集中。',
    'Hailstorm':'附加能力在拥有足够冰霜充能球时对敌人造成伤害。',
    'Hotfix':'附加原版临时集中。',
    'LightningRod':'附加能力在能量重置后生成闪电充能球。',
    'Loop':'附加能力在回合开始时触发最前方充能球被动。',
    'Spinner':'生成玻璃充能球，并附加相应持续能力。',
    'Storm':'附加能力在打出能力牌后生成闪电充能球。',
    'Synchronize':'根据自己的不同充能球类型提供原版临时集中。',
    'Thunder':'附加能力在闪电充能球激发后提供额外伤害。',
    'TrashToTreasure':'附加能力在生成状态牌后生成充能球。',
}.items():include('Resonance',name,reason,name+'Power')

COMBAT_FLOW_EXCLUSIONS = {
    'Resonance':{
        'BulkUp':'移除充能球栏位以获得力量和敏捷，不提供充能球收益。',
        'Hyperbeam':'自身效果降低集中，不生成、激发或强化充能球。',
        'Orbit':'仅根据消耗的能量返还能量，类名中的 Orbit 不是充能球机制。',
    },
}
