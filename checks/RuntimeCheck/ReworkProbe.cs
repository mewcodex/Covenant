using Covenant.Core;
using Covenant.Game;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.ValueProps;
using VoidCard=MegaCrit.Sts2.Core.Models.Cards.Void;

internal static class ReworkProbe
{
    private static bool ForceRoll(ref bool __result){__result=true;return false;}

    internal static async Task RunRitualLatch(SceneTree tree)
    {
        CardModel[] powers=[ModelDb.Card<Inflame>(),ModelDb.Card<Barricade>(),ModelDb.Card<Footwork>(),ModelDb.Card<DemonForm>(),
            ModelDb.Card<ToolsOfTheTrade>(),ModelDb.Card<MachineLearning>(),ModelDb.Card<EchoForm>(),ModelDb.Card<WraithForm>()];
        var ritual=BondCatalog.Get(Bond.Ritual);
        int? openingTier=null;
        void ObserveOpening(CombatState combat)
        {
            var openingPlayer=combat.Players.Single();
            openingTier=CovenantTerminal.For(openingPlayer)!.TierAtCount(Bond.Ritual,0);
        }
        CombatManager.Instance.CombatBegan+=ObserveOpening;
        try
        {
            await CombatProbe.Prepare(tree,beforeCombat:run=>
            {
                var runPlayer=run.Players.Single();
                foreach(var power in powers.Take(ritual.Basic))runPlayer.Deck.AddInternal(run.CreateCard(power,runPlayer),silent:true);
            });
            for(var i=0;i<100&&openingTier==null;i++)
                await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
        }
        finally{CombatManager.Instance.CombatBegan-=ObserveOpening;}
        var player=RunManager.Instance.DebugOnlyGetState()!.Players.Single();
        var terminal=CovenantTerminal.For(player)!;
        var state=player.PlayerCombatState!;
        var combat=player.Creature.CombatState!;
        for(var i=0;i<100&&state.Phase!=PlayerTurnPhase.Play;i++)
            await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
        void Check(bool passed,string description)
        {
            if(!passed)throw new Exception(description);
            Console.WriteLine("COVENANT_RITUAL_PASS "+description);
        }
        Check(state.Phase==PlayerTurnPhase.Play&&openingTier==1&&terminal.Tier(Bond.Ritual)==1,
            $"the real combat start latches Ritual I before drawing or playing cards (opening tier={openingTier})");
        var openingPowers=state.AllCards.Where(card=>card.Type==CardType.Power).ToArray();
        await CardPileCmd.RemoveFromCombat(openingPowers);
        Check(terminal.Counts().GetValueOrDefault(Bond.Ritual)==0&&terminal.Tier(Bond.Ritual)==1,
            "the opening tier remains active after all starting Powers leave combat");
        var newPowers=powers.Take(ritual.Advanced).Select(power=>combat.CreateCard(power,player)).ToArray();
        await CardPileCmd.Add(newPowers,PileType.Draw,skipVisuals:true);
        Check(terminal.Counts().GetValueOrDefault(Bond.Ritual)==ritual.Advanced&&terminal.Tier(Bond.Ritual)==2,
            "new Powers can unlock Ritual II in the same battle");
        await CardPileCmd.RemoveFromCombat(newPowers);
        Check(terminal.Counts().GetValueOrDefault(Bond.Ritual)==0&&terminal.Tier(Bond.Ritual)==2,
            "the mid-battle tier also latches after the Powers leave combat");
        foreach(var enemy in combat.HittableEnemies)
            if(enemy.GetPower<StockPower>() is {} stock)await PowerCmd.Remove(stock);
        await CreatureCmd.Kill(combat.HittableEnemies.ToArray(),force:true);
        if(CombatManager.Instance.IsInProgress)await CombatManager.Instance.CheckWinCondition();
        Check(!CombatManager.Instance.IsInProgress&&terminal.Tier(Bond.Ritual)==1,
            "battle settlement returns to the permanent deck's Ritual I");
        Console.WriteLine("COVENANT_RITUAL_LATCH_COMPLETE");
    }

    internal static async Task RunGrowthTriggers(SceneTree tree,Player player)
    {
        var combat=player.Creature.CombatState!;
        var terminal=CovenantTerminal.For(player)!;
        var context=new ThrowingPlayerChoiceContext();
        for(var i=0;i<100&&player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++)
            await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
        if(player.PlayerCombatState is not {Phase:PlayerTurnPhase.Play} state)
            throw new Exception("Growth probe did not reach a playable turn.");
        await CardPileCmd.RemoveFromCombat(state.AllCards.ToArray());

        async Task<T> Add<T>(PileType pile,bool upgraded=false) where T:CardModel
        {
            var card=combat.CreateCard<T>(player);
            if(upgraded)CardCmd.Upgrade(card);
            await CardPileCmd.Add(card,pile,skipVisuals:true);
            return card;
        }
        await Add<Defile>(PileType.Exhaust);
        await Add<Defy>(PileType.Exhaust);
        await Add<Parse>(PileType.Exhaust);
        await Add<KiRest>(PileType.Exhaust);
        await Add<WahTouch>(PileType.Exhaust);
        await Add<ChoBarbs>(PileType.Exhaust);
        await CardCmd.AutoPlay(context,await Add<MachineLearning>(PileType.Hand),null);
        await CardCmd.AutoPlay(context,await Add<Demesne>(PileType.Hand),null);
        await CardCmd.AutoPlay(context,await Add<Demesne>(PileType.Hand,upgraded:true),null);

        foreach(var upgraded in new[]{false,true})
        {
            await CardPileCmd.RemoveFromCombat(state.Hand.Cards.Concat(state.DrawPile.Cards).Concat(state.DiscardPile.Cards).ToArray());
            await CardCmd.AutoPlay(context,await Add<KiWhip>(PileType.Hand,upgraded),combat.HittableEnemies.First());
            // Include a matching draw among normal draws and another in Machine Learning's slot.
            await Add<Defy>(PileType.Draw);
            for(var i=0;i<4;i++)await Add<StrikeIronclad>(PileType.Draw);
            await Add<Defy>(PileType.Draw);
            if(upgraded)await Add<StrikeIronclad>(PileType.Draw);
            await Add<Defy>(PileType.Draw);
            if(!upgraded)await Add<StrikeIronclad>(PileType.Draw);

            if(terminal.Tier(Bond.Ethereal)==0||terminal.Tier(Bond.Melody)==0)
                throw new Exception("Growth probe did not activate both covenants.");
            var ethereal=terminal.Ledger.Get(Bond.Ethereal);
            var melody=terminal.Ledger.Get(Bond.Melody);
            await CombatProbe.EndTurn(tree);
            var etherealGrowth=terminal.Ledger.Get(Bond.Ethereal)-ethereal;
            var melodyGrowth=terminal.Ledger.Get(Bond.Melody)-melody;
            if(state.Hand.Cards.Count!=8||etherealGrowth!=(upgraded?4:3)||melodyGrowth!=(upgraded?8:6))
                throw new Exception($"Turn-start growth mismatch (upgraded={upgraded}, hand={state.Hand.Cards.Count}, Ethereal={etherealGrowth}, Melody={melodyGrowth}).");
        }
        Console.WriteLine("COVENANT_GROWTH_TRIGGERS_COMPLETE Demesne awarded 3/4 on its own extra draws, and Ki Whip awarded 6/8 on delayed energy.");
    }

