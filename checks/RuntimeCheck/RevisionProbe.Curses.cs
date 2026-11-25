using Covenant.Core;
using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.RichTextTags;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.addons.mega_text;

internal static partial class RevisionProbe
{
    private static async Task GuiltyCounter(Player player,CovenantTerminal terminal,Func<Task> clear,Action<bool,string> check)
    {
        var context=new ThrowingPlayerChoiceContext();
        var combat=player.Creature.CombatState!;
        foreach(var (before,after) in new[]{(1,4),(5,8)})
        {
            await clear();
            var deck=player.RunState.CreateCard<Guilty>(player);
            await CardPileCmd.Add(deck,PileType.Deck,skipVisuals:true);
            var card=combat.CreateCard<Guilty>(player);
            card.DeckVersion=deck;
            deck.CombatsSeen=card.CombatsSeen=Guilty.maxCombats-before;
            await CardPileCmd.Add(card,PileType.Hand);
            await CardPileCmd.Add(combat.CreateCard<Bash>(player),PileType.Draw);
            await CardPileCmd.Add(combat.CreateCard<DefendIronclad>(player),PileType.Draw);
            check(card.GetDescriptionForPile(PileType.Hand).StripBbCode().Contains($"在{before}场战斗后从你的牌组中移除")&&
                RevisionDescriptions.Text(card).Contains("剩余战斗数增加3"),"Guilty renders the native countdown and its extension");
            await CardCmd.AutoPlay(context,card,null);
            check(deck.Pile?.Type==PileType.Deck&&card.Pile?.Type==PileType.Exhaust&&
                deck.DynamicVars["Combats"].IntValue==after&&card.DynamicVars["Combats"].IntValue==after&&
                terminal.Ledger.Get(Bond.Curse)==before&&
                player.PlayerCombatState!.Hand.Cards.Count==2,
                $"Guilty grants {before} Curse layers, exhausts and extends the countdown to {after}");
            var restored=(Guilty)CardModel.FromSerializable(deck.ToSerializable());
            check(restored.DynamicVars["Combats"].IntValue==after,"the extended countdown survives card serialization");
            await deck.AfterCombatEnd((CombatRoom)player.RunState.CurrentRoom!);
            check(deck.DynamicVars["Combats"].IntValue==after-1,"the native end-of-combat countdown still ticks once");
            await CardPileCmd.RemoveFromDeck(deck,showPreview:false);
        }
        Console.WriteLine("COVENANT_GUILTY_COMPLETE");
    }

    private static async Task AscendersBaneGrowth(Player player,CovenantTerminal terminal,Func<Task> clear,Action<bool,string> check)
    {
        await clear();
        var context=new ThrowingPlayerChoiceContext();
        var combat=player.Creature.CombatState!;
        async Task<AscendersBane> AddBane()
        {
            var card=combat.CreateCard<AscendersBane>(player);
            await CardPileCmd.Add(card,PileType.Hand);
            return card;
        }
        var inactive=await AddBane();
        await CardCmd.Exhaust(context,inactive,causedByEthereal:true);
        check(terminal.Ledger.Get(Bond.Curse)==0,"the exhaust trait requires Curse to be active");

        var natural=await AddBane();
        for(var i=2;i<BondCatalog.Get(Bond.Curse).Basic;i++)
            await CardPileCmd.Add(combat.CreateCard<Injury>(player),PileType.Exhaust,skipVisuals:true);
        check(terminal.Tier(Bond.Curse)>0&&RevisionDescriptions.Text(natural).Contains("消耗此牌时，使已激活的【咒缚】增加2层"),
            "Ascender's Bane displays its exhaust trait once Curse is active");
        await CardCmd.Exhaust(context,natural,causedByEthereal:true);
        check(terminal.Ledger.Get(Bond.Curse)==2,"native Ethereal exhaust grants two layers");
        var played=await AddBane();
        check(played.CanPlay()&&played.EnergyCost.GetAmountToSpend()==0,"the active Curse trait enables free play");
        await CardCmd.AutoPlay(context,played,null).WaitAsync(TimeSpan.FromSeconds(15));
        check(played.Pile?.Type==PileType.Exhaust&&player.Creature.GetPowerAmount<PlatingPower>()==2&&
            terminal.Ledger.Get(Bond.Curse)==4,"playing the card grants its Plating and exhausts for exactly two additional layers");
        Console.WriteLine("COVENANT_ASCENDERS_BANE_COMPLETE");
    }

