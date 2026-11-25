using System.Reflection;
using System.Threading;
using Covenant.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private sealed class RotationShuffle(Player player, RevisionFrame? reboot)
    {
        internal readonly Player Player = player;
        internal readonly RevisionFrame? Reboot = reboot;
        internal readonly HashSet<CardModel> Incoming = [..player.PlayerCombatState!.DiscardPile.Cards,
            ..(reboot?.Items("before_Hand") ?? [])];
        internal int Returned;
    }

    private static readonly AsyncLocal<RotationShuffle?> CurrentShuffle = new();
    private Dictionary<CardModel, int> _rotationTop = new();
    private Dictionary<CardModel, int> _rotationRetention = new();
    private HashSet<CardModel> _rotationRetained = new();
    private Dictionary<CardModel, int> _rotationAttack = new();
    private HashSet<CardModel> _breachHits = new();
    private bool _firstShuffleSeen, _extraTurnPending, _ostyWasDowned, _initiativeDiscountUsed;
    private bool _quotingInitiative;
    private Queue<(int Index, CardModel Source)> _demesneOpeningDraws = new();
    private int _ordinaryOpeningDraws, _cardOpeningDraws, _cardOpeningDrawIndex;

    private void ResetNewBonds()
    {
        _rotationTop = new(); _rotationRetention = new(); _rotationRetained = new(); _rotationAttack = new();
        _breachHits = new();
        _firstShuffleSeen = _extraTurnPending = _ostyWasDowned = _initiativeDiscountUsed = _quotingInitiative = false;
        _ordinaryOpeningDraws = _cardOpeningDraws = _cardOpeningDrawIndex = 0;
        _demesneOpeningDraws = new();
    }

    internal static void InstallNewBonds(Harmony harmony)
    {
        HarmonyMethod Patch(string name) => new(typeof(CovenantTerminal), name);
        harmony.Patch(AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Shuffle)),
            prefix:Patch(nameof(ShuffleStarting)), postfix:Patch(nameof(ShuffleStarted)), finalizer:Patch(nameof(ShuffleStartingFailed)));
        harmony.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.AfterShuffle)), prefix:Patch(nameof(CountShuffledCards)));
        harmony.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.ModifyHandDraw)),
            prefix:Patch(nameof(BeforeOpeningDrawCount)), postfix:Patch(nameof(AfterOpeningDrawCount)));
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(PowerModel).Assembly).Where(t => t.IsSubclassOf(typeof(PowerModel))))
            if (type.GetMethod(nameof(PowerModel.ModifyHandDraw), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is {} method)
                harmony.Patch(method, postfix:Patch(nameof(RecordCardOpeningDraw)));
        harmony.Patch(AccessTools.Method(typeof(VulnerablePower), nameof(VulnerablePower.ModifyDamageMultiplicative)),
            postfix:Patch(nameof(ModifyBreachVulnerable)));
        harmony.Patch(AccessTools.Method(typeof(WeakPower), nameof(WeakPower.ModifyDamageMultiplicative)),
            postfix:Patch(nameof(ModifyBreachWeak)));
    }

    private static void ShuffleStarting(Player player, out RotationShuffle? __state)
    {
        __state = CurrentShuffle.Value;
        var card = RevisionTrace.Current?.Model as Reboot;
        var reboot = card?.Owner == player ? RevisionTrace.FindPlay(card) : null;
        CurrentShuffle.Value = For(player) == null ? null : new RotationShuffle(player, reboot);
    }

    private static void ShuffleStarted(RotationShuffle? __state) => CurrentShuffle.Value = __state;
    private static void ShuffleStartingFailed(Exception? __exception, RotationShuffle? __state)
    {
        if (__exception != null) CurrentShuffle.Value = __state;
    }

    private static void CountShuffledCards(Player shuffler)
    {
        if (CurrentShuffle.Value is not {} shuffle || shuffle.Player != shuffler) return;
        var deck = shuffler.PlayerCombatState!.DrawPile.Cards;
        shuffle.Returned = deck.Count(shuffle.Incoming.Contains);
    }

    public override async Task AfterShuffle(PlayerChoiceContext context, Player player)
    {
        if (player != Owner || CurrentShuffle.Value is not { Returned:>0 } shuffle) return;
        var scale = Scale(Bond.Rotation);
        var source = shuffle.Reboot?.Source ?? _currentCard;
        var tier = EffectiveTier(Bond.Rotation, source);
        if (!_firstShuffleSeen)
        {
            _firstShuffleSeen = true;
            _extraTurnPending = tier > 0 && scale.Layers >= 350;
        }
        if (tier == 0) return;
        var copies = shuffle.Returned / (30 - scale.A);
        if (tier >= 2 && copies > 0) await Power<DuplicationPower>(context, copies);
        RefreshBattleStatuses();
    }

    private async Task ResolveRotationEnd(PlayerChoiceContext context)
    {
        if (!_extraTurnPending) return;
        _extraTurnPending = false;
        if (!CombatManager.Instance.IsOverOrEnding && !Owner.Creature.IsDead)
            await Power<ExtraTurnPower>(context, 1);
    }

    private static bool IsBreachDebuff(PowerModel power) => power is WeakPower or VulnerablePower;
    private static bool HasBothBreachDebuffs(Creature target) =>
        target.GetPowerAmount<WeakPower>() > 0 && target.GetPowerAmount<VulnerablePower>() > 0;

    private async Task ApplyBreachBuff(PlayerChoiceContext context, PowerModel power, Creature? applier, CardModel? source)
    {
        if (!IsBreachDebuff(power) || !power.Owner.IsEnemy || applier != Owner.Creature && applier?.PetOwner != Owner)
            return;
        var origin = source ?? (RevisionTrace.Current?.Parent is {Suppressed:false,Model:CardModel nativeCard} ? nativeCard : null);
        if (!Active(Bond.Breach, source ?? _currentCard) || !Once(power is WeakPower ? "breach_weak" : "breach_vulnerable"))
            return;
        var amount = 2 + Scale(Bond.Breach).A;
        if (Once("breach_first")) await Power<VigorPower>(context, amount);
        else await Power<StretchPower>(context, amount);
    }

    private void ObserveBreachHit(Creature target, ValueProp props, Creature? dealer, CardModel? source)
    {
        if (target.IsEnemy && source is {Type:CardType.Attack} && source.Owner == Owner &&
            (dealer == Owner.Creature || dealer?.PetOwner == Owner) && props.IsPoweredAttack() &&
            Advanced(Bond.Breach, source) && HasBothBreachDebuffs(target) && Once("breach_hit"))
            _breachHits.Add(source);
    }

    private async Task ResolveBreachHit(PlayerChoiceContext context, CardModel card)
    {
        if (!_breachHits.Remove(card) || CombatManager.Instance.IsOverOrEnding) return;
        using var suppressed = RevisionTrace.Suppress(this);
        var enemies = Owner.Creature.CombatState!.HittableEnemies;
        if (enemies.Count > 0)
            await PowerCmd.Apply<WeakPower>(context, RandomFor(CovenantRandom.CombatTarget).NextItem(enemies)!, 1, Owner.Creature, null);
        enemies = Owner.Creature.CombatState!.HittableEnemies;
        if (enemies.Count > 0)
            await PowerCmd.Apply<VulnerablePower>(context, RandomFor(CovenantRandom.CombatTarget).NextItem(enemies)!, 1, Owner.Creature, null);
    }

    private int BreachMilestone(CardModel? source = null) =>
        Scale(Bond.Breach) is {Layers:>=200} scale && Active(Bond.Breach, source) ? scale.A : 0;

    private static void ModifyBreachVulnerable(VulnerablePower __instance, Creature? target, Creature? dealer,
        ValueProp props, CardModel? cardSource, ref decimal __result)
    {
        if (target?.IsEnemy == true && target == __instance.Owner && props.IsPoweredAttack() &&
            For(dealer?.Player ?? dealer?.PetOwner) is {} terminal)
            __result += .02m * terminal.BreachMilestone(cardSource);
    }

    private static void ModifyBreachWeak(WeakPower __instance, Creature? target, Creature? dealer,
        ValueProp props, ref decimal __result)
    {
        if (dealer?.IsEnemy == true && dealer == __instance.Owner && props.IsPoweredAttack() &&
            For(target?.Player ?? target?.PetOwner) is {} terminal)
            __result = Math.Max(0, __result - .01m * terminal.BreachMilestone());
    }

    private static bool IsOrdinaryCard(CardModel card) => card.Type is CardType.Attack or CardType.Skill or CardType.Power;

    private static void BeforeOpeningDrawCount(Player player)
    {
        if (For(player) is not {} terminal) return;
        terminal._cardOpeningDraws = terminal._cardOpeningDrawIndex = 0;
        terminal._demesneOpeningDraws.Clear();
    }

    private static void RecordCardOpeningDraw(PowerModel __instance, Player player, decimal count, decimal __result)
    {
        if (__result <= count || For(player) is not {} terminal) return;
        var sources = RevisionTrace.Sources(__instance);
        if (!sources.Any()) return;
        var added = (int)Math.Ceiling(__result) - (int)Math.Ceiling(count);
        if (__instance is DemesnePower && added > 0)
        {
            // Extra draws follow the normal hand draw and are assigned in modifier order.
            var index = terminal._cardOpeningDraws;
            var end = Math.Min(index + added, CardPile.MaxCardsInHand);
            foreach (var source in sources.OfType<Demesne>())
            {
                var sourceEnd = Math.Min(index + RevisionTrace.PowerApplications(__instance, source), end);
                for (; index < sourceEnd; index++) terminal._demesneOpeningDraws.Enqueue((index, source));
                if (index >= end) break;
            }
        }
        terminal._cardOpeningDraws += added;
    }

    private static void AfterOpeningDrawCount(Player player, decimal __result)
    {
        if (For(player) is not {} terminal) return;
        var drawCount = Math.Max(0, (int)Math.Ceiling(__result));
        var baseline = Math.Max(0, drawCount - terminal._cardOpeningDraws);
        var state = player.PlayerCombatState!;
        // Innate cards can raise the first hand's minimum draw count independently.
        if (state.TurnNumber == 1)
            baseline = Math.Max(baseline, state.DrawPile.Cards.Count(card =>
                card.Keywords.Contains(CardKeyword.Innate) && card.Enchantment?.ShouldStartAtBottomOfDrawPile != true));
        terminal._ordinaryOpeningDraws = baseline;
        terminal._cardOpeningDraws = Math.Max(0, drawCount - baseline);
    }

    private bool IsCardEffectDraw(bool normalHandDraw, out CardModel? source)
    {
        source = null;
        if (normalHandDraw)
        {
            if (_ordinaryOpeningDraws > 0) { _ordinaryOpeningDraws--; return false; }
            if (_cardOpeningDraws == 0) return false;
            _cardOpeningDraws--;
            if (_demesneOpeningDraws.TryPeek(out var bonus) && bonus.Index == _cardOpeningDrawIndex)
                source = _demesneOpeningDraws.Dequeue().Source;
            _cardOpeningDrawIndex++;
            return true;
        }
        var cause = RevisionTrace.Current;
        if (cause?.Model == this && cause.Method == nameof(AfterCardDrawn)) cause = cause.Parent;
        if (cause is not {Suppressed:false}) return false;
        source = cause.Model as CardModel;
        return source != null || cause.Model is PowerModel power && RevisionTrace.Sources(power).Any();
    }

    private async Task ObserveCovenantDraw(CardModel card, bool normalHandDraw)
    {
        var fromCard = IsCardEffectDraw(normalHandDraw, out var source);
        if (normalHandDraw && source is Demesne && card.Keywords.Contains(CardKeyword.Ethereal))
        {
            var frame = new RevisionFrame(source, "native") { Subject = card };
            frame.AddCard("drawn", card);
            await FireRevision(frame);
        }
        var inHand = card.Pile?.Type == PileType.Hand;
        if (inHand && _rotationTop.Remove(card, out var layers))
        {
            CardCmd.ApplySingleTurnRetain(card);
            if (Active(Bond.Rotation)) GrantLayers(Bond.Rotation, layers);
        }
        if (!fromCard || !Active(Bond.Rotation, source?.Owner == Owner ? source : _currentCard)) return;
        if (inHand && IsOrdinaryCard(card) && Once("rotation_draw"))
        {
            using var suppressed = RevisionTrace.Suppress(this);
            CardCmd.ApplySingleTurnRetain(card);
        }
        await Block(1 + Scale(Bond.Rotation).A / 4);
    }

    private void ObserveRotationPlay(CardModel card)
    {
        if (_rotationRetained.Remove(card) && _rotationRetention.Remove(card, out var layers) && Active(Bond.Rotation, card))
            GrantLayers(Bond.Rotation, layers);
    }

    private async Task ResolveNewTrait(RevisionFrame frame, int amount)
    {
        var source = frame.Source;
        switch (source)
        {
            case MeteorShower:
                var enemies = Owner.Creature.CombatState!.HittableEnemies;
                var weak = enemies.Sum(enemy => (long)enemy.GetPowerAmount<WeakPower>());
                var vulnerable = enemies.Sum(enemy => (long)enemy.GetPowerAmount<VulnerablePower>());
                var growth = (int)Math.Clamp(source.IsUpgraded ? (weak + vulnerable) / 2 : Math.Min(weak, vulnerable), 0, Ledger.MaxLayers);
                foreach (var bond in new[]{Bond.Stars, Bond.Breach})
                    if (growth > 0 && RevisionActive(frame, bond)) GrantLayers(bond, growth);
                break;
            case BeamCell when frame.Before.GetValueOrDefault("target_VulnerablePower") > 0 && frame.Target is {IsAlive:true} beamTarget:
                await PowerCmd.Apply<WeakPower>(Context, beamTarget, amount, Owner.Creature, source);
                break;
            case FallingStar when frame.Target is {IsAlive:true} fallingTarget:
                await PowerCmd.Apply<WeakPower>(Context, fallingTarget, 1, Owner.Creature, source);
                await PowerCmd.Apply<VulnerablePower>(Context, fallingTarget, 1, Owner.Creature, source);
                break;
            case Capacitor when frame.RightOrb is {} orb && Owner.PlayerCombatState!.OrbQueue is var queue &&
                queue.Capacity > frame.Before["orb_slots"] && queue.Orbs.Count < queue.Capacity:
                await OrbCmd.Channel(Context, (OrbModel)orb.MutableClone(), Owner);
                break;
            case Stratagem:
                foreach (var card in frame.Items("returned_from_Draw").Where(card => card.Pile?.Type == PileType.Hand))
                {
                    if (!card.Keywords.Contains(CardKeyword.Retain)) card.AddKeyword(CardKeyword.Retain);
                    if (source.IsUpgraded) card.EnergyCost.AddUntilPlayed(-1);
                }
                break;
            case Reboot:
                var hand = Owner.PlayerCombatState!.Hand;
                var candidates = frame.Items("before_Hand").Where(card => card.Pile?.Type == PileType.Draw).ToList();
                while (amount-- > 0 && candidates.Count > 0 && hand.Cards.Count < CardPile.MaxCardsInHand)
                {
                    var card = RandomFor(CovenantRandom.CombatCard).NextItem(candidates)!;
                    candidates.Remove(card);
                    await CardPileCmd.Add(card, PileType.Hand);
                }
                break;
            case Expertise:
                foreach (var card in frame.Items("drawn"))
                    _rotationRetention[card] = Math.Max(amount, _rotationRetention.GetValueOrDefault(card));
                break;
            case PhotonCut:
                foreach (var card in frame.Items("to_top")) _rotationTop[card] = amount;
                break;
            case RocketPunch:
                var status = frame.Items("drawn").FirstOrDefault(card => card.Type == CardType.Status && card.Pile?.Type == PileType.Hand);
                if (status != null && await Select(Context, [status], "你可以丢弃这张状态牌。") != null)
                {
                    if (status.Pile?.Type != PileType.Hand) break;
                    await CardCmd.Discard(Context, status);
                    if (source.IsUpgraded) await Draw(Context);
                }
                break;
            case SweepingBeam when frame.HitTargets.Count >= 2:
                foreach (var card in frame.Items("drawn").Where(card => card.Type == CardType.Attack)) _rotationAttack[card] = amount;
                break;
        }
    }

    public override Task BeforeFlush(PlayerChoiceContext context, Player player)
    {
        if (player == Owner && _turnEpoch == 1)
            foreach (var card in player.PlayerCombatState!.Hand.Cards.OfType<BootSequence>())
                CardCmd.ApplySingleTurnRetain(card);
        return Task.CompletedTask;
    }

    public override Task AfterCurrentHpChanged(Creature creature, decimal delta)
    {
        if (creature == Owner.Creature && delta < 0 && creature.CombatState?.CurrentSide == CombatSide.Player)
            _turn["lost_health"] = 1;
        return Task.CompletedTask;
    }

    private bool InitiativeDiscountAvailable(CardModel card) =>
        !_quotingInitiative && !_initiativeDiscountUsed && card.Owner == Owner &&
        card.Pile?.Type is PileType.Hand or PileType.Play && !CardMembership.IsXCost(card) &&
        _turnEpoch == 1 && CostTier(Bond.Initiative, card)>1;

    private void ConsumeInitiativeDiscount(CardPlay play)
    {
        if (!play.IsFirstInSeries || play.Resources.EnergySpent <= 0 || !InitiativeDiscountAvailable(play.Card)) return;
        _quotingInitiative = true;
        try
        {
            var cost = play.Card.EnergyCost.GetWithModifiers(CostModifiers.All);
            if (cost is 2 or 3 && play.Resources.EnergySpent == cost - 1) _initiativeDiscountUsed = true;
        }
        finally { _quotingInitiative = false; }
    }

    public override decimal ModifyHandDraw(Player player, decimal count) =>
        player == Owner && _turnEpoch == 1 && Active(Bond.Initiative) ? count + 1 : count;
}