    internal static async Task RunEnvenomPenetration(SceneTree tree,Player player)
    {
        var combat=player.Creature.CombatState!;
        var context=new ThrowingPlayerChoiceContext();
        for(var i=0;i<100&&player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++)
            await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
        if(player.PlayerCombatState is not {Phase:PlayerTurnPhase.Play} state)
            throw new Exception("Envenom probe did not reach a playable turn.");
        await CardPileCmd.RemoveFromCombat(state.AllCards.ToArray());
        var target=combat.HittableEnemies.First();
        if(target.Block>0)await CreatureCmd.LoseBlock(context,target,target.Block,null);

        var envenom=combat.CreateCard<Envenom>(player);
        await CardPileCmd.Add(envenom,PileType.Hand);
        await CardCmd.AutoPlay(context,envenom,null);
        await PowerCmd.Apply<PoisonPower>(context,target,3,player.Creature,null);
        await CreatureCmd.GainBlock(target,4,ValueProp.Unpowered,null);
        var hp=target.CurrentHp;
        async Task Hit()
        {
            var strike=combat.CreateCard<StrikeIronclad>(player);
            strike.DynamicVars.Damage.BaseValue=5;
            await CardPileCmd.Add(strike,PileType.Hand);
            await CardCmd.AutoPlay(context,strike,target);
        }

        await Hit();
        if(target.Block!=3||target.CurrentHp!=hp-4||target.GetPowerAmount<PoisonPower>()!=4)
            throw new Exception($"Envenom's 5 damage vs 4 Block and 3 Poison left {target.Block} Block, {hp-target.CurrentHp} HP lost, and {target.GetPowerAmount<PoisonPower>()} Poison.");
        await Hit();
        if(target.Block!=0||target.CurrentHp!=hp-6||target.GetPowerAmount<PoisonPower>()!=5)
            throw new Exception("Envenom's base trait should only pierce for one attack each turn.");
        Console.WriteLine("COVENANT_ENVENOM_COMPLETE 5 damage consumed 1 Block and 4 HP, applied 1 Poison, and left the next attack to resolve normally.");
    }

    internal static async Task RunDivineShotChance(Player player)
    {
        var terminal=CovenantTerminal.For(player)!;
        var combat=player.Creature.CombatState!;
        var context=new ThrowingPlayerChoiceContext();
        var attrition=player.Creature.GetPower<CovenantAttritionPower>()!;
        foreach(var (upgraded,locked,roll,chance,layers,marked) in new[]{
            (false,false,47,48,46,true),
            (true,false,48,49,50,true),
            (true,true,44,44,34,false),
        })
        {
            attrition.EnemyTurns=locked?attrition.EnemyTurnLimit/2+1:0;
            terminal.Ledger.Layers[Bond.Arrows]=34;
            var permanent=player.RunState.CreateCard<DivineShot>(player);
            if(upgraded)CardCmd.Upgrade(permanent);
            await CardPileCmd.Add(permanent,PileType.Deck,skipVisuals:true);
            var shot=combat.CreateCard<DivineShot>(player);
            if(upgraded)CardCmd.Upgrade(shot);
            shot.DeckVersion=permanent;
            await CardPileCmd.Add(shot,PileType.Hand);
            var description=RevisionDescriptions.Text(shot);
            if(!description.Contains($"{chance}%"))throw new Exception($"Divine Shot should show {chance}%: {description}");

            var seed=Enumerable.Range(0,4096).First(candidate=>new Rng((ulong)candidate).NextInt(100)==roll);
            terminal.RandomFor(CovenantRandom.CombatRoll).LoadFromSerializable(new Rng((ulong)seed).ToSerializable());
            var enemy=await CreatureCmd.Add<Axebot>(combat);
            enemy.SetCurrentHpInternal(1);
            player.PlayerCombatState!.GainArrow();
            await CardCmd.AutoPlay(context,shot,enemy).WaitAsync(TimeSpan.FromSeconds(16));
            if(!enemy.IsDead||terminal.Ledger.Get(Bond.Arrows)!=layers||terminal.ReworkAmount(shot)!=chance||
                terminal.HasDivineMark(permanent)!=marked)
                throw new Exception($"Divine Shot upgraded={upgraded}, growth locked={locked}, expected {chance}% and {layers} layers.");
            Console.WriteLine($"COVENANT_DIVINE_SHOT_PASS upgraded={upgraded} growthLocked={locked} chance={chance} roll={roll} marked={marked}");
        }
        attrition.EnemyTurns=0;
        terminal.Ledger.Layers[Bond.Arrows]=200;
        var canonical=ModelDb.Card<DivineShot>();
        if(terminal.ReworkAmount(canonical)!=99)throw new Exception("Divine Shot must cap at 201 post-kill layers, not 200.");
        terminal.Ledger.Add(Bond.Arrows,1);
        if(terminal.ReworkAmount(canonical)!=100)throw new Exception("Divine Shot did not cap at 201 layers.");
        Console.WriteLine("COVENANT_DIVINE_SHOT_COMPLETE");
    }