    private static async Task BadLuckPercentage(Player player,CovenantTerminal terminal,Func<Task> clear,Action<bool,string> check)
    {
        await clear();
        var eligible=player.Deck.Cards.Where(card=>card.Type!=CardType.Curse&&!card.Keywords.Contains(CardKeyword.Eternal)).ToArray();
        if(eligible.Length>10)await CardPileCmd.RemoveFromDeck(eligible[10..],showPreview:false);
        for(var i=eligible.Length;i<10;i++)
            await CardPileCmd.Add(player.RunState.CreateCard<Bash>(player),PileType.Deck,skipVisuals:true);
        var eternal=player.RunState.CreateCard<Bash>(player);
        eternal.AddKeyword(CardKeyword.Eternal);
        await CardPileCmd.Add(eternal,PileType.Deck,skipVisuals:true);
        var badLuck=player.RunState.CreateCard<BadLuck>(player);
        await CardPileCmd.Add(badLuck,PileType.Deck,skipVisuals:true);
        terminal.Ledger.Add(Bond.Curse,999);
        check(RevisionDescriptions.Text(badLuck).Contains("100%"),"Bad Luck caps its displayed fraction at 100 percent");
        terminal.Ledger.Layers[Bond.Curse]=95;
        check(RevisionDescriptions.Text(badLuck).Contains("10.5%"),"Bad Luck displays 10.5 percent at 95 Curse layers");
        await CreatureCmd.Kill(player.Creature);
        check(terminal.PendingBadLuckTransforms==1&&player.Creature.Powers.OfType<CovenantEffectPower>()
            .Any(power=>power.EffectName=="霉运"&&power.EffectText.Contains("10.5%")),
            "the pending native status uses the same fractional percentage");
        var before=player.Deck.Cards.Count(card=>card.Type==CardType.Curse);
        await terminal.AfterCombatEnd((CombatRoom)player.RunState.CurrentRoom!);
        check(player.Deck.Cards.Count(card=>card.Type==CardType.Curse)==before+2&&
            eternal.Pile?.Type==PileType.Deck&&terminal.PendingBadLuckTransforms==0,
            "10.5 percent of ten eligible cards rounds up to two and leaves Eternal cards alone");
        Console.WriteLine("COVENANT_BAD_LUCK_PERCENT_COMPLETE");
    }

    private static async Task DecayTargets(Player player,CovenantTerminal terminal,Func<Task> clear,Action<bool,string> check)
    {
        await clear();
        var combat=player.Creature.CombatState!;
        var context=new ThrowingPlayerChoiceContext();
        var decay=combat.CreateCard<Decay>(player);
        await CardPileCmd.Add(decay,PileType.Hand);
        await CardPileCmd.Add(combat.CreateCard<Injury>(player),PileType.Exhaust,skipVisuals:true);
        await CardPileCmd.Add(combat.CreateCard<Doubt>(player),PileType.Exhaust,skipVisuals:true);
        terminal.Ledger.Add(Bond.Curse,150);
        await OstyCmd.Summon(context,player,12,null);
        await PowerCmd.Remove(player.Osty!.GetPower<DieForYouPower>()!);
        var targets=combat.Creatures.Where(creature=>creature.IsHittable).ToArray();
        check(targets.Length==3&&targets.Contains(player.Creature)&&targets.Contains(player.Osty)&&
              targets.Count(creature=>creature.IsEnemy)==1,"Decay test has a player, a friendly summon and an enemy");
        foreach(var target in targets)await CreatureCmd.LoseBlock(context,target,target.Block,null);
        var before=targets.Select(target=>target.CurrentHp).ToArray();
        var seed=Enumerable.Range(0,128).First(value=>
        {
            var rng=new Rng((ulong)value);
            return Enumerable.Range(0,4).Select(_=>rng.NextItem(targets)).Distinct().Count()==3;
        });
        var previewRng=new Rng((ulong)seed);
        var expectedHits=Enumerable.Range(0,4).Select(_=>previewRng.NextItem(targets)).ToLookup(target=>target);
        check(RevisionDescriptions.Text(decay).Contains("4点伤害4次")&&
              RevisionDescriptions.Program(decay)!.Rules.All(rule=>rule.RequireActive),"Decay shows four hits of four at three Curse tiers and still requires Curse");
        terminal.RandomFor(CovenantRandom.CombatTarget).LoadFromSerializable(new Rng((ulong)seed).ToSerializable());
        await CardCmd.AutoPlay(context,decay,null).WaitAsync(TimeSpan.FromSeconds(15));
        check(targets.Select((target,index)=>before[index]-target.CurrentHp==4*expectedHits[target].Count()).All(matches=>matches)&&
              terminal.Ledger.Get(Bond.Curse)==154,"four real Decay hits deal four damage each across all sides and grant the original four layers");
        var text=BondDescription.Format(Presentation.TraitText(decay,annotateScaling:true),decay,splitSentences:false);
        var tip=GD.Load<PackedScene>("res://scenes/ui/hover_tip.tscn").Instantiate<Control>();
        NGame.Instance!.AddChild(tip);
        var label=tip.GetNode<MegaRichTextLabel>("%Description");
        label.Text=text;
        var effects=label.CustomEffects.Select(effect=>effect.AsGodotObject()).ToArray();
        var parsed=label.GetParsedText();
        tip.QueueFree();
        check(text.Contains("随机对[jitter][red]任意[/red][/jitter]目标",StringComparison.Ordinal),
            "Decay keeps its native red jitter markup around the two target characters");
        check(parsed.Contains("随机对任意目标",StringComparison.Ordinal)&&effects.Any(effect=>effect is RichTextRed)&&
            effects.Any(effect=>effect is RichTextJitter),"the native trait label parses both effects and displays the intended target wording");
        Console.WriteLine("COVENANT_DECAY_TARGETS_COMPLETE");
    }

