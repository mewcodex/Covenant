import json, collections, os, sys
ROOT = sys.argv[1]
OUT = os.path.join(ROOT, 'proposals', '0.5')
os.makedirs(OUT, exist_ok=True)
C = {r['className']: r for r in json.load(open(os.path.join(ROOT, 'Data/cards.json'), encoding='utf-8'))}
T = json.load(open(os.path.join(ROOT, 'Data/trait-design.json'), encoding='utf-8'))
CAT = {c['card']: c for c in json.load(open(os.path.join(ROOT, 'audits/single-trait-catalog.json'), encoding='utf-8'))['cards']}

# ---------------------------------------------------------------- bonds
# (id, 名称, 分组, 基础门槛, 进阶门槛, 基础, 进阶, 5档, 10档, 备注)
B = [
('Fury','狂战','铁甲',2,6,
 '战斗开始时，获得 1+A/4 点力量。每回合首次获得力量后，你的下一张攻击牌在本回合的能量消耗减少1。',
 '每回合每打出3张攻击牌，在本回合获得 1+A/4 点力量，并抽1张牌。',
 '进阶的条件改为每打出2张攻击牌。',
 '你的回合结束时，本回合通过盟约获得的临时力量有一半（向下取整）保留到本场战斗结束。',
 '开局力量由 1+A/3 降为 1+A/4：13档时为4点，与两张燃烧相当。'),
('Blood','血誓','铁甲',2,6,
 '每回合首次因自己的效果失去生命后，你可以消耗1张手牌。如果这样做，抽1张牌，并获得 4+A 点格挡。',
 '每回合每因自己的效果失去生命2次，获得1点能量。你的下一张攻击牌额外造成 4+A 点伤害。',
 '每次因自己的效果失去生命后，获得 2+A/2 点格挡。',
 '每回合首次因自己的效果失去生命时，改为不失去生命，但仍视为失去过生命。',
 '基础格挡由 3+A 提到 4+A，补偿需要额外消耗一张手牌的代价。'),
('Ember','余烬','铁甲',2,6,
 '每回合首次消耗卡牌后，获得 3+A 点格挡。你可以将弃牌堆中的1张牌放到抽牌堆顶部。',
 '每次消耗卡牌，额外获得 2+A/2 点格挡。每回合每消耗3张牌，获得1点能量。',
 '每回合每消耗3张牌时，额外抽1张牌。',
 '每回合第一张被消耗的、既不是状态也不是诅咒的牌，改为放入弃牌堆，但仍视为被消耗。',
 '进阶的每次格挡由 2+A 降为 2+A/2，因为它不限次数。'),
('Poison','毒棘','静默',2,6,
 '每回合前三次通过卡牌施加中毒时，额外施加 1+A/2 层中毒。每回合首次向没有中毒的敌人施加中毒时，对其施加1层虚弱。',
 '额外施加中毒不再限制次数。中毒的敌人死亡时，将其中毒层数的 min(100, 25+5A)% 转移给另一名敌人。',
 '你的回合结束时，对所有具有中毒的敌人施加 1+A/3 层中毒。',
 '你的回合结束时，所有敌人的中毒额外结算一次伤害，且这次结算不减少层数。',
 '额外中毒为 1+A/2，满层时每次多 10 层。'),
('Blades','飞刃','静默',2,6,
 '战斗中生成的攻击牌每次额外造成 1+A/2 点伤害。每回合首次打出这类牌后，抽1张牌。',
 '每回合每打出4张在战斗中生成的攻击牌，抽1张牌，你的下一张这类牌每次额外造成 2+A/2 点伤害。每回合首次触发时，你还可以将弃牌堆中的1张这类牌放入手牌，使其在本回合免费。',
 '你的回合开始时，生成1张小刀。',
 '改为生成2张小刀。每回合第一张小刀打出两次。',
 '每段加伤由 1+A 降为 1+A/2：13档时小刀为 4+7=11 点，约等于两张精准。'),
('Sleight','巧变','静默',2,6,
 '每回合首次主动弃牌后，获得 3+A 点格挡。你可以再抽1张牌，然后丢弃1张手牌。',
 '每回合每主动弃牌3次，抽1张牌，你的下一张牌造成的伤害和获得的格挡增加 2+A 点。每回合首次触发时，你还可以将本回合丢弃的1张牌放回手牌，使其本回合的能量消耗减少1。',
 '你的回合开始时，你可以抽1张牌，然后丢弃1张手牌。',
 '每次主动弃置攻击牌时，对随机敌人造成 A 点伤害。',
 ''),
('Resonance','共鸣','机器人',2,6,
 '战斗开始时，获得 A/4 点集中。每回合首次生成充能球后，获得 3+A 点格挡，并且可以交换两个充能球的位置。',
 '在你的回合结束时，额外触发最前方充能球的被动效果 1+A/4 次。',
 '进阶的额外被动改为同时作用于最前方的两个充能球。',
 '战斗开始时，充能球栏位+1。进阶的额外被动改为作用于所有充能球。',
 '集中为 A/4，额外被动为 1+A/4 次。满层时开局 4 点集中、每回合多触发 5 次。原版碎片整理是稀有牌，只给 1 点集中。'),
('Overload','过载','机器人',2,6,
 '激发充能球后，对所有敌人造成 3+A 点伤害。每回合首次触发时，你还可以将弃牌堆中的1张能够生成充能球的牌放到抽牌堆顶部。',
 '激发充能球后，生成1个同类型的初始充能球，并获得 4+A/2 点格挡。由此生成的充能球引发的激发不会再次触发过载。',
 '每回合首次激发充能球时，改为激发两次。',
 '你的回合结束时，激发最后方的充能球。',
 '进阶格挡由 4+A 降为 4+A/2，因为每次激发都触发。'),
('Zero','零界','机器人',3,7,
 '每回合首次打出原始费用为0的牌后，获得 3+A 点格挡。你可以将弃牌堆中的另一张原始费用为0的牌放到抽牌堆顶部。',
 '每回合每打出3张原始费用为0的牌，抽1张牌，并获得1点能量。你的下一张原始费用为0的攻击牌额外造成 3+A 点伤害。',
 '进阶的条件改为每打出2张原始费用为0的牌。',
 '你的回合开始时，手牌中1张随机的非0费牌在本回合费用变为0，并视为原始费用为0。',
 ''),
('Stars','星辉','储君',2,6,
 '每回合首次花费星星后，获得 4+A 点格挡。如果此次花费使你不再拥有星星，在下回合开始时获得1颗星星。',
 '每回合首次耗尽星星后，你可以将弃牌堆中的1张具有固定星星消耗的牌放入手牌并保留。这张牌下次打出时的星星消耗减少 1+A/4 颗。',
 '你的回合开始时，获得1颗星星。',
 '每回合首次花费星星时，返还所花费星星的一半（向上取整）。',
 '星耗优惠为 1+A/4 颗。'),
('Forge','铸王','储君',2,6,
 '每回合首次锻造时，此次锻造的数值增加 1+A/2。每回合第二次锻造后，将弃牌堆中的君王之剑放入手牌。',
 '每次锻造都获得基础效果的加值。每回合首次打出君王之剑后，在下回合开始时将它放回手牌并保留，它下次打出时额外造成 4+2A 点伤害。',
 '战斗开始时，锻造 2+A。',
 '君王之剑在每回合首次打出时费用为0。',
 '锻造加值由 1+A 降为 1+A/2，因为进阶后每次锻造都触发。'),
('Creation','造物','储君',4,10,
 '每回合首次生成既不是状态也不是诅咒的卡牌后，获得 3+A 点格挡。你可以丢弃1张手牌，然后抽1张牌。',
 '每回合每生成3张这类卡牌，抽1张牌，你的下一张牌造成的伤害和获得的格挡增加 2+A 点。每回合首次触发时，你还可以选择1张在战斗中生成的手牌，使其本回合免费并获得保留。',
 '每回合第一张生成的牌在本回合的能量消耗减少1。',
 '每回合首次生成卡牌时，额外生成1张它的复制品。',
 ''),
('Doom','厄兆','死灵',2,6,
 '每回合首次向敌人施加灾厄时，额外施加 1+A/2 层灾厄，抽1张牌，并且可以保留1张手牌。',
 '每次向敌人施加灾厄都额外施加基础效果的层数。在你的回合结束时，对灾厄层数最多的敌人造成 4+A 点伤害，再加上其灾厄层数四分之一的伤害。',
 '进阶的回合结束伤害改为对所有具有灾厄的敌人各结算一次。',
 '敌人因灾厄死亡时，将其灾厄层数的一半施加给另一名敌人。',
 '额外灾厄由 1+A 降为 1+A/2，因为进阶后不限次数。'),
('Bones','役骨','死灵',2,6,
 '每回合奥斯提首次攻击后，抽1张牌。本回合你的下一次召唤额外召唤 2+A。',
 '奥斯提被击倒后，在下回合开始时自动召唤 3+2A。',
 '每回合首次召唤时，额外抽1张牌。',
 '奥斯提每回合首次攻击后，额外攻击一次。',
 ''),
('Ethereal','幽逝','死灵',2,6,
 '在你的回合结束时，你可以让1张具有虚无的手牌免于此次虚无消耗，并获得保留。每回合首次因虚无消耗卡牌时，获得 4+A 点格挡。',
 '在你的回合开始时，你可以将上回合因虚无消耗的1张牌放回手牌。这张牌本回合的能量消耗减少1，下次打出时造成的伤害和获得的格挡增加 2+A 点。',
 '基础的保护和进阶的取回都改为至多2张。',
 '每次打出具有虚无的牌时，对随机敌人造成 A 点伤害。',
 '基础格挡由 3+A 提到 4+A，进阶取回的牌增加减费。原效果偏弱。'),
('Arrows','游弋','愚者',2,6,
 '每回合首次使用不具有发射的攻击牌命中被你扎穿的敌人时，额外推进一次松脱计数，并获得 2+A 点瞄准。',
 '每次取回箭矢后，获得 2+A 点瞄准。每回合首次触发时，你还可以将弃牌堆中的1张具有发射的牌放入手牌，使其本回合的能量消耗减少1。',
 '基础的额外松脱推进不再限制每回合首次。',
 '每次取回箭矢时，对被取回箭矢的敌人造成 A 点伤害。',
 '我不了解愚者的原版强度，这一行置信度低。'),
('Melody','合奏','愚者',2,6,
 '每回合获得一种尚未拥有的旋律时，获得 2+A 点格挡。获得第二种旋律时，额外抽1张牌。',
 '每回合集齐三种旋律时，获得1点能量，并选择一种旋律，在下回合开始时获得它。下回合你的第一张攻击牌额外造成 3+A 点伤害。',
 '集齐三种旋律时，额外抽1张牌。',
 '每回合集齐三种旋律后，旋律重置，可以在同一回合再次集齐并触发进阶。',
 '置信度低。下回合加伤由 3+2A 降为 3+A。'),
('Emotion','悲欢','愚者',2,6,
 '在你的回合结束时，你可以将1张悲伤变化为喜悦并保留。如果这样做，获得 3+A 点格挡。',
 '每次打出喜悦后，在本回合获得 1+A/4 点力量。每回合首次触发时，你还可以将弃牌堆中的1张不是喜悦的牌放入手牌。',
 '基础效果改为可以变化手牌中的所有悲伤，每张各获得一次格挡。',
 '喜悦打出后不再消耗，改为进入弃牌堆。',
 '置信度低。'),
('Strike','打击','关键词',2,6,
 '打击每次额外造成 2+A/2 点伤害。每回合首次打出打击后，你可以将弃牌堆中的另一张打击放到抽牌堆顶部。',
 '每回合每打出3张打击，抽1张牌，并获得1点能量。你的下一张打击每次额外造成 3+A/2 点伤害。',
 '进阶的条件改为每打出2张打击。',
 '打击牌打出后，对同一目标追加一次 A 点伤害。',
 '每段加伤由 2+A 降为 2+A/2。打击开局即激活，原系数使第一幕的打击伤害翻倍以上。'),
('Guard','坚守','关键词',5,9,
 '每回合首次通过卡牌获得格挡时，额外获得 3+A 点格挡，并且可以保留1张手牌。',
 '在你的回合结束时，保留至多 5+2A 点格挡。',
 '每次通过卡牌获得格挡时，额外获得 1+A/3 点格挡。',
 '在你的回合结束时，保留全部格挡。',
 ''),
('Chain','连携','关键词',2,6,
 '每回合交替打出攻击牌与技能牌，累计打出3张牌后，抽1张牌，并获得 3+A 点格挡。每回合只能触发一次。',
 '基础效果可以在每完成3张交替出牌后重复触发，并使你的下一张牌造成的伤害和获得的格挡增加 2+A 点。每回合首次触发时，你还可以将弃牌堆中的1张类型不同的牌放入手牌，使其本回合的能量消耗减少1。',
 '交替出牌的计数在回合结束时保留到下回合。',
 '每次触发基础效果时，额外获得1点能量。',
 ''),
('Heavy','重装','关键词',3,7,
 '实际花费至少2点能量打出的牌造成的伤害和获得的格挡增加 3+A 点。每回合首次打出这类牌后，你可以保留另一张原始能量消耗至少为2的手牌。',
 '每回合每实际花费至少2点能量打出2张牌，抽1张牌，获得1点能量，并获得 4+A 点格挡。',
 '每回合第一张原始能量消耗至少为3的牌，能量消耗减少1。',
 '每回合第一张实际花费至少2点能量打出的牌打出两次。',
 ''),
('Retain','延续','关键词',2,6,
 '上回合保留的牌造成的伤害和获得的格挡增加 2+A 点。每回合首次打出这类牌后，抽1张牌，并且可以保留另一张手牌。',
 '在你的回合开始时，你可以选择1张保留的牌，使其能量消耗减少1，直到打出。这张牌下次打出时造成的伤害和获得的格挡增加 2+A 点。',
 '在你的回合结束时，你可以额外保留1张手牌。',
 '保留的牌每多保留一个回合，下次打出时造成的伤害和获得的格挡再增加 A 点。',
 '原有的「每回合首次触发时延续增加2层」按新刻度改为增加4层，仍是永久。'),
('Ritual','仪式','关键词',3,6,
 '每回合首次打出能力牌后，获得 4+A 点格挡。你可以将抽牌堆顶部3张牌中的1张攻击牌或技能牌放到抽牌堆顶部。',
 '每次打出能力牌后，抽1张牌。你的下一张不是能力牌的卡牌造成的伤害和获得的格挡增加 3+A 点。',
 '战斗胜利时，你所有盟约的临时层数有一半转为永久层数。',
 '战斗开始时，将抽牌堆中1张随机能力牌放入手牌，它在本回合费用为0。',
 '仪式在 0.4.0 没有任何可重复的层数来源，A 实际恒为0，本稿用胜利成长和能力牌的打出成长补上。能力牌打出后的持续效果给的是临时层数，仪式的 5 档效果是把它们留下来的手段，不再作为全局规则存在。'),
('Mastery','绝技','特殊',4,10,
 '每回合首次打出升级过的牌后，获得 3+A 点格挡，并且可以升级1张手牌，直到本场战斗结束。',
 '每回合首张打出的升级牌造成的伤害和获得的格挡增加 3+A 点。每回合每打出3张升级牌，获得1点能量。',
 '基础的临时升级改为2张手牌。',
 '战斗开始时，升级你的所有手牌，直到本场战斗结束。',
 '原有的「每回合首次触发时绝技增加2层」按新刻度改为增加4层，仍是永久。'),
('Harmony','调和','特殊',1,2,
 '牌组中有熵时，你层数最高的已激活流派盟约的组件数量+1。',
 '改为+2。',
 '不设',
 '不设',
 '重做。卫戍协议里调和的作用就是核心盟约人数+1，这正是全卡池最缺的万能组件。仍然只计熵。原来「打出熵后附加变化能力」的效果移到熵自己的卡牌特性里。'),
('Ancient','先古','特殊',1,3,
 '每回合首次打出先古牌后，获得 4+A 点格挡，并且可以保留另一张手牌。',
 '每次打出先古牌后都获得基础效果的格挡。每回合首次打出先古牌后，你可以复制另一张手牌并放入手牌。复制品具有虚无，本回合的能量消耗减少1，下次打出时造成的伤害和获得的格挡增加 4+A 点。',
 '进阶的复制品不再具有虚无。',
 '战斗开始时，将抽牌堆中1张随机先古牌放入手牌。',
 ''),
('Encounter','奇遇','特殊',2,5,
 '奇遇牌造成的伤害和获得的格挡增加 2+A 点。每回合首次打出奇遇牌后，你可以将抽牌堆中的另一张奇遇牌放入手牌。',
 '每回合首次打出的奇遇牌进入弃牌堆时，你可以改为将其放到抽牌堆顶部。这张牌下次打出时的能量消耗减少1，造成的伤害和获得的格挡增加 3+A 点。',
 '战斗开始时，将1张随机奇遇牌放到抽牌堆顶部。',
 '奇遇牌的能量消耗减少1。',
 ''),
('Curse','咒缚','特殊',2,5,
 '每回合首次抽到诅咒牌后，你可以将其消耗。如果这样做，获得 4+A 点格挡。',
 '每次消耗诅咒牌后，抽1张牌，并对所有敌人造成 5+A 点伤害。',
 '基础效果改为每次抽到诅咒牌都可以触发。',
 '战斗开始时，消耗抽牌堆中的所有诅咒牌，每张各触发一次进阶效果。',
 '进阶伤害由 5+2A 降为 5+A。'),
('Status','残响','特殊',3,8,
 '每回合首次抽到状态牌后，你可以丢弃1张状态手牌。如果这样做，获得 3+A 点格挡。',
 '每次消耗状态牌后，获得 3+A 点格挡，你的下一张牌造成的伤害和获得的格挡增加 3+A 点。每回合首次触发时，你还可以将弃牌堆中的1张既不是状态也不是诅咒的牌放入手牌。',
 '基础效果改为每次抽到状态牌都可以触发。',
 '抽到状态牌时，自动将其消耗，并抽1张牌。',
 ''),
('Profit','逐利','特殊',2,4,
 '你可以放弃战斗后的卡牌奖励，改为获得 10+A 金币。未领取卡牌的兑换完成后，逐利增加8层。',
 '兑换获得的金币额外增加 5+A/2。你的卡牌「获得时」类特性触发两次。',
 '商店的移除卡牌服务价格降低25金币。',
 '「获得时」类特性触发三次。',
 '进阶由「先领一张再兑换」改为卫戍协议投资人的做法。现有100张牌带获得时叠层，逐利因此成为真正的经济流派。没有胜利成长，靠兑换成长。'),
('Foresight','远见','特殊',2,6,
 '你可以免费重掷一次战斗后的卡牌奖励。每次通过此效果重掷后，远见增加8层。',
 '重掷后的候选卡牌额外增加 1+A/4 张。',
 '正常领取奖励后，你还可以支付 max(10, 40-A) 金币购买1张剩余的候选卡牌。',
 '首领战的卡牌奖励中，固定有1张来自你层数最高的已激活流派盟约。',
 '把原进阶的购买移到5档，形成卫戍协议远见那样的层数里程碑盟约。没有胜利成长，靠重掷成长。'),
('Solitude','独行','特殊',2,6,
 '如果既不是基础、状态也不是诅咒的卡牌没有重名，每回合首次连续打出两张不同类型的牌后，抽1张牌。你的下一张牌造成的伤害和获得的格挡增加 2+A 点。',
 '基础效果在每次连续打出两张不同类型的牌后都能触发。每回合首次打出过攻击牌、技能牌和能力牌后，你可以将弃牌堆中的1张牌放入手牌，这张牌下次打出时造成的伤害和获得的格挡增加 3+A 点。',
 '每回合首次触发基础效果时，改为抽2张牌。',
 '你的回合开始时，如果手牌中没有重名的牌，获得1点能量。',
 ''),
]
BN = {b[0]: b[1] for b in B}
UNIT = 20

