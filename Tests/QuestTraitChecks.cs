using Covenant.Core;
using Covenant.Game;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Rooms;

internal static partial class MechanismChecks
{
    private static async Task QuestTraits(Action<bool,string> check)
    {
        var f=new Battle();var quests=new List<CardModel>();
        foreach(var native in new CardModel[]{ModelDb.Card<ByrdonisEgg>(),ModelDb.Card<Dowsing>(),ModelDb.Card<LanternKey>(),ModelDb.Card<SpoilsMap>()})
        {
            var card=f.Run.CreateCard(native,f.Player);f.Player.Deck.AddInternal(card,-1);quests.Add(card);
            check(CardMembership.Definition(card)!.Trait is {Trigger:"victory",Limit:1},"An unplayable quest has a reachable once-per-victory trait: "+card.Id.Entry);
            check(Presentation.TraitText(card).StartsWith("战斗胜利时"),"The quest tooltip describes its real victory trigger: "+card.Id.Entry);
        }
        check(CardMembership.Counts(f.Player).GetValueOrDefault(Bond.Encounter)==0&&CardMembership.Counts(f.Player,deck:true).GetValueOrDefault(Bond.Encounter)==4,"Deck-only quests do not invent combat-pile components but count at settlement");
        var room=new CombatRoom(f.State);await f.Terminal.AfterCombatEnd(room);await f.Terminal.AfterCombatVictory(room);
        check(f.Terminal.Ledger.Get(Bond.Encounter)==4&&quests.All(q=>f.Terminal.TraitUses(q)==1),"Native victory callback dispatches all four permanent-deck quest traits once even with a stale active combat fixture");
        await f.Terminal.AfterCombatVictory(room);
        check(f.Terminal.Ledger.Get(Bond.Encounter)==4,"Repeated victory notification cannot duplicate quest growth");
        await f.Terminal.BeforeCombatStart();await f.Terminal.AfterCombatEnd(room);await f.Terminal.AfterCombatVictory(room);
        check(f.Terminal.Ledger.Get(Bond.Encounter)==8,"The next battle resets the per-quest victory allowance while preserving permanent layers");
        foreach(var quest in quests.Skip(1))f.Player.Deck.RemoveInternal(quest);
        await f.Terminal.BeforeCombatStart();await f.Terminal.AfterCombatEnd(room);await f.Terminal.AfterCombatVictory(room);
        check(f.Terminal.Ledger.Get(Bond.Encounter)==8&&f.Terminal.TraitUses(quests[0])==0,"An inactive permanent-deck covenant receives no quest growth or successful-use counter");
        Console.WriteLine("COVENANT_QUEST_TRAITS_OK unplayable-reachable=pass actual-victory-hook=pass settlement-deck-counts=pass per-card-once=pass next-combat-reset=pass");
    }
}

