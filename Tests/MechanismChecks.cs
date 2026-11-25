using Covenant.Core;
using Covenant.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;

internal static partial class MechanismChecks
{
    private sealed class Battle
    {
        internal static Battle? Latest;
        public Player Player;
        public RunState Run;
        public CombatState State;
        public CovenantTerminal Terminal;
        public Creature Enemy,Other;
        public PlayerChoiceContext Context=new ThrowingPlayerChoiceContext();
        public Battle(EncounterModel? encounter=null)
        {
            Latest=this;
            CombatChecks.Choose=null;
            Player=Player.CreateForNewRun(ModelDb.Character<Ironclad>(),UnlockState.all,3);
            LocalContext.NetId=Player.NetId;
            Run=RunState.CreateForTest([Player],[],[],seed:"covenant-mechanisms");
            State=new CombatState(encounter??ModelDb.Encounter<CultistsNormal>().ToMutable(),Run);State.AddPlayer(Player);Player.ResetCombatState();
            Enemy=State.CreateCreature(ModelDb.Monster<BigDummy>().ToMutable(),CombatSide.Enemy,"0");State.AddCreature(Enemy);
            Other=State.CreateCreature(ModelDb.Monster<BigDummy>().ToMutable(),CombatSide.Enemy,"1");State.AddCreature(Other);
            // Native pet commands require the manager to own a turn state, as in the engine's internal test fixtures.
            // Construct that state without starting the asynchronous UI/turn loop.
            var turnType=typeof(CombatManager).Assembly.GetType("MegaCrit.Sts2.Core.Combat.CombatTurnState")!;
            var previous=AccessTools.Field(typeof(CombatManager),"_turnState").GetValue(CombatManager.Instance);
            if(previous!=null)AccessTools.Method(turnType,"Cancel").Invoke(previous,null);
            var turn=Activator.CreateInstance(turnType,[State])!;
            AccessTools.Property(turnType,"IsInProgress").SetValue(turn,true);AccessTools.Property(turnType,"IsStarting").SetValue(turn,false);
            AccessTools.Field(typeof(CombatManager),"_turnState").SetValue(CombatManager.Instance,turn);
            CombatManager.Instance.StateTracker.SetState(State);CombatManager.Instance.History.Clear();
            Terminal=CovenantTerminal.For(Player)!;
            foreach(var pile in Player.PlayerCombatState!.AllPiles)pile.Clear();
        }
        public T Add<T>(PileType pile=PileType.Exhaust) where T:CardModel
        {
            var original=Run.CreateCard<T>(Player);Player.Deck.AddInternal(original,0);
            var card=State.CreateCard<T>(Player);card.DeckVersion=original;pile.GetPile(Player)!.AddInternal(card,0);return card;
        }
        public void Copies<T>(int amount=6) where T:CardModel {for(int i=0;i<amount;i++)Add<T>();}
        public CardPlay Play(CardModel card,int energy=1)=>new(){Card=card,Player=Player,Target=Enemy,PlayCount=1,PlayIndex=0,IsAutoPlay=false,ResultPile=PileType.Discard,Resources=new ResourceInfo{EnergySpent=energy,EnergyValue=energy,StarsSpent=0,StarValue=0}};
        public async Task StartTurn()
        {
            State.RoundNumber++;Player.PlayerCombatState!.IncrementTurnNumber();
            await Hook.BeforeSideTurnStart(State,CombatSide.Player,[Player.Creature]);
            await Hook.AfterPlayerTurnStart(State,Context,Player);
        }
        public async Task<CardPlay> ResolveCallbacks(CardModel card,int energy=1)
        {
            var play=Play(card,energy);await CardPileCmd.Add(card,PileType.Play);await Hook.BeforeCardPlayed(State,play);
            CombatManager.Instance.History.CardPlayStarted(State,play);CombatManager.Instance.History.CardPlayFinished(State,play);
            await Hook.AfterCardPlayed(State,Context,play);await CardPileCmd.Add(card,PileType.Discard);return play;
        }
    }

