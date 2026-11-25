using System.Text.Json;
using Covenant.Core;
using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;

internal static class CombatProbe
{
    private sealed class Selector:ICardSelector
    {
        public Func<IEnumerable<CardModel>,int,int,IEnumerable<CardModel>> Pick=(cards,min,max)=>cards.Take(min);
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,int minSelect,int maxSelect)=>Task.FromResult(Pick(options,minSelect,maxSelect));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,IReadOnlyList<CardRewardAlternative> alternatives)=>new();
    }
    private static Player Player=null!;
    private static readonly PlayerChoiceContext Context=new ThrowingPlayerChoiceContext();
    private static readonly List<string> Passed=new();
    private static readonly List<string> Failed=new();
    private static void Check(bool ok,string message)
    {
        if(!ok){Failed.Add(message);Console.WriteLine("COVENANT_ENGINE_FAIL "+message);return;}Passed.Add(message);Console.WriteLine("COVENANT_ENGINE_PASS "+message);
    }
    private static async Task<T> Add<T>(PileType pile=PileType.Hand) where T:CardModel
    {
        var deck=Player.RunState!.CreateCard<T>(Player);await CardPileCmd.Add(deck,PileType.Deck,skipVisuals:true);
        var card=Player.Creature.CombatState!.CreateCard<T>(Player);card.DeckVersion=deck;
        await CardPileCmd.Add(card,pile,skipVisuals:pile!=PileType.Hand);return card;
    }
    private static async Task Sources<T>(int count=6) where T:CardModel {for(int i=0;i<count;i++)await Add<T>(PileType.Exhaust);}
    private static async Task Clear(CovenantTerminal terminal)
    {
        await CardPileCmd.RemoveFromCombat(Player.PlayerCombatState!.AllCards.ToArray(),skipVisuals:false);
        foreach(var power in Player.Creature.Powers.Where(p=>p is not CovenantAttritionPower).ToArray())await PowerCmd.Remove(power);
        terminal.ResetTransient();terminal.Ledger.ClearCombatUses();
        foreach(var enemy in Player.Creature.CombatState!.HittableEnemies.ToArray())
        {
            enemy.SetMaxHpInternal(10000);enemy.SetCurrentHpInternal(10000);
            foreach(var power in enemy.Powers.ToArray())await PowerCmd.Remove(power);
        }
    }
    private static Task Play(CardModel card,Creature? target=null)=>CardCmd.AutoPlay(Context,card,target??Player.Creature.CombatState!.HittableEnemies.First(),skipCardPileVisuals:false).WaitAsync(TimeSpan.FromSeconds(25));
    private static async Task ManualPlay(SceneTree tree,CardModel card,Creature? target=null)
    {
        if(!card.TryManualPlay(target))throw new Exception("Native manual play rejected "+card.Id+" energy="+Player.PlayerCombatState!.Energy+" stars="+Player.PlayerCombatState.Stars);
        for(int i=0;i<600;i++)
        {
            if((card.Pile?.Type==PileType.Discard||card.Pile==null&&card.Type==CardType.Power)&&CombatManager.Instance.History.CardPlaysFinished.Any(e=>e.CardPlay.Card==card))return;
            await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
        }
        throw new Exception("Native manual play did not finish "+card.Id+" pile="+card.Pile?.Type);
    }
    internal static async Task EndTurn(SceneTree tree)
    {
        var previous=Player.PlayerCombatState!.TurnNumber;
        NCombatRoom.Instance!.Ui.EndTurnButton.CallReleaseLogic();
        for(int i=0;i<900;i++)
        {
            if(Player.PlayerCombatState.TurnNumber>previous&&Player.PlayerCombatState.Phase==PlayerTurnPhase.Play)return;
            await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
        }
        throw new Exception("Native end-turn action did not reach the next playable turn; phase="+Player.PlayerCombatState.Phase);
    }

    internal static async Task Prepare(SceneTree tree, Action<RunState>? beforeCombat = null, EncounterModel? encounter = null,CharacterModel? character=null)
    {
        Console.WriteLine("COVENANT_ENGINE_START entering native singleplayer run");
        character??=ModelDb.Character<Ironclad>();
        Player=Player.CreateForNewRun(character,UnlockState.all,1);
        var acts=ActModel.GetRandomList(new Rng(39,"covenant-smoke"),UnlockState.all,false).ToArray();
        await NGame.Instance!.StartNewSingleplayerRun(Player,true,acts,[],"COVENANT_COMBAT",GameMode.Standard,false).WaitAsync(TimeSpan.FromSeconds(50));
        beforeCombat?.Invoke((RunState)Player.RunState);
        NModalContainer.Instance?.Clear();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster,model:encounter??ModelDb.Encounter<AxebotsNormal>().ToMutable(),showTransition:false).WaitAsync(TimeSpan.FromSeconds(40));
        for(int i=0;i<100&&!CombatManager.Instance.IsInProgress;i++)await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
        Check(CombatManager.Instance.IsInProgress,$"entered a real native combat with {character.Id.Entry}");
        var terminal=CovenantTerminal.For(Player)??throw new Exception("Native player has no covenant terminal");
        Check(terminal.AllCardPools,"native new run enables all six card pools");
        Check(Player.Creature.GetPower<CovenantAttritionPower>() is {EnemyTurnLimit:20,EnemyTurns:0},"native battle start attaches the enemy-turn attrition state");
    }
    internal static void VerifyStartingRelics()
    {
        CharacterModel[] characters=[ModelDb.Character<Ironclad>(),ModelDb.Character<Silent>(),ModelDb.Character<Defect>(),
            ModelDb.Character<Regent>(),ModelDb.Character<Necrobinder>(),ModelDb.Character<Fool>(),ModelDb.Character<Deprived>()];
        foreach(var character in characters)
        {
            var expected=character.StartingRelics.Select(relic=>relic.Id).ToArray();
            var player=Player.CreateForNewRun(character,UnlockState.all,1);
            var terminal=CovenantTerminal.For(player);
            if(terminal==null||player.Relics.Count!=expected.Length+1||player.Relics.Last()!=terminal||
                !player.Relics.Take(expected.Length).Select(relic=>relic.Id).SequenceEqual(expected)||expected.Contains(terminal.Id))
                throw new Exception($"New run did not grant the terminal separately from starting relics for {character.Id}.");
            var upgrade=(TouchOfOrobas)ModelDb.Relic<TouchOfOrobas>().ToMutable();
            if(upgrade.SetupForPlayer(player)!=(expected.Length>0)||upgrade.StarterRelic!=expected.FirstOrDefault())
                throw new Exception($"The native starter upgrade picked the wrong relic for {character.Id}.");
        }
        var active=CovenantTerminal.For(Player)!;
        if(!active.DynamicDescription.GetFormattedText().StartsWith("同一盟约内，同名牌只计一次。消耗牌堆也计入，状态牌和衍生牌除外。",StringComparison.Ordinal)||
            RelicModel.FromSerializable(active.ToSerializable()) is not CovenantTerminal)
            throw new Exception("The terminal must show the short description and remain serializable.");
        Console.WriteLine("COVENANT_STARTING_RELICS_PASS separate terminal grant for all six characters, native starter-upgrade eligibility, and terminal description and save identity");
    }
    internal static async Task Run(SceneTree tree,string output)
    {
        using var isolatedCovenants=new LegacyTraitIsolation();
        await Prepare(tree);
        var terminal=CovenantTerminal.For(Player)!;
        var selector=new Selector();using var choices=RunManager.Instance.CardSelectorStack.UseSelector(Player,selector);
        await Clear(terminal);var enemy=Player.Creature.CombatState!.HittableEnemies.First();

        await Sources<DeadlyPoison>();terminal.Ledger.Add(Bond.Poison,60);var poison=await Add<DeadlyPoison>();
        await Play(poison,enemy);
        Check(enemy.GetPowerAmount<PoisonPower>()>=9&&enemy.GetPowerAmount<WeakPower>()==1,"foreign Poison card fully plays with covenant augmentation and new-target Weak");
        Check(terminal.Ledger.Get(Bond.Poison)>30,"real card play grows permanent Poison layers");

        await Clear(terminal);await Sources<DeadlyPoison>();var venom=await Add<Envenom>();await Play(venom);
        var poisonLayers=terminal.Ledger.Get(Bond.Poison);var venomExpected=2+(poisonLayers+7)/BondCatalog.Get(Bond.Poison).LayerUnit;var poisonedAttack=await Add<Anger>();await Play(poisonedAttack,enemy);
        Check(enemy.GetPowerAmount<PoisonPower>()==venomExpected&&enemy.GetPowerAmount<WeakPower>()==1,"native Envenom from a fully played foreign power augments indirect poison and grants new-target Weak");
        Check(terminal.Ledger.Get(Bond.Poison)==poisonLayers+14,"native Envenom's source-null application grants exactly one trait trigger per poison source");

        await Clear(terminal);await Sources<DeadlyPoison>();var noxious=await Add<NoxiousFumes>();await Play(noxious);
        var fumes=Player.Creature.GetPower<NoxiousFumesPower>()??throw new Exception("Native Noxious Fumes missing after its card play");
        poisonLayers=terminal.Ledger.Get(Bond.Poison);var fumesExpected=3+(poisonLayers+7)/BondCatalog.Get(Bond.Poison).LayerUnit;var fumesTargets=Player.Creature.CombatState!.HittableEnemies.Count();
        await fumes.AfterSideTurnStart(CombatSide.Player,[Player.Creature],Player.Creature.CombatState!);
        Check(enemy.GetPowerAmount<PoisonPower>()==fumesExpected&&terminal.Ledger.Get(Bond.Poison)==poisonLayers+14*fumesTargets,"native turn-start Noxious Fumes applies indirect poison and continuous support with the scale at each application");

        await Clear(terminal);await Sources<DeathsDoor>();terminal.Ledger.Add(Bond.Doom,30);
        await Add<DefendIronclad>(PileType.Draw);await Add<DefendIronclad>(PileType.Draw);
        var surge=await Add<Neurosurge>();await Play(surge);
        var selfDoom=Player.Creature.GetPower<NeurosurgePower>()??throw new Exception("Native Neurosurge missing");
        var doomLayers=terminal.Ledger.Get(Bond.Doom);var afterSelfDraw=await Add<DefendIronclad>(PileType.Draw);
        await selfDoom.AfterSideTurnStart(CombatSide.Player,[Player.Creature],Player.Creature.CombatState!);
        Check(Player.Creature.GetPowerAmount<DoomPower>()==selfDoom.Amount&&terminal.Ledger.Get(Bond.Doom)==doomLayers&&afterSelfDraw.Pile?.Type==PileType.Draw,"native Neurosurge self-Doom is not amplified and does not consume Doom's layer or draw trigger");
        var toTopdeck=await Add<StrikeIronclad>();
        selector.Pick=(cards,min,max)=>cards.Where(card=>card==toTopdeck);
        await PowerCmd.Apply<DoomPower>(Context,enemy,10,Player.Creature,null);
        Check(enemy.GetPowerAmount<DoomPower>()==11+(doomLayers+6)/BondCatalog.Get(Bond.Doom).LayerUnit&&terminal.Ledger.Get(Bond.Doom)==doomLayers+12&&Player.PlayerCombatState!.DrawPile.Cards.FirstOrDefault()==toTopdeck,
            "hostile Doom uses its application-time scale and topdecks the selected card after drawing");
        selector.Pick=(cards,min,max)=>cards.Take(min);

        await Clear(terminal);await Sources<RefineBlade>();terminal.Ledger.Add(Bond.Forge,30);var refine=await Add<RefineBlade>();
        await Play(refine);
        var sword=Player.PlayerCombatState!.Hand.Cards.OfType<SovereignBlade>().Single();
        Check(sword.DynamicVars.Damage.BaseValue>=15,"foreign Forge card creates and augments native Sovereign Blade");
        await Play(sword,enemy);Check(sword.Pile?.Type==PileType.Discard,"native Sovereign Blade completes its full play and enters discard");
        // Deferred return runs through the same callback used by the next native turn.
        await terminal.AfterPlayerTurnStart(Context,Player);
        Check(sword.Pile?.Type==PileType.Hand&&sword.ShouldRetainThisTurn,"advanced Forge returns the played sword and retains it");

        await Clear(terminal);await Sources<Genesis>();terminal.Ledger.Add(Bond.Stars,30);
        await Add<DefendIronclad>(PileType.Draw);var genesisCard=await Add<Genesis>();await Play(genesisCard);
        var genesis=Player.Creature.GetPower<GenesisPower>()??throw new Exception("Native Genesis missing");
        var blackHoleCard=await Add<BlackHole>();await Play(blackHoleCard);
        var blackHole=Player.Creature.GetPower<BlackHolePower>()??throw new Exception("Native Black Hole missing");
        await PlayerCmd.SetStars(0,Player);await genesis.AfterEnergyReset(Player);
        Check(Player.PlayerCombatState!.Stars==genesis.Amount,"native foreign Genesis generates real stars after its power card leaves");
        await PlayerCmd.SetStars(3,Player);var returnedStar=await Add<Comet>(PileType.Discard);var manualStar=await Add<AstralPulse>();
        selector.Pick=(cards,min,max)=>cards.Where(c=>c==returnedStar).Take(max);
        await ManualPlay(tree,manualStar);
        var actualPlay=CombatManager.Instance.History.CardPlaysFinished.Last(e=>e.CardPlay.Card==manualStar).CardPlay;
        Check(!actualPlay.IsAutoPlay&&actualPlay.Resources.StarsSpent==3&&manualStar.LastStarsSpent==3&&Player.PlayerCombatState.Stars==0,"normal native manual card play pays three stars through the real action queue");
        Check(returnedStar.Pile?.Type==PileType.Hand&&returnedStar.ShouldRetainThisTurn&&returnedStar.CurrentStarCost==returnedStar.BaseStarCost-1-terminal.Scale(Bond.Stars).B,"actual star payment empties the pool and resolves the covenant's retained star-discount choice");
        var starHp=enemy.CurrentHp;await terminal.AfterPlayerTurnStart(Context,Player);
        Check(Player.PlayerCombatState.Stars==1&&starHp-enemy.CurrentHp==blackHole.Amount,"the pending next-turn star triggers the real native Black Hole damage");
        selector.Pick=(cards,min,max)=>cards.Take(min);

        await Clear(terminal);await Sources<Bodyguard>();terminal.Ledger.Add(Bond.Bones,30);var summon=await Add<Bodyguard>();
        await Play(summon);
        Check(Player.IsOstyAlive,"foreign summon card spawns native Osty and its scene for Ironclad");
        var draw=await Add<DefendIronclad>(PileType.Draw);var poke=await Add<Poke>();await Play(poke,enemy);
        Check(draw.Pile?.Type==PileType.Draw,"real native Osty attack leaves the draw pile alone");
        await CreatureCmd.Kill(Player.Osty!,true);terminal.BeginTurn();await terminal.AfterPlayerTurnStart(Context,Player);
        Check(Player.IsOstyAlive&&Player.Osty!.CurrentHp==4+2*terminal.Scale(Bond.Bones).A,
            "actual native Osty death schedules a scaled revival alongside the one Summon from its attack");

        await Clear(terminal);await Sources<FoolishShot>();terminal.Ledger.Add(Bond.Arrows,30);var shoot=await Add<FoolishShot>();
        await Play(shoot,enemy);var impaled=enemy.GetPowerInstances<ImpaledPower>().Single(x=>x.Applier==Player.Creature);
        Check(impaled.HitCounter==3&&ArrowCmd.GetAvailableArrowCount(Player)==0,"foreign Shoot card spends a native arrow and impales its target");
        var anger=await Add<Anger>();await Play(anger,enemy);
        Check(impaled.HitCounter==1&&Player.Creature.GetPowerAmount<AimPower>()==2+terminal.Scale(Bond.Arrows).A,$"real non-Shoot attack advances the counter twice and grants layer-scaled Aim; counter={impaled.HitCounter}, aim={Player.Creature.GetPowerAmount<AimPower>()}, expected={2+terminal.Scale(Bond.Arrows).A}");
        var next=await Add<Anger>();await Play(next,enemy);
        Check(ArrowCmd.GetAvailableArrowCount(Player)==1&&shoot.Pile?.Type==PileType.Hand,"actual arrow retrieval recalls a random Shoot card");

        await Clear(terminal);await Sources<WahChoKi>();terminal.Ledger.Add(Bond.Melody,30);var song=await Add<WahChoKi>();
        selector.Pick=(cards,min,max)=>cards.Take(min);
        await Add<DefendIronclad>(PileType.Draw);await Add<DefendIronclad>(PileType.Draw);
        var energy=Player.PlayerCombatState!.Energy;await Play(song,enemy);
        Check(MelodyCmd.MelodiesThisTurn(Player)==3&&Player.PlayerCombatState.Energy==energy+1,"foreign three-melody card fully plays and completes the covenant");

        var motifCard=await Add<Motif>();await Play(motifCard);
        var luteCard=await Add<LostLute>();await Play(luteCard);
        var chordCard=await Add<ViolentChord>();await Play(chordCard);
        for(int i=0;i<7;i++)await Add<DefendIronclad>(PileType.Draw);
        var melodyPlays=CombatManager.Instance.History.CardPlaysFinished.Count();var enemyTurns=Player.Creature.GetPower<CovenantAttritionPower>()!.EnemyTurns;
        await EndTurn(tree);
        Check(Player.Creature.GetPower<CovenantAttritionPower>()!.EnemyTurns==enemyTurns+1,"the actual native end-turn action advances attrition exactly once through the enemy's real turn");
        var inherited=new[]{CardTag.Wah,CardTag.Cho,CardTag.Ki}.Single(terminal.HasInheritedMelody);
        Check(MelodyCmd.PlayedThisTurn(Player,inherited)&&!CombatManager.Instance.History.CardPlaysStarted.Any(entry=>
            entry.Actor==Player.Creature&&entry.HappenedThisTurn(Player.Creature.CombatState)&&
            entry.CardPlay.Card.Tags.Any(tag=>tag is CardTag.Wah or CardTag.Cho or CardTag.Ki))&&
            CombatManager.Instance.History.CardPlaysFinished.Count()==melodyPlays+1,"real next-turn inheritance randomly grants one melody and native Lost Lute records only its actual autoplay");
        Check(Player.PlayerCombatState.Hand.Cards.Count==6&&Player.PlayerCombatState.DrawPile.Cards.Count==0,"real next-turn inherited melody triggers Motif draw and Lost Lute autoplay in addition to the normal hand draw");
        await Add<DefendIronclad>(PileType.Draw);await Add<DefendIronclad>(PileType.Draw);
        async Task<CardModel> AddRepeatedMelody()=>inherited switch
        {
            CardTag.Wah=>await Add<WahTouch>(),
            CardTag.Cho=>await Add<ChoFirm>(),
            _=>await Add<KiVerse>()
        };
        var repeated=await AddRepeatedMelody();melodyPlays=CombatManager.Instance.History.CardPlaysFinished.Count();
        await ManualPlay(tree,repeated,repeated.TargetType==TargetType.AnyEnemy?enemy:null);
        Check(Player.PlayerCombatState.DrawPile.Cards.Count==2&&CombatManager.Instance.History.CardPlaysFinished.Count()==melodyPlays+1,"normally playing an inherited melody does not repeat Motif draw or Lost Lute autoplay");
        await CardPileCmd.RemoveFromCombat(Player.PlayerCombatState.AllCards.Where(c=>CardMembership.Of(c).Contains(Bond.Melody)).ToArray(),skipVisuals:false);
        Check(terminal.Tier(Bond.Melody)==0&&MelodyCmd.PlayedThisTurn(Player,inherited),"real source removal deactivates the covenant while preserving the acquired melody for this turn");
        repeated=await AddRepeatedMelody();melodyPlays=CombatManager.Instance.History.CardPlaysFinished.Count();
        await ManualPlay(tree,repeated,repeated.TargetType==TargetType.AnyEnemy?enemy:null);
        Check(Player.PlayerCombatState.DrawPile.Cards.Count==2&&CombatManager.Instance.History.CardPlaysFinished.Count()==melodyPlays+1,"normal play after covenant deactivation still cannot duplicate inherited-melody acquisition rewards");

        await Clear(terminal);await Sources<Anguish>();terminal.Ledger.Add(Bond.Emotion,30);
        var sadness=Player.Creature.CombatState!.CreateCard<Sadness>(Player);await CardPileCmd.AddGeneratedCardToCombat(sadness,PileType.Hand,Player);
        selector.Pick=(cards,min,max)=>cards.Where(x=>x==sadness);
        await terminal.BeforeSideTurnEndVeryEarly(Context,CombatSide.Player,[Player.Creature]);
        var joy=Player.PlayerCombatState.Hand.Cards.OfType<Joy>().Single();Check(joy.ShouldRetainThisTurn,"real transformation replaces Sadness with retained Joy");
        selector.Pick=(cards,min,max)=>cards.Take(min);await Add<DefendIronclad>(PileType.Draw);await Play(joy);
        Check(Player.Creature.GetPowerAmount<StrengthPower>()>=1,"real Joy play grants native temporary strength with a visible covenant icon");

        await Clear(terminal);await Sources<Coolheaded>();terminal.Ledger.Add(Bond.Resonance,60);
        Player.PlayerCombatState.OrbQueue.Clear();await OrbCmd.AddSlots(Player,2);
        await OrbCmd.Channel<FrostOrb>(Context,Player);
        // Let the next channel open a real swap choice; the deterministic selector chooses the two slot options.
        terminal.ResetTransient();var cold=await Add<Coolheaded>();
        selector.Pick=(cards,min,max)=>cards.Take(min);await Add<DefendIronclad>(PileType.Draw);
        var block=Player.Creature.Block;await Play(cold);
        Check(Player.PlayerCombatState.OrbQueue.Orbs.Count==2&&Player.Creature.Block-block>=6,"foreign channel card spawns native orbs and resolves the two-orb swap choice");
        var powerIcon=ModelDb.Power<CovenantStrengthPower>().Icon;
        Check(powerIcon!=null,"custom temporary-strength power resolves its actual icon resource");

        await Clear(terminal);await Sources<Loop>();await Add<DefendIronclad>(PileType.Draw);
        var loop=await Add<Loop>();await Play(loop);
        var loopPower=Player.Creature.GetPower<LoopPower>()??throw new Exception("Native Loop missing after its card play");
        block=Player.Creature.Block;var passive=Player.PlayerCombatState.OrbQueue.Orbs.First().PassiveVal;
        await loopPower.AfterPlayerTurnStart(Context,Player);
        Check(terminal.Tier(Bond.Resonance)==2&&Player.Creature.Block-block==passive,"newly recognized Loop support fully plays for Ironclad and activates its real Frost passive with advanced Resonance membership");

        await Clear(terminal);var retention=await Add<Anointed>();
        await tree.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        bool HasRetentionBadge()=>NPlayerHand.Instance!.GetCard(retention)!.Body.GetNode<Control>("CovenantIcons").HasNode("Retain");
        Check(!HasRetentionBadge(),"native unupgraded card does not show a Retain covenant merely because its upgrade can retain");
        CardCmd.Upgrade(retention);
        await tree.ToSignal(tree.CreateTimer(.4),SceneTreeTimer.SignalName.Timeout);
        Check(retention.ShouldRetainThisTurn&&HasRetentionBadge(),"native card upgrade updates the actual Retain covenant badge");
        CardCmd.RemoveKeyword(retention,CardKeyword.Retain);
        await tree.ToSignal(tree.CreateTimer(.4),SceneTreeTimer.SignalName.Timeout);
        Check(!HasRetentionBadge(),"removing the native Retain keyword removes the live covenant badge");

        await Clear(terminal);await Sources<SpiritOfAsh>();var spirit=await Add<SpiritOfAsh>();
        Check(!spirit.Keywords.Contains(CardKeyword.Ethereal)&&NPlayerHand.Instance!.GetCard(spirit)!.Body.GetNode<Control>("CovenantIcons").HasNode("Ethereal"),"a real non-Ethereal support card shows its Ethereal covenant badge");
        await Play(spirit);var ghost=await Add<Apparition>();
        Check(NPlayerHand.Instance!.GetCard(ghost)!.Body.GetNode<Control>("CovenantIcons").HasNode("Ethereal"),"a real native Ethereal keyword shows its live badge");
        CardCmd.Upgrade(ghost);await tree.ToSignal(tree.CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
        Check(!NPlayerHand.Instance.GetCard(ghost)!.Body.GetNode<Control>("CovenantIcons").HasNode("Ethereal"),"native upgrade removal of Ethereal removes the live badge");
        var etherealAttack=await Add<Claw>();CardCmd.ApplyKeyword(etherealAttack,CardKeyword.Ethereal);
        var ash=Player.Creature.GetPower<SpiritOfAshPower>()??throw new Exception("Native Spirit of Ash missing");block=Player.Creature.Block;
        await Play(etherealAttack,enemy);
        Check(Player.Creature.Block-block==ash.Amount,"the native Ethereal support ability still grants its own block during a full foreign card play");

        await Clear(terminal);var exhaustOnly=await Add<SecretTechnique>();
        bool HasBadge(CardModel card,string bond)=>NPlayerHand.Instance!.GetCard(card)!.Body.GetNode<Control>("CovenantIcons").HasNode(bond);
        Check(HasBadge(exhaustOnly,"Ember"),"a real self-exhausting card shows its Ember badge");
        CardCmd.Upgrade(exhaustOnly);await tree.ToSignal(tree.CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
        Check(!HasBadge(exhaustOnly,"Ember"),"native upgrade removal of Exhaust removes the live Ember badge");
        var slyCard=await Add<DefendIronclad>();CardCmd.ApplySingleTurnSly(slyCard);
        await tree.ToSignal(tree.CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
        Check(HasBadge(slyCard,"Sleight"),"native temporary Sly adds the live Sleight badge to an ordinary card");
        slyCard.EndOfTurnCleanup();await tree.ToSignal(tree.CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
        Check(!HasBadge(slyCard,"Sleight"),"native temporary Sly cleanup removes the live Sleight badge");

        await Clear(terminal);var heavyBoundary=await Add<Feral>();
        Check(HasBadge(heavyBoundary,"Heavy"),"native two-energy Feral shows its live Heavy badge");
        CardCmd.Upgrade(heavyBoundary);await tree.ToSignal(tree.CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
        Check(!HasBadge(heavyBoundary,"Heavy"),"native upgrade to one energy removes the Heavy badge");
        CardCmd.Downgrade(heavyBoundary);await tree.ToSignal(tree.CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
        Check(HasBadge(heavyBoundary,"Heavy"),"native downgrade to two energy restores the Heavy badge");
        var randomGrit=await Add<TrueGrit>();var choiceArmaments=await Add<Armaments>();
        Check(!HasBadge(randomGrit,"Foresight")&&HasBadge(choiceArmaments,"Foresight"),"native unupgraded True Grit and Armaments show their actual selection memberships");
        CardCmd.Upgrade(randomGrit);CardCmd.Upgrade(choiceArmaments);await tree.ToSignal(tree.CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
        Check(HasBadge(randomGrit,"Foresight")&&!HasBadge(choiceArmaments,"Foresight"),"native upgrades add True Grit's manual choice and remove Armaments' whole-hand choice badge");
        var conditionalSword=await Add<SovereignBlade>();
        Check(!HasBadge(conditionalSword,"Guard")&&!HasBadge(conditionalSword,"Foresight"),"a real sword without Parry has neither a block badge nor a false Seek-name badge");
        var parry=await PowerCmd.Apply<ParryPower>(Context,Player.Creature,10,Player.Creature,null)??throw new Exception("Native Parry missing");
        await tree.ToSignal(tree.CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
        Check(HasBadge(conditionalSword,"Guard"),"native Parry dynamically gives the sword its block covenant badge");
        await PowerCmd.Remove(parry);await tree.ToSignal(tree.CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
        Check(!HasBadge(conditionalSword,"Guard"),"removing native Parry removes the conditional sword block badge");
        var debt=await Add<Debt>();var seekName=await Add<SeekingEdge>();
        Check(!HasBadge(debt,"Profit")&&!HasBadge(seekName,"Foresight"),"gold-loss curses and a Seek name alone do not create benefit memberships");

        await Clear(terminal);await Sources<Entrench>();var punch=await Add<Fisticuffs>();block=Player.Creature.Block;
        Check(HasBadge(punch,"Guard"),"a native damage-based block attack shows its Guard badge");
        var punchHp=enemy.CurrentHp;await Play(punch,enemy);
        Check(Player.Creature.Block-block==punchHp-enemy.CurrentHp+2+terminal.Scale(Bond.Guard).A,"a full native Fisticuffs play grants its damage-based block plus Guard's scaled first-block effect");

        await Clear(terminal);await Sources<MachineLearning>();var learningCard=await Add<MachineLearning>();
        Check(HasBadge(learningCard,"Chain"),"a native delayed-draw support shows its Chain badge");
        await Play(learningCard);
        var learning=Player.Creature.GetPower<MachineLearningPower>()??throw new Exception("Native Machine Learning missing");
        Check(learningCard.Pile==null&&terminal.Tier(Bond.Chain)==2&&learning.ModifyHandDraw(Player,5)==6,"a fully played foreign Machine Learning leaves live counts while its native extra draw remains");
        var chainAttack=await Add<StrikeIronclad>();var chainSkill=await Add<Deflect>();var chainFinish=await Add<Claw>();var chainDraw=await Add<DefendSilent>(PileType.Draw);
        await Play(chainAttack,enemy);await Play(chainSkill);await Play(chainFinish,enemy);
        Check(chainDraw.Pile?.Type==PileType.Hand,"a real attack-skill-attack sequence receives the draw from delayed-draw-supported Chain");
        var generatedCount=await Add<Jackpot>();var exhaustedCount=await Add<PactsEnd>();var exhaustReturn=await Add<KnifeTrap>();
        Check(!HasBadge(generatedCount,"Chain")&&!HasBadge(exhaustedCount,"Chain")&&HasBadge(exhaustedCount,"Ember")&&HasBadge(exhaustReturn,"Ember"),"native badges separate generated-card counts from actual draw and exhaust-pile payoffs");

        await Clear(terminal);await Sources<AllForOne>();
        var zeroSupport=await Add<AllForOne>();var zeroAttack=await Add<Claw>(PileType.Discard);block=Player.Creature.Block;
        Check(!CardMembership.IsPrintedZero(zeroSupport)&&HasBadge(zeroSupport,"Zero"),"a positive-cost native zero-flow support shows the Zero badge without becoming a zero-cost play");
        await Play(zeroSupport,enemy);
        Check(zeroAttack.Pile?.Type==PileType.Hand&&Player.Creature.Block==block,"full native All for One retrieves a real zero-cost attack without triggering the covenant's zero-play block");
        await Play(zeroAttack,enemy);
        Check(Player.Creature.Block-block==3+terminal.Scale(Bond.Zero).A,"the retrieved native Claw receives Zero's current scaled block from the support-enabled covenant");

        await Clear(terminal);await Sources<ToolsOfTheTrade>();terminal.Ledger.Add(Bond.Sleight,30);
        var toolsCard=await Add<ToolsOfTheTrade>();await Play(toolsCard);
        var discardMaterial=await Add<StrikeIronclad>();block=Player.Creature.Block;
        var tools=Player.Creature.GetPower<ToolsOfTheTradePower>()??throw new Exception("Native Tools missing");
        await tools.AfterPlayerTurnStart(Context,Player);
        Check(toolsCard.Pile==null&&discardMaterial.Pile?.Type==PileType.Discard&&Player.Creature.Block-block==3+terminal.Scale(Bond.Sleight).A,"a fully played foreign Tools power leaves live counts and its real later discard activates Sleight");

        await Clear(terminal);await Sources<Tyranny>(8);terminal.Ledger.Add(Bond.Ember,30);
        var tyrannyCard=await Add<Tyranny>();await Play(tyrannyCard);
        var exhaustMaterial=await Add<StrikeIronclad>();block=Player.Creature.Block;energy=Player.PlayerCombatState.Energy;
        var tyranny=Player.Creature.GetPower<TyrannyPower>()??throw new Exception("Native Tyranny missing");
        await tyranny.AfterPlayerTurnStart(Context,Player);
        Check(tyrannyCard.Pile==null&&exhaustMaterial.Pile?.Type==PileType.Exhaust&&Player.Creature.Block-block==5+2*terminal.Scale(Bond.Ember).A&&Player.PlayerCombatState.Energy==energy,"a fully played foreign Tyranny power grants the first-exhaust block without refunding energy early");
        for(int i=0;i<2;i++)await CardCmd.Exhaust(Context,await Add<StrikeIronclad>());
        Check(Player.PlayerCombatState.Energy==energy+1,"the third actual exhaust grants Ember's delayed energy");

        await Clear(terminal);await Sources<Brand>();terminal.Ledger.Add(Bond.Blood,30);
        selector.Pick=(cards,min,max)=>cards.Take(min);
        var branded=await Add<Brand>();await Add<DefendIronclad>();await Add<Anger>(PileType.Draw);
        var beforeHp=Player.Creature.CurrentHp;var beforeBlood=terminal.Ledger.Get(Bond.Blood);
        await Play(branded);
        Check(Player.Creature.CurrentHp==beforeHp-1&&terminal.Ledger.Get(Bond.Blood)>beforeBlood,"native Brand play is a voluntary HP-loss card and grows Blood layers");
        Check(Player.Creature.GetPowerAmount<StrengthPower>()>=1,"native Brand still resolves its own Strength effect after covenant choices");

        await Clear(terminal);await Sources<CreativeAi>(3);terminal.Ledger.Add(Bond.Creation,30);
        var generator=await Add<CreativeAi>();await Play(generator);
        var ai=Player.Creature.GetPower<CreativeAiPower>()??throw new Exception("Native Creative AI did not attach its ability");
        Check(generator.Pile==null&&terminal.Counts().GetValueOrDefault(Bond.Creation)==3,"a genuinely played ongoing power leaves the live Creation count while its native ability remains");
        block=Player.Creature.Block;
        await ai.BeforeHandDraw(Player,Context,Player.Creature.CombatState!);
        var created=Player.PlayerCombatState.Hand.Cards.Single();
        Check(created.Type==CardType.Power&&CardMembership.IsGenerated(created)&&terminal.Counts().GetValueOrDefault(Bond.Creation)==4,"foreign native Creative AI generates a real temporary power and reactivates Creation");
        Check(Player.Creature.Block-block==3+terminal.Scale(Bond.Creation).A,"ongoing generation grants Creation's current layer-scaled block after the source power leaves");
        NRun.Instance!.GetViewport().GetTexture().GetImage().SavePng(output+"/creation-native.png");

        await Clear(terminal);await Sources<AdaptiveStrike>(4);
        var change=await Add<DefendIronclad>();var beforeCreation=terminal.Ledger.Get(Bond.Creation);block=Player.Creature.Block;
        var changed=(await CardCmd.TransformTo<MinionStrike>(change))!.Value.cardAdded;
        Check(changed.Pile?.Type==PileType.Hand&&CardMembership.IsGenerated(changed),"native card transformation renders a real generated replacement in hand");
        Check(terminal.Ledger.Get(Bond.Creation)==beforeCreation+8&&Player.Creature.Block-block==3+terminal.Scale(Bond.Creation).A,"native transformation fires one Creation generation event and one trait per eligible source");

        await RunBalanceChecks(tree,terminal,selector);
        await Clear(terminal);await Sources<Acrobatics>();
        var firstDiscard=await Add<StrikeIronclad>();var kept=await Add<DefendIronclad>();var drawn=await Add<BattleTrance>(PileType.Draw);
        choices.Dispose(); // Exercise the real native choice screens, without the automated card selector.
        var discard=CardCmd.Discard(new BlockingPlayerChoiceContext(),firstDiscard);
        var option=await RewardProbe.PickGrid(tree,output+"/sleight-option.png");
        Check(option is CovenantOption&&option.Title=="抽牌并弃牌","Sleight renders its optional draw-and-discard choice as a real native card");
        var discarded=await RewardProbe.PickGrid(tree,output+"/sleight-discard.png",card=>card==drawn);
        await discard.WaitAsync(TimeSpan.FromSeconds(25));
        Check(discarded==drawn&&drawn.Pile?.Type==PileType.Discard&&kept.Pile?.Type==PileType.Hand,"Sleight's real selection lets the player discard the card just drawn");

        await tree.ToSignal(tree.CreateTimer(.4),SceneTreeTimer.SignalName.Timeout);
        var hud=NRun.Instance!.FindChild("CovenantRoot",true,false) as Control??throw new Exception("Live HUD missing");
        var bar=hud.GetNode<Control>("BondBar");var top=NRun.Instance.GlobalUi.TopBar;
        var edges=new[]{top.GetNode<Control>("%BgImage").GetGlobalRect().End.Y,top.LeftContainer.GetGlobalRect().End.Y,top.RightContainer.GetGlobalRect().End.Y};
        var topBarBottom=edges.Where(y=>y>0&&y<NRun.Instance.GetViewportRect().Size.Y/3).Max();
        Check(bar.Position.Y>=topBarBottom&&bar.Position.Y+bar.Size.Y<NRun.Instance.GetViewportRect().Size.Y/2,"live covenant HUD is visible immediately below the native status row");
        var relicBottom=NRun.Instance.GlobalUi.RelicInventory.RelicNodes.Where(h=>h.IsVisibleInTree()).Select(h=>h.GetGlobalRect().End.Y).DefaultIfEmpty(topBarBottom).Max();
        Check(bar.GetGlobalRect().Position.Y>=relicBottom+3.9f,"live covenant HUD clears the native relic inventory row");
        NRun.Instance.GetViewport().GetTexture().GetImage().SavePng(output+"/native-combat.png");
        var transition=NGame.Instance!.Transition;transition.Visible=true;await transition.FadeOut(.1f);
        await tree.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        var covered=NRun.Instance.GetViewport().GetTexture().GetImage();var viewportSize=NRun.Instance.GetViewportRect().Size;
        var point=bar.GetGlobalRect().Position+new Vector2(36,30);
        var pixel=covered.GetPixel((int)(point.X*covered.GetWidth()/viewportSize.X),(int)(point.Y*covered.GetHeight()/viewportSize.Y));
        Check(pixel.R+pixel.G+pixel.B<.03f,"the actual native black transition covers covenant UI");
        covered.SavePng(output+"/transition-covered-ui.png");await transition.FadeIn(.1f);transition.Visible=false;
        if(Failed.Count>0)throw new Exception(string.Join("; ",Failed));
        File.WriteAllText(output+"/combat-runtime.json",JsonSerializer.Serialize(new{game="v0.113.1",status="pass",nativeCharacter="Ironclad",checks=Passed,topBarBottom,covenantTop=bar.Position.Y},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("COVENANT_ENGINE_COMPLETE checks="+Passed.Count+" native-card-plays=poison,forge,sword,summon,osty-attack,shoot,non-shoot,melody,joy,channel native-sleight-choice=pass");
    }

    private static async Task RunBalanceChecks(SceneTree tree,CovenantTerminal terminal,Selector selector)
    {
        selector.Pick=(cards,min,max)=>cards.Take(min);
        await Clear(terminal);await Sources<BladeDance>(2);
        var before=terminal.Ledger.Get(Bond.Blades);
        Shiv? first=null;
        for(int i=0;i<9;i++)
        {
            var shiv=Player.Creature.CombatState!.CreateCard<Shiv>(Player);first??=shiv;
            await CardPileCmd.AddGeneratedCardToCombat(shiv,PileType.Hand,Player);await Play(shiv);
        }
        Check(terminal.Ledger.Get(Bond.Blades)==before+36,"nine native generated Shivs award eighteen own-action and eighteen permanent support layers");
        Check(first!.Pile?.Type==PileType.Exhaust&&!Presentation.TraitText(first).Contains("临时增加"),"a generated card declares permanent growth in the exhaust pile");
        var saved=terminal.ToSerializable();
        var restored=(CovenantTerminal)RelicModel.FromSerializable(saved);
        Check(restored.Ledger.Get(Bond.Blades)==before+36,"native relic save fields retain all generated-card permanent growth");

        await Clear(terminal);await Sources<StrikeIronclad>(12);await Sources<Claw>(8);await Sources<TrueGrit>(8);
        for(int i=0;i<8;i++)await Add<DefendIronclad>(PileType.Draw);
        var energy=Player.PlayerCombatState!.Energy;
        for(int i=0;i<3;i++)await Play(await Add<StrikeIronclad>());
        for(int i=0;i<3;i++)await Play(await Add<Claw>());
        Check(Player.PlayerCombatState.Energy==energy+2,"native Strike and Zero sequences each provide one covenant energy");
        var block=Player.Creature.Block;
        for(int i=0;i<3;i++)await CardCmd.Exhaust(Context,await Add<DefendIronclad>());
        Check(Player.PlayerCombatState.Energy==energy+3&&Player.Creature.Block>block,"the native third covenant energy source pays in full");
        await PlayerCmd.GainEnergy(1,Player);
        Check(Player.PlayerCombatState.Energy==energy+4,"native non-covenant energy remains independent");

        await Clear(terminal);
        for(int i=0;i<10;i++){var source=await Add<Bludgeon>(PileType.Exhaust);source.UpgradeInternal();source.FinalizeUpgradeInternal();}
        var enemy=Player.Creature.CombatState!.HittableEnemies.First();
        for(int i=0;i<2;i++)
        {
            var card=await Add<Bludgeon>();card.UpgradeInternal();card.FinalizeUpgradeInternal();
            var expected=card.DynamicVars.Damage.BaseValue+(i==0?3+terminal.Scale(Bond.Mastery).A:0);
            var hp=enemy.CurrentHp;block=enemy.Block;await Play(card,enemy);
            var actual=hp-enemy.CurrentHp+block-enemy.Block;
            Check(actual==expected,$"native upgraded heavy attack {i+1} receives only its eligible first-upgrade bonus; expected={expected}, actual={actual}");
        }
        await Clear(terminal);await Sources<Dualcast>();
        await OrbCmd.Channel<DarkOrb>(Context,Player);
        await Play(await Add<Dualcast>());var overloadBlock=Player.Creature.Block;
        await Play(await Add<Dualcast>());
        Check(Player.Creature.Block>overloadBlock&&Player.PlayerCombatState!.OrbQueue.Orbs.Count<=Player.PlayerCombatState.OrbQueue.Capacity,"two fully played Dualcasts with a full orb slot terminate their derived chains and both reward independent evocations");

        await Clear(terminal);await Sources<NoxiousFumes>(2);
        var originalFumes=await Add<NoxiousFumes>();var clone=originalFumes.CreateClone();
        await CardPileCmd.AddGeneratedCardToCombat(clone,PileType.Hand,Player);await Play(clone);
        var cloneGrowth=terminal.Ledger.Get(Bond.Poison);var nativeFumes=Player.Creature.GetPower<NoxiousFumesPower>()!;
        await nativeFumes.AfterSideTurnStart(CombatSide.Player,[Player.Creature],Player.Creature.CombatState!);
        Check(clone.Pile==null&&terminal.Ledger.Get(Bond.Poison)>cloneGrowth,$"a generated power clone keeps attached growth: pile={clone.Pile?.Type}, before={cloneGrowth}, after={terminal.Ledger.Get(Bond.Poison)}, tier={terminal.Tier(Bond.Poison)}");

        await Clear(terminal);await Sources<Glow>();
        Player.PlayerCombatState!.LoseStars(Player.PlayerCombatState.Stars);await PlayerCmd.GainStars(5,Player);await PlayerCmd.GainEnergy(8,Player);
        var beforeAegisLayers=terminal.Ledger.Get(Bond.Stars);
        var aegis=await Add<NeutronAegis>();await ManualPlay(tree,aegis);
        var aegisPlay=CombatManager.Instance.History.CardPlaysFinished.Last(e=>e.CardPlay.Card==aegis).CardPlay;
        Check(aegisPlay.Resources.StarsSpent==5&&Player.PlayerCombatState.Stars==0&&terminal.Ledger.Get(Bond.Stars)>=beforeAegisLayers+16&&Player.Creature.GetPowerAmount<PlatingPower>()==8,
            "Neutron Aegis pays five real stars, keeps eight native Plating and earns its play growth");
        var starsBefore=Player.PlayerCombatState.Stars;var knockout=await Add<KnockoutBlow>();await ManualPlay(tree,knockout,Player.Creature.CombatState!.HittableEnemies.First());
        Check(Player.PlayerCombatState.Stars==starsBefore,"a real nonlethal Knockout Blow waits for the enemy to die");
        if(Player.Creature.CombatState!.HittableEnemies.Count()==1)
        {
            var survivor=await CreatureCmd.Add<Axebot>(Player.Creature.CombatState);
            survivor.SetMaxHpInternal(10000);survivor.SetCurrentHpInternal(10000);
        }
        enemy=Player.Creature.CombatState!.HittableEnemies.First();enemy.SetCurrentHpInternal(1);starsBefore=Player.PlayerCombatState.Stars;
        await PlayerCmd.GainEnergy(3,Player);await ManualPlay(tree,await Add<KnockoutBlow>(),enemy);
        Check(Player.PlayerCombatState.Stars==starsBefore+10,"the delayed mark and the lethal Knockout Blow each pay their own five-star reward");

    }
}