    private static async Task Curses(Player player,CovenantTerminal terminal,Func<Task> clear,Action<bool,string> check)
    {
        var combat=player.Creature.CombatState!;
        var state=player.PlayerCombatState!;
        var context=new BlockingPlayerChoiceContext();
        async Task<T> Add<T>(PileType pile=PileType.Hand,T? original=null) where T:CardModel
        {
            var card=pile==PileType.Deck?player.RunState.CreateCard<T>(player):combat.CreateCard<T>(player);
            if(original!=null)card.DeckVersion=original;
            await CardPileCmd.Add(card,pile,skipVisuals:true);
            return card;
        }

        await clear();
        var patchCurse=await Add<Clumsy>();
        var starterCounts=terminal.Counts(deck:true);
        check(starterCounts.GetValueOrDefault(Bond.Strike)==2&&starterCounts.GetValueOrDefault(Bond.Guard)==2,
            "five basic Strikes count as two after rounding down");
        check(!patchCurse.CanPlay()&&patchCurse.EnergyCost.GetWithModifiers(CostModifiers.All)==-1,"conditional curses keep their original lock below three members");
        var hp=player.Creature.MaxHp;
        terminal.Ledger.Add(Bond.Curse,200);
        await Add<Clumsy>(PileType.Deck);
        await Add<Injury>(PileType.Deck);
        check(player.Creature.MaxHp==hp&&CardMembership.CurseTier(player,deck:true)==0,"two permanent curses have not reached Curse I");
        var normal=CardFactory.CreateForReward(player,3,CardCreationOptions.ForRoom(player,RoomType.Monster)
            .WithFlags(CardCreationFlags.IsCardReward)).ToArray();
        check(normal.Length==3&&normal.All(choice=>choice.Card.Type!=CardType.Curse),
            "ordinary rewards exclude Covenant curse candidates below three members");
        var elite=CardFactory.CreateForReward(player,3,CardCreationOptions.ForRoom(player,RoomType.Elite)
            .WithFlags(CardCreationFlags.IsCardReward)).ToArray();
        check(elite.Length==3&&elite.All(choice=>choice.Card.Type!=CardType.Curse),"elite rewards below Curse I retain their native pool");
        var ordinaryStock=ModelDb.Card<ShrugItOff>();
        var uncommonStock=ModelDb.Card<BattleTrance>();
        var mixed=terminal.AllCardPools;
        terminal.AllCardPools=false;
        MerchantCardEntry Shop(CardModel model)
        {
            var slot=new MerchantCardEntry(player,[model],CardType.Skill);
            slot.Populate(null);
            return slot;
        }
        check(Shop(ordinaryStock).CreationResult!.Card is ShrugItOff,"the native merchant does not add curses below Curse I");
        terminal.AllCardPools=mixed;
        await Add<Regret>(PileType.Deck);
        check(player.Creature.MaxHp==hp+5&&CardMembership.CurseTier(player,deck:true)==1&&BondCatalog.Get(Bond.Curse).Advanced==6,
            "acquiring the third curse enables Curse I and the scaled health reward, with Curse II at six");
        check(new[]{0,1,25,50,100,999}.Select(CurseCovenant.CurseWeight).SequenceEqual([50,51,75,100,150,150]),
            "curse weights are exact per layer, double at 50 layers, and capped at triple at 100");

        CardCreationOptions Reward(CardRarity rarity)=>CardCreationOptions.ForRoom(player,RoomType.Monster)
            .WithFlags(CardCreationFlags.IsCardReward)
            .WithFilter(card=>card.Type==CardType.Curse&&card.Rarity==rarity);
        var ordinary=CardFactory.CreateForReward(player,1,Reward(CardRarity.Common)).Single().Card;
        var uncommon=CardFactory.CreateForReward(player,1,Reward(CardRarity.Uncommon)).Single().Card;
        check(ordinary.CanBeGeneratedByModifiers&&ordinary.Rarity==CardRarity.Curse&&uncommon is Greed&&uncommon.Rarity==CardRarity.Curse,
            "native reward rolls include dice curses as Common and Greed as Uncommon while preserving their card identity");
        var foundReward=false;
        for(var i=0;i<40&&!foundReward;i++)
        {
            var reward=new CardReward(CardCreationOptions.ForRoom(player,RoomType.Monster),3,player);
            reward.Populate();
            foundReward=reward.Cards.Any(card=>card.Type==CardType.Curse);
            check(reward.Cards.Select(card=>card.Id).Distinct().Count()==3,"native reward keeps three distinct options");
        }
        check(foundReward&&terminal.RandomFor(CovenantRandom.CurseReward).ToSerializable().counter>0,
            "an unfiltered native card reward actually offers a curse from the mixed pool");

        terminal.AllCardPools=false;
        var commonShop=Enumerable.Range(0,32).Select(_=>Shop(ordinaryStock))
            .FirstOrDefault(slot=>slot.CreationResult!.Card.Type==CardType.Curse);
        var greedShop=Enumerable.Range(0,32).Select(_=>Shop(uncommonStock))
            .FirstOrDefault(slot=>slot.CreationResult!.Card is Greed);
        check(commonShop is {IsStocked:true,Cost:>=47 and <=53}&&commonShop.CreationResult!.Card.CanBeGeneratedByModifiers&&
            greedShop is {IsStocked:true,Cost:>=71 and <=79}&&greedShop.CreationResult!.Card.Rarity==CardRarity.Curse,
            "native merchant slots stock dice curses and Greed as buyable common and uncommon entries");
        check(CardFactory.CreateForMerchant(player,[uncommonStock],[ModelDb.Card<Greed>()],CardType.Skill).Card is BattleTrance,
            "a curse already stocked in another merchant slot respects the native blacklist");
        terminal.AllCardPools=mixed;
        var inventory=MerchantInventory.CreateForNormalMerchant(player);
        check(inventory.CharacterCardEntries.Count==5&&inventory.ColorlessCardEntries.All(slot=>slot.CreationResult!.Card.Type!=CardType.Curse)&&
            terminal.RandomFor(CovenantRandom.CurseShop).ToSerializable().counter>0,
            "actual merchant inventory preserves the five character slots and the separate colorless shelf");
        var curses=CardMembership.Definitions.Values.Where(definition=>definition.Type=="Curse");
        check(curses.All(definition=>definition.Trait.Revision!.Base.Rules.All(rule=>rule.Action!="grow"||rule.Bond==Bond.Curse)),
            "curse traits do not grant layers to other covenants");
        var shortTraits=new CardModel[]{ModelDb.Card<Greed>(),ModelDb.Card<Guilty>(),ModelDb.Card<TheHermit>(),ModelDb.Card<Writhe>()};
        check(shortTraits.All(card=>!BondDescription.Format(Presentation.TraitText(card),card,splitSentences:false).Contains('\n')),
            "related trait clauses share a paragraph without trailing blank lines");

        await Add<Injury>(PileType.Exhaust);await Add<Injury>(PileType.Exhaust);
        var clumsyRules=CardMembership.TraitRevision(patchCurse)!.Base.Rules;
        check(clumsyRules is [{Event:"curse_play"}]&&Presentation.TraitText(patchCurse).Contains("获得3点格挡"),
            "Clumsy's native play effect replaces the old trait in the catalog and its single trait description");
        check(patchCurse.CanPlay()&&patchCurse.EnergyCost.GetWithModifiers(CostModifiers.All)==0,"Curse I provides the printed playable zero-cost route");
        var strike=await Add<StrikeIronclad>();
        var before=player.Creature.Block;
        var (energy,stars)=await patchCurse.SpendResources();
        await patchCurse.OnPlayWrapper(context,null,false,new ResourceInfo{EnergySpent=energy,EnergyValue=energy,StarsSpent=stars,StarValue=stars},true);
        check(player.Creature.Block>=before+3&&strike.Pile?.Type==PileType.Discard,
            "playing Clumsy actually executes its card-specific effect");

        for(var i=0;i<4;i++)await Add<Injury>(PileType.Exhaust);
        var draw=await Add<Injury>(PileType.Draw);
        before=player.Creature.Block;
        await CardPileCmd.Draw(context,1,player);
        check(draw.Pile?.Type==PileType.Hand&&player.Creature.Block>=before+4,"Curse I blocks on draw and Curse II leaves the card available to play");
        var witness=await Add<DefendIronclad>(PileType.Draw);
        var enemy=combat.HittableEnemies.First();
        await CardPileCmd.Add(draw,PileType.Discard);
        var normality=await Add<Normality>();
        check(!normality.CanPlay(),"Normality is locked before its native limit triggers");
        for(var i=0;i<2;i++)await CardCmd.AutoPlay(context,await Add<DefendIronclad>(),null);
        await CardPileCmd.Add(strike,PileType.Hand);
        check(normality.CanPlay()&&normality.EnergyCost.GetAmountToSpend()==1,"Normality costs one once the native limit has triggered");
        var enemyHp=enemy.CurrentHp;
        await CardCmd.AutoPlay(context,normality,null);
        check(normality.Pile?.Type==PileType.Discard&&witness.Pile?.Type==PileType.Hand&&enemy.CurrentHp==enemyHp-6&&
            strike.EnergyCost.GetAmountToSpend()==0&&strike.CanPlay()&&!witness.CanPlay(),
            "Curse II fires on play, and Normality makes the selected card free and prioritizes it");
        await CardCmd.AutoPlay(context,strike,enemy);
        check(witness.CanPlay(),"the priority restriction is released when the marked card is played");

        await clear();
        await Add<Injury>(PileType.Exhaust);await Add<Injury>(PileType.Exhaust);
        var bleed=await Add<Decay>();
        terminal.Ledger.Add(Bond.Curse,300);
        var decayTargets=combat.Creatures.Where(target=>target.IsHittable).ToArray();
        player.Creature.SetCurrentHpInternal(player.Creature.MaxHp);
        foreach(var target in decayTargets)await CreatureCmd.LoseBlock(context,target,target.Block,null);
        var health=decayTargets.Sum(target=>target.CurrentHp);
        await CardCmd.AutoPlay(context,bleed,null);
        check(decayTargets.Sum(target=>target.CurrentHp)==health-42&&terminal.Ledger.Get(Bond.Curse)==305,
            "Decay makes seven six-point hits at A=6 and gains five layers");
        var injury=await Add<Injury>();
        before=player.Creature.Block;
        await CardCmd.AutoPlay(context,injury,null);
        check(injury.EnergyCost.GetAmountToSpend()==0&&player.Creature.Block==before&&terminal.Ledger.Get(Bond.Curse)==306,
            "Injury costs zero and has no effect beyond its single layer");
        var sownA=await Add<Bash>(PileType.Draw);var sownB=await Add<StrikeIronclad>(PileType.Discard);
        var sownC=await Add<DefendIronclad>(PileType.Discard);
        await CardCmd.AutoPlay(context,await Add<SporeMind>(),null);
        check(new CardModel[]{sownA,sownB,sownC}.All(card=>card.Enchantment is Sown),"Spore Mind sows across both piles and scales with Curse");

        await clear();
        var guiltDeck=await Add<Guilty>(PileType.Deck);
        var guilt=await Add<Guilty>(original:guiltDeck);
        guiltDeck.CombatsSeen=guilt.CombatsSeen=1;
        check(guilt.GetDescriptionForPile(PileType.Hand).StripBbCode().Contains("在4场战斗后"),"Guilty shows its native remaining count");
        var tree=(SceneTree)Engine.GetMainLoop();
        var scene=GD.Load<PackedScene>("res://scenes/cards/card.tscn");
        var cardNode=scene.Instantiate<NCard>();cardNode.OnInstantiated();cardNode.Model=guilt;cardNode.Visibility=ModelVisibility.Visible;
        tree.Root.AddChild(cardNode);cardNode.Position=new(600,470);cardNode.UpdateVisuals(PileType.Hand,CardPreviewMode.Normal);
        for(var i=0;i<4;i++)await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);
        var tip=cardNode.Body.GetNode<Control>("CovenantTraitTip");
        var label=tip.GetNode<MegaRichTextLabel>("%Description");
        var bottomInset=tip.Size.Y-(tip.GetGlobalTransform().AffineInverse()*label.GlobalPosition).Y-label.GetContentHeight();
        check(!label.GetParsedText().EndsWith('\n')&&bottomInset>=-2&&bottomInset<18,
            $"Guilty's rendered trait ends at the last line plus only the panel inset ({bottomInset:0.##} px)");
        cardNode.QueueFree();
        await Add<Bash>(PileType.Draw);await Add<DefendIronclad>(PileType.Draw);
        await CardCmd.AutoPlay(context,guilt,null);
        check(guilt.Pile?.Type==PileType.Exhaust&&guiltDeck.CombatsSeen==-2&&guilt.CombatsSeen==-2&&
            terminal.Ledger.Get(Bond.Curse)==4&&state.Hand.Cards.Count>=2,
            "Guilty grants the remaining counter in layers, extends its countdown, draws two and exhausts");