    internal static async Task Run(Action<bool,string> check)
    {
        var failures=new List<string>();
        foreach(var (name,test) in new (string,Func<Action<bool,string>,Task>)[]{
            (nameof(BalanceRegression),BalanceRegression),
            (nameof(Blood),Blood),(nameof(Poison),Poison),(nameof(Orbs),Orbs),(nameof(Forge),Forge),
            (nameof(Creation),Creation),(nameof(Stars),Stars),(nameof(Bones),Bones),(nameof(Guard),Guard),
            (nameof(Arrows),Arrows),(nameof(Melody),Melody),(nameof(Emotion),Emotion),(nameof(StartOrder),StartOrder),
            (nameof(Specials),Specials),(nameof(General),General),
            (nameof(CardSequences),CardSequences),(nameof(MembershipSemantics),MembershipSemantics),
            (nameof(CreationSemantics),CreationSemantics),(nameof(IndirectPoison),IndirectPoison),
            (nameof(ResourceSemantics),ResourceSemantics),(nameof(FoolAndEtherealSemantics),FoolAndEtherealSemantics),
            (nameof(ExhaustDiscardZeroSemantics),ExhaustDiscardZeroSemantics),(nameof(KeywordSpecialMemberships),KeywordSpecialMemberships),
            (nameof(ChainSourceSemantics),ChainSourceSemantics),(nameof(QuestTraits),QuestTraits),
            (nameof(FullTraitAmounts),FullTraitAmounts),(nameof(Doom),Doom),(nameof(RewardPhaseCount),RewardPhaseCount)})
        {try{await test((ok,message)=>
        {
            if(!ok&&Battle.Latest is {} current)
                Console.WriteLine("COVENANT_FAILURE_STATE "+name+" layers="+string.Join(",",current.Terminal.Ledger.Layers.Select(p=>$"{p.Key}:{p.Value}"))+
                    $" block={current.Player.Creature.Block} energy={current.Player.PlayerCombatState!.Energy} stars={current.Player.PlayerCombatState.Stars} poison={current.Enemy.GetPowerAmount<PoisonPower>()} doom={current.Enemy.GetPowerAmount<DoomPower>()} aim={current.Player.Creature.GetPowerAmount<AimPower>()}"+
                    " swords="+string.Join(",",current.Player.PlayerCombatState.AllPiles.SelectMany(p=>p.Cards).OfType<SovereignBlade>().Select(c=>c.DynamicVars.Damage.BaseValue)));
            check(ok,message);
        });}catch(Exception ex){failures.Add(name+": "+ex.Message);Console.WriteLine("COVENANT_CHECK_FAILED "+name+": "+ex);}}
        CombatChecks.Choose=null;
        if(failures.Count>0)throw new Exception(string.Join("\n",failures));
        Console.WriteLine("COVENANT_MECHANISMS_OK self-damage=pass poison=pass orbs=pass forge=pass creation=pass stars=pass bones=pass guard-retention=pass arrows=pass melody=pass emotion=pass early-turn-order=pass cross-character=Ironclad");
    }

    private static async Task Blood(Action<bool,string> check)
    {
        var b=new Battle();b.Copies<Bloodletting>();b.Terminal.Ledger.Add(Bond.Blood,20);
        var material=b.Add<StrikeIronclad>(PileType.Hand);var drawn=b.Add<DefendIronclad>(PileType.Draw);
        var source=b.Player.PlayerCombatState!.ExhaustPile.Cards.OfType<Bloodletting>().First();
        CombatChecks.Choose=options=>options.Where(x=>x==material);
        await CreatureCmd.Damage(b.Context,b.Player.Creature,1,ValueProp.Unblockable|ValueProp.Move,b.Enemy);
        check(material.Pile!.Type==PileType.Hand&&b.Player.Creature.Block==0,"Enemy damage does not trigger voluntary self-loss covenant");
        await CreatureCmd.Damage(b.Context,b.Player.Creature,3,ValueProp.Unblockable|ValueProp.Unpowered|ValueProp.Move,source,null);
        check(material.Pile!.Type==PileType.Exhaust&&drawn.Pile!.Type==PileType.Hand,"Blood uses real exhaust and draw commands after voluntary HP loss");
        check(b.Player.Creature.Block==5,"Blood block scales from twenty layers");
        check(b.Terminal.Ledger.Get(Bond.Blood)==26,"Self-loss traits on all six source cards fire once each");
        var energy=b.Player.PlayerCombatState.Energy;
        for(int i=0;i<2;i++)await CreatureCmd.Damage(b.Context,b.Player.Creature,1,ValueProp.Unblockable|ValueProp.Unpowered|ValueProp.Move,source,null);
        check(b.Player.PlayerCombatState.Energy==energy+1,"Third self-loss grants exactly one energy");
        var attack=b.Add<Anger>(PileType.Hand);var play=b.Play(attack);await b.Terminal.BeforeCardPlayed(play);
        var hp=b.Enemy.CurrentHp;await CreatureCmd.Damage(b.Context,b.Enemy,6,ValueProp.Move,attack,play);
        check(hp-b.Enemy.CurrentHp==13,"Third self-loss next-attack bonus uses forty-eight layers with diminishing scaling");
        await b.Terminal.AfterCardPlayedLate(b.Context,play);
    }

