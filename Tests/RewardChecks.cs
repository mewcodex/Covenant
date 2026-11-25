using System.Runtime.CompilerServices;
using Covenant.Core;
using Covenant.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;

internal static class RewardChecks
{
    private sealed class Selector:ICardSelector
    {
        internal Func<IReadOnlyList<CardCreationResult>,IReadOnlyList<CardRewardAlternative>,CardRewardSelection> Pick=(cards,_)=>new(){card=cards[0].Card};
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> cards,int min,int max)=>Task.FromResult(cards.Take(min));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> cards,IReadOnlyList<CardRewardAlternative> alternatives)=>Pick(cards,alternatives);
    }
    private sealed class Fixture
    {
        internal Player Player=Player.CreateForNewRun(ModelDb.Character<Ironclad>(),UnlockState.all,1);
        internal RunState Run;
        internal CovenantTerminal Terminal;
        internal PlayerChoiceSynchronizer Sync;
        internal CardSelectorStack Selectors;
        internal Fixture(int profit=0,int foresight=0)
        {
            LocalContext.NetId=1;CombatChecks.CombatActive=false;CombatChecks.Choose=null;
            Run=RunState.CreateForTest([Player],[],[],seed:"covenant-rewards");
            Run.PushRoom(new CombatRoom(ModelDb.Encounter<CultistsNormal>().ToMutable(),Run));
            Run.AppendToMapPointHistory(MapPointType.Monster,RoomType.Monster,ModelDb.Encounter<CultistsNormal>().Id);
            Terminal=CovenantTerminal.For(Player)!;Player.Deck.Clear();
            for(int i=0;i<profit;i++)Player.Deck.AddInternal(Run.CreateCard<HandOfGreed>(Player),-1);
            for(int i=0;i<foresight;i++)Player.Deck.AddInternal(Run.CreateCard<Acrobatics>(Player),-1);
            Sync=new((NetSingleplayerGameService)RuntimeHelpers.GetUninitializedObject(typeof(NetSingleplayerGameService)),Run);
            Selectors=new(Run);
        }
        internal CardReward Reward(CardCreationSource source=CardCreationSource.Encounter)
        {
            var options=new CardCreationOptions([Player.Character.CardPool],source,CardRarityOddsType.Uniform).WithFlags(CardCreationFlags.NoUpgradeRoll);
            var reward=new CardReward(options,3,Player,Sync,Selectors);
            CovenantRewards.Cards(reward).AddRange(new CardModel[]{Run.CreateCard<StrikeIronclad>(Player),Run.CreateCard<DefendIronclad>(Player),Run.CreateCard<Anger>(Player)}.Select(c=>new CardCreationResult(c)));
            return reward;
        }
    }
    internal static async Task Run(Action<bool,string> check)
    {
        var f=new Fixture(profit:2);f.Terminal.Ledger.Add(Bond.Profit,60);
        var reward=f.Reward();var alternatives=CardRewardAlternative.Generate(reward);var exchange=alternatives.Single(a=>a.OptionId=="COVENANT_EXCHANGE");
        check(exchange.Title.GetRawText().Contains("22金币"),"Profit offer displays current layer-scaled gold in a complete sentence");
        var gold=f.Player.Gold;var deck=f.Player.Deck.Cards.Count;
        var selector=new Selector{Pick=(_,alts)=>new(){alternative=alts.Single(a=>a.OptionId=="COVENANT_EXCHANGE")}};
        using(CardSelectorStack.SetTestSelector(selector))check(await reward.SelectUnsynchronized(),"Profit exchange completes the native reward");
        check(f.Player.Gold==gold+22&&f.Player.Deck.Cards.Count==deck,"Basic exchange grants gold without a card");
        await exchange.OnSelect();check(f.Player.Gold==gold+22,"A completed exchange cannot grant gold twice");

        f=new(profit:6,foresight:6);f.Terminal.Ledger.Add(Bond.Profit,60);f.Terminal.Ledger.Add(Bond.Foresight,60);
        reward=f.Reward();gold=f.Player.Gold;deck=f.Player.Deck.Cards.Count;
        var candidate=reward.Cards.First();candidate.UpgradeInternal();CombatChecks.Choose=cards=>[cards[0]];
        selector=new(){Pick=(_,alts)=>new(){alternative=alts.First(a=>a.OptionId=="COVENANT_EXCHANGE")}};
        using(CardSelectorStack.SetTestSelector(selector))check(await reward.SelectUnsynchronized(),"Advanced Profit completes after an optional native card selection");
        check(f.Player.Deck.Cards.Contains(candidate)&&candidate.IsUpgraded&&f.Player.Deck.Cards.Count==deck+1,"Advanced Profit grants the actual candidate with upgrades");
        check(f.Player.Gold==gold+16&&f.Terminal.Ledger.Get(Bond.Profit)==60,"Taking a card halves Profit gold, forgoes growth and does not offer a Foresight purchase");

        f=new(foresight:6);f.Terminal.Ledger.Add(Bond.Foresight,60);f.Player.Gold=100;
        reward=f.Reward();var previous=reward.Cards.ToArray();
        CombatChecks.Choose=_=>throw new Exception("Reroll must not open a lock selection");var reroll=CardRewardAlternative.Generate(reward).Single(a=>a.OptionId=="REROLL");
        await reroll.OnSelect();
        check(reward.Cards.Count()==6,$"Reroll produces native base count plus two extra candidates at 60 layers (actual {reward.Cards.Count()}, tier {f.Terminal.Counts(deck:true).GetValueOrDefault(Bond.Foresight)}, scale {f.Terminal.Scale(Bond.Foresight).B})");
        check(!reward.Cards.Intersect(previous).Any(),"Reroll replaces every candidate without retaining or locking an old card");
        check(!CardRewardAlternative.Generate(reward).Any(a=>a.OptionId=="REROLL"),"The free reroll is consumed once");
        var after=reward.Cards.ToArray();await reroll.OnSelect();check(after.SequenceEqual(reward.Cards),"Reusing an old reroll action cannot reroll twice");
        var claimed=reward.Cards.First();var bought=reward.Cards.Skip(1).First();CombatChecks.Choose=cards=>[bought];deck=f.Player.Deck.Cards.Count;gold=f.Player.Gold;
        selector=new();using(CardSelectorStack.SetTestSelector(selector))check(await reward.SelectUnsynchronized(),"Normal claim completes the native reward");
        check(f.Player.Deck.Cards.Contains(claimed)&&f.Player.Deck.Cards.Contains(bought)&&f.Player.Deck.Cards.Count==deck+2,"Normal claim may purchase exactly one different remaining candidate");
        check(f.Player.Gold==gold-34,"Foresight purchase charges the current layer-scaled price");
        await f.Terminal.AfterRewardTaken(f.Player,reward);check(f.Player.Gold==gold-34,"Repeated reward hooks cannot offer another purchase");

        f=new(foresight:6);f.Player.Gold=0;reward=f.Reward();CombatChecks.Choose=_=>throw new Exception("Unaffordable purchase should not open a chooser");
        deck=f.Player.Deck.Cards.Count;using(CardSelectorStack.SetTestSelector(new Selector()))await reward.SelectUnsynchronized();
        check(f.Player.Deck.Cards.Count==deck+1&&f.Player.Gold==0,"Insufficient gold does not offer or grant a purchase");

        f=new(foresight:2);reward=f.Reward();CombatChecks.Choose=_=>[];
        await CardRewardAlternative.Generate(reward).Last().OnSelect();check(reward.Cards.Count()==3,"Basic zero-layer reroll keeps native candidate count");
        var other=f.Reward(CardCreationSource.Other);check(!CovenantRewards.Eligible(other)&&!CardRewardAlternative.Generate(other).Any(a=>a.OptionId=="REROLL"),"Event and discovery rewards do not receive combat economy effects");
        f.Run.PopCurrentRoom();check(!CovenantRewards.Eligible(f.Reward()),"Encounter-like rewards outside a combat room are excluded");

        f=new(profit:2,foresight:2);f.Player.AddRelicInternal(ModelDb.Relic<PaelsWing>().ToMutable(),silent:true);reward=f.Reward();
        alternatives=CardRewardAlternative.Generate(reward);
        check(alternatives.Select(a=>a.OptionId).SequenceEqual(new[]{"Skip","SACRIFICE","COVENANT_EXCHANGE","REROLL"}),"Skip, Profit, native sacrifice and Foresight coexist with stable indices");
        CombatChecks.Choose=_=>[];await alternatives.Last().OnSelect();
        check(CardRewardAlternative.Generate(reward).Select(a=>a.OptionId).SequenceEqual(new[]{"Skip","SACRIFICE","COVENANT_EXCHANGE"}),"Removing consumed reroll preserves all remaining alternative indices");

        f=new(profit:1,foresight:1);reward=f.Reward();
        var combat=((CombatRoom)f.Run.CurrentRoom!).CombatState;combat.AddPlayer(f.Player);f.Player.ResetCombatState();
        for(int i=0;i<8;i++){var card=combat.CreateCard<HandOfGreed>(f.Player);f.Player.PlayerCombatState!.ExhaustPile.AddInternal(card,0);}
        check(CardRewardAlternative.Generate(reward).Single().AfterSelected==MegaCrit.Sts2.Core.Entities.Rewards.PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward,"Combat-generated cards cannot activate a post-combat economy tier");
        f=new();f.Player.Deck.AddInternal(f.Run.CreateCard<Finesse>(f.Player),-1);f.Player.Deck.AddInternal(f.Run.CreateCard<FlashOfSteel>(f.Player),-1);
        check(CardRewardAlternative.Generate(f.Reward()).Any(a=>a.OptionId=="COVENANT_EXCHANGE"),"Two purchased colorless components make basic Profit accessible without rare gold cards");
        check(CardMembership.Definitions[nameof(Finesse)].Trait.Bond!=Bond.Profit&&CardMembership.Definitions[nameof(HandOfGreed)].Trait.Bond==Bond.Profit,"Colorless investment membership preserves each card's actual flow trait");
        f=new(profit:4);CombatChecks.Choose=_=>[];gold=f.Player.Gold;
        for(int i=0;i<5;i++)await CardRewardAlternative.Generate(f.Reward()).Single(a=>a.OptionId=="COVENANT_EXCHANGE").OnSelect();
        check(f.Terminal.Ledger.Get(Bond.Profit)==10&&f.Player.Gold==gold+75,"Declining the optional card earns full gold and two Profit layers per distinct exchange");
        check(CardRewardAlternative.Generate(f.Reward()).Single(a=>a.OptionId=="COVENANT_EXCHANGE").Title.GetRawText().Contains("18金币"),"Profit's next offer visibly grows from repeated use");
        f=new(foresight:2);
        for(int i=0;i<5;i++){reward=f.Reward();await CardRewardAlternative.Generate(reward).Single(a=>a.OptionId=="REROLL").OnSelect();}
        check(f.Terminal.Ledger.Get(Bond.Foresight)==10&&reward.Cards.Count()==3,"Five independent rerolls earn ten layers without prematurely adding candidates");
        f.Terminal.Ledger.Add(Bond.Foresight,20);reward=f.Reward();await CardRewardAlternative.Generate(reward).Single(a=>a.OptionId=="REROLL").OnSelect();
        check(reward.Cards.Count()==4&&f.Terminal.Ledger.Get(Bond.Foresight)==32,"Foresight at thirty layers adds one candidate and then earns its next two layers");
        f=new(foresight:2);f.Terminal.Ledger.Add(Bond.Foresight,20);reward=f.Reward();
        var driftwood=(Driftwood)ModelDb.Relic<Driftwood>().ToMutable();f.Player.AddRelicInternal(driftwood,silent:true);
        driftwood.TryModifyRewardsLate(f.Player,[reward],f.Run.CurrentRoom);
        var combined=CardRewardAlternative.Generate(reward).Single(a=>a.OptionId=="REROLL");await combined.OnSelect();
        check(!reward.CanReroll&&reward.Cards.Count()==3&&f.Terminal.Ledger.Get(Bond.Foresight)==20,"Native Driftwood reroll consumes its own charge and grants no covenant growth");
        check(CardRewardAlternative.Generate(reward).Any(a=>a.OptionId=="REROLL"),"Foresight remains available after the native reroll");
        await combined.OnSelect();
        check(reward.Cards.Count()==4&&f.Terminal.Ledger.Get(Bond.Foresight)==22&&!CardRewardAlternative.Generate(reward).Any(a=>a.OptionId=="REROLL"),"The same native selection-loop action can consume Foresight's independent second reroll");
        var final=reward.Cards.ToArray();await combined.OnSelect();check(final.SequenceEqual(reward.Cards),"Both exhausted reroll charges leave no inert repeat action");
        driftwood.TryModifyRewardsLate(f.Player,[reward],f.Run.CurrentRoom);await combined.OnSelect();
        check(!reward.CanReroll&&f.Terminal.Ledger.Get(Bond.Foresight)==22,"A later native charge works even after the covenant charge was spent");
        CombatChecks.Choose=null;CombatChecks.CombatActive=true;
        Console.WriteLine("COVENANT_REWARDS_OK native-completion=pass gold=pass full-reroll=pass purchase=pass native-sacrifice=pass permanent-deck=pass");
    }
}
