using Covenant.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using VoidCard=MegaCrit.Sts2.Core.Models.Cards.Void;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private Dictionary<CardModel,CardModel> _juggled=new();
    private List<CardModel> _turboRefundSources=new();
    private Dictionary<CardModel,CardModel> _hellraiserExhausts=new();
    private HashSet<CardModel> _clawReturns=new();
    private Dictionary<CardModel,bool> _freeXReworks=new();
    private HashSet<CardPlay> _penetratingAttacks=new();
    private Dictionary<CardModel,int> _retainedShivs=new();
    private List<bool> _pendingPerfectedStrikes=[];
    private CardModel? _carryEnergySource;
    private int _jugglingReplay;
    private bool _reworkOpened;
    private decimal _reaperRemainder;
    [SavedProperty] public string PendingDivineShots { get; set; }="";

    private int _bludgeonDiscount;
    private static bool IsHeavyAttack(CardModel card)=>card.Type==CardType.Attack&&CardMembership.IsPrintedHeavy(card);

    private void ResetCardReworks()
    {
        _juggled=new();_turboRefundSources=new();_hellraiserExhausts=new();_clawReturns=new();_freeXReworks=new();_retainedShivs=new();
        _penetratingAttacks=new();_pendingPerfectedStrikes=[];
        _jugglingReplay=0;_carryEnergySource=null;_reworkOpened=false;_reaperRemainder=0;
        _bludgeonDiscount=0;
    }

    private static RevisionInstruction? ReworkRule(CardModel card)=>Program(card)?.Rules.FirstOrDefault(rule=>rule.Event=="redesign");
    internal static bool HasRework(CardModel card)=>ReworkRule(card)!=null;
    private bool ReworkEnabled(CardModel card, IReadOnlyDictionary<Bond,int>? activation=null)
    {
        var rule=ReworkRule(card);
        return rule!=null&&(!rule.RequireActive||(activation==null?EffectiveTier(rule.Bond,card):
            TierAtCount(rule.Bond,activation.GetValueOrDefault(rule.Bond)))>0);
    }
    internal int ReworkAmount(CardModel card)
    {
        var rule=ReworkRule(card);
        return rule==null?0:(int)Math.Floor(RuleExpression.Evaluate(rule.Amount,key=>key=="L"?Scale(rule.Bond).Layers:0));
    }
    internal bool ReworkRoll(CardModel card)
    {
        var chance=ReworkAmount(card);
        return chance>0&&RandomFor(card is Claw?CovenantRandom.Claw:CovenantRandom.CombatRoll).NextInt(100)<chance;
    }
    internal static T? ReworkSource<T>(PowerModel power) where T:CardModel=>RevisionTrace.Sources(power).OfType<T>()
        .Where(HasRework).OrderByDescending(card=>card.IsUpgraded).FirstOrDefault();
    internal T? LiveRework<T>(Creature? creature=null) where T:CardModel=>(creature??Owner.Creature).Powers
        .Select(ReworkSource<T>).Where(card=>card?.Owner==Owner).OrderByDescending(card=>card!.IsUpgraded).FirstOrDefault();

    public override async Task BeforeHandDrawLate(Player player,PlayerChoiceContext context,ICombatState combatState)
    {
        if(player!=Owner||_reworkOpened)return;
        _reworkOpened=true;
        var draw=Owner.PlayerCombatState!.DrawPile.Cards.ToArray();
        foreach(var card in draw.OfType<MakeItSo>().Where(HasRework))
            if(ReworkRoll(card))await CardPileCmd.Add(card,PileType.Discard);
        foreach(var card in draw.OfType<MeteorStrike>().Where(HasRework))
            if(card.Pile?.Type==PileType.Draw&&!CombatManager.Instance.IsOverOrEnding&&ReworkRoll(card))
                await PlayForCovenant(context,card);
    }

    public override void ModifyShuffleOrder(Player player,List<CardModel> cards,bool isInitialShuffle)
    {
        if(player!=Owner||isInitialShuffle)return;
        // Cards removed from this native list remain in their original discard pile.
        cards.RemoveAll(card=>card is MakeItSo&&card.Pile?.Type==PileType.Discard&&HasRework(card)&&ReworkRoll(card));
    }

    internal void ApplyDivineMarks()
    {
        if(PendingDivineShots.Length==0)return;
        var keys=PendingDivineShots.Split(',').ToHashSet(StringComparer.Ordinal);
        foreach(var card in CardMembership.Cards(Owner).OfType<DivineShot>())
            if(card.DeckVersion is {} original&&keys.Contains(DivineKey(original)))
            {
                card.AddKeyword(CardKeyword.Innate);
                card.AddKeyword(CardKeyword.Retain);
            }
        PendingDivineShots="";
    }
    private string DivineKey(CardModel card)
    {
        var peers=Owner.Deck.Cards.OfType<DivineShot>().Where(other=>other.FloorAddedToDeck==card.FloorAddedToDeck);
        return $"{card.FloorAddedToDeck}/{peers.TakeWhile(other=>other!=card).Count()}";
    }
    internal bool HasDivineMark(CardModel card)=>card is DivineShot&&card.IsMutable&&Owner.Deck.Cards.Contains(card)&&
        PendingDivineShots.Split(',').Contains(DivineKey(card),StringComparer.Ordinal);
    internal void RemoveDivineMark(CardModel card)
    {
        if(card is not DivineShot||PendingDivineShots.Length==0||!Owner.Deck.Cards.Contains(card))return;
        var keys=PendingDivineShots.Split(',').ToHashSet(StringComparer.Ordinal);
        var remaining=new List<string>();
        foreach(var group in Owner.Deck.Cards.OfType<DivineShot>().GroupBy(other=>other.FloorAddedToDeck))
        {
            var index=0;
            foreach(var other in group)
                if(other!=card)
                {
                    if(keys.Contains(DivineKey(other)))remaining.Add($"{other.FloorAddedToDeck}/{index}");
                    index++;
                }
        }
        PendingDivineShots=string.Join(",",remaining);
    }

    internal async Task ReworkBeforePlay(PlayerChoiceContext context,CardPlay play)
    {
        var card=play.Card;
        if(!ReworkEnabled(card,RevisionTrace.FindPlay(card)?.Activation))return;
        switch(card)
        {
            case GrandFinale:
                await PrepareGrandFinale(context,card);
                break;
            case Claw when play.PlayIndex==0:
                if(ReworkRoll(card))_clawReturns.Add(card);
                else _clawReturns.Remove(card);
                break;
            case StormOfSteel:
                foreach(var held in StormTargets(card))
                    CardCmd.ApplySingleTurnSly(held);
                break;
            case DeathsDoor:
                foreach(var enemy in Owner.Creature.CombatState!.HittableEnemies.ToArray())
                    if(enemy.IsAlive&&ReworkRoll(card))
                    {
                        await PowerCmd.Apply<DoomPower>(context,enemy,card.IsUpgraded?2:1,Owner.Creature,card);
                        if(enemy.IsAlive)await PowerCmd.Apply<WeakPower>(context,enemy,1,Owner.Creature,card);
                    }
                break;
            case Clash:
                var discarded=Owner.PlayerCombatState!.Hand.Cards.Where(held=>held!=card&&held.Type!=CardType.Attack).ToArray();
                if(discarded.Length==0)break;
                await CardCmd.Discard(context,discarded);
                GrantLayers(Bond.Encounter,discarded.Length*ReworkAmount(card));
                break;
        }
    }

    internal async Task ReworkAfterPlay(PlayerChoiceContext context,CardPlay play)
    {
        var card=play.Card;
        var frame=RevisionTrace.FindPlay(card)??_revisionPlays.GetValueOrDefault(card);
        if(card is SovereignBlade&&frame?.Values.GetValueOrDefault("rework_overkill") is >0 and var overkill&&
            LiveRework<SeekingEdge>() is {} seeking&&!CombatManager.Instance.IsOverOrEnding&&
            Owner.Creature.CombatState?.HittableEnemies.OrderByDescending(enemy=>enemy.CurrentHp).FirstOrDefault() is {} survivor)
        {
            using var suppressed=RevisionTrace.Suppress(this);
            await CreatureCmd.Damage(context,[new DamageTargetData(survivor)],Math.Floor(overkill*ReworkAmount(seeking)/100m),
                ValueProp.Unpowered,Owner.Creature,null,null,MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType.Cap);
        }
        if(_juggled.Remove(card,out var juggling)&&ReworkEnabled(juggling,frame?.Activation))
            _jugglingReplay+=ReworkAmount(juggling);
        if(!ReworkEnabled(card,frame?.Activation))return;
        switch(card)
        {
            case Bludgeon when frame?.Values.GetValueOrDefault("rework_broke_block")>0:
                _bludgeonDiscount=Math.Max(_bludgeonDiscount,ReworkAmount(card));
                break;
            case Uppercut when frame?.Before.GetValueOrDefault("target_WeakPower")>0&&play.Target is {IsAlive:true} target:
                await PowerCmd.Apply<VulnerablePower>(context,target,ReworkAmount(card),Owner.Creature,card);
                break;
            case ByrdSwoop or GodByrd:
                RewardBirdPlay(card);
                break;
            case PerfectedStrike when ReworkSlay(frame):
                _pendingPerfectedStrikes.Add(card.IsUpgraded);
                break;
            case BorrowedTime:
                if(_carryEnergySource==null||ReworkAmount(card)>ReworkAmount(_carryEnergySource))_carryEnergySource=card;
                break;
            case Turbo:
                _turboRefundSources.Add(card);
                break;
            case MakeItSo:
                var held=RandomFor(CovenantRandom.CombatCard).NextItem(Owner.PlayerCombatState!.Hand.Cards.Where(held=>held!=card).ToArray());
                if(held==null)break;
                foreach(var bond in CardMembership.Of(held).Where(bond=>BondCatalog.Get(bond).UsesLayers&&Active(bond)))
                    GrantLayers(bond,card.IsUpgraded?2:1);
                break;
            case DivineShot when ReworkSlay(frame):
                GrantLayers(Bond.Arrows,card.IsUpgraded?16:12);
                if(ReworkRoll(card)&&Original(card) is {} original&&Owner.Deck.Cards.Contains(original))
                    PendingDivineShots=string.Join(",",PendingDivineShots.Split(',',StringSplitOptions.RemoveEmptyEntries).Append(DivineKey(original)).Distinct());
                break;
            case WahChoKi:
                if(!Once("rework_wah_cho_ki"))break;
                var optionsWithMelody=SixPools.SelectMany(pool=>pool.GetUnlockedCards(Owner.UnlockState,Owner.RunState.CardMultiplayerConstraint))
                    .Where(candidate=>candidate.Tags.Count(tag=>tag is CardTag.Wah or CardTag.Cho or CardTag.Ki)==2&&candidate.IsAllowedInRun(Owner.RunState))
                    .DistinctBy(candidate=>candidate.Id);
                var melody=RandomFor(CovenantRandom.CombatCard).NextItem(optionsWithMelody);
                if(melody==null)break;
                var generated=Owner.Creature.CombatState!.CreateCard(melody,Owner);
                if(generated.EnergyCost.CostsX)_freeXReworks[generated]=true;else generated.EnergyCost.SetUntilPlayed(0);
                generated.AddKeyword(CardKeyword.Exhaust);
                if(card.IsUpgraded)generated.AddKeyword(CardKeyword.Retain);
                await CardPileCmd.AddGeneratedCardToCombat(generated,PileType.Hand,Owner);
                break;
        }
    }

    internal static bool IsStarterStrike(CardModel card)=>card.IsBasicStrikeOrDefend&&card.Tags.Contains(CardTag.Strike);

    private async Task ResolvePerfectedStrikes()
    {
        if(_pendingPerfectedStrikes.Count==0||Owner.Creature.IsDead)return;
        var pending=_pendingPerfectedStrikes;
        _pendingPerfectedStrikes=[];
        var candidates=SixPools.SelectMany(pool=>pool.GetUnlockedCards(Owner.UnlockState,Owner.RunState.CardMultiplayerConstraint))
            .Where(card=>CardMembership.IsStrikeCard(card)&&!IsStarterStrike(card)&&card.IsAllowedInRun(Owner.RunState))
            .DistinctBy(card=>card.Id).OrderBy(card=>card.Id).ToArray();
        if(candidates.Length==0)return;

        var rng=RandomFor(CovenantRandom.PerfectedStrike);
        var starters=Owner.Deck.Cards.Where(card=>IsStarterStrike(card)&&card.IsTransformable).ToList();
        var available=candidates.ToList();
        foreach(var upgraded in pending)
        {
            if(starters.Count==0)break;
            if(available.Count==0)available.AddRange(candidates);
            var starterIndex=rng.NextInt(starters.Count);
            var starter=starters[starterIndex];
            starters.RemoveAt(starterIndex);
            var replacementIndex=rng.NextInt(available.Count);
            var replacement=Owner.RunState.CreateCard(available[replacementIndex],Owner);
            available.RemoveAt(replacementIndex);
            if(upgraded&&replacement.IsUpgradable)CardCmd.Upgrade(replacement);
            await CardCmd.Transform(starter,replacement);
        }
    }
    private static bool ReworkSlay(RevisionFrame? frame)=>frame?.Target is {} target&&frame.KilledTargets.Contains(target)&&frame.Resolve("before_fatal_allowed")>0;

    internal async Task FinishReworkPlay(CardModel card)
    {
        if(_freeXReworks.GetValueOrDefault(card))_freeXReworks.Remove(card);
        if(_clawReturns.Remove(card)&&!CombatManager.Instance.IsOverOrEnding&&card.Pile?.Type is PileType.Discard or PileType.Draw or PileType.Exhaust)
            await CardPileCmd.Add(card,PileType.Hand);
        RefreshBattleStatuses();
    }

    public override CardLocation ModifyCardPlayResultLocation(CardModel card,bool isAutoPlay,ResourceInfo resources,CardLocation cardLocation)
    {
        if(card.Owner!=Owner||!isAutoPlay||!IsStarterStrike(card))return cardLocation;
        if(RevisionTrace.Current?.Parent?.Model is HellraiserPower hellraiser&&ReworkSource<Hellraiser>(hellraiser) is {} source)
        {
            _hellraiserExhausts[card]=source;
            return new(Owner,PileType.Exhaust,CardPilePosition.Bottom);
        }
        return cardLocation;
    }

    internal async Task ReworkExhausted(PlayerChoiceContext context,CardModel card)
    {
        if(_hellraiserExhausts.Remove(card,out var hellraiser))
            await Draw(context,ReworkAmount(hellraiser));
        if(card is not VoidCard||_turboRefundSources.Count==0)return;
        var sources=_turboRefundSources.ToArray();
        _turboRefundSources.Clear();
        foreach(var source in sources)
            await PowerCmd.Apply<EnergyNextTurnPower>(context,Owner.Creature,1,Owner.Creature,source);
    }

    internal void ReworkGenerated(CardModel generated,RevisionTrace.Scope scope)
    {
        if(scope.Suppressed||scope.Frames.Length==0)return;
        if(scope.Model is JugglingPower&&scope.Frames.Select(frame=>frame.Source).OfType<Juggling>().FirstOrDefault(HasRework) is {} juggling)
            _juggled[generated]=juggling;
        if(generated.Type==CardType.Attack&&scope.Model is CalamityPower&&scope.Frames.Select(frame=>frame.Source).OfType<Calamity>().OrderByDescending(card=>card.IsUpgraded).FirstOrDefault(HasRework) is {} calamity&&ReworkRoll(calamity))
        {
            if(generated.EnergyCost.CostsX)_freeXReworks[generated]=false;
            else generated.EnergyCost.SetThisCombat(0);
            generated.AddKeyword(CardKeyword.Exhaust);
        }
    }
    internal bool ReworkFreeX(CardModel card)=>_freeXReworks.ContainsKey(card);
    internal int ShivsRetainedBonus(CardModel card)=>_retainedShivs.GetValueOrDefault(card);
    private CardModel[] StormTargets(CardModel card)=>Owner.PlayerCombatState!.Hand.Cards.Where(held=>held!=card)
        .OrderByDescending(held=>held.EnergyCost.CostsX?Owner.PlayerCombatState.Energy:held.EnergyCost.GetAmountToSpend())
        .Take(ReworkAmount(card)).ToArray();

    internal string ReworkCardCue(CardModel card)
    {
        if(card.Pile?.Type!=PileType.Hand||Owner.PlayerCombatState is not {} state)return "";
        if(card is StormOfSteel&&ReworkEnabled(card))
        {
            var targets=StormTargets(card);
            if(targets.Length>0)return $"将获得奇巧的牌为{CardNames(targets)}。";
        }
        if(card is Clash&&HasRework(card))
        {
            var count=state.Hand.Cards.Count(held=>held!=card&&held.Type!=CardType.Attack);
            if(count>0)return $"当前会丢弃{count}张手牌，使【奇遇】增加{count*ReworkAmount(card)}层。";
        }
        if(card is SovereignBlade&&LiveRework<SwordSage>() is {} sage)
            return $"剑圣使其重放造成生命流失{(sage.IsUpgraded?"，并使生命流失量增加25%":"")}。";
        return "";
    }

    internal void ReworkAfterFlush(Player player,IReadOnlyCollection<CardModel> retained)
    {
        if(player!=Owner||LiveRework<PhantomBlades>() is not {} phantom)return;
        foreach(var shiv in retained.OfType<Shiv>())_retainedShivs[shiv]=_retainedShivs.GetValueOrDefault(shiv)+ReworkAmount(phantom);
        Presentation.RefreshCardTraits();
    }
    internal async Task ReworkEndTurn(PlayerChoiceContext context)
    {
        if(_carryEnergySource is not {} source)return;
        _carryEnergySource=null;
        var reserve=ReworkReservedEnergy(source);
        // Move the reserved energy out of the balance that Ice Cream or Channel can keep.
        if(reserve>0&&await PowerCmd.Apply<EnergyNextTurnPower>(context,Owner.Creature,reserve,Owner.Creature,source)!=null)
            Owner.PlayerCombatState!.LoseEnergy(reserve);
        RefreshBattleStatuses();
    }
    private int ReworkReservedEnergy(CardModel source)=>Owner.PlayerCombatState!.Energy*(source.IsUpgraded?2:1)/(source.IsUpgraded?3:2);

    private static TraitEntry ReworkEntry(CardModel card,string timing,string? state=null)
        =>new(Program(card)!.Rules[0].Bond,RevisionDescriptions.Text(card,annotateScaling:true),Presentation.CardTitle(card),timing,state);

    private IEnumerable<TraitEntry> ReworkStatus()
    {
        if(_bludgeonDiscount>0)
            yield return new(Bond.Heavy,$"你手动打出的下一张原始能量消耗至少为2的攻击牌少消耗{{energyPrefix:energyIcons({_bludgeonDiscount})}}。","重锤","",Counter:_bludgeonDiscount);
        var perfectedStrikes=_pendingPerfectedStrikes.Count;
        if(perfectedStrikes>0)
        {
            var upgraded=_pendingPerfectedStrikes.Count(value=>value);
            var detail=upgraded>0?$"其中至多{upgraded}张变化后的牌会升级。":null;
            yield return new(Bond.Strike,$"战斗结束后，将至多{perfectedStrikes}张基础打击变化为随机的非基础打击牌。","完美打击","战斗结束",detail,perfectedStrikes);
        }
        CardModel?[] ongoing=[LiveRework<Hellraiser>(),LiveRework<Juggling>(),LiveRework<Envenom>(),LiveRework<PhantomBlades>(),
            LiveRework<NoxiousFumes>(),LiveRework<Loop>(),LiveRework<SeekingEdge>(),LiveRework<SwordSage>(),
            LiveRework<ReaperForm>(),LiveRework<CallOfTheVoid>(),LiveRework<Ennui>(),LiveRework<Ecstasy>(),LiveRework<Calamity>()];
        foreach(var source in ongoing.OfType<CardModel>())
        {
            if(!ReworkEnabled(source))continue;
            var details=source switch
            {
                Envenom=>$"本回合还可触发{Math.Max(0,ReworkAmount(source)-_turn.GetValueOrDefault("rework_envenom"))}次。",
                ReaperForm when _reaperRemainder>0=>$"已累计{_reaperRemainder:0.##}层灾厄。",
                Juggling=>_juggled.Count>0?$"可触发的复制品：{string.Join("、",_juggled.Keys.Select(Presentation.CardTitle).Distinct())}。":null,
                _=>null
            };
            yield return ReworkEntry(source,"持续效果",details);
        }
        if(_jugglingReplay>0)
            yield return new(Bond.Creation,$"你下一张打出的技能牌获得重放{_jugglingReplay}。","杂耍","",Counter:_jugglingReplay);
        if(_carryEnergySource is {} carry)
            yield return new(Bond.Reflow,"本回合结束时，将部分未使用的能量保留到下一回合。","预借时间","",$"当前可保留{ReworkReservedEnergy(carry)}点能量。");
        foreach(var copy in CardMembership.Cards(Owner).OfType<MakeItSo>().Where(HasRework))
            if(ReturnStatus(copy) is {} pending)yield return ReworkEntry(copy,"回到手牌时",pending);
        var turboRefund=_turboRefundSources.Count;
        if(turboRefund>0)
            yield return new(Bond.Status,$"下一次消耗虚空时，你在下回合开始时获得{{energyPrefix:energyIcons({turboRefund})}}。","内核加速","",Counter:turboRefund);
    }

    public override int ModifyCardPlayCount(CardModel card,Creature? target,int playCount)=>
        card.Owner==Owner&&card.Type==CardType.Skill?playCount+_jugglingReplay:playCount;
    public override Task AfterModifyingCardPlayCount(CardModel card)
    {
        if(card.Owner==Owner&&card.Type==CardType.Skill)_jugglingReplay=0;
        return Task.CompletedTask;
    }
    internal void ResetReworkTurn()
    {
        foreach(var copy in _juggled.Keys.Where(card=>card.Pile?.IsCombatPile!=true).ToArray())
            _juggled.Remove(copy);
        _penetratingAttacks.Clear();
    }

    internal decimal ReworkDoomBonus(PowerModel reaper,decimal amount)
    {
        if(ReworkSource<ReaperForm>(reaper) is not {} source)return 0;
        _reaperRemainder+=amount*ReworkAmount(source)/100m;
        var whole=Math.Floor(_reaperRemainder);_reaperRemainder-=whole;return whole;
    }

    internal decimal ReworkPoisonPenetration(CardPlay play,Creature target,decimal blockable)
    {
        if(play.Card.Type!=CardType.Attack||!target.IsEnemy||blockable<=0)return 0;
        var poison=target.GetPowerAmount<PoisonPower>();
        if(poison<=0)return 0;
        if(!_penetratingAttacks.Contains(play))
        {
            if(LiveRework<Envenom>() is not {} source||!Once("rework_envenom",ReworkAmount(source)))return 0;
            _penetratingAttacks.Add(play);
        }
        return Math.Min(blockable,poison);
    }

    internal void ReworkSelfDamage(Creature? dealer,decimal amount)
    {
        if(amount<=0||dealer?.IsEnemy==true)return;
        foreach(var demon in Owner.PlayerCombatState!.Hand.Cards.OfType<DemonForm>())
            if(ReworkEnabled(demon))demon.EnergyCost.AddUntilPlayed(-ReworkAmount(demon));
    }

    internal async Task FinishReworkLoop(LoopPower power,OrbModel orb)
    {
        if(ReworkSource<Loop>(power) is not {} source||source.Owner!=Owner||CombatManager.Instance.IsOverOrEnding)return;
        var others=Owner.PlayerCombatState!.OrbQueue.Orbs.SkipWhile(candidate=>candidate!=orb).Skip(1).ToArray();
        foreach(var other in others)SwapOrbs(orb,other);
        if(source.IsUpgraded&&Owner.PlayerCombatState.OrbQueue.Orbs.Contains(orb))await OrbCmd.Passive(Context,orb,null);
        RefreshBattleStatuses();
    }

    public override bool TryModifyEnergyCostInCombatLate(CardModel card,decimal originalCost,out decimal modifiedCost)
    {
        if(CreationFreeThisTurn(card)||(originalCost>0&&card.IsMutable&&card.Owner==Owner&&
            (card.HasStarCostX||card.BaseStarCost>0||card.CurrentStarCost>0)&&Advanced(Bond.Stars)))
        {modifiedCost=0;return originalCost!=0;}
        modifiedCost=originalCost;
        if(originalCost is not (2 or 3)||!InitiativeDiscountAvailable(card))return false;
        modifiedCost--;
        return true;
    }
}
