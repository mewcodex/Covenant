using System.Text.Json;
using Covenant.Core;
using Covenant.Game;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

internal static class ApprovedTraitsProbe
{
    internal static async Task Run(SceneTree tree)
    {
        var player=RunManager.Instance.DebugOnlyGetState()!.Players.Single();
        var terminal=CovenantTerminal.For(player)!;
        var combat=player.Creature.CombatState!;
        var state=player.PlayerCombatState!;
        var context=new ThrowingPlayerChoiceContext();
        var enemy=combat.HittableEnemies.First();
        for(var i=0;i<100&&state.Phase!=PlayerTurnPhase.Play;i++)await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);

        void Check(bool passed,string message)
        {
            if(!passed)throw new Exception(message);
            Console.WriteLine("COVENANT_APPROVED_PASS "+message);
        }
        async Task Clear()
        {
            await CardPileCmd.RemoveFromCombat(state.AllCards.ToArray());
            foreach(var power in player.Creature.Powers.Where(power=>power is not CovenantAttritionPower).ToArray())await PowerCmd.Remove(power);
            terminal.ResetTransient();terminal.Ledger.Layers.Clear();terminal.Ledger.ClearCombatUses();
            enemy.SetMaxHpInternal(2000);enemy.SetCurrentHpInternal(2000);
            player.Creature.SetCurrentHpInternal(player.Creature.MaxHp);
            await CreatureCmd.LoseBlock(context,player.Creature,player.Creature.Block,null);
        }
        async Task<T> Add<T>(PileType pile=PileType.Hand,CardModel? original=null) where T:CardModel
        {
            var card=combat.CreateCard<T>(player);card.DeckVersion=original;
            await CardPileCmd.Add(card,pile,skipVisuals:true);
            return card;
        }
        async Task<T> Deck<T>() where T:CardModel
        {
            var card=player.RunState.CreateCard<T>(player);
            await CardPileCmd.Add(card,PileType.Deck,skipVisuals:true);return card;
        }
        Task Play(CardModel card)=>CardCmd.AutoPlay(context,card,card.TargetType==TargetType.AnyEnemy?enemy:null);

        var pools=CovenantTerminal.SixPools.Append(ModelDb.CardPool<ColorlessCardPool>()).ToArray();
        var stats=pools.SelectMany(pool=>pool.AllCards).DistinctBy(card=>card.Id)
            .Where(card=>card.MultiplayerConstraint!=CardMultiplayerConstraint.MultiplayerOnly)
            .GroupBy(card=>card.Rarity).OrderBy(group=>group.Key).ToDictionary(group=>group.Key.ToString(),group=>group.Count());
        Console.WriteLine("COVENANT_ACTIVE_CARD_POOLS "+JsonSerializer.Serialize(stats));
        Console.WriteLine("COVENANT_THRESHOLD_COUNTS "+JsonSerializer.Serialize(BondCatalog.All.Select(bond=>new{bond.Name,bond.Group,bond.Basic,bond.Advanced})));

