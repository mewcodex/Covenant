using Covenant.Core;
using Covenant.Game;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

internal static class OracleProbe
{
    internal static async Task Run(SceneTree tree)
    {
        await CombatProbe.Prepare(tree, run =>
        {
            var player = run.Players.Single();
            var terminal = CovenantTerminal.For(player)!;
            foreach (var card in terminal.OracleCards.Concat(
                terminal.FateCards.Where(card => !card.Keywords.Contains(CardKeyword.Innate)).Take(6)))
                player.Deck.AddInternal(run.CreateCard(card, player));
            player.AddRelicInternal(ModelDb.Relic<BagOfPreparation>().ToMutable());
        });

        var player = RunManager.Instance.DebugOnlyGetState()!.Players.Single();
        var terminal = CovenantTerminal.For(player)!;
        var fate = BondCatalog.Get(Bond.Fate);
        var oracle = BondCatalog.Get(Bond.Oracle);
        Check(fate.Basic == 3 && fate.Advanced == 6 && oracle.Basic == 5 && oracle.Advanced == 0 &&
            !oracle.UsesLayers && terminal.Ledger.Add(Bond.Oracle, 99) == 0, "Fate uses 3/6 and Oracle has one tier and no layers");
        Check(oracle.Tier(4) == 0 && terminal.Tier(Bond.Oracle) == 1, "Oracle activates from five deck members at combat start");
        var roster = terminal.OracleCards;
        Check(roster.Count == 5 && roster.Select(card => card.Id).Distinct().Count() == 5 &&
            roster.Count(card => card.Rarity == CardRarity.Common) == 2 && roster.Count(card => card.Rarity == CardRarity.Uncommon) == 3 &&
            !roster.Select(card => card.Id).Intersect(terminal.FateCards.Select(card => card.Id)).Any(), "Oracle samples two Commons and three Uncommons outside Fate");
        var cached = AccessTools.Field(typeof(CovenantTerminal), "_runRosters");
        cached.SetValue(terminal, null);
        Check(terminal.OracleCards.SequenceEqual(roster), "Oracle reproduces the run's seeded roster");

        var combat = player.PlayerCombatState!;
        await Wait(() => NOverlayStack.Instance?.Peek() is NCombatPileCardSelectScreen);
        var selection = (NCombatPileCardSelectScreen)NOverlayStack.Instance!.Peek()!;
        Check(combat.Phase == PlayerTurnPhase.Start && combat.Hand.Cards.Count == 0, "Oracle opens the native combat-pile selector before play");
        var grid = selection.GetNode<NCardGrid>("%CardGrid");
        var confirm = selection.GetNode<NConfirmButton>("%Confirm");
        var desired = grid.CurrentlyDisplayedCardHolders.Where(holder => holder.CardModel != null).TakeLast(7).ToArray();
        Check(desired.Length == 7, "opening offers a draw-pile selection larger than the unmodified hand");
        foreach (var holder in desired)
            grid.EmitSignal(NCardGrid.SignalName.HolderPressed, holder);
        var chosen = desired.Select(holder => holder.CardModel!).ToHashSet();
        confirm.EmitSignal(NClickableControl.SignalName.Released, confirm);
        await Wait(() => combat.Phase == PlayerTurnPhase.Play);
        Check(combat.Hand.Cards.Count == 7 && chosen.SetEquals(combat.Hand.Cards) &&
            !CombatManager.Instance.History.Entries.OfType<CardDrawnEntry>().Any(entry => entry.FromHandDraw),
            "Bag of Preparation makes Oracle put exactly the seven chosen cards in hand in place of the draw");

        var hud = NRun.Instance!.GlobalUi.GetNode<Control>("CovenantRoot");
        var badges = hud.GetNode<Control>("BondBar/Scroller/Items");
        await Wait(() => badges.GetChildCount() >= 2 && badges.GetChild(0).Name == "Oracle");
        var oracleBadge = badges.GetChild<Button>(0);
        Check(badges.GetChild(1).Name == "Fate" &&
            oracleBadge.GetNode<Label>("Badge/BondName").GetThemeColor("font_color") == new Color("f77c62") &&
            oracleBadge.GetNode<Label>("Badge/Caption").Text == "", "active red Oracle is before Fate and has no numeric layer caption");
        oracleBadge.EmitSignal(Button.SignalName.Pressed);
        var ui = NRun.Instance.GlobalUi;
        var page = (NCardLibrary)ui.SubmenuStack.Stack.Peek()!;
        var library = page.GetNode<NCardLibraryGrid>("%CardGrid");
        await Wait(() => library.VisibleCards.Count() == 5);
        Check(library.VisibleCards.Select(card => card.Id).ToHashSet().SetEquals(roster.Select(card => card.Id)), "the Oracle library shows the full run roster");
        ui.SubmenuStack.Stack.Pop();

        var representatives = combat.AllCards.Where(card => card.Id == roster[0].Id).ToArray();
        await CardPileCmd.RemoveFromCombat(representatives);
        Check(terminal.Counts().GetValueOrDefault(Bond.Oracle) == 4 && terminal.Tier(Bond.Oracle) == 1,
            "losing a member during combat does not change the locked activation");
        await CombatProbe.EndTurn(tree);
        Check(CombatManager.Instance.History.Entries.OfType<CardDrawnEntry>().Count(entry => entry.FromHandDraw) == 5 &&
            NOverlayStack.Instance.Peek() is not NCombatPileCardSelectScreen, "turn two draws the normal five cards without reopening Oracle");
        Console.WriteLine("COVENANT_ORACLE_COMPLETE");

        async Task Wait(Func<bool> condition)
        {
            for (var i = 0; i < 200 && !condition(); i++)
                await tree.ToSignal(tree.CreateTimer(.05), SceneTreeTimer.SignalName.Timeout);
            if (!condition()) throw new Exception("Timed out waiting for the native Oracle test.");
        }
    }

    private static void Check(bool passed, string detail)
    {
        if (!passed) throw new Exception(detail);
        Console.WriteLine("COVENANT_ORACLE_PASS " + detail);
    }
}
