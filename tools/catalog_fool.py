"""Reviewed Fool flows and Ethereal components; memberships are not character locks."""

FOOL_MEMBERS = {'Arrows':{},'Melody':{},'Emotion':{}}

def include(bond,names,reason,power=None):
    for name in names.split():
        if name in FOOL_MEMBERS[bond]:raise ValueError('Duplicate Fool review: '+name)
        FOOL_MEMBERS[bond][name]=(power,reason)

include('Arrows','BlossomShot DivineShot FoolishShot HeavyShot HobblingShot MelodicShot QuickShot RainShot TwinShot RepeatShot SkyShot TheShot TrickShot','实际效果具有发射标签并调用原版发射箭矢命令。')
include('Arrows','BowSmack ChoFirm KiHop Nail WahHammer','实际效果根据箭矢可用状态或敌人扎穿状态提供收益。')
include('Arrows','ArrowStab Brace ChoReady Nock ShowOff','实际效果为自己提供瞄准。')
include('Arrows','Quiver','增加原版箭矢栏位。')
include('Arrows','Recall Uproot Wrest','实际效果取回扎穿敌人的箭矢。')
include('Arrows','EpicRetrieval','由自己打出的攻击取回箭矢后获得能量。')
include('Arrows','WhistlingArrows','附加在卡牌本身的效果会在发射箭矢后从弃牌堆回到手牌。')
for name,reason in {
    'Archery':'附加能力在回合开始时提供瞄准。',
    'ExplosiveArrows':'附加能力为箭矢命中提供额外伤害。',
    'SerratedArrows':'附加能力为箭矢命中提供额外收益。',
    'CoveringFire':'附加能力在发射箭矢后为其他友方提供格挡。',
    'Quietus':'附加能力强化下一次箭矢攻击。',
    'Yah':'附加能力在每回合首次取回箭矢后提供能量。',
}.items():include('Arrows',name,reason,name+'Power')

include('Melody','ChoBarbs ChoChorus ChoFirm ChoKi ChoLimbs ChoReady KiChant KiHop KiRest KiVerse KiWhip WahBlast WahCho WahChoKi WahHammer WahKi WahSweep WahSymphony WahTouch','实际效果具有原版哇、啾或叽旋律标签，可能同时提供其他旋律收益。')
include('Melody','Coda HarmonicShield MelodicShot RhythmStrike SteadyCadence','根据已经获得的旋律数量提供伤害、格挡或抽牌收益。')
for name,reason in {
    'LostLute':'附加能力在获得新旋律后自动打出抽牌堆顶的牌。',
    'Lullaby':'附加能力在获得全部旋律后提供格挡。',
    'Motif':'附加能力在获得新旋律后抽牌。',
    'SongPath':'附加能力在获得啾旋律后提供格挡。',
    'Tune':'获得叽旋律时，附加能力升级手牌。',
    'ViolentChord':'附加能力在获得哇旋律后对敌人造成伤害。',
}.items():include('Melody',name,reason,name+'Power')

include('Emotion','Anguish','根据本场生成悲伤的次数提供攻击收益。')
include('Emotion','BrightenUp SelfDeprecate','实际效果生成悲伤和喜悦。')
include('Emotion','Growth Optimism','实际效果变化或消耗手牌中的悲伤。')
include('Emotion','Jest Rummage SorrowfulStrike Vitriol','实际效果生成悲伤。')
include('Emotion','MourningBlow','打出后会变化为悲伤。')
include('Emotion','PunchOfJoy','实际效果将悲伤变化为喜悦。')
include('Emotion','Sadness Joy','原版悲伤或喜悦本身。')
for name,reason in {
    'AllIsWell':'附加能力在生成悲伤后提供格挡。',
    'Ecstasy':'附加能力将悲伤变化为喜悦。',
    'Ennui':'附加能力在抽牌前生成悲伤。',
}.items():include('Emotion',name,reason,name+'Power')

FOOL_EXCLUSIONS={'Emotion':{'BundleOfJoy':'仅生成无色牌；英文类名中的 Joy 并不是喜悦卡牌机制。'}}

ETHEREAL_SUPPORT = {
    'BansheesCry':(None,'根据本回合打出过的虚无牌提供攻击收益。'),
    'CallOfTheVoid':('CallOfTheVoidPower','附加能力生成具有虚无的牌。'),
    'DarkEmbrace':('DarkEmbracePower','虚无消耗也会提供抽牌，并专门延迟到弃牌结算后执行。'),
    'Eidolon':(None,'重新打出消耗堆中具有虚无且可以打出的牌。'),
    'Pagestorm':('PagestormPower','附加能力在抽到虚无牌后额外抽牌。'),
    'PullFromBelow':(None,'根据本回合打出过的虚无牌数量决定攻击次数。'),
    'SculptingStrike':(None,'实际效果为选中的手牌赋予虚无。'),
    'SpiritOfAsh':('SpiritOfAshPower','附加能力在打出虚无牌前提供格挡。'),
    'Veilpiercer':('VeilpiercerPower','附加能力让下一张符合条件的虚无牌免费打出。'),
}

ETHEREAL_MEMBERS={name:(None,'原版卡牌自身具有虚无；运行时读取当前关键词，升级移除后同步退出。') for name in
    'Apparition AscendersBane Clumsy Dazed Defile Defy Demesne DyingStar EchoForm EnfeeblingTouch Fear Folly Lethality Parse Seance SweepingGaze Void VoidForm'.split()}
ETHEREAL_MEMBERS.update(ETHEREAL_SUPPORT)
