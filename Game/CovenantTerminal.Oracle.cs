using Covenant.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private bool? _oracleActiveAtStart;
    private bool _oracleOpeningPending;

    private void PrepareOracleOpening()
    {
        var roster = OracleCards;
        var required = BondCatalog.Get(Bond.Oracle).Basic;
        var deck = Owner.Deck.Cards.Select(card => card.Id).ToHashSet();
        _oracleActiveAtStart = _oracleOpeningPending = roster.Count == required && roster.All(card => deck.Contains(card.Id));
    }

    internal static void InstallOracle(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(CardPileCmd), nameof(CardPileCmd.Draw),
                [typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool)]),
            prefix: new HarmonyMethod(typeof(CovenantTerminal), nameof(ReplaceOracleOpening)));
    }

    private static bool ReplaceOracleOpening(PlayerChoiceContext choiceContext, decimal count, Player player, bool fromHandDraw,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!fromHandDraw || player.PlayerCombatState is not { TurnNumber: 1 } combat ||
            For(player) is not { _oracleOpeningPending: true } terminal || CombatManager.Instance.IsOverOrEnding)
            return true;

        terminal._oracleOpeningPending = false;
        var capacity = Math.Max(0, CardPile.MaxCardsInHand - combat.Hand.Cards.Count);
        var amount = Math.Min(Math.Min(combat.DrawPile.Cards.Count, capacity), Math.Max(0, (int)Math.Ceiling(count)));
        __result = ChooseOracleHand(choiceContext, player, combat.DrawPile, combat.Hand, amount);
        return false;
    }

    private static async Task<IEnumerable<CardModel>> ChooseOracleHand(PlayerChoiceContext context, Player player,
        CardPile draw, CardPile hand, int amount)
    {
        if (amount == 0) return [];
        // Use the final native hand size, after relic and Innate adjustments. Combat-pile selection hides draw order.
        var selected = draw.Cards.Count == amount
            ? draw.Cards.ToArray()
            : (await CardSelectCmd.FromCombatPile(context, draw, player,
                new CardSelectorPrefs(Presentation.Literal($"【神喻】选择{amount}张牌加入起手。"), amount)
                { RequireManualConfirmation = amount > 1 })).ToArray();
        await CardPileCmd.Add(selected, hand);
        return selected;
    }
}
