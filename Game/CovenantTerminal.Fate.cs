using Covenant.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private const int FateMinCards = 20;
    private const int FateMaxCards = 30;
    private const int FatePlaysPerTarot = 3;
    private sealed record CardRoster(CardModel[] Cards)
    {
        internal HashSet<ModelId> Ids { get; } = Cards.Select(card => card.Id).ToHashSet();
    }
    private sealed record RunRosters(CardRoster Fate, CardRoster Oracle);
    private RunRosters? _runRosters;
    private CardModel? _revealedTarot;
    private int _fatePlays;
    private HashSet<CardModel> _fateEnhanced = new(ReferenceEqualityComparer.Instance);

    private RunRosters? GetRunRosters()
    {
        if (_runRosters != null) return _runRosters;
        if (!IsMutable || Owner?.RunState is not RunState run) return null;

        var pools = AllCardPools ? SixPools : [Owner.Character.CardPool];
        var candidates = pools.Append(ModelDb.CardPool<ColorlessCardPool>())
            .SelectMany(pool => pool.GetUnlockedCards(Owner.UnlockState, Owner.RunState.CardMultiplayerConstraint)
                .Where(card => card.Pool == pool && card.Rarity != CardRarity.Basic))
            .DistinctBy(card => card.Id)
            .Where(FitsRoster)
            .OrderBy(card => card.Id.Entry, StringComparer.Ordinal)
            .ToArray();
        CardRoster ChooseFate(IEnumerable<CardModel> pool)
        {
            var rng = new Rng(run.Rng.Seed, "Covenant:FateRoster");
            return new(SampleCards(pool.ToArray(), rng.NextInt(FateMinCards, FateMaxCards + 1), rng));
        }

        var fate = ChooseFate(candidates);
        var remaining = candidates.Where(card => !fate.Ids.Contains(card.Id)).ToArray();
        var oracleRng = new Rng(run.Rng.Seed, "Covenant:OracleRoster");
        CardRoster ChooseOracle(CardModel[] pool) => new([
            ..SampleCards(pool.Where(card => card.Rarity == CardRarity.Common).ToArray(), 2, oracleRng),
            ..SampleCards(pool.Where(card => card.Rarity == CardRarity.Uncommon).ToArray(), 3, oracleRng)]);

        if (remaining.Count(card => card.Rarity == CardRarity.Common) >= 2 &&
            remaining.Count(card => card.Rarity == CardRarity.Uncommon) >= 3)
            return _runRosters = new(fate, ChooseOracle(remaining));

        // Preserve the existing Fate roster when possible. Reserve Oracle's full rarity quota for small pools.
        var oracle = ChooseOracle(candidates);
        fate = ChooseFate(candidates.Where(card => !oracle.Ids.Contains(card.Id)));
        return _runRosters = new(fate, oracle);
    }

    private static CardModel[] SampleCards(CardModel[] candidates, int count, Rng rng)
    {
        var total = Math.Min(candidates.Length, count);
        for (var draw = 0; draw < total; draw++)
        {
            var chosen = rng.NextInt(draw, candidates.Length);
            (candidates[draw], candidates[chosen]) = (candidates[chosen], candidates[draw]);
        }
        return candidates[..total];
    }

    private static bool FitsRoster(CardModel card)
    {
        if (CardMembership.PrintedBadgeCount(card) >= 5) return false;
        if (!card.IsUpgradable) return true;
        var upgraded = (CardModel)card.MutableClone();
        upgraded.UpgradeInternal();
        upgraded.FinalizeUpgradeInternal();
        return CardMembership.PrintedBadgeCount(upgraded) < 5;
    }

    internal ulong RunRosterMask(CardModel card)
    {
        var rosters = GetRunRosters();
        if (rosters?.Oracle.Ids.Contains(card.Id) == true) return CardMembership.Bit(Bond.Oracle);
        return rosters?.Fate.Ids.Contains(card.Id) == true ? CardMembership.Bit(Bond.Fate) : 0;
    }

    internal IReadOnlyList<CardModel> FateCards => GetRunRosters()?.Fate.Cards ?? [];
    internal IReadOnlyList<CardModel> OracleCards => GetRunRosters()?.Oracle.Cards ?? [];

    private void ResetFateCombat()
    {
        _revealedTarot = null;
        _fatePlays = 0;
        _fateEnhanced = new(ReferenceEqualityComparer.Instance);
    }

    private void RevealTarot()
    {
        _revealedTarot = RandomFor(CovenantRandom.FateTarot)
            .NextItem(TarotCards.All.Where(card => card.IsAllowedInRun(Owner.RunState)));
    }

    private async Task ResolveFatePlay(PlayerChoiceContext context, CardModel card)
    {
        var ended = CombatManager.Instance.IsOverOrEnding;
        if (!ended && _fateEnhanced.Contains(card))
            await Draw(context, 1 + Scale(Bond.Fate).A / 5);

        if ((RunRosterMask(card) & CardMembership.Bit(Bond.Fate)) == 0) return;
        var tier = EffectiveTier(Bond.Fate, card);
        if (tier == 0) return;
        GrantLayers(Bond.Fate, 2);
        if (ended || Owner.Creature.CombatState is not { } combat) return;

        if (_revealedTarot == null) RevealTarot();
        if (_revealedTarot == null || ++_fatePlays < FatePlaysPerTarot) return;

        var tarot = combat.CreateCard(_revealedTarot, Owner);
        _fatePlays = 0;
        RevealTarot();
        if (tier == 2)
        {
            if (!tarot.Keywords.Contains(CardKeyword.Retain)) tarot.AddKeyword(CardKeyword.Retain);
            _fateEnhanced.Add(tarot);
        }
        // The covenant awards the revealed card, rather than asking a card or player to generate a new candidate.
        await CardPileCmd.AddGeneratedCardToCombat(tarot, PileType.Hand, null);
    }

    private IEnumerable<TraitEntry> FateStatus(IReadOnlyDictionary<Bond, int> counts)
    {
        if (counts.GetValueOrDefault(Bond.Fate) >= BondCatalog.Get(Bond.Fate).Basic && _revealedTarot is { } tarot)
            yield return new(Bond.Fate, $"再打出{FatePlaysPerTarot - _fatePlays}张【命运】牌，将【{Presentation.CardTitle(tarot)}】加入手牌。",
                "命运", "", $"当前进度 {_fatePlays}/{FatePlaysPerTarot}", _fatePlays);
        var names = _fateEnhanced.Where(card => card.Pile?.IsCombatPile == true)
            .Select(card => $"【{Presentation.CardTitle(card)}】").Distinct().ToArray();
        if (names.Length > 0)
            yield return new(Bond.Fate, $"打出{string.Join("、", names)}后，抽{1 + Scale(Bond.Fate).A / 5}张牌。",
                "命运", "");
    }
}
