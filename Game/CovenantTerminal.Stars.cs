using Covenant.Core;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private int _starsSpentThisTurn, _starsTowardVigor;
    private bool _starPending;
    private List<(CardModel Source,Bond Bond,int Layers,bool ThisTurn)> _nextStarGrowth = new();
    private List<(CardModel Source,Bond Bond,int Layers)> _nextShivGrowth = new();
    private List<(CardModel Source,int Remaining)> _neutronMeters = new();
    private Dictionary<Creature,HashSet<KnockoutBlow>> _knockoutMarks = new();

    private void ResetStarTraits()
    {
        _starsSpentThisTurn = _starsTowardVigor = 0;
        _starPending = false;
        _nextStarGrowth = new();
        _nextShivGrowth = new();
        _neutronMeters = new();
        _knockoutMarks = new();
    }

    private void EndStarTurn()
    {
        _starPending = _starsSpentThisTurn >= 2 && Active(Bond.Stars);
        _starsSpentThisTurn = 0;
        _nextStarGrowth.RemoveAll(effect => effect.ThisTurn);
        _knockoutMarks.Clear();
    }

    private async Task StartStarTurn(PlayerChoiceContext context)
    {
        if (_starPending)
        {
            _starPending = false;
            await PlayerCmd.GainStars(1, Owner);
        }
        var scale = Scale(Bond.Stars);
        if (scale.Layers < 250 || _turnEpoch > scale.A / 5 || !Active(Bond.Stars)) return;
        var card = Owner.PlayerCombatState!.DrawPile.Cards.LastOrDefault(candidate =>
            NativeCapabilities.Of(candidate).GainStars || Program(candidate)?.Rules.Any(rule => rule.Action == "stars") == true);
        if (card != null) await PlayForCovenant(context, card);
    }

    private async Task ApplyStarSpending(int amount)
    {
        _starsSpentThisTurn += amount;
        var next = _nextStarGrowth.ToArray();
        _nextStarGrowth.Clear();
        foreach (var effect in next)
            if (Active(effect.Bond)) GrantLayers(effect.Bond, effect.Layers);

        if (Advanced(Bond.Stars))
        {
            var times = (_starsTowardVigor + amount) / 3;
            _starsTowardVigor = (_starsTowardVigor + amount) % 3;
            for (var i = 0; i < times; i++)
            {
                await Power<VigorPower>(Context, 1 + Scale(Bond.Stars).A / 4);
                GrantLayers(Bond.Stars, 2);
            }
        }

        for (var i = 0; i < _neutronMeters.Count; i++)
        {
            var (source, remaining) = _neutronMeters[i];
            var threshold = NeutronThreshold(source);
            var total = remaining + amount;
            _neutronMeters[i] = (source, total % threshold);
            var awards = total / threshold;
            if (awards <= 0) continue;
            await PowerCmd.Apply<AfterimagePower>(Context, Owner.Creature, awards, Owner.Creature, source);
            GrantLayers(Bond.Stars, 5 * awards);
        }
        RefreshBattleStatuses();
    }

    private static int NeutronThreshold(CardModel card) => card.IsUpgraded ? 8 : 10;

    private void ApplyNextShiv(CardModel shiv)
    {
        foreach (var effect in _nextShivGrowth)
            if (Active(effect.Bond,shiv)) GrantLayers(effect.Bond, effect.Layers);
        _nextShivGrowth.Clear();
    }

    private void MarkKnockoutTarget(Creature target, CardModel? source)
    {
        if (source is not KnockoutBlow knockout || knockout.Owner != Owner || !target.IsEnemy || !target.IsAlive) return;
        if (!_knockoutMarks.TryGetValue(target, out var marks)) _knockoutMarks[target] = marks = new();
        marks.Add(knockout);
        RefreshBattleStatuses();
    }

    private async Task ResolveKnockoutDeath(Creature target, CardModel? killer)
    {
        if (!_knockoutMarks.Remove(target, out var marks)) return;
        var stars = marks.Where(card => card != killer).Sum(card => card.DynamicVars.Stars.BaseValue);
        if (stars > 0) await PlayerCmd.GainStars(stars, Owner);
        RefreshBattleStatuses();
    }

    private IEnumerable<TraitEntry> StarTraitStatuses()
    {
        var star = "{starIcon}";
        if (_starPending) yield return new(Bond.Stars, $"下回合开始时，获得1{star}。", "星辉", "");
        else if (_starsSpentThisTurn > 0 && Active(Bond.Stars))
            yield return new(Bond.Stars, $"在回合结束时，若本回合至少花费了2{star}，下回合开始时获得1{star}。", "星辉", $"本回合已花费{_starsSpentThisTurn}{star}。");
        if (Advanced(Bond.Stars))
            yield return new(Bond.Stars, $"每累计消耗3{star}，获得{1+Scale(Bond.Stars).A/4}点活力，并使【星辉】增加2层。", "星辉", $"已累计{_starsTowardVigor}/3。", Counter:_starsTowardVigor);
        foreach (var (source, bond, layers, thisTurn) in _nextStarGrowth)
            yield return new(bond, $"{(thisTurn ? "本回合" : "")}下次花费{star}时，使已激活的【{BondCatalog.Get(bond).Name}】增加{layers}层。", Presentation.CardTitle(source), "");
        foreach (var (source, bond, layers) in _nextShivGrowth)
            yield return new(bond, $"下次打出小刀时，使已激活的【{BondCatalog.Get(bond).Name}】增加{layers}层。", Presentation.CardTitle(source), "");
        foreach (var (source, remaining) in _neutronMeters)
        {
            var threshold = NeutronThreshold(source);
            yield return new(Bond.Stars, $"每累计消耗{threshold}{star}，获得1点残影，并使【星辉】增加5层。", Presentation.CardTitle(source), $"已累计{remaining}/{threshold}。", Counter:remaining);
        }
        foreach (var (target, marks) in _knockoutMarks)
            foreach (var source in marks)
                yield return new(Bond.Stars, $"若{target.Name}在本回合死亡，获得{source.DynamicVars.Stars.BaseValue}{star}。", Presentation.CardTitle(source), "");
    }
}