    internal static async Task RunActivation(Player player)
    {
        var terminal=CovenantTerminal.For(player)!;
        var tree=NRun.Instance!.GetTree();
        var combat=player.Creature.CombatState!;
        var state=player.PlayerCombatState!;
        var context=new ThrowingPlayerChoiceContext();
        static void Check(bool condition,string message)
        {
            if(!condition)throw new InvalidOperationException(message);
            Console.WriteLine("COVENANT_TRAIT_ACTIVATION_PASS "+message);
        }
        async Task Clear()
        {
            await CardPileCmd.RemoveFromCombat(state.AllCards.ToArray());
            foreach(var power in player.Creature.Powers.Where(power=>power is not CovenantAttritionPower).ToArray())
                await PowerCmd.Remove(power);
            terminal.ResetTransient();terminal.Ledger.Layers.Clear();terminal.BeginTurn();
        }
        async Task<T> Add<T>(PileType pile=PileType.Hand,bool upgraded=false) where T:CardModel
        {
            var card=combat.CreateCard<T>(player);
            if(upgraded)CardCmd.Upgrade(card);
            await CardPileCmd.Add(card,pile,skipVisuals:pile!=PileType.Hand);
            return card;
        }
        Task Play(CardModel card)=>CardCmd.AutoPlay(context,card,null).WaitAsync(TimeSpan.FromSeconds(20));

        await Clear();
        var sample=await Add<BorrowedTime>(upgraded:true);
        await tree.ToSignal(tree.CreateTimer(.25),SceneTreeTimer.SignalName.Timeout);
        var node=NCard.FindOnTable(sample)??throw new Exception("Borrowed Time has no native hand card");
        var tip=node.Body.GetNode<Control>("CovenantTraitTip");
        var panel=tip.GetNode<Panel>("ToneFrame");
        async Task CheckTone(bool active)
        {
            await tree.ToSignal(tree.CreateTimer(.45),SceneTreeTimer.SignalName.Timeout);
            Check(tip.IsVisibleInTree()&&tip.GetMeta("tone_enabled").AsBool()==active&&
                ((StyleBoxFlat)panel.GetThemeStylebox("panel")).BorderColor==(active?BondBadge.Cyan:BondBadge.Dim),
                $"the native Borrowed Time trait box switches to {(active?"bright":"dim")} at the Heavy threshold");
        }
        terminal.Ledger.Add(Bond.Heavy,999);
        await CheckTone(false);
        var support=new List<CardModel>();
        for(var i=1;i<BondCatalog.Get(Bond.Heavy).Basic;i++)support.Add(await Add<Bludgeon>(PileType.Exhaust));
        await CheckTone(true);
        await CardPileCmd.RemoveFromCombat(support[^1]);
        await CheckTone(false);

        foreach(var (active,upgraded,reserve) in new[]{(false,false,0),(true,false,2),(false,true,0),(true,true,3)})
        {
            await Clear();
            var borrowed=await Add<BorrowedTime>(upgraded:upgraded);
            if(active)
                for(var i=1;i<BondCatalog.Get(Bond.Heavy).Basic;i++)await Add<Bludgeon>(PileType.Exhaust);
            else
                terminal.Ledger.Add(Bond.Heavy,999);
            Check((terminal.Tier(Bond.Heavy)>0)==active && TraitDisplay.Active(borrowed)==active &&
                Presentation.TraitText(borrowed).StartsWith("若【重装】已激活，"),"Borrowed Time displays the activation requirement");
            await PlayerCmd.SetEnergy(0,player);
            await Play(borrowed);
            Check(state.Energy==borrowed.DynamicVars.Energy.IntValue && player.Creature.GetPower<BorrowedTimePower>()!=null,
                "Borrowed Time still grants its native energy and applies its native surcharge");
            Check(player.Creature.Powers.OfType<CovenantEffectPower>().Any(power=>power.EffectText.Contains("当前可保留"))==active,
                "the pending energy status only appears when a played Borrowed Time was activated");
            await PlayerCmd.SetEnergy(5,player);
            await terminal.ReworkEndTurn(context);
            Check(player.Creature.GetPowerAmount<EnergyNextTurnPower>()==reserve&&state.Energy==5-reserve,
                $"Borrowed Time upgraded={upgraded}, Heavy active={active}, reserves exactly {reserve}");
        }
        foreach(var active in new[]{false,true})
        {
            await Clear();
            var storm=await Add<StormOfSteel>();
            if(active)
                for(var i=1;i<BondCatalog.Get(Bond.Sleight).Basic;i++)await Add<StormOfSteel>(PileType.Exhaust);
            var expensive=await Add<Impervious>();
            Check((terminal.Tier(Bond.Sleight)>0)==active&&TraitDisplay.Active(storm)==active,"Storm of Steel matches the current Sleight tier");
            await Play(storm);
            Check(expensive.Pile?.Type==(active?PileType.Exhaust:PileType.Discard),
                "Storm of Steel only plays the highest-cost discard through Sly when its covenant is active");

            await Clear();
            var thinking=await Add<ThinkingAhead>();
            var returned=await Add<Bludgeon>();
            await Add<DefendIronclad>(PileType.Draw);await Add<DefendIronclad>(PileType.Draw);
            if(active)
                for(var i=1;i<BondCatalog.Get(Bond.Rotation).Basic;i++)await Add<Skim>(PileType.Exhaust);
            Check((terminal.Tier(Bond.Rotation)>0)==active,"Thinking Ahead starts at the intended Rotation tier");
            var cost=returned.EnergyCost.GetAmountToSpend();
            await Play(thinking);
            Check(returned.Pile?.Type==PileType.Draw&&returned.EnergyCost.GetAmountToSpend()==cost-(active?1:0),
                "Thinking Ahead returns a card in both cases and only discounts it with Rotation active");
        }

        await Clear();
        var xCard=await Add<Whirlwind>();
        for(var i=1;i<BondCatalog.Get(Bond.Channel).Advanced;i++)await Add<Whirlwind>(PileType.Exhaust);
        for(var i=1;i<BondCatalog.Get(Bond.Heavy).Basic;i++)await Add<Bludgeon>(PileType.Exhaust);
        await Play(await Add<BorrowedTime>(upgraded:true));
        await PlayerCmd.SetEnergy(5,player);
        await terminal.AfterSideTurnEndLate(context,CombatSide.Player,[player.Creature]);
        Check(state.Energy==0&&player.Creature.GetPowerAmount<EnergyNextTurnPower>()==3,
            "Channel II and Borrowed Time+ split five energy into separate reserves at end of turn");
        state.ResetEnergy();
        await Hook.AfterEnergyReset(combat,player);
        Check(state.Energy==state.MaxEnergy+3&&terminal.ReleaseChannelEnergy(xCard)==2&&state.Energy==state.MaxEnergy+5,
            "Channel returns only the two energy that Borrowed Time did not already carry");

        await Clear();
        var iceCream=ModelDb.Relic<IceCream>().ToMutable();
        await RelicCmd.Obtain(iceCream,player);
        for(var i=1;i<BondCatalog.Get(Bond.Heavy).Basic;i++)await Add<Bludgeon>(PileType.Exhaust);
        await Play(await Add<BorrowedTime>(upgraded:true));
        await PlayerCmd.SetEnergy(5,player);
        var turn=state.TurnNumber;
        PlayerCmd.EndTurn(player,canBackOut:false);
        for(var i=0;i<160&&(state.TurnNumber<=turn||player.Creature.GetPower<EnergyNextTurnPower>()!=null);i++)
            await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
        Check(state.TurnNumber>turn&&!Hook.ShouldPlayerResetEnergy(combat,player)&&state.Energy==state.MaxEnergy+5,
            $"a real turn with Ice Cream and Borrowed Time+ retains exactly the original five energy (total {state.Energy})");
        await RelicCmd.Remove(iceCream);
        await Clear();
        Console.WriteLine("COVENANT_TRAIT_ACTIVATION_COMPLETE");
    }

