using Covenant.Core;
using Covenant.Game;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

internal static partial class MechanismChecks
{
    private static async Task ChainSourceSemantics(Action<bool,string> check)
    {
        foreach(var card in new CardModel[]{ModelDb.Card<MachineLearning>(),ModelDb.Card<Demesne>(),ModelDb.Card<Pagestorm>(),ModelDb.Card<DeathMarch>(),ModelDb.Card<Murder>(),ModelDb.Card<Panache>(),ModelDb.Card<Finisher>(),ModelDb.Card<MakeItSo>()})
            check(CardMembership.Of(card).Contains(Bond.Chain),"An actual delayed/attached draw or explicit sequence payoff supplies Chain: "+card.Id.Entry);
        foreach(var card in new CardModel[]{ModelDb.Card<Jackpot>(),ModelDb.Card<LeadingStrike>(),ModelDb.Card<DrainPower>(),ModelDb.Card<Reave>(),ModelDb.Card<NeowsFury>(),ModelDb.Card<Normality>()})
            check(!CardMembership.Of(card).Contains(Bond.Chain),"A card count without draw or sequence benefit does not supply Chain: "+card.Id.Entry);
        foreach(var card in new CardModel[]{ModelDb.Card<KnifeTrap>(),ModelDb.Card<Midnight>(),ModelDb.Card<PactsEnd>()})
            check(CardMembership.Of(card).Contains(Bond.Ember),"A native exhaust-pile/count payoff supplies Ember: "+card.Id.Entry);
        var f=new Battle();f.Copies<MachineLearning>();var learning=await PowerCmd.Apply<MachineLearningPower>(f.Context,f.Player.Creature,1,f.Player.Creature,null)??throw new Exception("Machine Learning missing");
        check(learning.ModifyHandDraw(f.Player,5)==6,"The native delayed-draw power still increases the ordinary turn draw");
        var attack=f.Add<StrikeIronclad>(PileType.Hand);var skill=f.Add<DefendIronclad>(PileType.Hand);var drawn=f.Add<DefendSilent>(PileType.Draw);
        await f.ResolveCallbacks(attack);await f.ResolveCallbacks(skill);await f.ResolveCallbacks(attack);
        check(drawn.Pile?.Type==PileType.Hand&&f.Player.Creature.Block==3,"An actual alternating sequence receives Chain's draw/block enabled by delayed-draw source cards");
        f=new();f.Copies<Midnight>();var midnight=f.Player.PlayerCombatState!.ExhaustPile.Cards.OfType<Midnight>().First();
        var before=midnight.EnergyCost.GetWithModifiers(CostModifiers.All);
        await CardCmd.Exhaust(f.Context,f.Add<StrikeIronclad>(PileType.Hand));
        check(midnight.EnergyCost.GetWithModifiers(CostModifiers.All)==before-1&&CardMembership.IsPrintedHeavy(midnight),"Native Midnight exhaustion reduces current cost while preserving its expensive base cost");
        await CardCmd.Exhaust(f.Context,f.Add<StrikeIronclad>(PileType.Hand));
        check(f.Terminal.Ledger.Get(Bond.Ember)>0&&f.Player.Creature.Block>0,"The newly recognized exhaust payoff activates native exhaust traits and the Ember effect");
        foreach(var card in new CardModel[]{f.Add<Bombardment>(),f.Add<ForgottenRitual>()})
        {
            CardCmd.RemoveKeyword(card,CardKeyword.Exhaust);
            check(CardMembership.Of(card).Contains(Bond.Ember),"A distinct exhaust payoff remains after its own keyword is removed: "+card.Id.Entry);
        }
        Console.WriteLine("COVENANT_CHAIN_SOURCE_SEMANTICS_OK actual-draw=pass sequence-payoffs=pass cardsvar-exclusions=pass native-machine-learning=pass native-midnight=pass exhaust-pile-support=pass");
    }
}

