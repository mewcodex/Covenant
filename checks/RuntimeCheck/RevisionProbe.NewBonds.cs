using Covenant.Core;
using Covenant.Game;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

internal static partial class RevisionProbe
{
    private static async Task NewBonds(Player player,CovenantTerminal terminal,Choices selector,Func<Task> clear,Action<bool,string> check)
    {
        var combat=player.Creature.CombatState!;
        var state=player.PlayerCombatState!;
        var creature=player.Creature;
        var enemy=combat.HittableEnemies.First();
        var context=new BlockingPlayerChoiceContext();
        async Task<T> Add<T>(PileType destination=PileType.Hand,bool upgraded=false) where T:CardModel
        {
            var card=combat.CreateCard<T>(player);
            if(upgraded)CardCmd.Upgrade(card);
            await CardPileCmd.Add(card,destination);
            return card;
        }
        async Task Copies<T>(int count,PileType destination=PileType.Exhaust) where T:CardModel
        {
            for(var i=0;i<count;i++)await Add<T>(destination);
        }
        Task Play(CardModel card)=>CardCmd.AutoPlay(context,card,enemy);
        async Task Manual(CardModel card,bool paidAutoPlay=false)
        {
            var cost=card.EnergyCost.GetAmountToSpend();
            var stars=card.GetStarCostWithModifiers();
            var (energySpent,starsSpent)=await card.SpendResources();
            if(paidAutoPlay)await CardPileCmd.Add(card,PileType.Play);
            await card.OnPlayWrapper(context,enemy,paidAutoPlay,new ResourceInfo
                {EnergySpent=energySpent,EnergyValue=cost,StarsSpent=starsSpent,StarValue=stars});
        }
        bool In<T>(Bond b) where T:CardModel=>CardMembership.Of(ModelDb.Card<T>()).Contains(b);

        await clear();terminal.BeginTurn();
        await Copies<BootSequence>(4);await Copies<Bludgeon>(12,PileType.Draw);
        await Copies<Uppercut>(40,PileType.Draw);await Copies<Bash>(5,PileType.Hand);
        terminal.Ledger.Add(Bond.Heavy,250);
        for(var i=0;i<20;i++)_ = terminal.Counts();
        const int countSamples=200;
        var allocatedBefore=GC.GetAllocatedBytesForCurrentThread();
        var timeBefore=System.Diagnostics.Stopwatch.GetTimestamp();
        for(var i=0;i<countSamples;i++)_ = terminal.Counts();
        var elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(timeBefore);
        var allocated=GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
        Console.WriteLine(FormattableString.Invariant($"COVENANT_COUNT_PERF cards=61 calls={countSamples} ms={elapsed.TotalMilliseconds:F2} bytes={allocated}"));
        await clear();

        terminal.BeginTurn();
        var heavyThreshold=BondCatalog.Get(Bond.Heavy).Basic;
        await Copies<Bludgeon>(heavyThreshold-2);
        var innerPlay=await Add<Bludgeon>(PileType.Discard);
        var outerPlay=await Add<BeatDown>();
        var deckBefore=terminal.Counts(deck:true);
        var countedDuringNestedPlay=false;
        var nestedPlay="inner card did not finish executing";
        innerPlay.ExecutionFinished+=_=>
        {
            if(outerPlay.Pile?.Type!=PileType.Play||innerPlay.Pile?.Type!=PileType.Play)return;
            var live=terminal.Counts().GetValueOrDefault(Bond.Heavy);
            var tier=terminal.Tier(Bond.Heavy);
            var cost=CardMembership.CountForCost(player,Bond.Heavy);
            var resolving=CardMembership.Counts(player,resolving:innerPlay).GetValueOrDefault(Bond.Heavy);
            nestedPlay=$"outer {outerPlay.Pile?.Type}, inner {innerPlay.Pile?.Type}, counts {live}/{cost}/{resolving}, tier {tier}";
            countedDuringNestedPlay=live==heavyThreshold&&tier==1&&cost==heavyThreshold&&resolving==heavyThreshold;
        };
        await Play(outerPlay);
        var afterNestedPlay=terminal.Counts().GetValueOrDefault(Bond.Heavy);
        var sameDeck=terminal.Counts(deck:true).SequenceEqual(deckBefore);
        check(countedDuringNestedPlay&&afterNestedPlay==heavyThreshold&&sameDeck,
            $"nested native plays both count once towards Heavy in live, cost and resolving queries, without changing deck counts ({nestedPlay}; after {afterNestedPlay}; same deck {sameDeck})");

        await clear();terminal.BeginTurn();await Copies<BootSequence>(3);
        var playedPower=await Add<MachineLearning>(upgraded:true);
        var powerCountedWhileResolving=false;
        playedPower.ExecutionFinished+=_=>
        {
            if(playedPower.Pile?.Type==PileType.Play)powerCountedWhileResolving=terminal.Tier(Bond.Initiative)==2;
        };
        await Play(playedPower);
        check(powerCountedWhileResolving&&playedPower.Pile==null&&terminal.Tier(Bond.Initiative)==1,
            "an Innate power counts while resolving and leaves the live count after native removal");
        await clear();

        check(In<Skim>(Bond.Rotation)&&!In<Skim>(Bond.Chain)&&In<Stratagem>(Bond.Rotation)&&In<Reboot>(Bond.Rotation),
            "draw and shuffle memberships migrate to Rotation");
        check(In<Speedster>(Bond.Rotation)&&In<PaleBlueDot>(Bond.Rotation)&&In<Automation>(Bond.Rotation)&&
            !In<PaleBlueDot>(Bond.Chain),"draw-related traits previously missing a Chain badge also get Rotation");
        check(In<DrumOfBattle>(Bond.Chain)&&In<DrumOfBattle>(Bond.Rotation)&&In<Flechettes>(Bond.Chain)&&
            In<Cascade>(Bond.Chain)&&!In<Cascade>(Bond.Rotation),
            "cross-card combo memberships stay");
        var learningPlus=ModelDb.Card<MachineLearning>().ToMutable();
        learningPlus.UpgradeInternal();learningPlus.FinalizeUpgradeInternal();
        check(In<BootSequence>(Bond.Initiative)&&In<Backstab>(Bond.Initiative)&&In<Writhe>(Bond.Initiative)&&
            !In<MachineLearning>(Bond.Initiative)&&CardMembership.Of(learningPlus).Contains(Bond.Initiative),
            "Innate cards count including Curses and only qualifying upgrade states");
        check(BondCatalog.Milestones(Bond.Initiative,new(999,50)).Count==0,
            "Initiative has no layer milestone");
        check(In<MeteorShower>(Bond.Stars)&&In<MeteorShower>(Bond.Breach)&&In<MeteorShower>(Bond.Ancient),
            "Meteor Shower counts both trait covenants and Ancient");
        check(In<Compact>(Bond.Status)&&In<FlakCannon>(Bond.Status)&&In<RocketPunch>(Bond.Status)&&
            In<Voltaic>(Bond.Overload)&&In<Dredge>(Bond.Retain)&&In<WellLaidPlans>(Bond.Retain),
            "six complementary cards qualify for Status, Overload, and Retain");

        await clear();terminal.BeginTurn();terminal.Ledger.Add(Bond.Initiative,250);
        var boot=await Add<BootSequence>();
        await Add<Backstab>();await Add<MindBlast>();await Add<Assassinate>();
        var bash=await Add<Bash>();var uppercut=await Add<Uppercut>();
        var openingBlock=creature.Block;
        check(terminal.ModifyHandDraw(player,5)==6,"Initiative I adds one opening draw");
        await terminal.AfterPlayerTurnStart(context,player);
        check(creature.Block-openingBlock==7,"Initiative grants opening Block before paying for or playing any card");
        check(bash.EnergyCost.GetAmountToSpend()==1&&uppercut.EnergyCost.GetAmountToSpend()==1,
            "Initiative II displays the shared discount on eligible opening plays");
        state.Energy=3;
        await Manual(bash);
        check(state.Energy==2&&uppercut.EnergyCost.GetAmountToSpend()==2,
            "the first eligible manual play consumes exactly one shared Initiative discount");
        await Hook.BeforeFlush(combat,player);
        await Hook.AfterFlush(combat,player,context,[],[boot]);
        check(boot.ShouldRetainThisTurn&&terminal.Ledger.Get(Bond.Initiative)==255,
            "retaining Boot Sequence on the opening turn grants Initiative layers");
        terminal.BeginTurn();
        check(terminal.ModifyHandDraw(player,5)==5&&uppercut.EnergyCost.GetAmountToSpend()==2,
            "Initiative draws and discounts expire after the first turn");

        await clear();terminal.BeginTurn();await Copies<BootSequence>(4);
        uppercut=await Add<Uppercut>();
        await Play(await Add<Bash>());
        check(uppercut.EnergyCost.GetAmountToSpend()==1,"free autoplay leaves the Initiative discount available");
        terminal.Ledger.Add(Bond.Initiative,999);
        terminal.BeginTurn();
        check(uppercut.EnergyCost.GetAmountToSpend()==2,"even at 999 layers an unused discount expires");

        await clear();terminal.BeginTurn();await Copies<BootSequence>(4);
        bash=await Add<Bash>();uppercut=await Add<Uppercut>();
        state.Energy=3;
        await Manual(bash,paidAutoPlay:true);
        check(state.Energy==2&&uppercut.EnergyCost.GetAmountToSpend()==2,
            "paid autoplay consumes the Initiative discount when it uses the reduced cost");

        await clear();terminal.BeginTurn();await Copies<BootSequence>(3);
        var apotheosis=await Add<Apotheosis>();bash=await Add<Bash>();
        state.Energy=3;
        check(apotheosis.EnergyCost.GetAmountToSpend()==1,"the fourth Innate card receives the Initiative discount while still in hand");
        await Manual(apotheosis);
        check(state.Energy==2&&bash.EnergyCost.GetAmountToSpend()==2,
            "the fourth Innate card remains eligible while resolving and consumes the discount before it exhausts");

        await clear();terminal.BeginTurn();await Copies<BootSequence>(4);await Copies<Bludgeon>(2);
        terminal.Ledger.Add(Bond.Heavy,250);
        var bludgeon=await Add<Bludgeon>();bash=await Add<Bash>();state.Energy=3;
        check(bludgeon.EnergyCost.GetAmountToSpend()==1,"Heavy and Initiative both discount the threshold card in hand");
        await Manual(bludgeon);
        check(state.Energy==2&&bash.EnergyCost.GetAmountToSpend()==2,
            "the paid Initiative discount is consumed even when Heavy supplied the first reduction");

        await clear();terminal.BeginTurn();
        await Play(await Add<Assassinate>());
        check(enemy.GetPowerAmount<VulnerablePower>()>0&&terminal.Ledger.Get(Bond.Initiative)==0,
            "Assassinate applies its native Vulnerable without growing an inactive Initiative");
        await clear();terminal.BeginTurn();
        await Play(await Add<Suppress>());
        check(enemy.GetPowerAmount<WeakPower>()>0&&terminal.Ledger.Get(Bond.Initiative)==0,
            "Suppress applies its native Weak without growing an inactive Initiative");

        await clear();terminal.BeginTurn();await Copies<Bash>(3);
        await PowerCmd.Apply<VulnerablePower>(context,enemy,1,enemy,null);
        await Play(await Add<Bash>());
        check(terminal.Ledger.Get(Bond.Breach)==4&&terminal.Ledger.Get(Bond.Heavy)==0,
            "Bash adds four Breach layers on play");

        await clear();terminal.BeginTurn();await Copies<Whirlwind>(3);
        state.Energy=3;
        await Manual(await Add<Malaise>());
        check(terminal.Ledger.Get(Bond.Channel)==8,"Malaise uses the resolved X including Channel's bonus");
        state.Energy=3;
        await Manual(await Add<HeavenlyDrill>());
        check(terminal.Ledger.Get(Bond.Channel)==14,"Heavenly Drill gains layers when Channel's bonus reaches its doubling threshold");

        await clear();terminal.BeginTurn();await Copies<Whirlwind>(2);
        state.Stars=2;
        await PowerCmd.Apply<DuplicationPower>(context,creature,1,creature,null);
        await Manual(await Add<Stardust>());
        check(state.Stars==0&&terminal.Ledger.Get(Bond.Channel)==2,
            "duplicated Stardust counts the two actually paid stars only once for Channel");

        await clear();terminal.BeginTurn();await Copies<Headbutt>(3);
        var returned=await Add<StrikeIronclad>(PileType.Discard);
        await Add<DefendIronclad>();
        selector.Target=returned;
        await Play(await Add<Headbutt>());
        selector.Target=null;
        check(returned.Pile?.Type is PileType.Draw or PileType.Hand&&terminal.Ledger.Get(Bond.Foresight)==0,
            "Headbutt puts the selected card on top without immediate Foresight growth");

        await clear(); await Copies<Bash>(6);
        terminal.BeginTurn();terminal.Ledger.Add(Bond.Breach,999);
        await PowerCmd.Apply<ArtifactPower>(context,enemy,1,enemy,null);
        await PowerCmd.Apply<WeakPower>(context,enemy,1,creature,null);
        check(creature.GetPowerAmount<VigorPower>()==0,"Artifact blocking Vulnerable or Weak does not trigger Breach");
        await PowerCmd.Apply<VulnerablePower>(context,enemy,1,creature,null);
        await PowerCmd.Apply<VulnerablePower>(context,enemy,1,creature,null);
        check(creature.GetPowerAmount<VigorPower>()==21&&creature.GetPowerAmount<StretchPower>()==0,
            "Breach grants Vigor only on the first successful type");
        await PowerCmd.Apply<WeakPower>(context,enemy,1,creature,null);
        check(creature.GetPowerAmount<StretchPower>()==21,"the other debuff grants Stretch");
        check(enemy.GetPower<VulnerablePower>()!.ModifyDamageMultiplicative(enemy,100,ValueProp.Move,creature,null,null)==1.88m&&
            enemy.GetPower<WeakPower>()!.ModifyDamageMultiplicative(creature,100,ValueProp.Move,enemy,null,null)==.56m&&
            enemy.GetPower<WeakPower>()!.ModifyDamageMultiplicative(creature,100,ValueProp.Unpowered,enemy,null,null)==1m,
            "999 layers gives 188% Vulnerable and 56% Weak without changing unpowered damage");
        await Play(await Add<StrikeIronclad>());
        var weak=enemy.GetPowerAmount<WeakPower>();var vulnerable=enemy.GetPowerAmount<VulnerablePower>();
        check(weak==2&&vulnerable==3,"Breach II can apply both debuffs to the sole enemy");
        await Play(await Add<StrikeIronclad>());
        check(enemy.GetPowerAmount<WeakPower>()==weak&&enemy.GetPowerAmount<VulnerablePower>()==vulnerable,
            "Breach II triggers once per turn");

        await clear(); await Copies<Skim>(4); terminal.BeginTurn();
        terminal.Ledger.Add(Bond.Rotation,400);
        await Add<Dazed>(PileType.Draw);
        await Copies<Bash>(4,PileType.Draw);
        var blockBeforeDraw=creature.Block;
        await Play(await Add<Skim>());
        check(state.Hand.Cards.Count==3&&state.Hand.Cards.Any(card=>card is Dazed)&&
            state.Hand.Cards.Count(card=>card.ShouldRetainThisTurn)==1&&creature.Block-blockBeforeDraw==9,
            $"Rotation I grants scaling Block per card-effect draw including status, retaining the first eligible card (Block gained: {creature.Block-blockBeforeDraw})");
        blockBeforeDraw=creature.Block;
        await CardPileCmd.Draw(context,1,player,fromHandDraw:true);
        check(creature.Block==blockBeforeDraw,"ordinary draws grant no Rotation Block");

        await clear();await Copies<Skim>(4);terminal.BeginTurn();
        await Play(await Add<MachineLearning>());
        await Copies<Bash>(6,PileType.Draw);
        var drawCount=Hook.ModifyHandDraw(combat,player,5,out var drawModifiers);
        await Hook.AfterModifyingHandDraw(combat,drawModifiers);
        blockBeforeDraw=creature.Block;
        await CardPileCmd.Draw(context,drawCount,player,fromHandDraw:true);
        check(state.Hand.Cards.Count==6&&creature.Block-blockBeforeDraw==1&&state.Hand.Cards.Last().ShouldRetainThisTurn,
            "Rotation I credits only the actual extra opening draw from Machine Learning");

        await clear(); await Copies<Skim>(9); terminal.BeginTurn();
        await Copies<Bash>(23,PileType.Discard);await Copies<StrikeIronclad>(7,PileType.Hand);
        var blockBeforeReboot=creature.Block;
        await Play(await Add<Reboot>());
        check(creature.Block-blockBeforeReboot==4&&creature.GetPowerAmount<DuplicationPower>()==1,
            "Reboot counts 30 returned cards for duplication and only its four actual draws for Block");

        await clear(); await Copies<Skim>(9); terminal.BeginTurn();terminal.Ledger.Add(Bond.Rotation,350);
        await Copies<StrikeIronclad>(18,PileType.Draw);
        await CardPileCmd.Shuffle(context,player);
        await terminal.AfterSideTurnEndLate(context,CombatSide.Player,[creature]);
        check(creature.GetPower<ExtraTurnPower>()==null,"shuffling with no cards returned does not use the first Rotation shuffle");
        await Copies<Bash>(22,PileType.Discard);
        var blockBeforeShuffle=creature.Block;
        await CardPileCmd.Shuffle(context,player);
        check(creature.Block==blockBeforeShuffle&&creature.GetPowerAmount<DuplicationPower>()==0,
            "a plain shuffle grants no Block and only the 22 returned cards count for duplication");
        check(creature.Powers.OfType<CovenantEffectPower>().Any(status=>status.IconBond==Bond.Rotation&&status.EffectText.Contains("额外回合")),
            "first shuffle exposes the pending extra turn");
        await terminal.AfterSideTurnEndLate(context,CombatSide.Player,[creature]);
        check(creature.GetPower<ExtraTurnPower>()?.ShouldTakeExtraTurn(player)==true,
            "end of the first shuffle turn schedules a native extra turn");
        check(Hook.ShouldTakeExtraTurn(combat,player,out var extraSource)&&extraSource is ExtraTurnPower,
            "the native turn switch sees Rotation's extra turn");
        await PowerCmd.Remove(creature.GetPower<ExtraTurnPower>()!);
        await Copies<Bash>(23,PileType.Discard);await CardPileCmd.Shuffle(context,player);
        check(creature.GetPowerAmount<DuplicationPower>()==1,"350 layers grants native duplication per 23 returned cards");
        await terminal.AfterSideTurnEndLate(context,CombatSide.Player,[creature]);
        check(creature.GetPower<ExtraTurnPower>()==null,"later shuffles never grant a second extra turn");
        var history=CombatManager.Instance.History.Entries.Count();
        await Play(await Add<StrikeIronclad>());
        check(CombatManager.Instance.History.Entries.Count()>history&&creature.GetPower<DuplicationPower>()==null,
            "native duplication charge is consumed by the next play");

        await clear(); terminal.BeginTurn();await Copies<Skim>(BondCatalog.Get(Bond.Rotation).Basic);
        await Play(await Add<Stratagem>(upgraded:true));
        var chosen=await Add<Bash>(PileType.Discard);
        selector.Target=chosen;
        await CardPileCmd.Shuffle(context,player);
        selector.Target=null;
        check(chosen.Pile?.Type==PileType.Hand&&chosen.ShouldRetainThisTurn&&chosen.EnergyCost.GetAmountToSpend()==1,
            "Stratagem+ retains and discounts the card actually chosen");

        await clear();terminal.BeginTurn();await Copies<Skim>(4);
        var top=await Add<DefendIronclad>();
        await Add<StrikeIronclad>(PileType.Draw);
        selector.Target=top;
        await Play(await Add<PhotonCut>(upgraded:true));
        selector.Target=null;
        await CardPileCmd.Draw(context,1,player);
        check(top.Pile?.Type==PileType.Hand&&top.ShouldRetainThisTurn&&terminal.Ledger.Get(Bond.Rotation)==6,
            "Photon Cut+ marks the selected card and pays once after it is drawn");

        await clear();terminal.BeginTurn();await Copies<Skim>(4);await Copies<Bash>(3);
        await Play(await Add<Vicious>(upgraded:true));
        var drawn=await Add<Neutralize>(PileType.Draw);
        await Play(await Add<Bash>());
        check(drawn.Pile?.Type==PileType.Hand&&terminal.Ledger.Get(Bond.Rotation)==0&&terminal.Ledger.Get(Bond.Breach)==9,
            "Vicious+ gives three Breach layers as soon as its own draw resolves");

        await clear();terminal.BeginTurn();await Copies<ShrugItOff>(11);
        await Play(await Add<Barricade>(upgraded:true));
        terminal.Ledger.Add(Bond.Guard,150);
        var barricadeBlock=creature.Block;
        await terminal.AfterPlayerTurnStart(context,player);
        check(creature.Block==barricadeBlock+5,"Barricade+ turns active Guard II into starting Block");

        await clear();terminal.BeginTurn();
        await Play(await Add<Bash>());
        await Play(await Add<BeamCell>(upgraded:true));
        check(enemy.GetPowerAmount<WeakPower>()==2,"Beam Cell+ gives Weak to a previously Vulnerable enemy");
        var beforeBlock=creature.Block;
        await Play(await Add<Neutralize>());
        check(creature.Block-beforeBlock==(enemy.Monster?.IntendsToAttack==true?2:0),
            "Neutralize checks the intent even when the target is already Weak");

        await clear();terminal.BeginTurn();
        var handCard=await Add<BootSequence>();
        await terminal.BeforeFlush(context,player);
        check(handCard.ShouldRetainThisTurn&&!handCard.Keywords.Contains(CardKeyword.Retain),
            "Boot Sequence receives only a single-turn retention");

        await clear();terminal.BeginTurn();
        var hp=creature.CurrentHp;
        await Play(await Add<Bloodletting>());
        check(creature.CurrentHp==hp-3,"Bloodletting loses three health before any loss");
        var lost=creature.CurrentHp;
        await Play(await Add<Bloodletting>(upgraded:true));
        check(creature.CurrentHp==lost-1,"Bloodletting+ loses one health after any prior loss");

        await clear();await PlayerCmd.SetStars(0,player);
        await Copies<Coolheaded>(BondCatalog.Get(Bond.Resonance).Basic);
        await Copies<Constellation>(BondCatalog.Get(Bond.Stars).Basic);
        await Copies<Bodyguard>(BondCatalog.Get(Bond.Bones).Basic);
        var previousOstyHp=player.IsOstyAlive?player.Osty!.MaxHp:0;
        await terminal.BeforeCombatStart();
        check(state.OrbQueue.Capacity==1&&state.OrbQueue.Orbs.Count==0&&state.Stars==2&&player.IsOstyAlive&&
            player.Osty!.MaxHp==previousOstyHp+3,"opening Resonance, Stars and Bones provide a slot, two stars and three Summon to Ironclad");
        terminal.BeginTurn();
        var drawWitness=await Add<DefendIronclad>(PileType.Draw);
        await Play(await Add<Rattle>());
        await Play(await Add<Rattle>());
        var ostyHp=player.Osty!.MaxHp;
        check(drawWitness.Pile?.Type==PileType.Draw&&creature.Powers.OfType<CovenantEffectPower>()
            .Any(power=>power.IconBond==Bond.Bones&&power.EffectText.Contains("下回合开始时，召唤1。")),
            "Osty attacks queue a visible next-turn Summon and do not draw a card");
        await OstyCmd.Summon(context,player,1,null);
        check(player.Osty.MaxHp==ostyHp+3+terminal.Scale(Bond.Bones).A,"the first real summon this turn keeps the Bones bonus");
        ostyHp=player.Osty.MaxHp;
        terminal.BeginTurn();await terminal.AfterPlayerTurnStart(context,player);
        check(player.Osty.MaxHp==ostyHp+1,"two Osty attacks in one turn schedule exactly one Summon at the next turn start");
        terminal.BeginTurn();await terminal.AfterPlayerTurnStart(context,player);
        check(player.Osty.MaxHp==ostyHp+1,"the scheduled Summon clears after it resolves");

        await clear();terminal.BeginTurn();
        await OrbCmd.AddSlots(player,2);
        await OrbCmd.Channel<DarkOrb>(context,player);
        await OrbCmd.Channel<LightningOrb>(context,player);
        var right=state.OrbQueue.Orbs.First();
        await OrbCmd.Passive(context,right,null);
        await Play(await Add<Capacitor>());
        check(state.OrbQueue.Orbs.Count==3&&state.OrbQueue.Orbs.Last() is DarkOrb replica&&replica.EvokeVal==right.EvokeVal,
            "Capacitor copies the original rightmost orb, not the second slot, and preserves charged Dark value");

        await clear();terminal.BeginTurn();
        var reanimate=await Add<Reanimate>();
        check(reanimate.EnergyCost.GetAmountToSpend()==3,"Reanimate costs three before Osty is downed");
        await OstyCmd.Summon(context,player,1,null);
        await CreatureCmd.Kill(player.Osty!);
        await OstyCmd.Summon(context,player,1,null);
        check(reanimate.EnergyCost.GetAmountToSpend()==1,"Reanimate stays discounted after Osty is automatically summoned again");

        await clear();terminal.BeginTurn();
        state.Stars=2;
        await Manual(await Add<FallingStar>());
        check(enemy.GetPowerAmount<WeakPower>()==2&&enemy.GetPowerAmount<VulnerablePower>()==2,
            "Falling Star adds both debuffs regardless of how many stars remain");

        await CreatureCmd.Add<Axebot>(combat);
        foreach(var upgraded in new[]{false,true})
        {
            await clear();terminal.BeginTurn();
            await Copies<GammaBlast>(2);
            await PowerCmd.Apply<WeakPower>(context,enemy,3,enemy,null);
            await PowerCmd.Apply<VulnerablePower>(context,enemy,6,enemy,null);
            state.Stars=0;
            await Play(await Add<MeteorShower>(upgraded:upgraded));
            var weakTotal=combat.HittableEnemies.Sum(x=>x.GetPowerAmount<WeakPower>());
            var vulnerableTotal=combat.HittableEnemies.Sum(x=>x.GetPowerAmount<VulnerablePower>());
            var expected=upgraded?(weakTotal+vulnerableTotal)/2:Math.Min(weakTotal,vulnerableTotal);
            const int ancient=4;
            check(weakTotal>3&&vulnerableTotal>6&&state.Stars==0&&
                terminal.Ledger.Get(Bond.Stars)==expected+ancient&&terminal.Ledger.Get(Bond.Breach)==expected+ancient,
                $"Meteor Shower {(upgraded?"upgraded":"base")} grants the {(upgraded?"mean":"minimum")} after applying native debuffs: weak={weakTotal}, vulnerable={vulnerableTotal}, stars={terminal.Ledger.Get(Bond.Stars)}, breach={terminal.Ledger.Get(Bond.Breach)}, expected={expected+ancient}");
        }

        await MadScienceVariants(player,terminal,enemy,clear,check);
        await Solitude(player,terminal,clear,check);
        await clear();terminal.BeginTurn();
        var curse=await Add<Doubt>();var twin=await Add<Doubt>(PileType.Draw);
        await Copies<Bash>(23);
        await CardCmd.Exhaust(context,curse);
        check(twin.Pile==null&&curse.Pile==null,"Solitude II changes same-name curses when one is exhausted");
        Console.WriteLine("COVENANT_NEW_BONDS_COMPLETE");
    }