    private static async Task Poison(Action<bool,string> check)
    {
        var b=new Battle();b.Copies<DeadlyPoison>();b.Terminal.Ledger.Add(Bond.Poison,60);
        var source=b.Player.PlayerCombatState!.ExhaustPile.Cards.First();
        await PowerCmd.Apply<PoisonPower>(b.Context,b.Enemy,10,b.Player.Creature,source);
        check(b.Enemy.GetPowerAmount<PoisonPower>()==14,"Native poison application receives layer-scaled augmentation");
        check(b.Enemy.GetPowerAmount<WeakPower>()==1,"New-target Weak is not lost after the recursive poison augmentation");
        await PowerCmd.Apply<PoisonPower>(b.Context,b.Enemy,10,b.Player.Creature,source);
        check(b.Enemy.GetPowerAmount<PoisonPower>()==28&&b.Enemy.GetPowerAmount<WeakPower>()==1,"Later poison applications scale without repeating new-target Weak");
        await CreatureCmd.Kill(b.Enemy,true);
        check(b.Other.GetPowerAmount<PoisonPower>()==12,$"Real native death transfers forty percent rounded down; actual={b.Other.GetPowerAmount<PoisonPower>()}");
        check(b.Terminal.Ledger.Get(Bond.Poison)==90,"Poison augmentation and transfer do not recursively create extra trait events");
    }

    private static async Task Orbs(Action<bool,string> check)
    {
        var b=new Battle();b.Copies<Coolheaded>();b.Terminal.Ledger.Add(Bond.Resonance,60);
        await OrbCmd.Channel<FrostOrb>(b.Context,b.Player);
        check(b.Player.PlayerCombatState!.OrbQueue.Capacity==1,"A non-Defect character receives a native orb slot");
        check(b.Player.Creature.Block==6,"First channel grants layer-scaled block");
        var before=b.Player.Creature.Block;
        await b.Terminal.BeforeSideTurnEnd(b.Context,CombatSide.Player,[b.Player.Creature]);
        check(b.Player.Creature.Block-before==4,"Advanced resonance triggers the front Frost orb twice at thirty layers");
        var frost=b.Player.PlayerCombatState.OrbQueue.Orbs.Single();
        await OrbCmd.AddSlots(b.Player,1);
        // The first-channel choice is already used; this tests the real queue swap separately from UI selection.
        await OrbCmd.Channel<DarkOrb>(b.Context,b.Player);var dark=b.Player.PlayerCombatState.OrbQueue.Orbs.Last();
        await OrbCmd.Passive(b.Context,dark,null);var charge=dark.EvokeVal;
        check(b.Terminal.SwapOrbs(frost,dark),"Two owned orbs can be swapped");
        check(b.Player.PlayerCombatState.OrbQueue.Orbs[0]==dark&&b.Player.PlayerCombatState.OrbQueue.Orbs[1]==frost&&dark.EvokeVal==charge,"Swap keeps exact orb instances and accumulated Dark charge");
        check(!b.Terminal.SwapOrbs(dark,dark),"Swapping a slot with itself is a no-op");
        var o=new Battle();o.Copies<Dualcast>();o.Terminal.Ledger.Add(Bond.Overload,40);
        await OrbCmd.Channel<DarkOrb>(o.Context,o.Player);var old=o.Player.PlayerCombatState!.OrbQueue.Orbs.Single();
        await OrbCmd.Passive(o.Context,old,null);var hp=o.Enemy.CurrentHp;var hpOther=o.Other.CurrentHp;var block=o.Player.Creature.Block;
        await OrbCmd.EvokeNext(o.Context,o.Player);
        check(o.Enemy.CurrentHp<hp&&hpOther-o.Other.CurrentHp==5,"First evoke adds layer-scaled area damage to native orb damage");
        check(o.Player.PlayerCombatState.OrbQueue.Orbs.Single() is DarkOrb replacement&&replacement!=old&&replacement.EvokeVal==6,"Advanced overload channels a clean same-type orb rather than copying Dark charge");
        check(o.Player.Creature.Block-block==6,"Advanced overload adds layer-scaled block independently of resonance");
    }