        var eligible=pools.SelectMany(pool=>pool.AllCards).Where(card=>card.MultiplayerConstraint!=CardMultiplayerConstraint.MultiplayerOnly).Select(card=>card.Id).ToHashSet();
        var catalog=await CovenantLibrary.PrepareMembership(tree);
        Console.WriteLine("COVENANT_ZERO_COUNT "+catalog[Bond.Zero].Where(card=>eligible.Contains(card.Id)).Select(card=>card.Id).Distinct().Count());
        var ember=catalog[Bond.Ember].Select(card=>card.Id).Distinct().ToArray();
        var removedEmber=ModelDb.AllCards.Where(card=>CardMembership.Definition(card)?.EmberExcluded==true).ToArray();
        Console.WriteLine("COVENANT_EMBER_COUNTS "+JsonSerializer.Serialize(new
        {
            all=ember.Length,soloPool=ember.Count(eligible.Contains),
            excluded=removedEmber.Length,excludedSoloPool=removedEmber.Count(card=>eligible.Contains(card.Id))
        }));
        Check(removedEmber.Length==19&&removedEmber.All(card=>
        {
            var upgraded=(CardModel)card.MutableClone();
            upgraded.UpgradeInternal();upgraded.FinalizeUpgradeInternal();
            return card.Keywords.Contains(CardKeyword.Exhaust)&&!upgraded.Keywords.Contains(CardKeyword.Exhaust)&&
                !CardMembership.Of(card).Contains(Bond.Ember)&&!CardMembership.Of(upgraded).Contains(Bond.Ember)&&!ember.Contains(card.Id);
        }),"all nineteen printed Exhaust removals are excluded at both levels and in the covenant library");
        Check(ember.Count(eligible.Contains)==83&&ember.Length==134&&CardMembership.Of(ModelDb.Card<BurningPact>()).Contains(Bond.Ember),
            "Ember counts 83 single-player full-pool cards and 134 total, including existing Exhaust support");

        await Clear();
        var adrenaline=await Add<Adrenaline>();CardCmd.Upgrade(adrenaline);
        var chill=await Add<Chill>();CardCmd.Upgrade(chill);
        Check(adrenaline.Keywords.Contains(CardKeyword.Exhaust)&&!CardMembership.Of(adrenaline).Contains(Bond.Zero)&&
            RevisionDescriptions.Text(adrenaline).Contains("【连携】")&&CardMembership.Of(chill).Contains(Bond.Zero),
            "Zero excludes persistent self-exhaust, retains eligibility after Exhaust is removed by upgrade, and redirects Adrenaline's trait");
        chill.AddKeyword(CardKeyword.Exhaust);
        Check(!CardMembership.Of(chill).Contains(Bond.Ember),"temporarily restoring Exhaust does not restore Ember membership");
        var compost=await Add<Compost>();
        var automation=await Add<Automation>();CardCmd.Upgrade(automation);
        var feral=await Add<Feral>();
        Check(!CardMembership.Of(compost).Contains(Bond.Zero)&&RevisionDescriptions.Text(compost).Contains("【仪式】")&&
            CardMembership.IsPrintedZero(automation)&&!CardMembership.Of(automation).Contains(Bond.Zero)&&CardMembership.Of(feral).Contains(Bond.Zero),
            "Zero excludes printed free Powers and preserves paid supports, while Compost grows Ritual");
        feral.EnergyCost.SetThisTurnOrUntilPlayed(0);
        var gammaBlast=await Add<GammaBlast>();gammaBlast.SetStarCostThisTurn(0);
        var stardust=await Add<Stardust>();
        Check(!CardMembership.Of(feral).Contains(Bond.Zero)&&!CardMembership.Of(gammaBlast).Contains(Bond.Zero)&&
            !CardMembership.Of(stardust).Contains(Bond.Zero),"Zero excludes temporarily free Powers, discounted star costs and star X costs");
        var channelPool=catalog[Bond.Channel].Where(card=>eligible.Contains(card.Id)).DistinctBy(card=>card.Id).ToArray();
        Console.WriteLine("COVENANT_CHANNEL_POOL "+JsonSerializer.Serialize(channelPool.Select(card=>new{card=card.GetType().Name,title=Presentation.CardTitle(card)})));
        Check(channelPool.Length==13&&channelPool.Count(card=>card.HasStarCostX)==1&&CardMembership.Of(stardust).Contains(Bond.Channel)&&
            !Presentation.Icon(Bond.Channel).GetSize().IsZeroApprox()&&!Presentation.Icon(Bond.Aid).GetSize().IsZeroApprox(),
            "Channel has twelve energy X cards and Stardust, and both icons load");