# ---------------------------------------------------------------- cards
LISTEN = {'exhaust','discard','poison','generated_attack','generate','summon','channel','evoke','forge','stars','doom',
          'self_loss','melody','arrow','emotion','zero_play','any_play','ethereal'}
WHEN = {'own_play':'打出时','own_draw':'抽到这张牌时','own_exhaust':'这张牌被消耗时','own_discard':'主动弃置这张牌时',
 'own_acquire':'获得或在战斗中生成此牌时','own_remove':'从牌组移除时','victory':'战斗胜利时',
 'attached_turn':'打出后，在你的回合开始时','any_play':'打出卡牌时','poison':'施加中毒时','doom':'向敌人施加灾厄时',
 'exhaust':'消耗卡牌时','discard':'主动弃牌时','generate':'生成既不是状态也不是诅咒的牌时','forge':'锻造时',
 'channel':'生成充能球时','evoke':'激发充能球时','summon':'召唤时','stars':'花费星星时','arrow':'发射箭矢时',
 'melody':'获得新的旋律时','emotion':'生成悲伤或打出喜悦时','self_loss':'因自己的效果失去生命时',
 'ethereal':'因虚无消耗卡牌时','zero_play':'打出原始费用为0的牌时','generated_attack':'打出衍生攻击牌时',
 'passive':'','attached_passive':'打出后，'}