    private static async Task Forge(Action<bool,string> check)
    {
        var b=new Battle();b.Copies<RefineBlade>(2);b.Terminal.Ledger.Add(Bond.Forge,20);
        var source=b.Player.PlayerCombatState!.ExhaustPile.Cards.First();
        await ForgeCmd.Forge(5,b.Player,source);
        var sword=b.Player.PlayerCombatState.Hand.Cards.OfType<SovereignBlade>().Single();
        check(sword.DynamicVars.Damage.BaseValue==18,"First native Forge adds its base amount and exactly one layer-scaled augmentation");
        await CardPileCmd.Add(sword,PileType.Discard);
        CombatChecks.Choose=options=>options.Where(x=>x==sword);
        await ForgeCmd.Forge(2,b.Player,source);
        check(sword.Pile!.Type==PileType.Hand&&sword.DynamicVars.Damage.BaseValue==20,"Second real forge returns Sovereign Blade without counting the augmentation as a separate forge");
        await b.StartTurn();
        await ForgeCmd.Forge(1,b.Player,source);
        check(sword.DynamicVars.Damage.BaseValue==24,"A new turn resets first-forge augmentation");
        var advanced=new Battle();advanced.Copies<RefineBlade>();advanced.Terminal.Ledger.Add(Bond.Forge,10);
        await ForgeCmd.Forge(2,advanced.Player,advanced.Player.PlayerCombatState!.ExhaustPile.Cards.First());
        var blade=advanced.Player.PlayerCombatState.Hand.Cards.OfType<SovereignBlade>().Single();var play=advanced.Play(blade);
        await advanced.Terminal.BeforeCardPlayed(play);await advanced.Terminal.AfterCardPlayed(advanced.Context,play);await advanced.Terminal.AfterCardPlayedLate(advanced.Context,play);
        await CardPileCmd.Add(blade,PileType.Discard);await advanced.StartTurn();
        check(blade.Pile!.Type==PileType.Hand,"Advanced forge returns the first played sword at the next turn start");
        check(advanced.Terminal.ModifyDamageAdditive(advanced.Enemy,10,ValueProp.Move,advanced.Player.Creature,blade,advanced.Play(blade))==8,"Returned sword receives its separate layer-scaled next-play bonus");
    }

    private static async Task Creation(Action<bool,string> check)
    {
        var b=new Battle();b.Copies<AdaptiveStrike>(10);b.Terminal.Ledger.Add(Bond.Creation,60);
        var first=b.State.CreateCard<Shiv>(b.Player);await CardPileCmd.AddGeneratedCardToCombat(first,PileType.Hand,b.Player);
        check(b.Player.Creature.Block==5,"Ten rare sources award twenty full layers before the first generation's scaled block");
        var status=b.State.CreateCard<Dazed>(b.Player);await CardPileCmd.AddGeneratedCardToCombat(status,PileType.Hand,b.Player);
        var second=b.State.CreateCard<Shiv>(b.Player);await CardPileCmd.AddGeneratedCardToCombat(second,PileType.Hand,b.Player);
        check(b.Terminal.Ledger.Get(Bond.Creation)==100,"Generated status is excluded while both valid generations award every source's full two-layer amount");
        CombatChecks.Choose=options=>options.Where(x=>x==first);
        var third=b.State.CreateCard<Shiv>(b.Player);await CardPileCmd.AddGeneratedCardToCombat(third,PileType.Hand,b.Player);
        check(first.ShouldRetainThisTurn,"Third valid generation grants Retain even when a status was generated between valid cards");
        await b.Terminal.BeforeCardPlayed(b.Play(first));
        check(b.Terminal.ModifyDamageAdditive(b.Enemy,4,ValueProp.Move,b.Player.Creature,first,null)==7,"Third-generation bonus uses ninety Creation layers and combines with the generated-attack covenant");
    }

    private static async Task Stars(Action<bool,string> check)
    {
        var b=new Battle();b.Copies<Comet>();b.Terminal.Ledger.Add(Bond.Stars,60);
        var returned=b.Add<Comet>(PileType.Discard);CombatChecks.Choose=options=>options.Where(x=>x==returned);
        await PlayerCmd.GainStars(1,b.Player);b.Player.PlayerCombatState!.LoseStars(1);await b.Terminal.AfterStarsSpent(1,b.Player);
        check(b.Player.Creature.Block==7,"Star spending grants block after all rare-card trait growth under diminishing scaling");
        check(returned.Pile!.Type==PileType.Hand&&returned.ShouldRetainThisTurn,"First empty-star event retrieves and retains a fixed-star card");
        check(returned.CurrentStarCost==Math.Max(0,returned.BaseStarCost-2),"Retrieved star card receives a layer-scaled until-played star discount");
        await b.StartTurn();
        check(b.Player.PlayerCombatState.Stars==1,"Empty-star transition grants a star at the next turn start");
    }

