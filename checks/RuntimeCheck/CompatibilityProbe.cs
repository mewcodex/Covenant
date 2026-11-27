using Covenant.Core;
using Covenant.Game;
using Godot;
using HarmonyLib;
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
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.addons.mega_text;

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
        async Task<T> Add<T>(PileType pile=PileType.Hand,bool upgraded=false) where T:CardModel
        {
            var card=combat.CreateCard<T>(player);
            if(upgraded)CardCmd.Upgrade(card);
            await CardPileCmd.Add(card,pile,skipVisuals:pile!=PileType.Hand);
            return card;
        }
        Task Play(CardModel card)=>CardCmd.AutoPlay(context,card,card.TargetType==TargetType.AnyEnemy?enemy:null);
        void Check(bool passed,string message)
        {
            if(!passed)throw new Exception(message);
            Console.WriteLine("COVENANT_COMPAT_PASS "+message);
        }

        await Clear();
        await Add<Underworld>(PileType.Exhaust);
        for(var i=0;i<7;i++)await Add<DefendIronclad>(PileType.Draw);
        await Play(await Add<Countdown>());
        var countdown=player.Creature.GetPower<CountdownPower>()!;
        var nextTurn=CombatProbe.EndTurn(tree);
        NCardGrid? countdownChoices=null;
        for(var i=0;i<120&&!nextTurn.IsCompleted;i++)
        {
            if(NOverlayStack.Instance?.Peek() is NSimpleCardSelectScreen screen&&
                screen.GetNode<NCardGrid>("%CardGrid") is {} grid&&grid.CurrentlyDisplayedCardHolders.Any())
            {
                countdownChoices=grid;
                break;
            }
            await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
        }
        Check(countdownChoices!=null&&state.Phase==PlayerTurnPhase.Start&&state.Hand.Cards.Count==6&&
            combat.HittableEnemies.Sum(target=>target.GetPowerAmount<DoomPower>())>countdown.Amount,
            "Countdown applies Doom and draws before opening a real topdeck selection during turn setup");
        var topdeck=state.Hand.Cards[0];
        countdownChoices!.EmitSignal(NCardGrid.SignalName.HolderPressed,countdownChoices.GetCardHolder(topdeck)!);
        await nextTurn.WaitAsync(TimeSpan.FromSeconds(15));
        Check(state.Phase==PlayerTurnPhase.Play&&state.DrawPile.Cards.FirstOrDefault()==topdeck&&
            NOverlayStack.Instance?.Peek() is not NSimpleCardSelectScreen,
            "one choice topdecks the selected card and lets the native turn enter its play phase");
        await PowerCmd.Remove(countdown);
        await Clear();
        using var selector=RunManager.Instance.CardSelectorStack.UseSelector(player,new FirstChoice());

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
        var recall=await Add<Recall>();
        await Play(recall);
        Check(!CardMembership.Of(recall).Contains(Bond.Foresight)&&terminal.Ledger.Get(Bond.Arrows)==18,
            "Recall grows Arrows on native dislodge and no longer counts toward Foresight");
        await Add<DefendIronclad>(PileType.Draw);await Add<StrikeIronclad>(PileType.Draw);await Add<DefendSilent>(PileType.Draw);
        var trickShot=await Add<TrickShot>(upgraded:true);
        await Play(trickShot);
        Check(CardMembership.Of(trickShot).Contains(Bond.Rotation)&&terminal.Ledger.Get(Bond.Arrows)==24,
            "upgraded Trick Shot grows two Arrows layers per card drawn by its shot and counts toward Rotation");

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

        await Clear();
        await Add<WahTouch>(PileType.Exhaust);await Add<ChoBarbs>(PileType.Exhaust);
        var returned=await Add<DefendIronclad>(PileType.Discard);
        var wahKi=await Add<WahKi>();
        await Play(wahKi);
        // Its two melodies activate the covenant draw, which immediately picks up the reclaimed card.
        Check(returned.Pile?.Type==PileType.Hand&&terminal.Ledger.Get(Bond.Melody)==0&&
            CardMembership.Of(wahKi).Contains(Bond.Foresight)&&!CardMembership.Of(wahKi).Contains(Bond.Rotation),
            "Wah Ki's second melody draws the reclaimed card without growing layers yet, and Wah Ki counts toward Foresight");
        terminal.BeginTurn();
        await Play(returned);
        await CardPileCmd.Add(returned,PileType.Hand,skipVisuals:true);
        await Play(returned);
        Check(terminal.Ledger.Get(Bond.Melody)==6,"Wah Ki grows Melody on the returned card's next play only");

        await Clear();
        await Add<Bludgeon>(PileType.Draw);await Add<DemonForm>(PileType.Draw);
        await Add<Barricade>(PileType.Draw);await Add<WraithForm>(PileType.Draw);
        await Play(await Add<Simpleton>(upgraded:true));
        var zero=await Add<Slice>();var one=await Add<DefendIronclad>();
        await player.Creature.GetPower<SimpletonPower>()!.AfterAutoPostPlayPhaseEntered(context,player);
        Check(zero.Pile?.Type==PileType.Discard&&one.Pile?.Type==PileType.Discard&&terminal.Ledger.Get(Bond.Heavy)==3,
            "upgraded Simpleton plays two cards and grows Heavy by each card's printed cost plus one");

        await Clear();
        await PlayerCmd.SetEnergy(3,player);
        var energyX=await Add<Whirlwind>();
        var starX=await Add<Stardust>();
        var dualCost=await Add<Devastate>();
        var flatCost=await Add<DefendIronclad>();
        (string Energy,string? Stars) CostsOnCard(CardModel model)
        {
            var card=NPlayerHand.Instance!.GetCard(model)!;
            card.UpdateVisuals(PileType.Hand,CardPreviewMode.Normal);
            return (card.GetNode<MegaLabel>("%EnergyLabel").Text,
                card.GetNode<Control>("%StarIcon").Visible?card.GetNode<MegaLabel>("%StarLabel").Text:null);
        }
        Check(CostsOnCard(energyX)==("X",null)&&energyX.EnergyCost.GetAmountToSpend()==3,
            "ordinary energy X displays X with no star badge and spends all available energy");
        var free=(HashSet<CardModel>)AccessTools.Field(typeof(CovenantTerminal),"_creationFree").GetValue(terminal)!;
        free.UnionWith([energyX,starX,dualCost,flatCost]);
        Check(CostsOnCard(energyX)==("X",null)&&energyX.EnergyCost.GetAmountToSpend()==0&&energyX.GetStarCostWithModifiers()<0,
            "Creation's free energy X keeps its printed X and does not acquire a star badge");
        Check(CostsOnCard(starX)==("0","X")&&starX.GetStarCostWithModifiers()==0&&
            CostsOnCard(dualCost)==("0","0")&&CostsOnCard(flatCost)==("0",null),
            "Creation preserves star X, genuine zero star costs, and standard zero energy costs");
        free.Clear();
        Check(CostsOnCard(dualCost)==("1","4")&&CostsOnCard(energyX)==("X",null),
            "refreshing card faces restores the correct fees after the waiver expires");
    }
}
