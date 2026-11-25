using Covenant.Core;
using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

internal static class CompatibilityProbe
{
    private sealed class FirstChoice : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> cards,int min,int max)
            =>Task.FromResult(cards.Take(min));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> cards,IReadOnlyList<CardRewardAlternative> alternatives)=>new();
    }

    internal static async Task Run(SceneTree tree,Player player)
    {
        var state=player.PlayerCombatState!;
        var combat=player.Creature.CombatState!;
        var terminal=CovenantTerminal.For(player)!;
        for(var i=0;i<100&&state.Phase!=PlayerTurnPhase.Play;i++)
            await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
        if(state.Phase!=PlayerTurnPhase.Play)throw new Exception("Compatibility probe never reached the play phase.");
        var context=new ThrowingPlayerChoiceContext();
        using var selector=RunManager.Instance.CardSelectorStack.UseSelector(player,new FirstChoice());
        var enemy=combat.HittableEnemies.First();
        enemy.SetMaxHpInternal(10000);
        enemy.SetCurrentHpInternal(10000);

        async Task Clear()
        {
            await CardPileCmd.RemoveFromCombat(state.AllCards.ToArray());
            foreach(var power in player.Creature.Powers.Where(power=>power is not CovenantAttritionPower).ToArray())await PowerCmd.Remove(power);
            terminal.ResetTransient();
            terminal.Ledger.Layers.Clear();
            terminal.BeginTurn();
        }
        async Task<T> Add<T>(PileType pile=PileType.Hand) where T:CardModel
        {
            var card=combat.CreateCard<T>(player);
            await CardPileCmd.Add(card,pile,skipVisuals:pile!=PileType.Hand);
            return card;
        }
        Task Play(CardModel card)=>CardCmd.AutoPlay(context,card,card.TargetType==TargetType.AnyEnemy?enemy:null);
        void Check(bool passed,string message)
        {
            if(!passed)throw new Exception(message);
            Console.WriteLine("COVENANT_COMPAT_PASS "+message);
        }

        var run=(RunState)player.RunState;
        run.AppendToMapPointHistory(MapPointType.Monster,RoomType.Monster,null);
        terminal.EnemyFloorScaling=true;
        try
        {
            var multiplier=CovenantFloorScaling.CombatMultiplier(terminal);
            var plating=ModelDb.Power<PlatingPower>().GetScaledAmountForMultiplayer(combat,enemy,10,enemy,null);
            Check(multiplier>1&&plating==10*multiplier,"enemy floor scaling applies to the new Plating overload exactly once");
        }
        finally{terminal.EnemyFloorScaling=false;}

        await Clear();
        await Add<HeavyShot>(PileType.Exhaust);
        await Add<QuickShot>(PileType.Exhaust);
        if(state.AvailableArrowCount==0)ArrowCmd.GainArrowSlot(player);
        await Play(await Add<Quietus>());
        await Play(await Add<StrikeIronclad>());
        Check(terminal.Ledger.Get(Bond.Arrows)==0&&player.Creature.HasPower<QuietusPower>(),"Quietus waits for an arrow attack");
        var arrowCount=state.Arrows.Count;
        await Play(await Add<TwinShot>());
        Check(terminal.Ledger.Get(Bond.Arrows)==8&&!player.Creature.HasPower<QuietusPower>()&&state.Arrows.Count==arrowCount,
            "Quietus adds eight layers once for the two-hit Twin Shot and preserves the arrow slot");
        await Play(await Add<ArrowStab>());
        Check(terminal.Ledger.Get(Bond.Arrows)==12&&player.Creature.GetPowerAmount<AimPower>()>=6,"Arrow Stab follows its new Aim effect");

        await Clear();
        await Add<Jest>(PileType.Exhaust);
        await Add<SorrowfulStrike>(PileType.Exhaust);
        await Add<Sadness>();
        await Play(await Add<Growth>());
        Check(terminal.Ledger.Get(Bond.Emotion)==4&&state.Hand.Cards.OfType<Growth>().Count()==1,"Growth turns Sadness into its copy and grows Emotion");
        await Add<Sadness>();await Add<Sadness>();
        await Add<DefendIronclad>(PileType.Draw);await Add<DefendIronclad>(PileType.Draw);
        await Play(await Add<Optimism>());
        Check(terminal.Ledger.Get(Bond.Emotion)==12&&state.ExhaustPile.Cards.OfType<Sadness>().Count()==2,"Optimism grows Emotion once per exhausted Sadness");

        await Clear();
        await Add<Anger>(PileType.Exhaust);await Add<Discovery>(PileType.Exhaust);
        await Add<DualWield>(PileType.Exhaust);await Add<Growth>(PileType.Exhaust);
        await Add<DefendIronclad>();
        await Play(await Add<Florescence>());
        var shot=state.Hand.Cards.OfType<TheShot>().Single();
        Check(terminal.Ledger.Get(Bond.Creation)==6&&shot.Keywords.Contains(CardKeyword.Exhaust)&&
            RevisionDescriptions.Program(shot)?.Rules.Single().Event=="damage_bonus","Florescence grows Creation from the transformation and The Shot retains its new native Exhaust");

        await Clear();
        await Add<WahTouch>(PileType.Exhaust);await Add<ChoBarbs>(PileType.Exhaust);
        await Play(await Add<Tune>());
        var candidate=await Add<DefendIronclad>();
        await Play(await Add<KiWhip>());
        Check(candidate.IsUpgraded&&terminal.Ledger.Get(Bond.Melody)==3,"Tune grows Melody from a native Ki-triggered upgrade");
        var inheritedCandidate=await Add<DefendIronclad>();
        await CovenantMelody.Inherit(context,player,CardTag.Ki);
        Check(inheritedCandidate.IsUpgraded&&terminal.Ledger.Get(Bond.Melody)==6,"inherited Ki also upgrades a card and credits Tune");
    }
}