    private static async Task Bones(Action<bool,string> check)
    {
        var b=new Battle();b.Copies<Bodyguard>();b.Terminal.Ledger.Add(Bond.Bones,60);var source=b.Player.PlayerCombatState!.ExhaustPile.Cards.First();
        await OstyCmd.Summon(b.Context,b.Player,3,source);var osty=b.Player.Osty;
        check(osty!=null&&osty.MaxHp==3,"A non-Necrobinder character can summon native Osty");
        var draw=b.Add<DefendIronclad>(PileType.Draw);
        var attack=DamageCmd.Attack(5).FromOsty(osty!,source,null).Targeting(b.Enemy);
        await attack.WithNoAttackerAnim().Execute(b.Context);
        check(draw.Pile!.Type==PileType.Hand,"First real Osty attack draws a real card");
        check(b.Terminal.ModifySummonAmount(b.Player,3,source)==7&&b.Terminal.ModifySummonAmount(b.Player,3,source)==7,"Repeated summon previews do not consume the next-summon bonus");
        var enhanced=await OstyCmd.Summon(b.Context,b.Player,3,source);var plain=await OstyCmd.Summon(b.Context,b.Player,3,source);
        check(enhanced.Amount==7&&plain.Amount==3,"A real summon consumes the pending enhancement exactly once");
        await CreatureCmd.Kill(osty!,true);await b.StartTurn();
        check(b.Player.IsOstyAlive&&osty!.MaxHp==7&&osty.CurrentHp==7,"Advanced Bones revives native Osty with the layer-scaled summon amount after real death");
        await CreatureCmd.Kill(osty!,true);await b.StartTurn();
        check(!b.Player.IsOstyAlive,"Automatic death-response summon is once per combat");
    }

    private static async Task Guard(Action<bool,string> check)
    {
        var b=new Battle();b.Copies<DefendIronclad>(9);b.Terminal.Ledger.Add(Bond.Guard,20);
        await CreatureCmd.GainBlock(b.Player.Creature,100,ValueProp.Unpowered,null);
        await b.Player.Creature.AfterTurnStart(CombatSide.Player);
        check(b.Player.Creature.Block==100,"Native first-turn exception is preserved");
        b.Player.PlayerCombatState!.IncrementTurnNumber();await b.Player.Creature.AfterTurnStart(CombatSide.Player);
        check(b.Player.Creature.Block==9,"Native block clearing retains only the advanced covenant's scaled cap");
        b.Terminal.Ledger.Add(Bond.Guard,30);await CreatureCmd.GainBlock(b.Player.Creature,100,ValueProp.Unpowered,null);
        await b.Player.Creature.AfterTurnStart(CombatSide.Player);
        check(b.Player.Creature.Block==15,"Additional Guard layers increase the actual retained block cap");
        await PowerCmd.Apply<BarricadePower>(b.Context,b.Player.Creature,1,b.Player.Creature,null);
        await CreatureCmd.GainBlock(b.Player.Creature,100,ValueProp.Unpowered,null);var before=b.Player.Creature.Block;
        await b.Player.Creature.AfterTurnStart(CombatSide.Player);
        check(b.Player.Creature.Block==before,"Native full block retention is not reduced by the covenant's partial retention");
    }