COND = {None:'','first':'','low_hp':'若你的生命值不高于一半，','blocked':'若你有格挡，','unblocked':'若你没有格挡，',
 'poisoned_enemy':'若有敌人具有中毒，','weak_enemy':'若有敌人具有虚弱，','paid_two':'若此次出牌实际支付至少2点能量，'}
RANK = {'Basic':0,'Common':1,'Uncommon':2,'Rare':3,'Ancient':3,'Event':3,'Quest':3,'Token':0,'Status':0,'Curse':0}

# 改为「按层数兑现」的牌：伤害或格挡
PAY_D = '''AdaptiveStrike HeirloomHammer Severance Eradicate HeavenlyDrill HelixDrill Misery Skewer Ftl Anguish PunchOfJoy
Bombardment FlakCannon SoulStorm TheScythe Thrash AshenStrike PullFromBelow Veilpiercer BansheesCry BeatIntoShape
SkyShot RepeatShot RapidShot BlossomShot Bludgeon Bury KinglyKick Sunder Mangle TearAsunder Squeeze SicEm Rattle
Coda MelodicShot WahChoKi Comet Devastate GammaBlast SevenStars Murder Finisher DeathMarch Hemokinesis IceLance
MeteorStrike Null MementoMori Scrape PerfectedStrike Shatter TimesUp'''.split()
PAY_B = 'Impervious Bounce Shadowmeld Dash'.split()