        await clear();
        var origGold=player.Gold;
        await PlayerCmd.SetGold(103,player);
        await Add<Injury>(PileType.Exhaust);await Add<Injury>(PileType.Exhaust);
        var debt=await Add<Debt>();
        before=player.Creature.Block;
        await CardCmd.AutoPlay(context,debt,null);
        check(CardMembership.Of(debt).Contains(Bond.Profit)&&debt.EnergyCost.GetAmountToSpend()==1&&
            debt.Pile?.Type==PileType.Exhaust&&player.Gold==98&&player.Creature.Block==before+5&&terminal.Ledger.Get(Bond.Curse)==5,
            "Debt exchanges the same five gold for Block and Curse layers, exhausts, and counts for Profit");
        await PlayerCmd.SetGold(origGold,player);

        await clear();
        var curseTarget=await Add<Injury>();
        check(!curseTarget.CanPlay(),"the selected curse starts unplayable");
        await CardCmd.AutoPlay(context,await Add<TheHermit>(),null);
        check(curseTarget.Pile?.Type==PileType.Exhaust&&
            CombatManager.Instance.History.CardPlaysFinished.Any(entry=>entry.CardPlay.Card==curseTarget)&&terminal.Ledger.Get(Bond.Curse)==0,
            "Hermit plays and exhausts the selected curse without enabling its inactive Curse trait");
        await clear();
        await Add<Prepared>(PileType.Exhaust);await Add<DaggerThrow>(PileType.Exhaust);await Add<Acrobatics>(PileType.Exhaust);
        var nonCurseTarget=await Add<Bash>();
        for(var i=0;i<4;i++)await Add<DefendIronclad>(PileType.Draw);
        await CardCmd.AutoPlay(context,await Add<TheHermit>(),null);
        check(nonCurseTarget.Pile?.Type==PileType.Discard&&terminal.Ledger.Get(Bond.Sleight)==2,
            $"Hermit still discards a non-curse and grows Sleight (pile={nonCurseTarget.Pile?.Type}, layers={terminal.Ledger.Get(Bond.Sleight)})");