    private static async Task Arrows(Action<bool,string> check)
    {
        var b=new Battle();b.Copies<FoolishShot>();b.Terminal.Ledger.Add(Bond.Arrows,40);
        var shot=b.Player.PlayerCombatState!.ExhaustPile.Cards.First();var returned=b.Add<FoolishShot>(PileType.Discard);var attack=b.Add<Anger>(PileType.Hand);
        check(b.Player.PlayerCombatState.Arrows.Count==1,"A non-Fool character gains one native arrow slot from Shoot cards");
        await ArrowCmd.ShootArrowAtTarget(b.Enemy,b.Player,b.Context,shot);
        var impaled=b.Enemy.GetPowerInstances<ImpaledPower>().Single();
        check(impaled.HitCounter==3&&ArrowCmd.GetAvailableArrowCount(b.Player)==0,"Native shot spends the arrow and impales its target");
        await CreatureCmd.Damage(b.Context,b.Enemy,1,ValueProp.Move,attack,null);
        check(impaled.HitCounter==1&&b.Player.Creature.GetPowerAmount<AimPower>()==4,"First non-Shoot hit advances native impale twice and grants scaled Aim");
        CombatChecks.Choose=options=>options.Where(x=>x==returned);
        await CreatureCmd.Damage(b.Context,b.Enemy,1,ValueProp.Move,attack,null);
        check(ArrowCmd.GetAvailableArrowCount(b.Player)==1&&returned.Pile!.Type==PileType.Hand,"Native dislodge retrieves the arrow and advanced covenant retrieves a Shoot card");
        check(returned.EnergyCost.GetWithModifiers(CostModifiers.Local)==0&&b.Player.Creature.GetPowerAmount<AimPower>()==8,"Retrieved Shoot card is discounted and receives the advanced Aim payoff");
        await b.StartTurn();await ArrowCmd.ShootArrowAtTarget(b.Enemy,b.Player,b.Context,shot);
        impaled=b.Enemy.GetPowerInstances<ImpaledPower>().Single();impaled.HitCounter=1;var aim=b.Player.Creature.GetPowerAmount<AimPower>();
        await CreatureCmd.Damage(b.Context,b.Enemy,1,ValueProp.Move,attack,null);
        check(ArrowCmd.GetAvailableArrowCount(b.Player)==1&&b.Player.Creature.GetPowerAmount<AimPower>()-aim==8,"A first hit that naturally dislodges the arrow still receives both scaled Aim payoffs");
        var other=new Battle();other.Copies<FoolishShot>();var ownShot=other.Player.PlayerCombatState!.ExhaustPile.Cards.First();
        await ArrowCmd.ShootArrowAtTarget(other.Enemy,other.Player,other.Context,ownShot);
        await CreatureCmd.Damage(other.Context,other.Enemy,1,ValueProp.Move|ValueProp.FromArrow,ownShot,null);
        check(other.Enemy.GetPowerInstances<ImpaledPower>().Single().HitCounter==2&&other.Player.Creature.GetPowerAmount<AimPower>()==0,"Shoot damage does not trigger the non-Shoot covenant benefit");
    }

    private static async Task Melody(Action<bool,string> check)
    {
        var b=new Battle();b.Copies<WahChoKi>();b.Terminal.Ledger.Add(Bond.Melody,20);
        var song=b.Player.PlayerCombatState!.ExhaustPile.Cards.First();var draw=b.Add<Anger>(PileType.Draw);
        CombatChecks.Choose=options=>options.OfType<CovenantOption>().Take(1).Cast<CardModel>();
        var energy=b.Player.PlayerCombatState.Energy;await b.ResolveCallbacks(song);
        check(b.Player.Creature.Block==18&&draw.Pile!.Type==PileType.Hand,"Three melody events award all six rare sources in full and grant blocks of four, four and five");
        check(b.Player.PlayerCombatState.Energy==energy+1&&MelodyCmd.MelodiesThisTurn(b.Player)==3,"Three-melody completion grants energy and uses real native melody history");
        var block=b.Player.Creature.Block;energy=b.Player.PlayerCombatState.Energy;await b.ResolveCallbacks(song);
        check(b.Player.Creature.Block==block&&b.Player.PlayerCombatState.Energy==energy,"Repeating the same three melodies does not gain them again");
        var sameTurn=b.Play(draw);await b.Terminal.BeforeCardPlayed(sameTurn);
        check(b.Terminal.ModifyDamageAdditive(b.Enemy,6,ValueProp.Move,b.Player.Creature,draw,sameTurn)==0,"Melody completion does not grant the next-turn attack bonus early");
        await b.Terminal.AfterCardPlayedLate(b.Context,sameTurn);
        var histories=CombatManager.Instance.History.CardPlaysFinished.Count();await b.StartTurn();
        check(MelodyCmd.PlayedWahThisTurn(b.Player)&&MelodyCmd.MelodiesThisTurn(b.Player)==1,"Inherited melody is visible to native card conditions in the following turn");
        check(CombatManager.Instance.History.CardPlaysFinished.Count()==histories,"Inheriting a melody does not fabricate a card play");
        var nextTurn=b.Play(draw);await b.Terminal.BeforeCardPlayed(nextTurn);
        check(b.Terminal.ModifyDamageAdditive(b.Enemy,6,ValueProp.Move,b.Player.Creature,draw,nextTurn)==13,"Following-turn first attack receives the bonus from sixty-six fully earned melody layers");
        await b.Terminal.AfterCardPlayedLate(b.Context,nextTurn);
        block=b.Player.Creature.Block;var nextSong=await b.ResolveCallbacks(song);
        check(!MelodyCmd.IsFirstPlayThisTurn(nextSong,CardTag.Wah)&&b.Player.Creature.Block-block==21,"Inherited Wah is not counted as new; only two five-point melody blocks remain");
    }

