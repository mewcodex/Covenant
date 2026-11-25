using Covenant.Core;
using Covenant.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

internal static partial class MechanismChecks
{
    private sealed class MandatoryHandChoices:ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> cards,int min,int max)=>Task.FromResult(cards.Take(min));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> cards,IReadOnlyList<CardRewardAlternative> alternatives)=>new();
    }
    private static IDisposable NativeHandChoices(Battle f)
    {
        var stack=new CardSelectorStack(f.Run);
        AccessTools.Property(typeof(RunManager),nameof(RunManager.CardSelectorStack)).SetValue(RunManager.Instance,stack);
        return stack.UseSelector(f.Player,new MandatoryHandChoices());
    }
    private static async Task ExhaustDiscardZeroSemantics(Action<bool,string> check)
    {
        var f=new Battle();
        var secret=f.Add<SecretTechnique>();
        check(CardMembership.Of(secret).Contains(Bond.Ember),"A native self-exhausting card counts Ember before upgrade");
        secret.UpgradeInternal();
        check(!CardMembership.Of(secret).Contains(Bond.Ember),"Removing Exhaust through the native upgrade removes the self-only Ember membership");
        CardCmd.ApplyKeyword(secret,CardKeyword.Exhaust);
        check(CardMembership.Of(secret).Contains(Bond.Ember),"A native keyword grant restores the actual Ember membership");
        CardCmd.RemoveKeyword(secret,CardKeyword.Exhaust);
        check(!CardMembership.Of(secret).Contains(Bond.Ember),"Removing the actual Exhaust keyword removes the membership again");
        foreach(var source in new CardModel[]{ModelDb.Card<SoulStorm>(),ModelDb.Card<Havoc>(),ModelDb.Card<Tyranny>(),ModelDb.Card<DrumOfBattle>(),ModelDb.Card<TheShot>()})
            check(CardMembership.Of(source).Contains(Bond.Ember),"An explicit exhaust source or payoff counts Ember without its own keyword: "+source.Id.Entry);
        var fiend=f.Add<FiendFire>();CardCmd.RemoveKeyword(fiend,CardKeyword.Exhaust);
        check(CardMembership.Of(fiend).Contains(Bond.Ember),"Fiend Fire remains an exhaust enabler after its own Exhaust keyword is removed");

        var sly=f.Add<DefendIronclad>();
        check(!CardMembership.Of(sly).Contains(Bond.Sleight),"An ordinary block card has no discard synergy membership");
        CardCmd.ApplySingleTurnSly(sly);
        check(CardMembership.Of(sly).Contains(Bond.Sleight),"The native single-turn Sly grant contributes to Sleight");
        sly.EndOfTurnCleanup();
        check(!CardMembership.Of(sly).Contains(Bond.Sleight),"Single-turn Sly membership expires in native cleanup");
        CardCmd.ApplyKeyword(sly,CardKeyword.Sly);
        check(CardMembership.Of(sly).Contains(Bond.Sleight),"An ongoing Sly keyword also contributes to Sleight");
        CardCmd.RemoveKeyword(sly,CardKeyword.Sly);
        check(!CardMembership.Of(sly).Contains(Bond.Sleight),"Removing ongoing Sly removes the actual membership");
        check(CardMembership.Of(ModelDb.Card<ToolsOfTheTrade>()).Contains(Bond.Sleight),"An attached turn-start discard ability counts Sleight");

        f=new();f.Copies<ToolsOfTheTrade>();f.Terminal.Ledger.Add(Bond.Sleight,60);
        var material=f.Add<StrikeIronclad>(PileType.Hand);
        var tools=await PowerCmd.Apply<ToolsOfTheTradePower>(f.Context,f.Player.Creature,1,f.Player.Creature,null)??throw new Exception("Tools missing");
        check(tools.ModifyHandDraw(f.Player,5)==6,"Native Tools of the Trade still increases the normal turn draw");
        using(NativeHandChoices(f))await tools.AfterPlayerTurnStart(f.Context,f.Player);
        check(material.Pile?.Type==PileType.Discard&&f.Player.Creature.Block==5,"Native Tools turn-start discard activates scaled Sleight while optional cycling may be declined");

        f=new();f.Copies<Tyranny>(8);f.Terminal.Ledger.Add(Bond.Ember,60);
        material=f.Add<StrikeIronclad>(PileType.Hand);var energy=f.Player.PlayerCombatState!.Energy;
        var tyranny=await PowerCmd.Apply<TyrannyPower>(f.Context,f.Player.Creature,1,f.Player.Creature,null)??throw new Exception("Tyranny missing");
        using(NativeHandChoices(f))await tyranny.AfterPlayerTurnStart(f.Context,f.Player);
        check(material.Pile?.Type==PileType.Exhaust&&f.Player.Creature.Block==9&&f.Player.PlayerCombatState.Energy==energy,"Native Tyranny's first exhaust grants Ember's block without refunding energy early");
        for(int i=0;i<2;i++)await CardCmd.Exhaust(f.Context,f.Add<StrikeIronclad>(PileType.Hand));
        check(f.Player.Creature.Block==17&&f.Player.PlayerCombatState.Energy==energy+1,"The third real exhaust grants Ember's energy exactly once");

        foreach(var source in new CardModel[]{ModelDb.Card<AllForOne>(),ModelDb.Card<Feral>(),ModelDb.Card<Jackpot>(),ModelDb.Card<OneForAll>(),ModelDb.Card<Scrape>()})
            check(CardMembership.Of(source).Contains(Bond.Zero)&&!CardMembership.IsPrintedZero(source),"A zero-cost support counts without being treated as a printed-zero play: "+source.Id.Entry);
        f=new();f.Copies<AllForOne>();
        await f.ResolveCallbacks(f.Player.PlayerCombatState!.ExhaustPile.Cards.OfType<AllForOne>().First(),2);
        check(f.Player.Creature.Block==0&&f.Terminal.Ledger.Get(Bond.Zero)==0,"A positive-cost Zero support play does not trigger zero-play rewards or traits");
        await f.ResolveCallbacks(f.Add<Claw>(PileType.Hand),0);
        await f.ResolveCallbacks(f.Add<Claw>(PileType.Hand),0);await f.ResolveCallbacks(f.Add<Claw>(PileType.Hand),0);
        check(f.Player.Creature.Block>0&&f.Terminal.Ledger.Get(Bond.Zero)>0,"A real printed-zero attack receives the covenant enabled by its support cards");
        var discounted=f.Add<StrikeIronclad>();discounted.EnergyCost.SetThisTurn(0);
        check(!CardMembership.IsPrintedZero(discounted)&&!CardMembership.Of(discounted).Contains(Bond.Zero),"An ordinary temporary discount does not change printed-zero eligibility");
        var upgraded=f.Add<Havoc>();upgraded.UpgradeInternal();
        check(CardMembership.IsPrintedZero(upgraded)&&CardMembership.Of(upgraded).Contains(Bond.Zero),"A native upgrade to base zero cost does change zero eligibility");
        CombatChecks.Choose=null;
        Console.WriteLine("COVENANT_EXHAUST_DISCARD_ZERO_OK native-keyword-changes=pass tools-turn-start=pass tyranny-turn-start=pass zero-support=pass printed-zero-boundary=pass");
    }
}

