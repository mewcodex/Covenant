using System.Reflection;
using Covenant.Core;
using Covenant.Game;
using HarmonyLib;
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
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.TestSupport;

internal static class CombatChecks
{
    private sealed class Skip:ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,int minSelect,int maxSelect)=>Task.FromResult<IEnumerable<CardModel>>(options.Take(minSelect));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,IReadOnlyList<CardRewardAlternative> alternatives)=>new();
    }
    private static bool NoEnd(ref bool __result){__result=false;return false;}
    private static bool Yes(ref bool __result){__result=true;return false;}
    internal static bool CombatActive=true;
    private static bool InProgress(ref bool __result){__result=CombatActive;return false;}
    private static bool NoName(ref Task __result){__result=Task.CompletedTask;return false;}
    private static bool NoThink()=>false;
    internal static Func<IReadOnlyList<CardModel>,IEnumerable<CardModel>>? Choose;
    private static bool SkipChoices(IReadOnlyList<CardModel> cardsIn,ref Task<IEnumerable<CardModel>> __result){__result=Task.FromResult(Choose?.Invoke(cardsIn)??[]);return false;}
    private static bool _installed;
    internal static void InstallEnvironment()
    {
        if(_installed)return;_installed=true;
        var h=new Harmony("Covenant.Tests.CombatEnvironment");
        foreach(var name in new[]{"IsOverOrEnding","IsEnding","ShouldSkipCombatEffect"})h.Patch(AccessTools.PropertyGetter(typeof(CombatManager),name),prefix:new HarmonyMethod(typeof(CombatChecks),nameof(NoEnd)));
        h.Patch(AccessTools.PropertyGetter(typeof(CombatManager),"IsInProgress"),prefix:new HarmonyMethod(typeof(CombatChecks),nameof(InProgress)));
        h.Patch(AccessTools.Method(typeof(CardSelectCmd),nameof(CardSelectCmd.FromSimpleGrid)),prefix:new HarmonyMethod(typeof(CombatChecks),nameof(SkipChoices)));
        // This callback only resolves the applier's platform display name, which is unavailable in this CLI fixture.
        h.Patch(AccessTools.Method(typeof(ImpaledPower),nameof(ImpaledPower.AfterApplied)),prefix:new HarmonyMethod(typeof(CombatChecks),nameof(NoName)));
        // Empty-draw thought bubbles are presentation and require the game's loaded localization tables.
        h.Patch(AccessTools.Method(typeof(ThinkCmd),nameof(ThinkCmd.Play)),prefix:new HarmonyMethod(typeof(CombatChecks),nameof(NoThink)));
    }
    internal static async Task Run(Action<bool,string> check)
    {
        InstallEnvironment();
        var p=Player.CreateForNewRun(ModelDb.Character<Ironclad>(),UnlockState.all,2);
        var run=RunState.CreateForTest([p],[],[],seed:"covenant-combat");
        var combat=new CombatState(ModelDb.Encounter<CultistsNormal>().ToMutable(),run);combat.AddPlayer(p);p.ResetCombatState();
        var enemy=combat.CreateCreature(ModelDb.Monster<BigDummy>().ToMutable(),CombatSide.Enemy,"0");combat.AddCreature(enemy);
        var terminal=CovenantTerminal.For(p)!;var c=new ThrowingPlayerChoiceContext();
        CardModel Add<T>(PileType pile=PileType.Hand) where T:CardModel
        {
            var card=combat.CreateCard<T>(p);card.DeckVersion=p.Deck.Cards.FirstOrDefault(x=>x is T);pile.GetPile(p).AddInternal(card,0);return card;
        }
        foreach(var pile in p.PlayerCombatState!.AllPiles)pile.Clear();
        var attack=Add<StrikeIronclad>();Add<StrikeIronclad>(PileType.Draw);
        terminal.Ledger.Add(Bond.Strike,42);
        check(terminal.ModifyDamageAdditive(enemy,6,ValueProp.Move,p.Creature,attack,null)==6,"42 layers add four damage per Strike hit with slower uncapped growth");
        terminal.Ledger.Add(Bond.Strike,20);
        check(terminal.ModifyDamageAdditive(enemy,6,ValueProp.Move,p.Creature,attack,null)==8,"62 layers add five damage; stacking still matters");
        check(terminal.ModifyDamageAdditive(enemy,6,ValueProp.Unpowered,p.Creature,attack,null)==0,"Unpowered derivative damage does not recursively inherit card bonuses");
        var play=new CardPlay{Card=attack,Player=p,Target=enemy,PlayCount=1,PlayIndex=0,IsAutoPlay=false,ResultPile=PileType.Discard,Resources=new ResourceInfo{EnergySpent=1,EnergyValue=1,StarsSpent=0,StarValue=0}};
        p.PlayerCombatState.Hand.RemoveInternal(attack);p.PlayerCombatState.PlayPile.AddInternal(attack,0);
        await terminal.BeforeCardPlayed(play);
        check(terminal.Counts()[Bond.Strike]==1,"Live tier drops while card is in Play");
        check(terminal.ModifyDamageAdditive(enemy,6,ValueProp.Move,p.Creature,attack,play)==8,"This play keeps its pre-play tier snapshot");
        var before=enemy.CurrentHp;
        await CreatureCmd.Damage(c,enemy,6,ValueProp.Move,attack,play);
        check(before-enemy.CurrentHp==14,"Real native damage command includes covenant bonus");
        await terminal.AfterCardPlayed(c,play);
        check(terminal.Ledger.Get(Bond.Strike)==63,"Card's own trait grants permanent layer through actual post-play hook");
        await terminal.AfterCardPlayedLate(c,play);
        check(terminal.ModifyDamageAdditive(enemy,6,ValueProp.Move,p.Creature,attack,null)==0,"Snapshot is cleared after resolution");
        p.PlayerCombatState.PlayPile.RemoveInternal(attack);p.PlayerCombatState.ExhaustPile.AddInternal(attack,0);
        check(terminal.Counts()[Bond.Strike]==2,"Exhaust restores membership; exhausting does not remove covenant source");
        for(int i=0;i<5;i++)Add<DefendIronclad>();
        var blockBefore=p.Creature.Block;
        await terminal.AfterBlockGained(p.Creature,5,ValueProp.Move,p.PlayerCombatState.Hand.Cards.OfType<DefendIronclad>().First());
        check(p.Creature.Block-blockBefore==3,"Guard uses native block command");
        await terminal.AfterBlockGained(p.Creature,5,ValueProp.Move,p.PlayerCombatState.Hand.Cards.OfType<DefendIronclad>().First());
        check(p.Creature.Block-blockBefore==3,"Guard is once per turn");
        check(terminal.TurnLimit==20,"Monster turn limit is 20");
        var elite=new CombatState(ModelDb.Encounter<BygoneEffigyElite>().ToMutable(),run);elite.AddPlayer(p);
        check(terminal.TurnLimit==30,"Elite turn limit is 30");
        var boss=new CombatState(ModelDb.Encounter<AeonglassBoss>().ToMutable(),run);boss.AddPlayer(p);
        check(terminal.TurnLimit==100,"Boss turn limit is 100");
        Console.WriteLine("COVENANT_COMBAT_PRIMITIVES_OK native-damage=pass layer-trait=pass snapshot=pass native-block=pass limits=20/30/100");
    }
}