def self_exhaust(c):
    return str(c['evidence'].get('Ember', '')).startswith('原版定义具有消耗关键词')

def perm_amount(c, kind):
    r = RANK[c['rarity']]
    if kind == 'acquire': return {0:2,1:4,2:5,3:6}[r], 2
    if kind == 'once':    return {0:2,1:2,2:3,3:4}[r], 1
    raise ValueError(kind)

def temp_amount(c, kind):
    r = RANK[c['rarity']]
    if kind == 'play':   return (1 if r == 0 else 3 if r == 3 else 2), 1
    if kind == 'listen': return (2 if r == 3 else 1), 1
    if kind == 'turn':   return (3 if r == 3 else 2), 1
    raise ValueError(kind)

def text_of(c, e):
    bond = BN.get(e.get('bond') or c['trait']['bond'], '')
    when = WHEN.get(e['trigger'], e['trigger'] + '时')
    if c['type'] == 'Power' and e['trigger'] in LISTEN: when = '打出后，' + when
    cond = COND.get(e.get('condition'), '')
    k, a, u = e['kind'], e['amount'], e.get('upgradeAmount', 0)
    up = f'（升级后{a+u}）' if u else ''
    sep = '，' if when and not when.endswith('，') else ''
    if k == 'grow':
        act = f"使{'已激活的' if e.get('requireActive') else ''}【{bond}】{'临时' if e.get('temp') else ''}增加{a}层{up}"
    elif k == 'grow_memberships':
        act = f"使此牌所属的盟约各增加{a}层{up}"
    elif k == 'grow_other':
        act = f"使另一张随机手牌的目标盟约临时增加{a}层{up}"
    elif k in ('pay_damage', 'pay_block'):
        noun = '造成' if k == 'pay_damage' else '获得'; unit = '伤害' if k == 'pay_damage' else '格挡'
        per = '每1档' if e['per'] == 1 else '每2档'
        return f"此牌额外{noun}{a}点{unit}；【{bond}】{per}再增加1点。"
    else:
        return None
    return f"{when}{sep}{cond}{act}。"