    private static async Task MadScienceVariants(Player player,CovenantTerminal terminal,Creature enemy,Func<Task> clear,Action<bool,string> check)
    {
        (CardType Type,TinkerTime.RiderEffect Rider,Bond[] Traits,Bond[] Extra)[] cases=
        [
            (CardType.Attack,TinkerTime.RiderEffect.Sapping,[Bond.Breach],[]),
            (CardType.Attack,TinkerTime.RiderEffect.Violence,[Bond.Fury],[]),
            (CardType.Attack,TinkerTime.RiderEffect.Choking,[Bond.Chain],[]),
            (CardType.Skill,TinkerTime.RiderEffect.Energized,[Bond.Heavy],[Bond.Guard]),
            (CardType.Skill,TinkerTime.RiderEffect.Wisdom,[Bond.Rotation],[Bond.Guard]),
            (CardType.Skill,TinkerTime.RiderEffect.Chaos,[Bond.Creation],[Bond.Guard]),
            (CardType.Power,TinkerTime.RiderEffect.Expertise,[Bond.Fury,Bond.Guard],[Bond.Ritual]),
            (CardType.Power,TinkerTime.RiderEffect.Curious,[Bond.Ritual],[]),
            (CardType.Power,TinkerTime.RiderEffect.Improvement,[Bond.Foresight],[Bond.Ritual]),
        ];
        var combat=player.Creature.CombatState!;
        var context=new BlockingPlayerChoiceContext();
        foreach(var (type,rider,traits,extra) in cases)
        {
            var deck=player.RunState.CreateCard<MadScience>(player);
            deck.TinkerTimeType=type;deck.TinkerTimeRider=rider;
            var members=traits.Concat(extra).Append(Bond.Encounter).ToHashSet();
            if(rider==TinkerTime.RiderEffect.Improvement)members.Add(Bond.Mastery);
            var labels=traits.Select(bond=>BondCatalog.Get(bond).Name).ToArray();
            var baseText=Presentation.TraitText(deck);
            check(members.SetEquals(CardMembership.Of(deck))&&TraitDisplay.MainBond(deck)==traits[0]&&
                TraitDisplay.SecondTraitBond(deck)==(traits.Length==2?traits[1]:(Bond?)null)&&
                labels.All(label=>baseText.Contains($"【{label}】"))&&baseText.Contains("6层")&&
                Presentation.TraitText(deck,upgradePreview:true).Contains("8层"),$"Mad Science {rider} has exactly its own memberships, trait icons, and preview");
            CardCmd.Upgrade(deck);
            check(members.Append(Bond.Mastery).Append(Bond.Initiative).ToHashSet().SetEquals(CardMembership.Of(deck))&&
                Presentation.TraitText(deck).Contains("8层"),$"Mad Science {rider} keeps its native Innate upgrade, covenants, and upgraded trait");

            if(rider is not (TinkerTime.RiderEffect.Sapping or TinkerTime.RiderEffect.Energized or TinkerTime.RiderEffect.Expertise))continue;
            await clear();terminal.BeginTurn();
            var card=(MadScience)combat.CloneCard(deck);card.DeckVersion=deck;
            await CardPileCmd.Add(card,PileType.Hand);
            foreach(var bond in traits.Concat(extra))
            {
                var threshold=BondCatalog.Get(bond).Basic;
                for(var i=1;i<threshold;i++)
                {
                    CardModel support=bond switch
                    {
                        Bond.Breach=>combat.CreateCard<GammaBlast>(player),
                        Bond.Heavy=>combat.CreateCard<Bludgeon>(player),
                        Bond.Fury=>combat.CreateCard<Inflame>(player),
                        Bond.Guard=>combat.CreateCard<ShrugItOff>(player),
                        Bond.Ritual=>combat.CreateCard<MachineLearning>(player),
                        _=>throw new InvalidOperationException(bond.ToString())
                    };
                    await CardPileCmd.Add(support,PileType.Exhaust);
                }
            }
            await CardCmd.AutoPlay(context,card,enemy);
            check(traits.All(bond=>terminal.Ledger.Get(bond)==8)&&extra.All(bond=>terminal.Ledger.Get(bond)==0),
                $"Mad Science {rider} grants layers only to its active trait covenants in combat");
        }
    }
}