        await Clear();
        var brace=await Add<Brace>(PileType.Draw);
        brace.AddKeyword(CardKeyword.Retain);
        for(var i=0;i<5;i++)await Add<Skewer>(PileType.Exhaust);
        terminal.Ledger.Add(Bond.Channel,500);
        await terminal.BeforeHandDraw(player,context,combat);
        await PlayerCmd.SetEnergy(2,player);
        Check(await CardPileCmd.Draw(context,player)==brace&&state.Energy==3&&brace.ShouldRetainThisTurn,
            "Channel's 250 milestone can select an already Retained card and grants energy on its first draw");
        await terminal.AfterSideTurnEndLate(context,CombatSide.Player,new[]{player.Creature});
        Check(state.Energy==0&&player.Creature.Powers.OfType<CovenantEffectPower>().Any(power=>power.EffectName=="储能"&&power.DisplayAmount==3),
            "Channel II stores unused energy in a visible three-point status");
        await PlayerCmd.SetEnergy(1,player);
        await terminal.AfterSideTurnEndLate(context,CombatSide.Player,new[]{player.Creature});
        var strike=await Add<StrikeIronclad>();
        await PlayerCmd.SetEnergy(2,player);
        Check((await strike.SpendResources()).Item1==1&&state.Energy==1,"Fixed costs leave stored energy untouched");
        var (braceEnergy,braceStars)=await brace.SpendResources();
        await brace.OnPlayWrapper(context,null,false,new ResourceInfo{EnergySpent=braceEnergy,EnergyValue=braceEnergy,StarsSpent=braceStars,StarValue=braceStars});
        Check(braceEnergy==5&&state.Energy==0&&player.Creature.GetPowerAmount<AimPower>()==72&&
            !player.Creature.Powers.OfType<CovenantEffectPower>().Any(power=>power.EffectName=="储能"),
            "Channel spends accumulated energy before payment, adds four to the resolved X, and clears the storage status");
        await CardPileCmd.Add(brace,PileType.Draw);
        Check(await CardPileCmd.Draw(context,player)==brace&&state.Energy==1,"The marked X card grants energy again when redrawn");

        await Clear();
        var starX=await Add<Stardust>(PileType.Draw);
        for(var i=0;i<5;i++)await Add<Skewer>(PileType.Exhaust);
        terminal.Ledger.Add(Bond.Channel,500);
        await terminal.BeforeHandDraw(player,context,combat);
        await PlayerCmd.SetEnergy(1,player);
        Check(await CardPileCmd.Draw(context,player)==starX&&starX.ShouldRetainThisTurn&&state.Energy==2,
            "Stardust is selectable for the milestone and grants energy on draw");
        await terminal.AfterSideTurnEndLate(context,CombatSide.Player,new[]{player.Creature});
        await PlayerCmd.SetStars(5,player);
        var (starXEnergy,starXStars)=await starX.SpendResources();
        await starX.OnPlayWrapper(context,null,false,new ResourceInfo{EnergySpent=starXEnergy,EnergyValue=starXEnergy,StarsSpent=starXStars,StarValue=starXStars});
        Check(starXEnergy==0&&starXStars==5&&state.Energy==2&&state.Stars==0&&starX.ResolveStarXValue()==9,
            "Stardust returns storage as energy and the four-point X bonus uses its five paid stars");

        await Clear();
        for(var i=0;i<11;i++)await Add<IronWave>(PileType.Exhaust);
        var guard=await Add<IronWave>();var unretained=await Add<StrikeIronclad>();
        terminal.Ledger.Add(Bond.Guard,250);
        await terminal.AfterBlockGained(player.Creature,5,ValueProp.Move,guard);
        Check(player.Creature.Block==8&&!unretained.ShouldRetainThisTurn,"Guard at 250 layers grants seven first-block bonus and one milestone Block without hand Retain");
        await terminal.AfterBlockGained(player.Creature,5,ValueProp.Move,guard);
        Check(player.Creature.Block==9,"Guard's 250-layer milestone adds exactly one Block on subsequent card-based gains");
        await CreatureCmd.GainBlock(player.Creature,30,ValueProp.Unpowered,null);
        await terminal.AfterPreventingBlockClear(terminal,player.Creature);
        Check(player.Creature.Block==13,"Guard II keeps up to thirteen Block at 250 layers");