        await clear();
        terminal.Ledger.Add(Bond.Curse,500);
        while(player.Deck.Cards.Count(card=>card.Type!=CardType.Curse)<49)await Add<Bash>(PileType.Deck);
        var eternal=await Add<Bash>(PileType.Deck);eternal.AddKeyword(CardKeyword.Eternal);
        var original=await Add<BadLuck>(PileType.Deck);
        await Add<Injury>(PileType.Exhaust);await Add<Injury>(PileType.Exhaust);
        var badLuck=await Add<BadLuck>(original:original);
        check(CardMembership.CurseTier(player)>0&&!badLuck.CanPlay()&&CurseCovenant.PlayCost(badLuck)==null&&
            !Presentation.TraitText(badLuck).Contains("向上取整"),
            "Bad Luck remains unplayable while Curse is active and omits its rounding implementation");
        await CreatureCmd.Kill(player.Creature);
        check(player.Creature.CurrentHp==player.Creature.MaxHp&&badLuck.Pile==null&&original.Pile==null&&terminal.PendingBadLuckTransforms==1,
            "Bad Luck prevents an unrelated lethal action, removes both copies, and defers the deck transformation");
        var beforeCurses=player.Deck.Cards.Count(card=>card.Type==CardType.Curse);
        var deckCount=player.Deck.Cards.Count;
        await terminal.AfterCombatEnd((CombatRoom)player.RunState.CurrentRoom!);
        check(player.Deck.Cards.Count==deckCount&&player.Deck.Cards.Count(card=>card.Type==CardType.Curse)==beforeCurses+25&&
            eternal.Pile?.Type==PileType.Deck&&terminal.PendingBadLuckTransforms==0,
            "Bad Luck at 500 layers converts 51 percent of 49 eligible cards, rounded up to 25");