def revise(name):
    c = C[name]; effs = T[name]['effects']; e = dict(effs[0]); tb = e.get('bond') or c['trait']['bond']
    r = RANK[c['rarity']]
    if name == 'NeutronAegis':
        return [dict(trigger='own_play', kind='grow', amount=8, upgradeAmount=2, bond='Stars')], \
               '打出时，使【星辉】增加8层（升级后10），并获得 6+2A 点格挡。', '弱牌补偿', '按新单位换算。保留两项特性的例外。'
    if name == 'KnockoutBlow':
        return [dict(trigger='own_play', kind='grow', amount=4, upgradeAmount=1, bond='Stars')], \
               '打出时，使【星辉】增加4层（升级后5），并获得2颗星星。', '弱牌补偿', '按新单位换算。保留两项特性的例外。'
    if name == 'Entropy':
        return [dict(trigger='attached_passive', kind='harmony')], \
               '打出后，每回合首次变化卡牌时，获得 3+A 点格挡，并使变化后的牌在本回合的能量消耗减少1。', '承接盟约效果', '调和盟约重做后，原盟约效果移到这张牌上。'
    if name in PAY_D or name in PAY_B:
        dmg = name in PAY_D; heavy = c['cost'] >= 2 or c['cost'] < 0
        n = dict(trigger='passive', kind='pay_damage' if dmg else 'pay_block', amount=3 if heavy else 2,
                 per=1 if heavy else 2, bond=tb)
        return [n], text_of(c, n), '改为层数兑现', '由叠层牌改为该盟约的核心兑现牌。'
    k, t = e['kind'], e['trigger']
    if k == 'grow_memberships':
        a, u = perm_amount(c, 'acquire'); e.update(amount=a, upgradeAmount=u)
        return [e], text_of(c, e), '数值换算', '获得时成长保持永久。'
    if k == 'grow_other':
        e['temp'] = True
        return [e], text_of(c, e), '改为临时', '可重复的转移成长。'
    if k != 'grow':
        return effs, CAT[name]['text'], '不变', ''
    token = 'TokenCardPool' in c['pools'] or c['rarity'] in ('Token', 'Status', 'Curse')
    hard = e.get('condition') in ('low_hp',)
    if t == 'victory' or t == 'own_remove':
        return [e], text_of(c, e), '不变', '一场一次，保持永久。'
    if t == 'own_acquire':
        a, u = perm_amount(c, 'acquire'); e.update(amount=a, upgradeAmount=u)
        return [e], text_of(c, e), '数值换算', ''
    if t == 'own_play':
        once = c['type'] == 'Power' or self_exhaust(c) or c['cost'] >= 3 or c['cost'] < 0
        if token:
            e.update(amount=1, upgradeAmount=1, temp=True); why = '衍生牌：高频，改为临时。'
        elif once or hard:
            a, u = perm_amount(c, 'once'); e.update(amount=a, upgradeAmount=u, temp=False)
            why = ('能力牌一场只打一次' if c['type'] == 'Power' else '自带消耗，一场只打一次' if self_exhaust(c)
                   else '条件难以达成' if hard else '费用高，出牌次数少') + '，保持永久。'
        else:
            a, u = temp_amount(c, 'play'); e.update(amount=a, upgradeAmount=u, temp=True); why = '一场可反复打出，改为临时。'
        return [e], text_of(c, e), '永久' if not e['temp'] else '改为临时', why
    if t == 'own_exhaust':
        a, u = perm_amount(c, 'once'); e.update(amount=max(a, e['amount']), upgradeAmount=u, temp=False)
        if token: e.update(amount=1, upgradeAmount=1, temp=True)
        return [e], text_of(c, e), '改为临时' if e['temp'] else '永久', '衍生牌：改为临时。' if token else '一场通常只被消耗一次，保持永久。'
    if t in ('own_draw', 'own_discard'):
        e.update(amount=2, upgradeAmount=1, temp=True)
        return [e], text_of(c, e), '改为临时', '每次洗牌都会重复，改为临时。'
    if t == 'attached_turn':
        a, u = temp_amount(c, 'turn'); e.update(amount=a, upgradeAmount=u, temp=True)
        return [e], text_of(c, e), '改为临时', '每回合结算，随战斗长度增长，改为临时。'
    if t in ('self_loss', 'ethereal'):
        e.update(amount=2 if RANK[c['rarity']] == 3 else 1, upgradeAmount=1, temp=False)
        return [e], text_of(c, e), '永久', '触发需要付出生命或难以稳定达成，保持永久且不限次数。'
    if t in LISTEN:
        a, u = temp_amount(c, 'listen'); e.update(amount=a, upgradeAmount=u, temp=True)
        return [e], text_of(c, e), '改为临时', '全场事件监听，高频，改为临时。'
    return effs, CAT[name]['text'], '不变', ''