    private static async Task Emotion(Action<bool,string> check)
    {
        var b=new Battle();b.Copies<Anguish>();b.Terminal.Ledger.Add(Bond.Emotion,40);
        var sadness=b.State.CreateCard<Sadness>(b.Player);await CardPileCmd.AddGeneratedCardToCombat(sadness,PileType.Hand,b.Player);
        check(b.Terminal.Ledger.Get(Bond.Emotion)==46,"Generated Sadness fires emotion traits even though it is a status card");
        CombatChecks.Choose=options=>options.Where(x=>x==sadness);
        await b.Terminal.BeforeSideTurnEndVeryEarly(b.Context,CombatSide.Player,[b.Player.Creature]);
        var joy=b.Player.PlayerCombatState!.Hand.Cards.OfType<Joy>().Single();
        check(sadness.Pile==null&&joy.ShouldRetainThisTurn&&b.Player.Creature.Block==5,"End-of-turn emotion uses real transformation, Retain and scaled block");
        var returned=b.Add<Anger>(PileType.Discard);CombatChecks.Choose=options=>options.Where(x=>x==returned);
        await b.ResolveCallbacks(joy);
        check(returned.Pile!.Type==PileType.Hand&&b.Player.Creature.GetPowerAmount<StrengthPower>()==1,"First Joy callback grants scaled temporary strength and retrieves a non-Joy card");
        var again=b.Add<Joy>(PileType.Hand);await b.ResolveCallbacks(again);
        check(b.Player.Creature.GetPowerAmount<StrengthPower>()==3,"Each Joy grants temporary strength while manual retrieval remains once per turn");
        await Hook.AfterSideTurnEnd(b.State,CombatSide.Player,[b.Player.Creature]);
        check(b.Player.Creature.GetPowerAmount<StrengthPower>()==0,"Temporary emotion strength is removed by the native end-of-turn power");
    }

    private static async Task StartOrder(Action<bool,string> check)
    {
        var b=new Battle();b.Copies<AdaptiveStrike>(10);b.Terminal.Ledger.Add(Bond.Creation,60);
        b.State.RoundNumber++;b.Player.PlayerCombatState!.IncrementTurnNumber();
        await Hook.BeforeSideTurnStart(b.State,CombatSide.Player,[b.Player.Creature]);
        await CardPileCmd.AddGeneratedCardToCombat(b.State.CreateCard<Shiv>(b.Player),PileType.Hand,b.Player);var block=b.Player.Creature.Block;
        await Hook.AfterPlayerTurnStart(b.State,b.Context,b.Player);
        await CardPileCmd.AddGeneratedCardToCombat(b.State.CreateCard<Shiv>(b.Player),PileType.Hand,b.Player);
        check(block>0&&b.Player.Creature.Block==block,"A generation between early turn start and player start is counted once, not reset by the relic's later callback");
    }