        await Clear();
        var guilt=await Deck<Guilty>();guilt.CombatsSeen=4;
        var guilty=await Add<Guilty>(original:guilt);
        await Add<DefendIronclad>(PileType.Draw);await Add<DefendIronclad>(PileType.Draw);
        await PlayerCmd.SetEnergy(2,player);
        Check(guilty.CanPlay()&&guilty.EnergyCost.GetAmountToSpend()==1,"Guilty is playable for one energy");
        var (energy,stars)=await guilty.SpendResources();
        await guilty.OnPlayWrapper(context,null,false,new ResourceInfo{EnergySpent=energy,EnergyValue=energy,StarsSpent=stars,StarValue=stars});
        Check(energy==1&&state.Hand.Cards.Count==2&&terminal.Ledger.Get(Bond.Curse)==1&&
            guilt.Pile?.Type==PileType.Deck&&guilty.Pile?.Type==PileType.Exhaust&&guilt.CombatsSeen==1&&guilty.CombatsSeen==1,
            "Guilty draws two, grants its remaining count, adds three more battles and exhausts");

        await Clear();
        var egg=await Deck<ByrdonisEgg>();var eggCard=await Add<ByrdonisEgg>(original:egg);
        var startingSolitude=terminal.Counts(deck:true).GetValueOrDefault(Bond.Solitude);
        Check(eggCard.CanPlay()&&eggCard.EnergyCost.GetAmountToSpend()==0&&CardMembership.Of(eggCard).Contains(Bond.Aid)&&
            startingSolitude==player.Deck.Cards.Count,"the egg is playable for free and counts for Solitude");
        await Play(eggCard);
        Check(CovenantTerminal.GetBirdExperience(egg)==1,"playing the egg updates the matching permanent card's experience");
        var swoop=(await CardCmd.TransformTo<ByrdSwoop>(egg))!.Value.cardAdded;
        var god=(await CardCmd.TransformTo<GodByrd>(swoop))!.Value.cardAdded;
        Check(CovenantTerminal.GetBirdExperience(swoop)==1&&CovenantTerminal.GetBirdExperience(god)==1,"both native transformations carry the experience forward");
        Check(CovenantTerminal.GetBirdExperience((CardModel)god.MutableClone())==1,"inspection copies report the bird's current experience");
        var saved=Player.FromSerializable(player.ToSerializable());
        Check(CovenantTerminal.GetBirdExperience(saved.Deck.Cards.OfType<GodByrd>().Single())==1,"experience survives native player serialization");
        terminal.BirdExperienceByDeckOrder=[99];terminal.RestoreBirdExperience();
        for(var i=0;i<3;i++)await Add<DeadlyPoison>(PileType.Exhaust);
        var godCard=await Add<GodByrd>(original:god);
        await Play(godCard);
        Check(CovenantTerminal.GetBirdExperience(god)==100&&terminal.Ledger.Get(Bond.Poison)==12,"God Byrd caps experience at 100 and uses the new experience for growth");

        await Clear();
        for(var i=0;i<6;i++)await Add<Slice>(PileType.Exhaust);
        for(var i=0;i<3;i++)await Add<DeadlyPoison>(PileType.Exhaust);
        terminal.Ledger.Add(Bond.Aid,150);
        for(var i=0;i<3;i++)await Play(await Add<Slice>());
        Check(terminal.Ledger.Get(Bond.Poison)==4&&terminal.Ledger.Get(Bond.Aid)==154,"Aid II grants two layers twice at 150 layers and includes Aid itself");