rows = []
for name in sorted(C):
    c = C[name]; new, txt, change, why = revise(name)
    rows.append(dict(card=name, name=CAT[name]['name'], bond=c['trait']['bond'], role=c['role'], rarity=c['rarity'],
                     type=c['type'], cost=c['cost'], current=CAT[name]['text'], proposed=txt, change=change, reason=why,
                     effects=new))
json.dump(dict(unit=UNIT, note='0.5 修订稿的逐卡提案。temp=true 表示临时层数。pay_damage/pay_block 为按层数兑现。',
               cards=rows), open(os.path.join(OUT, 'card-revisions.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)

RZ = {'Basic':'基础','Common':'普通','Uncommon':'罕见','Rare':'稀有','Ancient':'先古','Event':'事件','Quest':'任务',
      'Token':'衍生','Status':'状态','Curse':'诅咒'}
TZ = {'Attack':'攻击','Skill':'技能','Power':'能力','Status':'状态','Curse':'诅咒','Quest':'任务'}
by = collections.defaultdict(list)
for r in rows: by[r['bond']].append(r)
L = ['# 逐卡修订表（0.5 提案）', '',
     '本表由规则生成后人工指定例外，共 %d 张。规则见《盟约修订稿》第一节。' % len(rows),
     '「现特性」取自 0.4.0 实际渲染文案的初始值。「修订」列里的 A 指该盟约的当前档位，游戏内显示为实际数值。', '']
cnt = collections.Counter(r['change'] for r in rows)
L += ['| 变化类型 | 张数 |', '|---|---|'] + [f'| {k} | {v} |' for k, v in cnt.most_common()] + ['']
order = [b[0] for b in B]
for b in order:
    if b not in by: continue
    L += [f'## {BN[b]}（{len(by[b])} 张）', '', '| 牌 | 稀有度·类型·费用 | 现特性 | 修订 | 变化 | 理由 |', '|---|---|---|---|---|---|']
    for r in sorted(by[b], key=lambda r: (r['change'] == '不变', -RANK[r['rarity']], r['name'])):
        cost = 'X' if r['cost'] < 0 and r['type'] in ('Attack', 'Skill') else '' if r['cost'] < 0 else str(r['cost'])
        same = r['change'] == '不变'
        L.append(f"| {r['name']} | {RZ[r['rarity']]}·{TZ.get(r['type'], r['type'])}·{cost} | {r['current']} | {'同左' if same else r['proposed']} | {r['change']} | {r['reason']} |")
    L.append('')
open(os.path.join(OUT, '逐卡修订表.md'), 'w', encoding='utf-8').write('\n'.join(L))

# bonds table fragment for the main doc
F = []; ALT = []
ONE = {'Strike','Guard','Heavy','Zero','Ritual','Mastery'}; TWO = {'Profit','Foresight'}
for b in B:
    n = len(by.get(b[0], []))
    F += [f'### {b[1]}（{b[2]}，{b[3]}／{b[4]} 张）', '', f'- **基础**：{b[5]}', f'- **进阶**：{b[6]}']
    if b[0] not in ('Profit','Foresight','Harmony'): F.append('- **胜利成长**：已激活时，战斗胜利使此盟约增加6层；精英战12层，首领战18层。')
    if b[0] in ONE: F.append(f'- **5档（250层）**：{b[7]}')
    elif b[0] in TWO: F += [f'- **5档（250层）**：{b[7]}', f'- **10档（500层）**：{b[8]}']
    elif b[7] != '不设': ALT.append(f'| {b[1]} | {b[7]} | {b[8]} |')
    if b[0] in ONE and b[8] != '不设': ALT.append(f'| {b[1]} | （已采用为5档效果） | {b[8]} |')
    if b[9]: F.append(f'- **说明**：{b[9]}')
    pays = [r['name'] for r in by.get(b[0], []) if r['change'] == '改为层数兑现']
    if False: F.append(f"- **兑现牌**：{'、'.join(pays)}")
    F.append('')
HERE=os.path.dirname(os.path.abspath(__file__))
HEAD=open(os.path.join(HERE,'head.md'),encoding='utf-8').read()
F.append(open(os.path.join(HERE,'vakuu.md'),encoding='utf-8').read())
TAIL=open(os.path.join(HERE,'tail.md'),encoding='utf-8').read()
TAIL=TAIL.replace('{{ALT}}',chr(10).join(ALT)).replace('{{COUNTS}}',chr(10).join(f'| {k} | {v} |' for k,v in cnt.most_common()))
open(os.path.join(OUT,'盟约修订稿.md'),'w',encoding='utf-8').write(HEAD+chr(10).join(F)+TAIL)
print(len(rows), cnt)
print('perm per bond', {BN[b]: sum(1 for r in by[b] if r['change'] in ('永久', '数值换算')) for b in order if b in by})
print('temp per bond', {BN[b]: sum(1 for r in by[b] if r['change'] == '改为临时') for b in order if b in by})
for n in ['StrikeIronclad', 'Shiv', 'Defragment', 'DeadlyPoison', 'Offering', 'Bludgeon', 'Impervious', 'Zap', 'Accuracy', 'Soul', 'Apotheosis', 'DemonForm']:
    r = next(x for x in rows if x['card'] == n); print(r['name'], '|', r['current'], '=>', r['proposed'], '|', r['change'])