        await clear();terminal.BeginTurn();
        for(var i=0;i<3;i++)await Add<SolarStrike>(PileType.Exhaust);
        var shining=await Add<ShiningStrike>();
        await CardCmd.AutoPlay(context,shining,enemy);
        check(terminal.Ledger.Permanent(Bond.Stars)==4,"Shining Strike grows on its first play by the number of stars it provides");

        await clear();terminal.BeginTurn();
        await Add<Sadness>(PileType.Exhaust);await Add<Sadness>(PileType.Exhaust);
        var sorrow=await Add<SorrowfulStrike>();
        await CardCmd.AutoPlay(context,sorrow,enemy);
        check(terminal.Ledger.Get(Bond.Emotion)==6&&!Presentation.TraitText(sorrow).Contains("生成"),
            "Sorrowful Strike grows Sadness/Joy directly and displays no redundant generation condition");

        var badLuckCanonical=ModelDb.Card<BadLuck>();
        var shownPercent=BondDescription.Format(Presentation.TraitText(badLuckCanonical,annotateScaling:true),badLuckCanonical,showFormulas:true);
        check(shownPercent.Contains($"{BondDescription.ScalingColorTag}1%[/color] =(min(100,1+{BondDescription.ScalingColorTag}0[/color]/10))%"),
            "percentage formulas highlight the result including its percent sign and keep the percent unit on the expression");