        await Clear();
        var bomb=await Add<TheBomb>();CardCmd.Upgrade(bomb);await Play(bomb);
        var charge=player.Creature.GetPower<TheBombPower>()!;
        terminal.Ledger.Add(Bond.Guard,20);
        await PlayerCmd.SetEnergy(20,player);
        await (await Add<Bludgeon>()).SpendResources();
        Check(charge.Amount==2,"spending three energy immediately advances the existing bomb once");
        await CreatureCmd.GainBlock(enemy,100,ValueProp.Unpowered,null);
        var health=enemy.CurrentHp;
        await (await Add<Bludgeon>()).SpendResources();await (await Add<Bludgeon>()).SpendResources();
        Check(!player.Creature.Powers.Contains(charge)&&enemy.CurrentHp==health-50&&enemy.Block==100&&terminal.Ledger.Get(Bond.Guard)==36,
            "an upgraded accelerated bomb bypasses block and grants sixteen layers to the highest covenant");

        await Clear();
        var finale=await Add<GrandFinale>();
        var first=await Add<DefendIronclad>(PileType.Draw);var second=await Add<DefendIronclad>(PileType.Draw);
        Check(finale.CanPlay()&&!finale.GetDescriptionForPile(PileType.Hand).Contains("只有"),"Grand Finale can be played from a nonempty draw pile and drops the old restriction text");
        await Play(finale);
        Check(state.DrawPile.Cards.Count==0&&first.Pile?.Type is PileType.Discard or PileType.Exhaust&&second.Pile?.Type is PileType.Discard or PileType.Exhaust&&
            player.Creature.CurrentHp==player.Creature.MaxHp-2,"Grand Finale processes every card and incurs one damage per card at zero layers");
        terminal.Ledger.Add(Bond.Guard,1);
        Check(Presentation.TraitText(finale,annotateScaling:true).Contains("99.95"),"Grand Finale shows the precise reduction from the total layers");

        await Clear();
        terminal.Ledger.Add(Bond.Poison,250);
        var snake=await Add<Snakebite>();CardCmd.Upgrade(snake);
        await Play(await Add<DeadlyPoison>());
        var poison=enemy.GetPowerAmount<PoisonPower>();
        await Play(snake);
        Check(enemy.GetPowerAmount<PoisonPower>()-poison==13,"Snakebite+ adds stored poison based on Poison layers");
        var pendingSnake=await Add<Snakebite>();
        for(var i=0;i<5&&pendingSnake.Pile?.Type==PileType.Hand;i++)await terminal.AfterFlush(context,player,[],[pendingSnake]);
        Check(pendingSnake.Pile?.Type!=PileType.Hand,"five retained turns guarantee Snakebite auto-play");

        await Clear();
        await Deck<Deflect>();await Deck<Slice>();
        terminal.Ledger.Add(Bond.Poison,250);terminal.Ledger.Add(Bond.Aid,100);
        var badLuck=await Deck<BadLuck>();var combatCurse=await Add<BadLuck>(original:badLuck);
        player.Creature.SetCurrentHpInternal(10);
        await CreatureCmd.Damage(context,player.Creature,13,ValueProp.Unpowered|ValueProp.Unblockable,combatCurse,null);
        Check(player.Creature.CurrentHp==player.Creature.MaxHp&&badLuck.Pile==null&&combatCurse.Pile==null&&terminal.PendingBadLuckTransforms==1,
            "native lethal damage restores full health, removes Eternal Bad Luck and queues transformation");
        var deckSize=player.Deck.Cards.Count;
        await terminal.AfterCombatEnd((CombatRoom)player.RunState.CurrentRoom!);
        Check(player.Deck.Cards.Count==deckSize&&terminal.PendingBadLuckTransforms==0&&terminal.Ledger.Get(Bond.Poison)==258,
            "combat end resolves the deck transformation and applies Aid I once");
        Console.WriteLine("COVENANT_APPROVED_COMPLETE");
    }
}