    internal static async Task RunStatusSelection(SceneTree tree,Player player)
    {
        var terminal=CovenantTerminal.For(player)!;
        var combat=player.Creature.CombatState!;
        var state=player.PlayerCombatState!;
        for(var attempt=0;attempt<100&&state.Phase!=PlayerTurnPhase.Play;attempt++)
            await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
        await CardPileCmd.RemoveFromCombat(state.AllCards.ToArray());
        terminal.ResetTransient();terminal.BeginTurn();
        var hand=NCombatRoom.Instance!.Ui.Hand;
        var confirm=hand.GetNode<NConfirmButton>("%SelectModeConfirmButton");
        var header=hand.GetNode<MegaRichTextLabel>("%SelectionHeader");
        static void Check(bool condition,string message)
        {
            if(!condition)throw new InvalidOperationException(message);
            Console.WriteLine("COVENANT_STATUS_SELECTION_PASS "+message);
        }
        async Task<T> Add<T>(PileType pile) where T:CardModel
        {
            var card=combat.CreateCard<T>(player);
            await CardPileCmd.Add(card,pile,skipVisuals:pile!=PileType.Hand);
            return card;
        }
        var other=await Add<Dazed>(PileType.Hand);
        for(var i=0;i<BondCatalog.Get(Bond.Status).Basic-2;i++)await Add<Wound>(PileType.Exhaust);
        async Task<CardModel?> Answer(CardModel offered,bool discard)
        {
            var draw=CardPileCmd.Draw(new BlockingPlayerChoiceContext(),player);
            for(var attempt=0;attempt<80&&!hand.IsInCardSelection&&!draw.IsCompleted;attempt++)
                await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
            Check(hand.CurrentMode==NPlayerHand.Mode.SimpleSelect&&NModalContainer.Instance?.OpenModal==null&&
                NOverlayStack.Instance is {ScreenCount:0},"Status I opens the native combat hand selector without a modal or a card grid");
            var holder=hand.GetCardHolder(offered);
            Check(holder!=null&&holder.IsVisibleInTree()&&hand.GetCardHolder(other)?.Visible==false&&
                header.Text.Contains(Presentation.CardTitle(offered))&&confirm.IsVisibleInTree()&&confirm.IsEnabled,
                "the selector offers only the newly drawn status and shows the native confirmation button");
            if(discard)
            {
                holder!.EmitSignal(NCardHolder.SignalName.Pressed,holder);
                Check(hand.GetNode<NSelectedHandCardContainer>("%SelectedHandCardContainer").Holders
                    .Any(selected=>selected.CardNode?.Model==offered),"clicking the drawn status highlights it in the native selection area");
            }
            confirm.EmitSignal(NClickableControl.SignalName.Released,confirm);
            var card=await draw.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!hand.IsInCardSelection&&hand.GetCardHolder(other)?.Visible==true,"confirming restores the normal hand");
            return card;
        }
        var initialBlock=player.Creature.Block;
        var kept=await Add<Slimed>(PileType.Draw);
        Check(await Answer(kept,false)==kept&&kept.Pile?.Type==PileType.Hand&&player.Creature.Block==initialBlock,
            "confirming with no selection keeps the status and grants no Block");
        var later=await Add<Slimed>(PileType.Draw);
        Check(await CardPileCmd.Draw(new ThrowingPlayerChoiceContext(),player)==later&&later.Pile?.Type==PileType.Hand,
            "skipping the first offer does not prompt again in the same turn");
        terminal.BeginTurn();
        var discarded=await Add<Slimed>(PileType.Draw);
        Check(await Answer(discarded,true)==discarded&&discarded.Pile?.Type==PileType.Discard&&
            kept.Pile?.Type==PileType.Hand&&other.Pile?.Type==PileType.Hand&&
            player.Creature.Block==initialBlock+3+terminal.Scale(Bond.Status).A,
            "selecting and confirming discards only the offered card and grants the expected Block");
        Console.WriteLine("COVENANT_STATUS_SELECTION_COMPLETE");
    }

    internal static async Task RunSettlement(Player player)
    {
        var terminal=CovenantTerminal.For(player)!;
        var combat=player.Creature.CombatState!;
        var state=player.PlayerCombatState!;
        var witness=combat.HittableEnemies.First();
        var context=new ThrowingPlayerChoiceContext();

        void Check(bool condition,string description)
        {
            if(!condition)throw new InvalidOperationException(description);
            Console.WriteLine("COVENANT_SETTLEMENT_PASS "+description);
        }
        async Task<T> Add<T>(PileType pile=PileType.Hand,bool upgraded=false) where T:CardModel
        {
            var card=combat.CreateCard<T>(player);
            if(upgraded)CardCmd.Upgrade(card);
            await CardPileCmd.Add(card,pile,skipVisuals:true);
            return card;
        }
        async Task Clear()
        {
            await CardPileCmd.RemoveFromCombat(state.AllCards.ToArray());
            foreach(var power in player.Creature.Powers.Where(power=>power is not CovenantAttritionPower).ToArray())await PowerCmd.Remove(power);
            terminal.ResetTransient();terminal.Ledger.Layers.Clear();terminal.BeginTurn();
        }
        Task Play(CardModel card,Creature? target=null)=>CardCmd.AutoPlay(context,card,target).WaitAsync(TimeSpan.FromSeconds(18));

        await Clear();
        await Add<ByrdonisEgg>(PileType.Exhaust);
        await Add<ByrdSwoop>(PileType.Exhaust);
        await Add<GodByrd>(PileType.Exhaust);
        Check(CardMembership.Counts(player,respectSuppression:false).GetValueOrDefault(Bond.Solitude)==3,"all three bird cards count for Solitude");
        Check(BondCatalog.Intro(Bond.Mastery)=="所有已升级的牌计入张数。"&&
            BondCatalog.Intro(Bond.Solitude).Contains("在本场战斗中失效")&&!BondCatalog.Get(Bond.Solitude).BasicText(default).Contains("失效")&&
            BondCatalog.Intro(Bond.Ritual)=="此盟约的激活状态会持续到战斗结束。","Mastery, Solitude and Ritual use the shared pre-tier descriptions");

        await Clear();
        var ritual=BondCatalog.Get(Bond.Ritual);
        var firstPower=await Add<Inflame>();
        for(var i=1;i<ritual.Advanced;i++)await Add<Inflame>(PileType.Exhaust);
        await Play(firstPower);
        Check(terminal.Counts().GetValueOrDefault(Bond.Ritual)==ritual.Advanced-1&&terminal.Tier(Bond.Ritual)==2,
            "playing a Power below the advanced threshold preserves Ritual II");
        await CardPileCmd.RemoveFromCombat(state.ExhaustPile.Cards.ToArray());
        Check(terminal.Counts().GetValueOrDefault(Bond.Ritual)==0&&terminal.Tier(Bond.Ritual)==2,
            "Ritual II remains active with no live Power cards");
        terminal.BeginTurn();
        var nextPower=await Add<Inflame>();
        var blockBeforePower=player.Creature.Block;
        Check(nextPower.EnergyCost.GetAmountToSpend()==0,"the preserved Ritual tier carries its first-Power discount into the next turn");
        await Play(nextPower);
        Check(player.Creature.Block==blockBeforePower+4,"Ritual I still triggers on a Power below the basic threshold");

        await Clear();
        await RunStatusSelection(NRun.Instance!.GetTree(),player);

        await Clear();
        var target=await CreatureCmd.Add<Axebot>(combat);target.SetCurrentHpInternal(1);
        if(target.GetPower<StockPower>() is {} targetStock)await PowerCmd.Remove(targetStock);
        var priorDeck=player.Deck.Cards.ToHashSet();
        var perfectRng=terminal.RandomFor(CovenantRandom.PerfectedStrike);
        var randomBefore=perfectRng.ToSerializable().counter;
        var starters=player.Deck.Cards.Where(CovenantTerminal.IsStarterStrike).ToArray();
        var enchantedStarter=starters[0];var upgradedStarter=starters[1];var plainStarter=starters[2];
        CardCmd.Enchant<Sharp>(enchantedStarter,1);
        foreach(var otherStarter in starters.Skip(1).Where(card=>card!=plainStarter))CardCmd.Upgrade(otherStarter);
        await Play(await Add<PerfectedStrike>(),target);
        Check(priorDeck.SetEquals(player.Deck.Cards)&&
            player.Creature.Powers.OfType<CovenantEffectPower>().Any(power=>power.EffectName=="完美打击"&&power.DisplayAmount==1),
            "Perfected Strike queues its slay for the end of combat and displays it without a choice");

        var others=combat.Enemies.Where(enemy=>enemy.IsAlive&&enemy!=witness).ToArray();
        foreach(var other in others)
            if(other.GetPower<StockPower>() is {} otherStock)await PowerCmd.Remove(otherStock);
        if(others.Length>0)await CreatureCmd.Kill(others,true);
        if(witness.GetPower<StockPower>() is {} stock)await PowerCmd.Remove(stock);
        if(witness.Block>0)await CreatureCmd.LoseBlock(context,witness,witness.Block,null);
        witness.SetCurrentHpInternal(1);
        Check(combat.Enemies.Where(enemy=>enemy.IsAlive).SequenceEqual([witness]),"the final test has exactly one living enemy");
        await Play(await Add<PerfectedStrike>(upgraded:true),witness);
        if(CombatManager.Instance.IsInProgress)await CombatManager.Instance.CheckWinCondition();
        var replacements=player.Deck.Cards.Except(priorDeck).ToArray();
        Check(!CombatManager.Instance.IsInProgress&&replacements.Length==2&&
            replacements.All(card=>CardMembership.IsStrikeCard(card)&&!CovenantTerminal.IsStarterStrike(card))&&
            replacements.Count(card=>card.IsUpgraded)==1,
            $"both Perfected Strike slays settle once at victory, including the last enemy and the upgraded reward (active={CombatManager.Instance.IsInProgress}, foe HP={witness.CurrentHp}, replacements={string.Join(',',replacements.Select(card=>card.Id.Entry+(card.IsUpgraded?"+":"")))})");
        Check(priorDeck.Except(player.Deck.Cards).All(CovenantTerminal.IsStarterStrike)&&
            replacements.Select(card=>card.Id).Distinct().Count()==2&&perfectRng.ToSerializable().counter==randomBefore+4,
            "Perfected Strike rolls a starter and a distinct replacement for each slay with its own advancing stream");
        Check(terminal.Tier(Bond.Ritual)==0,"Ritual returns to the deck count after battle");
        Console.WriteLine("COVENANT_SETTLEMENT_COMPLETE");
    }

    internal static async Task Run(SceneTree tree,Player player,bool balanceOnly=false)
    {
        var terminal=CovenantTerminal.For(player)!;
        var combat=player.Creature.CombatState!;
        var state=player.PlayerCombatState!;
        var enemy=combat.HittableEnemies.First();
        var context=new ThrowingPlayerChoiceContext();
        var patches=new Harmony("Covenant.RuntimeCheck.Reworks");
        patches.Patch(AccessTools.Method(typeof(CovenantTerminal),nameof(CovenantTerminal.ReworkRoll)),
            prefix:new HarmonyMethod(typeof(ReworkProbe),nameof(ForceRoll)));
        var checks=0;
        void Check(bool condition,string description)
        {
            if(!condition)throw new InvalidOperationException(description);
            checks++;Console.WriteLine("COVENANT_REWORK_PASS "+description);
        }
        async Task<T> Add<T>(PileType pile=PileType.Hand,bool upgraded=false) where T:MegaCrit.Sts2.Core.Models.CardModel
        {
            var card=combat.CreateCard<T>(player);
            if(upgraded)CardCmd.Upgrade(card);
            await CardPileCmd.Add(card,pile);return card;
        }
        Task Play(MegaCrit.Sts2.Core.Models.CardModel card)=>CardCmd.AutoPlay(context,card,enemy).WaitAsync(TimeSpan.FromSeconds(14));
        async Task ActivateWith<T>(Bond bond) where T:CardModel
        {
            var missing=BondCatalog.Get(bond).Basic-terminal.Counts().GetValueOrDefault(bond);
            for(var i=0;i<missing;i++)await Add<T>(PileType.Exhaust);
            Check(terminal.Tier(bond)>0,$"{bond} activates with the matching fixture");
        }
        async Task Clear()
        {
            await CardPileCmd.RemoveFromCombat(state.AllCards.ToArray());
            if(state.OrbQueue.Capacity>0)OrbCmd.RemoveSlots(player,state.OrbQueue.Capacity);
            state.OrbQueue.Clear();
            foreach(var power in player.Creature.Powers.Where(power=>power is not CovenantAttritionPower).ToArray())await PowerCmd.Remove(power);
            foreach(var power in enemy.Powers.ToArray())await PowerCmd.Remove(power);
            terminal.ResetTransient();terminal.Ledger.Layers.Clear();terminal.BeginTurn();
            enemy.SetMaxHpInternal(1000);enemy.SetCurrentHpInternal(1000);
            player.Creature.SetCurrentHpInternal(player.Creature.MaxHp);
            await PlayerCmd.SetEnergy(6,player);
        }
        try
        {
            terminal.Ledger.Layers.Clear();
            var chances=new (CardModel Card,Bond Bond,int Cap)[]
            {
                (ModelDb.Card<MeteorStrike>(),Bond.Overload,40),(ModelDb.Card<Claw>(),Bond.Zero,50),
                (ModelDb.Card<MakeItSo>(),Bond.Chain,70),(ModelDb.Card<DeathsDoor>(),Bond.Doom,75),
                (ModelDb.Card<CallOfTheVoid>(),Bond.Ethereal,75),(ModelDb.Card<DivineShot>(),Bond.Arrows,100),
                (ModelDb.Card<Ennui>(),Bond.Emotion,75),(ModelDb.Card<Calamity>(),Bond.Creation,40)
            };
            Check(chances.All(item=>terminal.ReworkAmount(item.Card)==(item.Card is DivineShot?33:0)),
                "beneficial rolls start at zero layers, with Divine Shot retaining its 33 percent base chance");
            foreach(var (_,bond,_) in chances)terminal.Ledger.Add(bond,999);
            Check(chances.All(item=>terminal.ReworkAmount(item.Card)==item.Cap),
                "each beneficial roll reaches its own cap at high layers");

            await Clear();
            var reaper=await Add<ReaperForm>();var calamityFactor=await Add<Calamity>();
            terminal.Ledger.Add(Bond.Doom,250);terminal.Ledger.Add(Bond.Creation,250);
            Check(terminal.ReworkAmount(reaper)==41&&terminal.ReworkAmount(calamityFactor)==25,"base Reaper Form and Calamity use the reduced mid-game curves");
            CardCmd.Upgrade(reaper);CardCmd.Upgrade(calamityFactor);
            Check(terminal.ReworkAmount(reaper)==50&&terminal.ReworkAmount(calamityFactor)==31,"upgraded Reaper Form and Calamity reach 50 and 31 percent at 250 layers");
            terminal.Ledger.Add(Bond.Doom,999);terminal.Ledger.Add(Bond.Creation,999);
            Check(terminal.ReworkAmount(reaper)==125&&terminal.ReworkAmount(calamityFactor)==45,"upgraded Reaper Form and Calamity respect the new caps");

            await Clear();
            var voidCard=await Add<VoidCard>(PileType.Exhaust);
            var curse=await Add<AscendersBane>(PileType.Exhaust);
            var unplayable=await Add<StrikeIronclad>(PileType.Exhaust);
            unplayable.AddKeyword(CardKeyword.Ethereal);unplayable.AddKeyword(CardKeyword.Unplayable);
            var eligible=await Add<DefendIronclad>(PileType.Exhaust);eligible.AddKeyword(CardKeyword.Ethereal);
            for(var i=0;i<BondCatalog.Get(Bond.Ethereal).Advanced;i++)(await Add<StrikeIronclad>(PileType.Draw)).AddKeyword(CardKeyword.Ethereal);
            await terminal.AfterPlayerTurnStart(context,player);
            Check(eligible.Pile?.Type==PileType.Hand&&new CardModel[]{voidCard,curse,unplayable}.All(card=>card.Pile?.Type==PileType.Exhaust),
                "Ethereal II returns the playable Ethereal card and leaves status, curse and unplayable cards exhausted");

            await Clear();
            await Play(await Add<Uppercut>());
            Check(enemy.GetPowerAmount<VulnerablePower>()==1,"Uppercut's own Weak does not qualify for its trait");
            await Play(await Add<Uppercut>(upgraded:true));
            Check(enemy.GetPowerAmount<VulnerablePower>()==5,"Uppercut+ adds two Vulnerable against a previously weakened enemy");

            await Clear();
            var hefty=await Add<Sunder>();
            await Play(await Add<Bludgeon>());
            Check(hefty.EnergyCost.GetAmountToSpend()==3,"Bludgeon grants no discount when the enemy has no Block");
            await CreatureCmd.GainBlock(enemy,10,ValueProp.Unpowered,null);
            await Play(await Add<Bludgeon>(upgraded:true));
            Check(hefty.EnergyCost.GetAmountToSpend()==1&&player.Creature.Powers.OfType<CovenantEffectPower>().Any(power=>power.EffectName=="重锤"),
                "Bludgeon+ earns and displays a two-energy discount by breaking Block");
            await Play(await Add<Sunder>());
            Check(hefty.EnergyCost.GetAmountToSpend()==1,"automatic heavy attacks leave Bludgeon's pending discount intact");
            Check(hefty.TryManualPlay(enemy),"a manually played heavy attack can spend the discount");
            for(var i=0;i<100&&hefty.Pile?.Type!=PileType.Discard;i++)await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
            Check(hefty.Pile?.Type==PileType.Discard&&state.Energy==5&&(await Add<Sunder>()).EnergyCost.GetAmountToSpend()==3,
                "the discount changes the real payment and is consumed once");

            await Clear();
            terminal.Ledger.Add(Bond.Doom,250);
            await Play(await Add<ReaperForm>());
            await Play(await Add<StrikeIronclad>());
            Check(enemy.GetPowerAmount<DoomPower>()==8,"Reaper Form applies its 41 percent bonus to a real six-damage attack");
            await PowerCmd.Apply<DoomPower>(context,enemy,3,player.Creature,null);
            Check(enemy.GetPowerAmount<DoomPower>()==11,"Reaper Form leaves Doom from other sources unchanged");

            await Clear();
            var generationSource=await Add<Calamity>(upgraded:true);
            await Play(generationSource);await Play(await Add<StrikeIronclad>());
            Check(state.Hand.Cards.Any(card=>card.Type==CardType.Attack&&card.EnergyCost.GetAmountToSpend()==0&&card.Keywords.Contains(CardKeyword.Exhaust)),
                "Calamity+ still makes a successful attack roll free and Exhaust");
            if(balanceOnly)
            {
                Console.WriteLine("COVENANT_BALANCE_058_COMPLETE checks="+checks);
                return;
            }

            await Clear();
            var demon=await Add<DemonForm>(upgraded:true);var cost=demon.EnergyCost.GetAmountToSpend();
            await ActivateWith<DemonForm>(Bond.Fury);
            await Play(await Add<Bloodletting>());
            Check(demon.EnergyCost.GetAmountToSpend()==cost-2,"Demon Form+ reserves its real two-energy self-loss discount");

            await Clear();
            var meteor=await Add<MeteorStrike>(PileType.Draw);var make=await Add<MakeItSo>(PileType.Draw,upgraded:true);
            await terminal.BeforeHandDrawLate(player,context,combat);
            Check(meteor.Pile?.Type==PileType.Discard&&enemy.CurrentHp==1000-meteor.DynamicVars.Damage.IntValue&&make.Pile?.Type==PileType.Discard,
                "Meteor Strike and Make It So resolve before the opening draw");
            await CardPileCmd.Shuffle(context,player);
            Check(make.Pile?.Type==PileType.Discard&&meteor.Pile?.Type==PileType.Draw,"Make It So can stay out of a native reshuffle");
            await CardPileCmd.Add(make,PileType.Hand,skipVisuals:true);
            var claw=await Add<Claw>();
            for(var i=0;i<4;i++)await Add<Claw>(PileType.Exhaust);
            await Play(make);
            Check(terminal.Ledger.Permanent(Bond.Zero)==2,"Make It So+ adds two layers to the selected card's active Zero covenant");
            await Play(claw);
            Check(claw.Pile?.Type==PileType.Hand,"Claw returns after the native result-pile move");

            await Clear();
            var clash=await Add<Clash>(upgraded:true);var guard=await Add<DefendIronclad>();var attack=await Add<StrikeIronclad>();
            Check(clash.CanPlay(),"Clash can be played with a Skill in hand");
            await Play(clash);
            Check(guard.Pile?.Type==PileType.Discard&&attack.Pile?.Type==PileType.Hand&&terminal.Ledger.Permanent(Bond.Encounter)==2,
                "Clash+ discards only non-attacks and grants two layers per discard");

            await Clear();
            await Play(await Add<PhantomBlades>(upgraded:true));
            terminal.Ledger.Add(Bond.Blades,100);var shiv=await Add<Shiv>();
            await terminal.AfterFlush(context,player,[],[shiv]);
            Check(terminal.ShivsRetainedBonus(shiv)==3,"Phantom Blades+ applies base two plus one at 100 layers to the retained Shiv");

            await Clear();
            await Play(await Add<Hellraiser>(upgraded:true));var starter=await Add<StrikeIronclad>(PileType.Draw);
            await Add<DefendIronclad>(PileType.Draw);await Add<ShrugItOff>(PileType.Draw);
            await CardPileCmd.Draw(context,1,player);
            Check(starter.Pile?.Type==PileType.Exhaust&&state.Hand.Cards.Count==2,
                "Hellraiser+ exhausts the basic Strike it plays and draws two more cards");

            await Clear();
            await Play(await Add<Juggling>(upgraded:true));
            await ActivateWith<Juggling>(Bond.Creation);
            await player.Creature.GetPower<JugglingPower>()!.AfterSideTurnEnd(context,CombatSide.Player,[player.Creature]);
            for(var i=0;i<3;i++)await Play(await Add<StrikeIronclad>());
            var copy=state.Hand.Cards.Single(card=>card.IsClone);
            await Play(copy);
            var nextSkill=await Add<DefendIronclad>();await Play(nextSkill);
            Check(CombatManager.Instance.History.CardPlaysFinished.Count(entry=>entry.CardPlay.Card==nextSkill)==3,
                "Juggling+ grants two actual replays to the next Skill after its copy is played");

            await Clear();
            await Play(await Add<Envenom>());
            await PowerCmd.Apply<PoisonPower>(context,enemy,3,player.Creature,null);
            await CreatureCmd.GainBlock(enemy,20,ValueProp.Unpowered,null);
            var poisonedBlock=enemy.Block;
            await Play(await Add<StrikeIronclad>());
            Check(enemy.CurrentHp==997&&enemy.Block==poisonedBlock-3,"Envenom pierces three Block for the first qualifying attack");

            await Clear();
            await Play(await Add<NoxiousFumes>());
            await PowerCmd.Apply<PoisonPower>(context,enemy,4,player.Creature,null);
            await player.Creature.GetPower<NoxiousFumesPower>()!.AfterSideTurnStart(CombatSide.Player,[player.Creature],combat);
            Check(enemy.CurrentHp==996&&enemy.GetPowerAmount<PoisonPower>()==5,
                "Noxious Fumes ticks and decays the strongest Poison before adding its normal stacks");

            await Clear();
            var storm=await Add<StormOfSteel>();var expensive=await Add<Impervious>();await Add<DefendIronclad>();
            await ActivateWith<StormOfSteel>(Bond.Sleight);
            await Play(storm);
            Check(expensive.Pile?.Type==PileType.Exhaust&&player.Creature.Block>=30,
                "Storm of Steel applies Sly before the native discard and plays its highest-cost card");

            await Clear();
            await OrbCmd.AddSlots(player,2);
            await OrbCmd.Channel<FrostOrb>(context,player);
            await OrbCmd.Channel<PlasmaOrb>(context,player);
            var firstOrb=state.OrbQueue.Orbs[0];
            await Play(await Add<Loop>(upgraded:true));
            var loopBlock=player.Creature.Block;
            await player.Creature.GetPower<LoopPower>()!.AfterPlayerTurnStart(context,player);
            Check(state.OrbQueue.Orbs.Last()==firstOrb&&player.Creature.Block>=loopBlock+6,
                "Loop+ rotates the native orb after all triggers and then triggers one extra passive");

            await Clear();
            await ActivateWith<Bludgeon>(Bond.Heavy);
            await Play(await Add<BorrowedTime>(upgraded:true));await PlayerCmd.SetEnergy(5,player);
            await terminal.ReworkEndTurn(context);
            Check(player.Creature.GetPowerAmount<EnergyNextTurnPower>()==3,"Borrowed Time+ banks two thirds of all remaining energy");

            await Clear();
            await ActivateWith<Dazed>(Bond.Status);
            await Play(await Add<Turbo>(upgraded:true));
            var empty=state.DiscardPile.Cards.OfType<VoidCard>().Single();
            await CardPileCmd.Add(empty,PileType.Draw,skipVisuals:true);await CardPileCmd.Draw(context,1,player);
            await CardCmd.Exhaust(context,empty);
            Check(player.Creature.GetPowerAmount<EnergyNextTurnPower>()==1,"TURBO+ grants one energy next turn when a Void is exhausted");

            await Clear();
            var door=await Add<DeathsDoor>(upgraded:true);var blockBefore=player.Creature.Block;
            await Play(door);
            Check(enemy.GetPowerAmount<DoomPower>()==2&&enemy.GetPowerAmount<WeakPower>()==1&&player.Creature.Block-blockBefore==21,
                "Death's Door+ applies Doom and Weak before checking its three Block gains");

            await Clear();
            await Play(await Add<CallOfTheVoid>());
            await player.Creature.GetPower<CallOfTheVoidPower>()!.BeforeHandDraw(player,context,combat);
            Check(state.Hand.Cards.Count==1&&state.Hand.Cards[0].Rarity==CardRarity.Rare&&state.Hand.Cards[0].Keywords.Contains(CardKeyword.Ethereal),
                "Call of the Void restricts a successful batch to rares while preserving Ethereal");

            await Clear();
            await Play(await Add<ReaperForm>());
            terminal.Ledger.Add(Bond.Doom,150);
            await Play(await Add<StrikeIronclad>());
            Check(enemy.GetPowerAmount<DoomPower>()==7,"Reaper Form amplifies its own Doom application by 25% at 150 layers");

            await Clear();
            await Play(await Add<Ennui>(upgraded:true));
            await player.Creature.GetPower<EnnuiPower>()!.BeforeHandDraw(player,context,combat);
            Check(state.Hand.Cards.OfType<Joy>().Count()==1&&state.Hand.Cards.OfType<Sadness>().Count()==1,
                "Ennui can generate one Joy alongside the native Sadness");

            await Clear();
            await Add<WahChoKi>(PileType.Exhaust);
            await Play(await Add<WahChoKi>(upgraded:true));await Play(await Add<WahChoKi>(upgraded:true));
            var songs=state.Hand.Cards.Where(card=>card.Tags.Count(tag=>tag is CardTag.Wah or CardTag.Cho or CardTag.Ki)==2).ToArray();
            Check(songs is [var song]&&song.EnergyCost.GetAmountToSpend()==0&&
                  song.Keywords.Contains(CardKeyword.Retain)&&song.Keywords.Contains(CardKeyword.Exhaust),
                "Wah Cho Ki+ makes one free two-Melody card and shares one trigger across copies");

            await Clear();
            await Play(await Add<Ecstasy>());
            var discardedSad=await Add<Sadness>(PileType.Discard);var exhaustedSad=await Add<Sadness>(PileType.Exhaust);
            await player.Creature.GetPower<EcstasyPower>()!.AfterPlayerTurnStart(context,player);
            Check(state.Hand.Cards.OfType<Joy>().Any()&&discardedSad.Pile?.Type!=PileType.Discard&&exhaustedSad.Pile?.Type==PileType.Exhaust,
                "Ecstasy retrieves from the discard pile before it checks the exhaust pile");

            await Clear();
            var meteorShower=await Add<MeteorShower>(upgraded:true);
            await PlayerCmd.SetStars(0,player);await PlayerCmd.SetEnergy(3,player);
            Check(meteorShower.GetStarCostWithModifiers()==0&&meteorShower.EnergyCost.GetAmountToSpend()==2,
                "Meteor Shower+ quotes both missing stars as energy");
            var paid=await meteorShower.SpendResources();
            Check(paid==(2,0)&&state.Energy==1,"Meteor Shower+ spends exactly the previewed mixed payment");

            await Clear();
            var sword=await Add<SovereignBlade>();
            await Play(await Add<SwordSage>(upgraded:true));
            var initialHp=enemy.CurrentHp;
            await CreatureCmd.GainBlock(enemy,100,ValueProp.Unpowered,null);
            var swordTargetBlock=enemy.Block;
            var swordDamage=sword.DynamicVars.Damage.IntValue;
            await Play(sword);
            Check(initialHp-enemy.CurrentHp==Math.Floor(swordDamage*1.25m)&&enemy.Block==swordTargetBlock-swordDamage,
                $"Sword Sage+ applies the replay's 125% HP loss through Block (HP lost={initialHp-enemy.CurrentHp}, Block lost={swordTargetBlock-enemy.Block}, native damage={swordDamage})");

            await Clear();
            var calamity=await Add<Calamity>(upgraded:true);
            await Play(calamity);await Play(await Add<StrikeIronclad>());
            Check(state.Hand.Cards.Any(card=>card.Type==CardType.Attack&&card.EnergyCost.GetAmountToSpend()==0&&card.Keywords.Contains(CardKeyword.Exhaust)),
                "Calamity+ labels a successful generated attack as free and Exhaust");
            var x=await Add<Whirlwind>();
            terminal.ReworkGenerated(x,new RevisionTrace.Scope{Model=player.Creature.GetPower<CalamityPower>()!,Frames=[new RevisionFrame(calamity,"native")]});
            var xPayment=await x.SpendResources();
            Check(xPayment==(0,0)&&state.Energy==6&&x.Keywords.Contains(CardKeyword.Exhaust),
                "Calamity's X-cost receipt grants free payment without spending energy");

            await Clear();
            var witness=enemy;
            if(witness.Block>0)await CreatureCmd.LoseBlock(context,witness,witness.Block,null);
            var splash=await CreatureCmd.Add<Axebot>(combat);splash.SetCurrentHpInternal(1);
            var groupSword=await Add<SovereignBlade>();await Play(await Add<SeekingEdge>());
            var groupDamage=groupSword.DynamicVars.Damage.IntValue;
            await Play(groupSword);
            Check(splash.IsDead&&witness.CurrentHp==1000-(2*groupDamage-1),
                $"Seeking Edge applies the finished group's overflow once (group damage={groupDamage}, survivor HP={witness.CurrentHp})");

            enemy=witness;await Clear();
            enemy=await CreatureCmd.Add<Axebot>(combat);enemy.SetCurrentHpInternal(1);
            var permanentShot=player.RunState.CreateCard<DivineShot>(player);await CardPileCmd.Add(permanentShot,PileType.Deck,skipVisuals:true);
            var shot=await Add<DivineShot>(upgraded:true);shot.DeckVersion=permanentShot;
            state.GainArrow();
            await Play(shot);
            Check(terminal.Ledger.Permanent(Bond.Arrows)==16&&terminal.HasDivineMark(permanentShot),
                "Divine Shot+ records sixteen layers and a pending mark on its actual permanent card");
            terminal.ApplyDivineMarks();
            Check(shot.Keywords.Contains(CardKeyword.Innate)&&shot.Keywords.Contains(CardKeyword.Retain)&&!terminal.HasDivineMark(permanentShot),
                "the Divine Shot mark applies both native keywords and is consumed once");

            await RunSettlement(player);
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop).WaitAsync(TimeSpan.FromSeconds(35));
            var room=NMerchantRoom.Instance!;room.OpenInventory();
            await tree.ToSignal(tree.CreateTimer(.6),SceneTreeTimer.SignalName.Timeout);
            var hand=player.RunState.CreateCard<HandOfGreed>(player);await CardPileCmd.Add(hand,PileType.Deck,skipVisuals:true);
            var inventory=RunManager.Instance.MerchantSynchronizer.GetLocalInventory();
            var entry=inventory.CardEntries.OrderByDescending(entry=>entry.Cost).First();
            var card=entry.CreationResult!.Card;
            terminal.Ledger.Layers.Remove(Bond.Profit);
            await PlayerCmd.SetGold(entry.Cost-39,player);
            var baseOffer=GreedCredit.Offer(entry);
            CardCmd.Upgrade(hand);
            Check(baseOffer is {Checks:2,RiskPerCheck:20,ShownRisk:"36%"}&&
                  GreedCredit.Offer(entry) is {Checks:1,RiskPerCheck:20,ShownRisk:"20%"},
                "a 39-gold shortfall rolls two 15-gold checks on the base card and one 20-gold check when upgraded");
            await PlayerCmd.SetGold(entry.Cost-40,player);
            var startingRisk=GreedCredit.Offer(entry)?.ShownRisk;
            terminal.Ledger.Add(Bond.Profit,200);
            await PlayerCmd.SetGold(entry.Cost-40,player);
            Check(startingRisk=="36%"&&GreedCredit.Offer(entry) is {Checks:2,RiskPerCheck:10,ShownRisk:"19%"},
                "two hundred Profit layers reduce the combined two-roll risk from 36% to 19%");
            terminal.Ledger.Add(Bond.Profit,799);
            await PlayerCmd.SetGold(entry.Cost-40,player);
            Check(GreedCredit.Offer(entry) is {Checks:2,RiskPerCheck:1,ShownRisk:"1.99%"},
                "the loss risk has a floor of 1% per independent roll");

            var expectedRng=new Rng(59);var actualRng=new Rng(59);
            var quote=new GreedCredit.Quote(75,0,5,20);
            var expected=Enumerable.Range(0,quote.Checks).Select(_=>expectedRng.NextInt(100)<quote.RiskPerCheck).ToArray().Any(result=>result);
            Check(GreedCredit.TriggersCardLoss(quote,actualRng)==expected&&actualRng.NextInt(100)==expectedRng.NextInt(100),
                "checkout consumes one independent draw per complete interval");
            terminal.Ledger.Layers.Remove(Bond.Profit);
            await PlayerCmd.SetGold(3,player);
            Check(GreedCredit.Offer(entry) is {Payment:3,Checks:>0,RiskPerCheck:20}&&entry.EnoughGold,"the merchant accepts a short balance and quotes the independent loss risk");
            var remaining=inventory.CardEntries.First(other=>other!=entry&&GreedCredit.Offer(other)!=null);
            patches.Patch(AccessTools.Method(typeof(GreedCredit),nameof(GreedCredit.TriggersCardLoss)),
                prefix:new HarmonyMethod(typeof(ReworkProbe),nameof(ForceRoll)));
            var bought=await RunManager.Instance.MerchantSynchronizer.PurchaseLocalEntry(entry).WaitAsync(TimeSpan.FromSeconds(20));
            Check(bought&&player.Gold==0&&player.Deck.Cards.Contains(card)&&!player.Deck.Cards.Contains(hand),
                "a credit purchase with a triggered loss spends the available gold and removes exactly one Hand of Greed");
            var remainingSlot=room.Inventory.GetAllSlots().Single(slot=>slot.Entry==remaining);
            Check(!remaining.EnoughGold&&remainingSlot.GetNode<MegaCrit.Sts2.addons.mega_text.MegaLabel>("%CostLabel").Text==remaining.Cost.ToString(),
                "losing the only Hand of Greed restores the remaining merchant price and affordability");
            Console.WriteLine("COVENANT_REWORK_COMPLETE checks="+checks);
        }
        finally{patches.UnpatchAll(patches.Id);}
    }
}