    private static async Task Specials(Action<bool,string> check)
    {
        var retain=new Battle();retain.Copies<SovereignBlade>();retain.Terminal.Ledger.Add(Bond.Retain,20);
        var retained=retain.Add<DefendIronclad>(PileType.Hand);CardCmd.ApplySingleTurnRetain(retained);
        await retain.Terminal.AfterFlush(retain.Context,retain.Player,[],[retained]);
        CombatChecks.Choose=options=>options.Where(x=>x==retained);await retain.StartTurn();
        check(retained.EnergyCost.GetWithModifiers(CostModifiers.Local)==0,"Advanced Retain grants a real until-played energy discount");
        var retainedPlay=retain.Play(retained);await retain.Terminal.BeforeCardPlayed(retainedPlay);
        check(retain.Terminal.ModifyBlockAdditive(retain.Player.Creature,5,ValueProp.Move,retained,retainedPlay)==8,"Advanced retained-card bonus combines with its base bonus at twenty layers");
        await retain.Terminal.AfterCardPlayedLate(retain.Context,retainedPlay);

        var ancient=new Battle();ancient.Copies<Apparition>(3);ancient.Terminal.Ledger.Add(Bond.Ancient,20);
        var original=ancient.Add<DefendIronclad>(PileType.Hand);original.UpgradeInternal();
        CombatChecks.Choose=options=>options.Where(x=>x==original);
        await ancient.ResolveCallbacks(ancient.Player.PlayerCombatState!.ExhaustPile.Cards.OfType<Apparition>().First());
        var clone=ancient.Player.PlayerCombatState.Hand.Cards.Single(x=>x.CloneOf==original);
        check(ancient.Player.Creature.Block==6&&original.ShouldRetainThisTurn,"First Ancient play grants scaled block and retains another card");
        check(clone.Keywords.Contains(CardKeyword.Ethereal)&&clone.IsUpgraded&&clone.DeckVersion==null,"Advanced Ancient creates a real temporary clone with Ethereal and inherited upgrade");
        check(clone.EnergyCost.GetWithModifiers(CostModifiers.Local)==0&&ancient.Terminal.ModifyBlockAdditive(ancient.Player.Creature,8,ValueProp.Move,clone,null)==6,"Ancient clone receives its energy discount and separate layer-scaled next-play bonus");

        var encounter=new Battle();encounter.Copies<Peck>(5);encounter.Terminal.Ledger.Add(Bond.Encounter,20);
        var played=encounter.Player.PlayerCombatState!.ExhaustPile.Cards.First();var drawn=encounter.Add<Peck>(PileType.Draw);
        CombatChecks.Choose=options=>options.Contains(drawn)?[drawn]:options.Contains(played)?[played]:[];
        await encounter.ResolveCallbacks(played);
        check(drawn.Pile!.Type==PileType.Hand,"First non-temporary off-pool card retrieves another Encounter card from draw");
        check(played.Pile!.Type==PileType.Draw&&encounter.Player.PlayerCombatState.DrawPile.Cards.First()==played,"Advanced Encounter redirects the first card from discard to the top of draw");
        AccessTools.Method(typeof(CardEnergyCost),"AfterCardPlayedCleanup").Invoke(played.EnergyCost,null);
        check(played.EnergyCost.GetWithModifiers(CostModifiers.All)==0,"Encounter next-play discount survives native current-play cleanup");
        check(encounter.Terminal.ModifyDamageAdditive(encounter.Enemy,1,ValueProp.Move,encounter.Player.Creature,played,null)==9,"Encounter base damage and next-play enhancement both scale with layers");
        await encounter.StartTurn();await encounter.ResolveCallbacks(drawn);
        check(drawn.Pile!.Type==PileType.Draw,"Encounter refreshes its retrieval and return on a new player turn");
        var next=encounter.Play(played);await encounter.Terminal.BeforeCardPlayed(next);
        check(played.EnergyCost.GetWithModifiers(CostModifiers.All)==1,"Encounter discount is consumed by the next actual play callback");
        await encounter.Terminal.AfterCardPlayedLate(encounter.Context,next);
        Console.WriteLine("COVENANT_SPECIALS_OK retain-advanced=pass ancient-clone=pass encounter-once-per-combat=pass cleanup-safe-discount=pass");
    }

    private static async Task Doom(Action<bool,string> check)
    {
        var b=new Battle();b.Copies<DeathsDoor>();b.Terminal.Ledger.Add(Bond.Doom,40);
        var drawn=b.Add<DefendIronclad>(PileType.Draw);
        await PowerCmd.Apply<DoomPower>(b.Context,b.Enemy,10,b.Player.Creature,null);
        check(b.Enemy.GetPowerAmount<DoomPower>()==13&&drawn.Pile!.Type==PileType.Hand,"An indirect self-applied Doom effect receives the covenant augmentation and draw");
        check(b.Terminal.Ledger.Get(Bond.Doom)==52,"Doom augmentation does not recursively count its own added layers as a new source event");
        await PowerCmd.Apply<DoomPower>(b.Context,b.Enemy,10,b.Player.Creature,null);
        check(b.Enemy.GetPowerAmount<DoomPower>()==26,"Doom's first-application bonus is not repeated in the same turn");
        var hp=b.Enemy.CurrentHp;await b.Terminal.BeforeSideTurnEnd(b.Context,CombatSide.Player,[b.Player.Creature]);
        check(hp-b.Enemy.CurrentHp==13,"Advanced Doom damage combines current covenant layers and one quarter of the target's Doom");
    }
    private static async Task RewardPhaseCount(Action<bool,string> check)
    {
        var b=new Battle();b.Copies<AdaptiveStrike>();
        await CardPileCmd.AddGeneratedCardToCombat(b.State.CreateCard<Shiv>(b.Player),PileType.Hand,b.Player);
        check(b.Terminal.Counts()[Bond.Creation]==7,"Active combat counts the generated combat card alongside permanent sources");
        CombatChecks.CombatActive=false;
        try
        {
            check(b.Player.Creature.CombatState!=null&&b.Terminal.Counts()[Bond.Creation]==6,"Post-combat count switches to the permanent deck even while the native combat state remains attached");
        }
        finally{CombatChecks.CombatActive=true;}
    }
}

