using Covenant.Core;
using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

internal static partial class RevisionProbe
{
    internal static async Task TraitSelectionUi(SceneTree tree,Player player)
    {
        void Check(bool condition,string message)
        {
            if(!condition)throw new Exception(message);
            Console.WriteLine("COVENANT_TRAIT_UI_PASS "+message);
        }
        var hud=NRun.Instance!.GlobalUi.GetNode<Control>("CovenantRoot");
        var toggle=hud.GetNode<Control>("TraitToggle");
        var fold=hud.GetNode<Button>("FoldCovenants");
        fold.EmitSignal(Button.SignalName.Pressed);
        Check(!toggle.Visible&&!hud.GetNode<Control>("BondBar").Visible,"fold hides the covenant bar and both toggles");
        fold.EmitSignal(Button.SignalName.Pressed);
        Check(toggle.Visible&&hud.GetNode<Control>("BondBar").Visible&&!hud.GetNode<Control>("TraitSidebar").Visible,"expanding restores the toggles without showing outside traits in combat");

        var first=player.Creature.CombatState!.CreateCard<StrikeIronclad>(player);
        var second=player.Creature.CombatState.CreateCard<DefendIronclad>(player);
        await CardPileCmd.Add(first,PileType.Hand);
        await CardPileCmd.Add(second,PileType.Hand);
        await tree.ToSignal(tree.CreateTimer(.5),SceneTreeTimer.SignalName.Timeout);
        var holder=NPlayerHand.Instance!.GetCardHolder(second)??throw new Exception("Native hand holder is missing");
        var traitBox=holder.CardNode!.Body.GetNode<Control>("CovenantTraitTip");
        var previousSetting=CovenantSettings.ShowCardTraits;
        try
        {
            CovenantSettings.SetShowCardTraits(false);
            holder.Hitbox.EmitSignal(Control.SignalName.MouseExited);
            Check(!traitBox.Visible,"a hand trait starts hidden with the persistent display disabled");
            holder.Hitbox.EmitSignal(Control.SignalName.MouseEntered);
            Check(traitBox.Visible,"native hand hover displays the trait even with persistence disabled");
            holder.Hitbox.EmitSignal(Control.SignalName.MouseExited);
            Check(!traitBox.Visible,"native mouse exit hides the trait instead of latching it");
            holder.Hitbox.EmitSignal(Control.SignalName.MouseEntered);
            Check(traitBox.Visible,"the same hand card can show its trait again");
            NHoverTipSet.Remove(holder);
            Check(!traitBox.Visible,"direct native tooltip cleanup also hides the hand trait when a card is selected for play");
            holder.Hitbox.EmitSignal(Control.SignalName.MouseExited);
        }
        finally{CovenantSettings.SetShowCardTraits(previousSetting);}
        CardModel[] cards=[first,second];
        async Task<NSimpleCardSelectScreen> Screen()
        {
            for(var i=0;i<20;i++)
            {
                await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
                if(NOverlayStack.Instance?.Peek() is NSimpleCardSelectScreen screen&&screen.GetNode<NCardGrid>("%CardGrid").CurrentlyDisplayedCardHolders.Any())return screen;
            }
            throw new Exception("Native card selection did not show its holders");
        }
        void Choose(NSimpleCardSelectScreen screen,CardModel card)
        {
            var grid=screen.GetNode<NCardGrid>("%CardGrid");
            grid.EmitSignal(NCardGrid.SignalName.HolderPressed,grid.GetCardHolder(card)!);
        }
        var selection=CardSelectCmd.FromSimpleGrid(new BlockingPlayerChoiceContext(),cards,player,CovenantSelection.Single(Presentation.Literal("你可以选择1张牌。"),optional:true));
        var gridScreen=await Screen();
        Check(!gridScreen.GetNode<Control>("%Confirm").Visible&&gridScreen.GetNode<Control>("CovenantSkip").Visible,"optional single selection uses native Skip and no confirmation");
        Choose(gridScreen,first);
        Check((await selection.WaitAsync(TimeSpan.FromSeconds(3))).SequenceEqual([first]),"a single click completes card selection");

        selection=CardSelectCmd.FromSimpleGrid(new BlockingPlayerChoiceContext(),cards,player,CovenantSelection.Single(Presentation.Literal("你可以选择1张牌。"),optional:true));
        gridScreen=await Screen();
        var skip=gridScreen.GetNode<NChoiceSelectionSkipButton>("CovenantSkip");
        skip.EmitSignal(NClickableControl.SignalName.Released,skip);
        Check(!(await selection.WaitAsync(TimeSpan.FromSeconds(3))).Any(),"native Skip returns an empty optional choice");

        selection=CardSelectCmd.FromSimpleGrid(new BlockingPlayerChoiceContext(),cards,player,CovenantSelection.Pair(Presentation.Literal("选择2张牌。")));
        gridScreen=await Screen();
        Choose(gridScreen,first);
        var border=(ShaderMaterial)gridScreen.GetNode<NCardGrid>("%CardGrid").GetCardNode(first)!.CardHighlight.Material;
        Check(border.GetShaderParameter("width").AsSingle()>.05f&&!selection.IsCompleted,"a real two-card selection immediately highlights the first choice");
        Choose(gridScreen,second);
        var confirm=gridScreen.GetNode<NConfirmButton>("%Confirm");
        confirm.EmitSignal(NClickableControl.SignalName.Released,confirm);
        Check((await selection.WaitAsync(TimeSpan.FromSeconds(3))).ToHashSet().SetEquals(cards),"the pair still uses native confirmation");
    }