        await clear();terminal.BeginTurn();
        for(var i=0;i<3;i++)await Add<DeadlyPoison>(PileType.Exhaust);
        foreach(var target in combat.HittableEnemies)await PowerCmd.Apply<PoisonPower>(context,target,1,player.Creature,null);
        var flask=await Add<BouncingFlask>();
        await CardCmd.AutoPlay(context,flask,null);
        check(terminal.Ledger.Permanent(Bond.Poison)==2*flask.DynamicVars.Repeat.IntValue,"Bouncing Flask counts each native application even when the enemies are already poisoned");

        await clear();terminal.BeginTurn();
        for(var i=0;i<4;i++)await Add<Bludgeon>(PileType.Exhaust);
        await CardCmd.AutoPlay(context,await Add<Friendship>(),null);
        check(terminal.Ledger.Permanent(Bond.Heavy)==2,"Friendship grows on its visible maximum-energy increase");

        await clear();terminal.BeginTurn();
        await CardCmd.AutoPlay(context,await Add<Juggling>(),null);
        for(var i=0;i<BondCatalog.Get(Bond.Creation).Basic;i++)await Add<Juggling>(PileType.Exhaust);
        await player.Creature.GetPower<JugglingPower>()!.AfterSideTurnEnd(context,player.Creature.Side,[player.Creature]);
        for(var i=0;i<3;i++)await CardCmd.AutoPlay(context,await Add<StrikeIronclad>(),enemy);
        var copy=state.Hand.Cards.Single(card=>card.IsClone);
        terminal.BeginTurn();
        await CardCmd.AutoPlay(context,copy,enemy);
        terminal.BeginTurn();
        var skill=await Add<DefendIronclad>();
        await CardCmd.AutoPlay(context,skill,null);
        check(CombatManager.Instance.History.CardPlaysFinished.Count(entry=>entry.CardPlay.Card==skill)==2,
            "Juggling's copy and the following Skill can be played on later turns");
        Console.WriteLine("COVENANT_CURSES_COMPLETE");
    }
}
