using Covenant.Core;
using Covenant.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

/// <summary>Catalog-wide dispatch coverage; full native card plays are checked separately in the engine probe.</summary>
internal static class TraitChecks
{
    internal static async Task Run(Action<bool,string> check)
    {
        CombatChecks.CombatActive=true;
        var player=Player.CreateForNewRun(ModelDb.Character<Ironclad>(),UnlockState.all,6);
        var run=RunState.CreateForTest([player],[],[],seed:"covenant-all-card-traits");
        var state=new CombatState(ModelDb.Encounter<CultistsNormal>().ToMutable(),run);state.AddPlayer(player);player.ResetCombatState();
        var terminal=CovenantTerminal.For(player)!;
        var natives=typeof(CardModel).Assembly.GetTypes().Where(t=>CardMembership.Definitions.ContainsKey(t.Name)).ToDictionary(t=>t.Name,t=>ModelDb.GetById<CardModel>(ModelDb.GetId(t)));
        CardModel Add(CardModel native,bool upgraded=false,bool place=true)
        {
            var original=run.CreateCard(native,player);player.Deck.AddInternal(original,-1);
            var card=state.CreateCard(native,player);card.DeckVersion=original;
            if(upgraded)card.UpgradeInternal();if(place)player.PlayerCombatState!.ExhaustPile.AddInternal(card,-1);return card;
        }
        var cards=CardMembership.Definitions.Values.ToDictionary(d=>d.ClassName,d=>Add(natives[d.ClassName],place:false));
        var fire=AccessTools.Method(typeof(CovenantTerminal),"FireTraits");
        Task Fire(CardModel card,string trigger)=>(Task)fire.Invoke(terminal,[trigger,card])!;
        var dispatched=0;var upgraded=0;
        foreach(var pair in cards)
        {
            var card=pair.Value;var trait=CardMembership.Definitions[pair.Key].Trait;
            // Keep a realistic small pile for each card. Native retention queries consult all combat listeners;
            // putting all 717 definitions in one pile makes an unrelated quadratic stress test of those hooks.
            foreach(var pile in player.PlayerCombatState!.AllPiles)pile.Clear();
            player.PlayerCombatState.ExhaustPile.AddInternal(card,-1);
            var bond=BondCatalog.Get(trait.Bond);
            void ActivateTarget()
            {
                var attempts=0;
                while(terminal.Counts().GetValueOrDefault(trait.Bond)<bond.Basic)
                {
                    if(++attempts>bond.Basic+2)throw new Exception("Cannot activate trait target "+pair.Key+" "+trait.Bond);
                    if(trait.Bond==Bond.Solitude)
                    {
                        var unique=cards.Values.First(c=>c.Pile==null&&CardMembership.Of(c).Contains(Bond.Solitude));
                        player.PlayerCombatState.ExhaustPile.AddInternal(unique,-1);
                    }
                    else
                    {
                        var source=trait.Bond==Bond.Mastery?natives[nameof(StrikeIronclad)]:natives.Values.First(c=>CardMembership.Of(c).Contains(trait.Bond));
                        Add(source,trait.Bond==Bond.Mastery);
                    }
                }
            }
            ActivateTarget();
            terminal.ResetTransient();terminal.Ledger.ClearCombatUses();
            var before=terminal.Ledger.Get(trait.Bond);
            await Fire(card,"unrelated-event");check(terminal.Ledger.Get(trait.Bond)==before,"Unrelated events do not fire "+pair.Key);
            var events=trait.Limit>0?trait.Limit:trait.Every*5;
            for(int i=0;i<events;i++)await Fire(card,trait.Trigger);
            var awards=events/trait.Every;
            check(terminal.Ledger.Get(trait.Bond)==before+trait.Amount*awards,"Declared cadence grants this card's own layer effect "+pair.Key);
            await Fire(card,trait.Trigger);
            var next=trait.Limit>0?awards:(events+1)/trait.Every;
            check(terminal.Ledger.Get(trait.Bond)==before+trait.Amount*next,"Growth continues past the third trigger unless the trait is a victory event "+pair.Key);
            check(trait.Limit>0?Presentation.TraitText(card).Contains($"（{trait.Limit}/{trait.Limit}）"):!Presentation.TraitText(card).Contains("至多"),"Visible cadence does not invent a battle cap "+pair.Key);
            check(Presentation.TraitText(card).Contains(BondCatalog.Get(trait.Bond).Name),"Visible card trait names its actual effect target "+pair.Key);
            dispatched++;
            if(card.IsUpgradable)
            {
                terminal.ResetTransient();terminal.Ledger.ClearCombatUses();card.UpgradeInternal();
                // A native upgrade may remove a keyword and legitimately deactivate the covenant.
                // Restore an independent source before testing the upgraded trait's amount.
                ActivateTarget();before=terminal.Ledger.Get(trait.Bond);
                for(int i=0;i<trait.Every;i++)await Fire(card,trait.Trigger);
                check(terminal.Ledger.Get(trait.Bond)==before+trait.Amount+trait.UpgradeAmount,"Upgraded trait amount matches gameplay "+pair.Key);
                upgraded++;
            }
        }
        check(dispatched==717,"Every catalog card has dispatch coverage");
        Console.WriteLine($"COVENANT_ALL_CARD_TRAITS_OK cards={dispatched} upgraded={upgraded} trigger-match=pass continuous-growth=pass visible-text=pass");
    }
}