    private static async Task TraitPowers(SceneTree tree,Player player,CovenantTerminal terminal,Func<Task> clear,Func<CardModel,Task> play,Action<bool,string> check)
    {
        async Task<T> Add<T>(PileType pile=PileType.Hand) where T:CardModel
        {
            var card=player.Creature.CombatState!.CreateCard<T>(player);
            await CardPileCmd.Add(card,pile);return card;
        }
        await clear();
        var unrelenting=await Add<Unrelenting>();for(var i=0;i<3;i++)await Add<Bludgeon>(PileType.Exhaust);
        await play(unrelenting);
        var charge=player.Creature.GetPower<CovenantUnrelentingPower>();
        check(charge is {Multiplier:2}&&charge.Title.GetFormattedText()=="无情猛攻"&&charge.Icon==Presentation.Icon(Bond.Heavy)&&
            terminal.Ledger.Get(Bond.Heavy)==0,"Unrelenting places its own native Heavy status before awarding any layers");
        await play(await Add<DefendIronclad>());
        check(player.Creature.GetPower<CovenantUnrelentingPower>()==charge,"a skill does not consume the next-attack status");
        await play(await Add<Bludgeon>());
        check(player.Creature.GetPower<CovenantUnrelentingPower>()==null&&terminal.Ledger.Get(Bond.Heavy)==6,"the next printed-cost-three attack consumes the status and awards six layers");

        await clear();
        var spite=await Add<Spite>();var bloodletting=await Add<Bloodletting>();await Add<Offering>(PileType.Exhaust);await Add<Offering>(PileType.Exhaust);
        await play(spite);
        check(terminal.Ledger.Get(Bond.Blood)==0,"Spite does not grow Blood before losing HP");
        await play(bloodletting);
        var blood=terminal.Ledger.Get(Bond.Blood);
        await CardPileCmd.Add(spite,PileType.Hand);await play(spite);
        check(terminal.Ledger.Get(Bond.Blood)==blood+4,"Spite grows Blood after actual HP loss without requiring a draw");

        await clear();
        terminal.Ledger.Add(Bond.Chain,275);
        var speedster=await Add<Speedster>();
        var text=BondDescription.Format(Presentation.TraitText(speedster,annotateScaling:true),speedster,showFormulas:true);
        check(text.StartsWith("此牌每次造成的伤害增加")&&text.Contains("275[/color]/250)")&&!text.Contains("【连携】"),
            "Speedster combines nested divisors and uses a direct intrinsic effect description");
        await play(speedster);
        check(player.Creature.Powers.OfType<CovenantEffectPower>().Any(p=>p.EffectName==speedster.Title&&p.EffectText.Contains("此牌每次造成的伤害增加")),
            "a played Speedster advertises its ongoing trait beside its native power");

        await clear();
        var noise=await Add<WhiteNoise>();for(var i=0;i<6;i++)await Add<Soul>(PileType.Exhaust);
        await play(noise);
        var status=player.Creature.Powers.OfType<CovenantEffectPower>().Single(p=>p.EffectName==noise.Title);
        check(status.EffectText.Contains("已关联"),"generated-card support appears in its own named status");

        var burn=await Add<Burn>();
        var statusRows=terminal.BattleTraitStatus().ToArray();
        check(statusRows.Any(row=>row.Source==Presentation.CardTitle(noise)&&row.State?.Contains("已关联")==true)&&
            !statusRows.Any(row=>row.Source==Presentation.CardTitle(burn)),"the status tracks linked effects and excludes direct in-hand triggers");
        var powerNode=NRun.Instance!.FindChildren("*","",true,false).OfType<NPower>().Single(node=>node.Model==status);
        check(powerNode.GetNode<TextureRect>("%Icon").Texture==Presentation.Icon(status.IconBond)&&
            powerNode.GetNode<CpuParticles2D>("%PowerFlash").Texture==Presentation.Icon(status.IconBond),"the actual native status icon and flash use the source covenant's embedded texture");

        await clear();
        var bolas=await Add<Bolas>();var bounce=await Add<Bounce>();
        for(var i=0;i<3;i++)await Add<Claw>(PileType.Exhaust);
        for(var i=0;i<6;i++)await Add<ShrugItOff>(PileType.Exhaust);
        await play(bolas);await play(bounce);
        check(new[]{bolas.Title,bounce.Title}.All(title=>player.Creature.Powers.OfType<CovenantEffectPower>()
            .Any(power=>power.EffectName==title&&power.EffectText.Contains("下回合抽牌前"))),
            "off-hand returns expose both cards as separate pending statuses before the next draw");
        var zero=terminal.Ledger.Get(Bond.Zero);var guard=terminal.Ledger.Get(Bond.Guard);
        await CombatProbe.EndTurn(tree);
        check(bolas.Pile?.Type==PileType.Hand&&bounce.Pile?.Type==PileType.Hand&&terminal.Ledger.Get(Bond.Zero)>zero&&terminal.Ledger.Get(Bond.Guard)>guard,
            "Bolas and Bounce grow their covenants when the original game returns them before the normal draw");
        check(!player.Creature.Powers.OfType<CovenantEffectPower>().Any(power=>power.EffectName==bolas.Title||power.EffectName==bounce.Title),
            "the off-hand statuses clear once their cards are back in hand");

        await clear();
        var makeItSo=await Add<MakeItSo>(PileType.Discard);
        for(var i=0;i<3;i++)await Add<Claw>(PileType.Exhaust);
        await play(await Add<DefendIronclad>());
        check(terminal.BattleTraitStatus().Any(row=>row.Source==makeItSo.Title&&row.State?.Contains("再打出2张技能牌")==true),
            "an off-hand repeatable return shows how many more Skills it needs");
        for(var i=0;i<2;i++)await play(await Add<DefendIronclad>());
        check(makeItSo.Pile?.Type==PileType.Hand&&terminal.Ledger.Get(Bond.Zero)==0,"Make It So still returns after three Skills");
        await Add<Claw>();
        await play(makeItSo);
        check(terminal.Ledger.Get(Bond.Zero)==1,"Make It So grows Zero when played with an eligible hand card");

        await clear();
        var headbutt=await Add<Headbutt>();var returned=await Add<IronWave>(PileType.Discard);
        headbutt.DeckVersion=player.RunState.CreateCard<Headbutt>(player);
        var enemy=player.Creature.CombatState!.HittableEnemies.First();
        var hp=enemy.CurrentHp;
        await play(headbutt);
        check(hp-enemy.CurrentHp==headbutt.DynamicVars.Damage.BaseValue&&returned.Pile?.Type==PileType.Draw,
            "Headbutt keeps its native damage and returns the selected card to the draw pile");
        check(terminal.BattleTraitStatus().Count(row=>row.Source==headbutt.Title&&row.State?.Contains(returned.Title)==true)==1,
            "Headbutt shows one linked status entry naming the returned card");
        var debt=await Add<Debt>();
        check(!Presentation.TraitText(debt,directOnly:true).Contains("胜利")&&
            !terminal.BattleTraitStatus().Any(row=>row.Source==debt.Title),
            "a generated Debt displays its own draw trait on the card without a separate icon");
        var entropy=await Add<Entropy>();
        var entropyText=BondDescription.Format(Presentation.TraitText(entropy,annotateScaling:true),entropy,showFormulas:true);
        check(entropyText.Contains("当前已激活盟约数量")&&!entropyText.Contains(BondDescription.ScalingColorTag),
            "Entropy describes its active covenant count without advertising nonexistent Harmony layers");

        await clear();
        for(var i=0;i<6;i++)await Add<TwinStrike>(PileType.Exhaust);
        for(var i=0;i<4;i++)await play(await Add<StrikeIronclad>());
        var wave=await Add<IronWave>();var block=player.Creature.Block;hp=enemy.CurrentHp;
        await play(wave);
        check(hp-enemy.CurrentHp==wave.DynamicVars.Damage.BaseValue+3&&player.Creature.Block-block==wave.DynamicVars.Block.BaseValue,
            $"Blades' pending attack bonus raises damage without raising Iron Wave's Block ({hp-enemy.CurrentHp} damage, {player.Creature.Block-block} Block)");

        await clear();
        var refine=await Add<RefineBlade>();refine.DynamicVars["Energy"].BaseValue=3;
        await Add<RefineBlade>(PileType.Exhaust);await Add<RefineBlade>(PileType.Exhaust);
        await play(refine);
        check(terminal.BattleTraitStatus().Any(row=>row.Source==refine.Title&&row.State=="等待获得资源"),"delayed energy is listed as pending before it is granted");
        await PlayerCmd.SetEnergy(0,player);
        await player.Creature.GetPower<EnergyNextTurnPower>()!.AfterEnergyReset(player);
        check(terminal.BattleTraitStatus().Any(row=>row.Source==refine.Title&&row.State=="可用资源 3点能量"),"the status displays the amount of resource from the native grant");
        await (await Add<DefendIronclad>()).SpendResources();
        check(terminal.BattleTraitStatus().Any(row=>row.Source==refine.Title&&row.State=="可用资源 2点能量"),"resource spending updates the actual remaining attributable amount");
        check(terminal.Ledger.Get(Bond.Forge)==0,"paying for a different card does not trigger Refine Blade's covenant trait");
        await player.PlayerCombatState!.Hand.Cards.OfType<SovereignBlade>().Single().SpendResources();
        check(terminal.Ledger.Get(Bond.Forge)==8,"spending the remainder on the sword still qualifies once for the same grant");

        await clear();
        for(var i=0;i<3;i++)await Add<Claw>(PileType.Exhaust);
        var zeroReturn=await Add<Claw>(PileType.Discard);
        await play(await Add<Claw>());
        check(zeroReturn.Pile?.Type==PileType.Hand,"Zero moves its randomly chosen discarded card directly into hand");

        await clear();
        var starterStrike=player.Deck.Cards.OfType<StrikeIronclad>().First();
        async Task<StrikeIronclad> AddStarterStrike(PileType pile)
        {
            var strike=await Add<StrikeIronclad>(pile);strike.DeckVersion=starterStrike;return strike;
        }
        for(var i=0;i<4;i++)await AddStarterStrike(PileType.Exhaust);
        await Add<BorrowedTime>(PileType.Exhaust);await Add<Burst>(PileType.Exhaust);await Add<Cascade>(PileType.Exhaust);
        await Add<DefendIronclad>(PileType.Draw);
        var strikeReturn=await AddStarterStrike(PileType.Discard);
        await play(await Add<DefendIronclad>());
        var beforeStrikes=enemy.CurrentHp;
        await play(await AddStarterStrike(PileType.Hand));
        check(MegaCrit.Sts2.Core.Combat.CombatManager.Instance.History.CardPlaysFinished.Count(entry=>entry.CardPlay.Card==strikeReturn)==1,
            "Strike immediately plays one eligible card from discard without recursively retriggering");
        check(beforeStrikes-enemy.CurrentHp==2*(strikeReturn.DynamicVars.Damage.BaseValue+2),
            "both the original and automatically played Strike receive the Strike covenant bonus");

        await clear();
        for(var i=0;i<BondCatalog.Get(Bond.Ember).Advanced;i++)await Add<FiendFire>(PileType.Exhaust);
        var exhausted=await Add<Slimed>();var priorBlock=player.Creature.Block;
        await CardCmd.Exhaust(new ThrowingPlayerChoiceContext(),exhausted);
        check(player.Creature.Block==priorBlock+2&&!CardMembership.Of(exhausted).Contains(Bond.Ember),
            "active Ember still rewards exhausting a card that does not count as a member");

        CovenantEffectPower? Status(CardModel card)=>player.Creature.Powers.OfType<CovenantEffectPower>().FirstOrDefault(p=>p.EffectName==card.Title);
        await clear();
        var frenzy=await Add<FeedingFrenzy>();var byrd=await Add<GodByrd>();var energy=await Add<Outmaneuver>();
        var direct=await Add<Bombardment>();await Add<AscendersBane>();
        await Add<Inflame>(PileType.Exhaust);await Add<DemonForm>(PileType.Exhaust);
        await play(frenzy);await play(byrd);await play(energy);
        check(Status(frenzy) is {IconBond:Bond.Fury} feeding&&feeding.EffectText.StartsWith("本回合，你每打出")&&
            Status(byrd)!=null&&Status(energy)!=null&&Status(direct)==null,
            "Frenzy, Byrd and Outmaneuver expose separate pending statuses, while Bombardment stays on the card");
        var fury=terminal.Ledger.Permanent(Bond.Fury);await play(direct);
        check(terminal.Ledger.Permanent(Bond.Fury)==fury+2,"Feeding Frenzy awards its own permanent layers on a later attack");
        await CombatProbe.EndTurn(tree);
        check(Status(frenzy)==null&&Status(byrd)==null&&Status(energy)==null,"the three statuses clear when their respective effects end");

        await clear();
        var veilpiercer=await Add<Veilpiercer>();
        await play(veilpiercer);
        check(Status(veilpiercer)?.EffectText.Contains("虚无牌")==true&&
            !Status(veilpiercer)!.EffectText.Contains("此牌额外造成")&&
            !Presentation.TraitText(veilpiercer,directOnly:true).Contains("虚无牌"),
            "Veilpiercer advertises the other card's bonus separately from its own attack damage");
        var quietus=await Add<Quietus>();await play(quietus);
        check(Status(quietus)?.EffectText.Contains("强化的射击结算后")==true,
            "Quietus advertises its delayed arrow covenant as soon as its native power is applied");

        await clear();
        var reb=await Add<Rebound>();
        var reboundDeck=player.RunState.CreateCard<Rebound>(player);reb.DeckVersion=reboundDeck;
        var otherEvent=await Add<Peck>(PileType.Exhaust);otherEvent.DeckVersion=player.RunState.CreateCard<Peck>(player);
        var thirdEvent=await Add<Wish>(PileType.Exhaust);thirdEvent.DeckVersion=player.RunState.CreateCard<Wish>(player);
        var marked=await Add<DefendIronclad>();
        await play(reb);await play(marked);
        check(marked.Pile?.Type==PileType.Draw&&Status(reb)?.EffectText.Contains(marked.Title)==true,
            "Rebound keeps a native status naming the card it moved to the top");
        var encounter=terminal.Ledger.Permanent(Bond.Encounter);
        await play(marked);
        check(terminal.Ledger.Permanent(Bond.Encounter)==encounter+6&&Status(reb)==null,"Rebound works on the next play even in the same turn, then consumes the link");
        await CardPileCmd.Add(reb,PileType.Hand);
        await play(reb);await play(marked);
        await CombatProbe.EndTurn(tree);await CombatProbe.EndTurn(tree);
        check(Status(reb)?.EffectText.Contains(marked.Title)==true,"Rebound's link stays visible after the following turn has passed");
        encounter=terminal.Ledger.Permanent(Bond.Encounter);await play(marked);
        check(terminal.Ledger.Permanent(Bond.Encounter)==encounter+6&&Status(reb)==null,"the next-play link pays exactly once even two turns later");

        await clear();
        var breaker=await Add<Break>();await Add<Inflame>(PileType.Exhaust);await Add<DemonForm>(PileType.Exhaust);
        check(CardMembership.Of(breaker).Contains(Bond.Fury)&&terminal.Tier(Bond.Ancient)==1,"Break belongs to Fury and activates the first Ancient tier alone");
        await Add<WraithForm>(PileType.Exhaust);await Add<Whistle>(PileType.Exhaust);
        await play(breaker);
        var rare=player.PlayerCombatState!.Hand.Cards.Single(card=>card.Rarity==CardRarity.Rare);
        check(terminal.Ledger.Permanent(Bond.Ancient)==0&&terminal.Ledger.Permanent(Bond.Fury)==14,
            "Break triggers its own Vulnerable trait, then Ancient adds four to each active eligible covenant");
        check(rare.Pool==breaker.Pool&&rare.Keywords.Contains(CardKeyword.Ethereal)&&rare.Keywords.Contains(CardKeyword.Exhaust)&&rare.EnergyCost.GetAmountToSpend()==0,
            "Ancient adds a same-color rare card with Ethereal, Exhaust and one free play");

    }
}
