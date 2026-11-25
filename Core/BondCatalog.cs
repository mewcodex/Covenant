namespace Covenant.Core;

public static class BondCatalog
{
    private const string E = "{energyPrefix:energyIcons(1)}";
    private const string S = "{starIcon}";
    internal const int PoisonTransferBasePercent = 50;
    internal const int PoisonTransferPercentPerTier = 10;
    internal const int ProfitGoldThreshold = 150;
    internal const int ProfitRemovalLayers = 100;
    internal const int ProfitVictoryLayers = 250;
    internal const int ForesightPurchaseLayers = 150;
    internal const int ForesightBossLayers = 300;
    internal const int RitualDrawLayers = 200;
    internal const int RitualPlayLayers = 450;
    internal const int ArrowsPlayAimLayers = 150;
    internal const int ArrowsPlayAimDivisor = 3;
    internal static readonly LayerChance RitualPowerChance = new(3,20);
    internal static readonly LayerChance RitualRarePowerChance = new(1,60);
    internal static readonly LayerChance CreationFreeChance = new(10,25,30);
    internal static readonly LayerChance CreationAncientChance = new(1,250,3);
    private static readonly IReadOnlyDictionary<Bond,int> LayerUnits=LoadLayerUnits();
    private static IReadOnlyDictionary<Bond,int> LoadLayerUnits()
    {
        using var stream=typeof(BondCatalog).Assembly.GetManifestResourceStream("Covenant.Data.layer-units.json")??throw new InvalidDataException("Missing covenant growth units.");
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,int>>(stream)!.ToDictionary(p=>Enum.Parse<Bond>(p.Key),p=>p.Value>0?p.Value:throw new InvalidDataException("Invalid covenant growth unit."));
    }
    public static readonly IReadOnlyList<BondDefinition> All = new BondDefinition[]
    {
        D(Bond.Fury,"狂战","铁甲",3,8,
            s=>$"战斗开始时，获得{ScalingText.Amount(s, "1+floor(A/4)")}点力量。每回合首次获得力量后，你的下一张攻击牌在本回合的能量消耗减少{E}。",
            s=>$"每回合每打出3张攻击牌，在本回合获得{ScalingText.Amount(s, "1+floor(A/4)")}点力量，并抽1张牌。"),
        D(Bond.Blood,"血誓","铁甲",2,4,
            s=>$"每回合首次因效果失去生命后，获得{ScalingText.Amount(s, "4+A")}点格挡并抽1张牌。",
            s=>$"战斗开始时，失去1点生命。战斗结束时，回复{ScalingText.Amount(s, "1+floor(A/2)")}点生命。"),
        D(Bond.Ember,"余烬","铁甲",3,9,
            s=>$"每回合首次消耗卡牌后，按引发消耗的牌实际支付的能量判定。每点能量提供{ScalingText.Amount(s, "min(50,10+4*A)")}%的概率，将消耗堆中随机1张攻击牌或技能牌放入手牌。",
            s=>$"每次消耗卡牌后，额外获得{ScalingText.Amount(s, "2+floor(A/2)")}点格挡。每回合每消耗3张牌后，获得{E}。"),
        D(Bond.Poison,"毒棘","静默",3,6,
            s=>$"你每回合前3次施加中毒时，额外施加{ScalingText.Amount(s, "1+floor(A/2)")}层中毒。你每回合首次向未中毒的敌人施加中毒时，也对该敌人施加1层虚弱。",
            s=>$"战斗开始时，随机对敌人施加1层中毒{ScalingText.Amount(s, "max(1,A)")}次。中毒的敌人死亡时，随机对另一名敌人施加相当于死者中毒层数{ScalingText.Amount(s, $"{PoisonTransferBasePercent}+{PoisonTransferPercentPerTier}*A")}%的中毒。"),
        D(Bond.Blades,"飞刃","静默",3,6,
            s=>$"战斗中生成的攻击牌每次额外造成{ScalingText.Amount(s, "1+floor(A/2)")}点伤害。每回合首次打出这类牌后，抽1张牌。",
            s=>$"每回合每打出4张在战斗中生成的攻击牌后，抽1张牌，并使你随后打出的下一张这类攻击牌每次额外造成{ScalingText.Amount(s, "2+floor(A/2)")}点伤害。每回合首次满足此条件时，随机将弃牌堆或消耗堆中的1张此类牌放入手牌，优先从弃牌堆取回。"),
        D(Bond.Sleight,"巧变","静默",3,8,
            s=>$"每回合首次主动弃牌后，获得{ScalingText.Amount(s, "3+A")}点格挡并抽1张牌，然后选择1张手牌丢弃。",
            s=>$"每回合每主动弃牌3次后，抽1张牌，并使你随后打出的下一张牌造成的伤害和获得的格挡增加{ScalingText.Amount(s, "2+A")}点。每回合首次满足此条件时，还可以从弃牌堆取回1张本回合丢弃的牌，使其在本回合的能量消耗减少{E}。"),
        D(Bond.Resonance,"共鸣","机器人",3,8,
            s=>$"战斗开始时，获得1个充能球栏位和{ScalingText.Amount(s, "floor(A/4)")}点集中。每回合首次生成充能球后，获得{ScalingText.Amount(s, "3+A")}点格挡，并且可以交换两个充能球的位置。",
            s=>$"在你的回合结束时，额外触发最右侧充能球的被动效果{ScalingText.Amount(s, "1+floor(A/4)")}次。"),
        D(Bond.Overload,"过载","机器人",2,4,
            s=>$"每次激发充能球后，对所有敌人造成{ScalingText.Amount(s, "3+A")}点伤害。每回合首次触发此效果时，还可以将弃牌堆中的1张【共鸣】牌放到抽牌堆顶部。",
            s=>$"激发充能球后，生成1个同类型的初始充能球并获得{ScalingText.Amount(s, "4+floor(A/2)")}点格挡，由这次生成直接引发的激发不会再次触发【过载】。"),
        D(Bond.Zero,"零界","机器人",3,8,
            s=>$"每回合首次打出能量费用为0{E}的牌后，获得{ScalingText.Amount(s, "3+A")}点格挡，并将弃牌堆中另一张随机的能量费用为0{E}的牌放入手牌。",
            s=>$"每回合每打出3张能量费用为0{E}的牌后，使你随后打出的下一张能量费用为0{E}的攻击牌额外造成{ScalingText.Amount(s, "3+A")}点伤害。每回合首次触发此效果时，还会抽1张牌并获得{E}。"),
        D(Bond.Stars,"星辉","储君",3,8,
            s=>$"战斗开始时，获得{ScalingText.Amount(s, "2+floor(A/5)")}{S}。在你的回合结束时，若本回合至少花费了2{S}，下回合开始时获得1{S}。",
            s=>$"消耗{S}的牌不再消耗{E}。每累计消耗3{S}，获得{ScalingText.Amount(s, "1+floor(A/4)")}点活力，并使【星辉】增加2层。"),
        D(Bond.Forge,"铸王","储君",3,6,
            s=>$"每回合首次锻造时，此次锻造的数值增加{ScalingText.Amount(s, "1+floor(A/2)")}。每回合第二次锻造后，将弃牌堆中的1张君王之剑放入手牌。",
            s=>$"每次锻造都能触发基础效果的锻造加值。每次锻造还会使每张君王之剑获得一个随机附魔，替换原有附魔。"),
        D(Bond.Creation,"造物","储君",5,12,
            s=>$"每回合首次生成非状态牌后，获得{ScalingText.Amount(s, "3+A")}点格挡。第二次生成时，获得{E}。",
            s=>$"每生成1张牌，有{CreationFreeChance.Text(s)}的概率使其在本回合可以免费打出一次。另有{CreationAncientChance.Text(s)}的独立概率将这张牌替换为1张随机颜色的先古牌。替换后的牌获得虚无和消耗。"),
        D(Bond.Doom,"厄兆","死灵",1,6,
            s=>$"每回合首次向敌人施加灾厄时，额外施加{ScalingText.Amount(s, "1+floor(A/2)")}层灾厄，抽1张牌，然后选择1张手牌放到抽牌堆顶部。",
            s=>$"每次向敌人施加灾厄时，都会触发基础效果的额外灾厄。在你的回合结束时，对灾厄层数最高的敌人造成{ScalingText.Amount(s, "4+A")}点伤害，再按该敌人灾厄层数的四分之一追加伤害，追加伤害向下取整。"),
        D(Bond.Bones,"役骨","死灵",3,8,
            s=>$"战斗开始时，召唤3。每回合奥斯提首次攻击后，使你本回合接下来的第一次召唤数值增加{ScalingText.Amount(s, "2+A")}，并在下回合开始时召唤1。",
            s=>$"奥斯提被击倒后，在下回合开始时自动召唤{ScalingText.Amount(s, "3+2*A")}。"),
        D(Bond.Ethereal,"幽逝","死灵",3,8,
            s=>$"你的虚无牌造成的伤害和获得的格挡额外增加{ScalingText.Amount(s, "2+A")}点。",
            s=>$"在你的回合开始时，从消耗堆随机取回1张具有虚无的攻击牌、技能牌或能力牌。你的牌即将因虚无而被消耗时，有{ScalingText.Amount(s, "min(100,3*A)")}%的概率改为对随机目标打出此牌。"),
        D(Bond.Arrows,"游弋","愚者",3,8,
            s=>$"每回合首次命中被你扎穿的敌人时，额外推进一次松脱计数，并获得{ScalingText.Amount(s, "2+A")}点瞄准。",
            s=>$"当你使箭矢松脱时，随机将弃牌堆中的1张具有发射的牌放回手牌。"),
        D(Bond.Melody,"合奏","愚者",3,8,
            s=>$"每回合每获得一种尚未拥有的旋律时，获得{ScalingText.Amount(s, "2+A")}点格挡。每回合获得第二种旋律时，抽1张牌。",
            s=>$"当你集齐三种旋律时，获得{E}，并在下回合开始时随机获得一种旋律。"),
        D(Bond.Emotion,"悲欢","愚者",3,6,
            s=>$"在你的回合结束时，将手牌中1张悲伤变化为喜悦并保留这张牌，然后获得{ScalingText.Amount(s, "3+A")}点格挡。",
            s=>$"每次打出喜悦后，在本回合获得{ScalingText.Amount(s, "1+floor(A/4)")}点力量。每回合首次触发此效果时，可以将弃牌堆中的1张不是喜悦的牌放入手牌。"),
        D(Bond.Strike,"打击","关键词",3,8,
            s=>$"打击牌每次额外造成{ScalingText.Amount(s, "2+floor(A/2)")}点伤害。每回合首次打出打击牌后，从弃牌堆随机打出另一张打击牌。",
            s=>$"每回合每打出3张打击牌后，抽1张牌，并使你的下一张打击牌每次额外造成{ScalingText.Amount(s, "3+floor(A/2)")}点伤害。每回合首次触发此效果时，额外获得{E}。"),
        D(Bond.Guard,"坚守","关键词",6,11,
            s=>$"每回合首次通过卡牌获得格挡时，额外获得{ScalingText.Amount(s, "2+A")}点格挡。",
            s=>$"在你的回合开始时，保留至多{ScalingText.Amount(s, "3+2*A")}点已有格挡。"),
        D(Bond.Chain,"连携","关键词",3,8,
            s=>$"每回合首次在攻击牌和技能牌之间连续交替打出3张牌后，抽1张牌并获得{ScalingText.Amount(s, "3+A")}点格挡。",
            s=>$"每回合连续交替打出攻击牌和技能牌时，每打满3张就触发一次基础效果，并获得{ScalingText.Amount(s, "2+A")}点活力和等量的拉伸。"),
        D(Bond.Heavy,"重装","关键词",4,9,
            s=>$"原始能量消耗至少为2{E}的牌造成的伤害和获得的格挡增加{ScalingText.Amount(s, "3+A")}点。每回合首次打出这类牌后，你可以使另一张符合条件的手牌获得保留。",
            s=>$"每回合每打出2张原始能量消耗至少为2{E}的牌后，抽1张牌，获得{E}，并获得{ScalingText.Amount(s, "4+A")}点格挡。"),
        D(Bond.Retain,"延续","关键词",3,8,
            s=>$"上回合保留的牌造成的伤害和获得的格挡增加{ScalingText.Amount(s, "2+A")}点。每回合首次打出这类牌后，【延续】增加4层，抽1张牌，并且可以保留另一张手牌。",
            s=>$"在你的回合开始时，选择1张上回合保留的手牌，使其能量消耗减少{E}直到打出，并使其下次打出时造成的伤害和获得的格挡增加{ScalingText.Amount(s, "2+A")}点。"),
        D(Bond.Ritual,"仪式","关键词",4,8,
            s=>$"每回合首次打出能力牌后，获得{ScalingText.Amount(s, "4+A")}点格挡，并使你下一回合打出的第一张能力牌的能量消耗减少{E}。",
            s=>$"在你的回合开始时，有{RitualPowerChance.Text(s)}的概率将1张随机虚无能力牌加入手牌，这张牌的能量消耗减少{E}。另有{RitualRarePowerChance.Text(s)}的概率将1张随机虚无稀有能力牌加入手牌，这张牌的能量消耗也减少{E}。"),
        D(Bond.Mastery,"绝技","特殊",10,25,
            s=>$"每回合首次打出升级过的牌后，【绝技】增加4层，获得{ScalingText.Amount(s, "3+A")}点格挡，并升级1张手牌。",
            s=>$"每回合打出的第一张已升级卡牌造成的伤害和获得的格挡增加{ScalingText.Amount(s, "3+A")}点。每回合每打出3张已升级卡牌后，使1张随机手牌的能量消耗减少{E}，直到打出。"),
        D(Bond.Harmony,"调和","特殊",1,0,
            _=>"其他盟约达到I级所需张数时，其计入张数额外增加1张。",
            _=>""),
        D(Bond.Ancient,"先古","特殊",1,3,
            _=>"打出先古牌时，使此牌已激活的其他盟约各增加4层。",
            _=>"打出先古牌时，将1张随机同色稀有牌加入手牌。这张牌获得虚无和消耗，本回合可以免费打出一次。"),
        D(Bond.Encounter,"奇遇","特殊",3,7,
            s=>$"奇遇牌造成的伤害和获得的格挡增加{ScalingText.Amount(s, "2+A")}点。每回合首次打出奇遇牌后，你可以将抽牌堆中的另一张奇遇牌放入手牌。",
            s=>$"每回合首次打出的奇遇牌进入弃牌堆时，你可以改为将此牌放到抽牌堆顶部，使此牌下次打出时的能量消耗减少{E}。"),
        D(Bond.Curse,"咒缚","特殊",3,6,
            s=>$"每当你获得1张诅咒牌，生命上限提高{ScalingText.Amount(s, "1+A")}点。每当你抽到1张诅咒牌，获得{ScalingText.Amount(s, "2+floor(A/2)")}点格挡。",
            s=>$"每当你打出1张诅咒牌，对所有敌人造成{ScalingText.Amount(s, "2+A")}点伤害，然后抽1张牌。"),
        D(Bond.Status,"残响","特殊",3,6,
            s=>$"每回合首次抽到状态牌后，你可以将其丢弃。若如此做，获得{ScalingText.Amount(s, "3+A")}点格挡。",
            s=>$"本回合内，手牌中每有1张状态牌，你获得1点临时力量。"),
        D(Bond.Profit,"逐利","特殊",3,6,
            s=>$"你可以将战斗后的卡牌奖励兑换为{ScalingText.Amount(s, "10+A")}金币，并在完成兑换时使【逐利】增加8层。每累计花费{ProfitGoldThreshold}金币，获得{ScalingText.Amount(s, "1+floor(A/2)")}点生命上限。",
            s=>$"兑换卡牌奖励时，额外获得{ScalingText.Amount(s, "5+floor(A/2)")}金币。每场战斗胜利时，额外获得{ScalingText.Amount(s, "6+A")}金币。"),
        D(Bond.Foresight,"远见","特殊",3,8,
            s=>"选择卡牌奖励时，你可以重置一次，并使【远见】增加8层。",
            s=>$"通过此效果重置卡牌奖励时，额外生成{ScalingText.Amount(s, "1+floor(A/4)")}张候选牌。"),
        D(Bond.Solitude,"独行","特殊",10,25,
            _=>"战斗开始时，抽牌堆中每有一种不同的能量费用，获得2点活力、2点拉伸和2点覆甲。",
            _=>"当你打出或消耗一张牌时，将场上与此牌同名的牌各变成1张随机颜色的牌。"),
        D(Bond.Vakuu,"瓦库","副盟约",5,0,
            s=>$"战斗的第一回合中，若你亲自从手牌打出的第一张牌位于手牌最左侧，瓦库将接管本场战斗。接管期间，瓦库每回合替你自动打出至多13张牌，并在接下来的每个回合开始时让你额外获得{E}。",
            s=>""),
        D(Bond.Tony,"东尼","副盟约",5,0,
            s=>$"你的回合结束时，若格挡恰好为16点，则开始一个额外回合，并在该回合开始时少获得{E}，连续触发时少获得的能量每次再增加{E}。",
            s=>""),
        D(Bond.Aid,"助力","特殊",2,4,
            s=>$"战斗结束时，使你层数最高的可叠层盟约增加{ScalingText.Amount(s, "6+A")}层。",
            s=>$"每回合前{ScalingText.Amount(s, "1+floor(A/2)")}次打出【助力】牌后，使所有已激活的可叠层盟约各增加{ScalingText.Amount(s, "1+floor(A/3)")}层。"),
        D(Bond.Channel,"倾注","关键词",2,4,
            s=>$"X费牌结算时，X增加{ScalingText.Amount(s, "1+floor(A/10)")}点。该牌每消耗{ScalingText.Amount(s, "max(2,4-floor(A/5))")}点能量或辉星，X再增加1点。",
            _=>"在你的回合结束时，将剩余能量转化为储能。打出X费牌前，将所有储能转回能量。"),
        D(Bond.Breach,"破绽","关键词",3,6,
            s=>$"每回合首次施加虚弱或易伤后，获得{ScalingText.Amount(s, "2+A")}点活力。首次施加另一种时，获得{ScalingText.Amount(s, "2+A")}点拉伸。",
            _=>"每回合首次用攻击牌命中同时具有虚弱和易伤的敌人后，分别随机对敌人施加1层虚弱和1层易伤。"),
        D(Bond.Rotation,"轮转","关键词",4,9,
            s=>$"每回合首次通过卡牌效果抽到攻击牌、技能牌或能力牌时，使其在本回合获得保留。每通过卡牌效果抽1张牌，获得{ScalingText.Amount(s, "1+floor(A/4)")}点格挡。",
            s=>$"每次洗牌时，每洗回{ScalingText.Amount(s, "30-A")}张牌，获得1点复制。"),
        D(Bond.Initiative,"先机","关键词",2,4,
            s=>$"战斗的第一个回合多抽1张牌，并在回合开始时获得{ScalingText.Amount(s, "2+A")}点格挡。",
            s=>$"在战斗的第一个回合，使你手牌中能量消耗为2或3的牌少消耗{E}，直到你以此方式打出其中一张。"),
        D(Bond.Fate,"命运","特殊",3,6,
            _=>"战斗开始时，翻开1张塔罗牌。每当你打出1张【命运】牌，使【命运】增加2层。每累计打出3张【命运】牌，将翻开的塔罗牌加入手牌，然后翻开下一张。",
            s=>$"由此获得的塔罗牌具有保留。每次打出这类塔罗牌后，抽{ScalingText.Amount(s, "1+floor(A/5)")}张牌。"),
        D(Bond.Reflow,"回流","关键词",3,6,
            s=>$"每回合首次通过卡牌效果获得能量后，抽1张牌并获得{ScalingText.Amount(s, "2+floor(A/2)")}点拉伸。",
            s=>$"当你打出一张牌并为此消耗能量时，对随机敌人造成{ScalingText.Amount(s, "1+A")}点伤害。每打出一张这样的牌，这个伤害增加{ScalingText.Amount(s, "1+floor(A/4)")}点，回合结束时复原。"),
        D(Bond.Oracle,"神喻","特殊",5,0,
            _=>"第一回合开始时，从抽牌堆选择等同于起手抽牌张数的牌加入手牌，代替本次抽牌。",_=>""),
        D(Bond.Spectrum,"万象","特殊",5,7,
            _=>$"每场战斗首次打出某种角色颜色的牌后，使手牌中1张随机其他角色颜色的牌少消耗{E}，直到打出。",
            _=>"基础效果对全部7种颜色触发后，立即刷新各颜色的触发机会。")
    };
    public static BondDefinition Get(Bond id) => All[(int)id];
    public static string Intro(Bond id)=>id switch
    {
        Bond.Ritual=>"此盟约的激活状态会持续到战斗结束。",
        Bond.Harmony=>"此盟约的激活状态在战斗开始时确定，并持续到战斗结束。",
        Bond.Mastery=>"已升级的牌和能升级现有牌的牌计入张数。",
        Bond.Strike=>"打击牌是名称中含有「打击」的牌。",
        Bond.Curse=>"此盟约激活时，你的卡池包含诅咒牌。",
        Bond.Fate=>"每局随机选出20至30张牌计入【命运】。",
        Bond.Oracle=>"每局随机选出2张普通牌和3张罕见牌计入【神喻】。此盟约的激活状态在战斗开始时确定。",
        Bond.Spectrum=>"此盟约按牌组中的不同角色颜色计数。",
        Bond.Solitude=>"此盟约计入所有不同名的牌。若你的牌组中有重名的非诅咒牌，此盟约在本场战斗中失效。",
        _=>""
    };
    public static IReadOnlyList<(int Layers,string Text)> Milestones(Bond id,Scale s)=>id switch
    {
        Bond.Blood=>[(250,$"基础效果还会使你获得{E}。")],
        Bond.Ember=>[(250,"每次消耗卡牌时，都可以触发基础效果。")],
        Bond.Poison=>[(250,"每回合首次对敌人施加中毒时，也对其施加1层虚弱，无论目标是否已经中毒。")],
        Bond.Zero=>[(250,$"进阶效果改为每回合每打出2张能量费用为0{E}的牌后触发。")],
        Bond.Stars=>[(250,$"每场战斗的前{ScalingText.Amount(s, "floor(A/5)")}个回合开始时，打出抽牌堆中最靠近堆底、能获得{S}的1张牌。"),(500,$"能量不足时，每2{S}可代付{E}。")],
        Bond.Creation=>[(250,$"每回合前6次生成非状态牌时，各额外获得{ScalingText.Amount(s, "max(1,floor(A/7))")}点格挡。本场战斗首次在同一回合生成第7张非状态牌时，回复7点生命。")],
        Bond.Arrows=>[(ArrowsPlayAimLayers,$"每次打出卡牌后，获得{ScalingText.Amount(s, $"floor(A/{ArrowsPlayAimDivisor})")}点瞄准。")],
        Bond.Melody=>[(250,$"你的旋律牌额外造成{ScalingText.Amount(s, "floor(A/2)")}点伤害。")],
        Bond.Strike=>[(250,"进阶效果改为每回合每打出2张打击牌后触发。")],
        Bond.Guard=>[(250,$"每次通过卡牌获得格挡时，额外获得{ScalingText.Amount(s, "floor(A/3)")}点格挡。")],
        Bond.Heavy=>[(250,$"每回合打出的第一张原始能量消耗至少为3{E}的牌，能量消耗减少{E}。")],
        Bond.Reflow=>[(150,"回合结束时，从弃牌堆随机打出1张你当前付得起能量和辉星的牌。"),(250,"进阶效果改为对所有敌人造成伤害。若只有1名敌人，则额外对其造成等同于本次伤害一半的伤害。")],
        Bond.Ritual=>[(RitualDrawLayers,"战斗开始时，从抽牌堆选择1张能力牌放入手牌。"),(RitualPlayLayers,"改为从抽牌堆选择1张能力牌并打出。")],
        Bond.Status=>[(250,$"你抽到或获得状态牌时，有{ScalingText.Amount(s, "2*A")}%的概率将其替换为黏液或晕眩。")],
        Bond.Mastery=>[(250,"基础效果改为升级2张手牌。")],
        Bond.Profit=>[(ProfitRemovalLayers,"商店的移除卡牌服务价格降低25金币。"),(ProfitVictoryLayers,"精英和首领战斗胜利时，逐利进阶效果提供的胜利金币翻倍。")],
        Bond.Foresight=>[(ForesightPurchaseLayers,$"正常领取1张战后奖励牌后，你还可以支付{ScalingText.Amount(s, "max(10,40-A)")}金币购买该奖励中剩余的1张候选牌。"),(ForesightBossLayers,"首领战后的卡牌奖励中，若有符合条件的候选牌，至少有1张属于你层数最高的已激活流派盟约。")],
        Bond.Channel=>[(250,$"战斗开始时，选择1张X费牌，使其获得保留。每次抽到这张牌时，获得{E}。")],
        Bond.Breach=>[(200,$"易伤使敌人额外受到的伤害增加{ScalingText.Amount(s,"2*A")}个百分点。虚弱使敌人少造成的伤害增加{ScalingText.Amount(s,"A")}个百分点。")],
        Bond.Rotation=>[(350,"每场战斗首次洗牌的回合结束后，进行一个额外回合。")],
        _=>[]
    };
    private static BondDefinition D(Bond id,string name,string group,int basic,int advanced,Func<Scale,string> first,Func<Scale,string> second)
        =>new(id,name,group,basic,advanced,first,second,LayerUnits.GetValueOrDefault(id,1));
}
