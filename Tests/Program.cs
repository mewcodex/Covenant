using System.Reflection;
using Covenant.Core;
using Covenant.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.Saves;
using System.Runtime.CompilerServices;

int assertions=0;
void Check(bool value,string message){if(!value)throw new Exception(message);assertions++;}
TestMode.IsOn=true;
var silent=new Harmony("Covenant.Tests.Log");
silent.Patch(typeof(Logger).TypeInitializer!,prefix:new HarmonyMethod(typeof(Helpers),nameof(Helpers.NoLog)));
silent.Patch(typeof(MegaCrit.Sts2.Core.ControllerInput.MegaInput).TypeInitializer!,prefix:new HarmonyMethod(typeof(Helpers),nameof(Helpers.NoLog)));
foreach(var method in typeof(Logger).GetMethods(BindingFlags.Public|BindingFlags.Instance).Where(m=>m.ReturnType==typeof(void)))silent.Patch(method,prefix:new HarmonyMethod(typeof(Helpers),nameof(Helpers.NoLog)));
// The v113 draw command plays debug audio even in TestMode. Suppress only that engine presentation boundary.
silent.Patch(typeof(MegaCrit.Sts2.Core.Audio.Debug.NDebugAudioManager).TypeInitializer!,prefix:new HarmonyMethod(typeof(Helpers),nameof(Helpers.NoLog)));
silent.Patch(AccessTools.PropertyGetter(typeof(MegaCrit.Sts2.Core.Audio.Debug.NDebugAudioManager),"Instance"),prefix:new HarmonyMethod(typeof(Helpers),nameof(Helpers.NoAudio)));
// Native card play measures its visual minimum delay with the Godot clock, even in TestMode.
// The CLI has no engine singleton; keep all play rules intact and replace only that timing boundary.
silent.Patch(typeof(Godot.Time).TypeInitializer!,prefix:new HarmonyMethod(typeof(Helpers),nameof(Helpers.NoLog)));
silent.Patch(AccessTools.Method(typeof(Godot.Time),nameof(Godot.Time.GetTicksMsec)),prefix:new HarmonyMethod(typeof(Helpers),nameof(Helpers.NoTicks)));
foreach(var method in typeof(Log).GetMethods(BindingFlags.Public|BindingFlags.Static).Where(m=>m.ReturnType==typeof(void)))silent.Patch(method,prefix:new HarmonyMethod(typeof(Helpers),nameof(Helpers.NoLog)));
Bootstrap.Initialize();
var models=typeof(CardModel).Assembly.GetTypes().Concat(typeof(CovenantTerminal).Assembly.GetTypes()).Where(t=>!t.IsAbstract&&t.IsSubclassOf(typeof(AbstractModel))&&t.GetConstructor(Type.EmptyTypes)!=null).ToArray();
AccessTools.Field(typeof(ModelDb),"_allAbstractModelSubtypes").SetValue(null,models);
ModelDb.Init(models);
foreach(var type in models)ModelIdSerializationCache.CacheSavedPropertiesForTypeDebug(type);
AccessTools.Field(typeof(ModelIdSerializationCache),"_initialized").SetValue(null,true);
SaveManager.MockInstanceForTesting((SaveManager)RuntimeHelpers.GetUninitializedObject(typeof(SaveManager)));
silent.Patch(AccessTools.Method(typeof(SaveManager),nameof(SaveManager.MarkRelicAsSeen)),prefix:new HarmonyMethod(typeof(Helpers),nameof(Helpers.NoLog)));
silent.Patch(AccessTools.Method(typeof(SaveManager),nameof(SaveManager.SeenFtue)),prefix:new HarmonyMethod(typeof(Helpers),nameof(Helpers.SeenTutorial)));
CombatChecks.InstallEnvironment();

Check(BondCatalog.All.Count==33,"All 33 covenants exist");
foreach(var (bond,index) in BondCatalog.All.Select((d,i)=>(d,i)))
{
    Check((int)bond.Id==index,"Stable model order "+bond.Id);
    Check(bond.BasicText(new(0))!=bond.BasicText(new(60)),"Base tier grows with layers "+bond.Id);
    Check(bond.AdvancedText(new(0))!=bond.AdvancedText(new(60)),"Advanced tier grows with layers "+bond.Id);
    Check(bond.Tier(bond.Basic-1)==0&&bond.Tier(bond.Basic)==1&&bond.Tier(bond.Advanced)==2,"Tier boundaries "+bond.Id);
}
var player=Player.CreateForNewRun(ModelDb.Character<Ironclad>(),UnlockState.all,1);
Check(player.Relics.OfType<CovenantTerminal>().Count()==1,"Terminal automatically added once");
foreach(var character in ModelDb.AllCharacters)
    Check(character.StartingRelics.OfType<CovenantTerminal>().Count()==1,"All six starting relic sets "+character.Id.Entry);
var terminal=CovenantTerminal.For(player)!;
Check(terminal.AllCardPools,"Full six-character pool enabled by default");
terminal.Ledger.Add(Bond.Poison,56);
var restored=(CovenantTerminal)RelicModel.FromSerializable(terminal.ToSerializable());
Check(restored.Ledger.Get(Bond.Poison)==56,"Native saved properties preserve layers");
restored.Ledger.Add(Bond.Poison,1);
Check(terminal.Ledger.Get(Bond.Poison)==56,"Restored ledger independent");
var other=(CovenantTerminal)ModelDb.Relic<CovenantTerminal>().ToMutable();
Check(other.Ledger.Get(Bond.Poison)==0,"New run has no previous layers");
var run=RunState.CreateForTest([player],[],[],seed:"covenant-tests");
var rewardOptions=CardCreationOptions.ForRoom(player,MegaCrit.Sts2.Core.Rooms.RoomType.Monster).WithFlags(CardCreationFlags.IsCardReward);
Check(terminal.ModifyCardRewardCreationOptions(player,rewardOptions).CardPools.Intersect(CovenantTerminal.SixPools).Count()==6,"Reward options include all six native pools");
var fixedOptions=CardCreationOptions.ForRoom(player,MegaCrit.Sts2.Core.Rooms.RoomType.Monster).WithFlags(CardCreationFlags.IsCardReward|CardCreationFlags.NoCardPoolModifications);
Check(terminal.ModifyCardRewardCreationOptions(player,fixedOptions).CardPools.Count==1,"NoCardPoolModifications remains authoritative");
var shelf=terminal.ModifyMerchantCardPool(player,player.Character.CardPool.GetUnlockedCards(player.UnlockState,player.RunState!.CardMultiplayerConstraint)).ToArray();
Check(shelf.Select(c=>c.Pool).Intersect(CovenantTerminal.SixPools).Count()==6,"Merchant colored shelf includes all six pools");
terminal.AllCardPools=false;
var disabledOptions=CardCreationOptions.ForRoom(player,MegaCrit.Sts2.Core.Rooms.RoomType.Monster).WithFlags(CardCreationFlags.IsCardReward);
Check(terminal.ModifyCardRewardCreationOptions(player,disabledOptions).CardPools.Count==1,"Disabled full pool preserves native reward pool");
var savedPool=(CovenantTerminal)RelicModel.FromSerializable(terminal.ToSerializable());
Check(!savedPool.AllCardPools,"Saved run retains disabled pool independent of current default");
terminal.AllCardPools=true;
Check(CardMembership.Definitions["Discovery"].Bonds.Contains(Bond.Creation),"Shared card can belong to Creation without Regent identity");
var combat=new CombatState(runState:run);combat.AddPlayer(player);player.ResetCombatState();
var piles=new[]{player.PlayerCombatState!.Hand,player.PlayerCombatState.DrawPile,player.PlayerCombatState.DiscardPile,player.PlayerCombatState.ExhaustPile};
foreach(var pile in piles)pile.Clear();
foreach(var pile in piles)
{
    var card=combat.CreateCard<StrikeIronclad>(player);pile.AddInternal(card,0);
}
Check(CardMembership.Counts(player)[Bond.Strike]==4,"Four named combat piles all count");
var playing=combat.CreateCard<StrikeIronclad>(player);player.PlayerCombatState.PlayPile.AddInternal(playing,0);
Check(CardMembership.Counts(player)[Bond.Strike]==4,"Play pile excluded from live count");
Check(CardMembership.Counts(player,resolving:playing)[Bond.Strike]==5,"Play snapshot explicitly includes resolving card once");
Check(CardMembership.Counts(player,deck:true)[Bond.Strike]==5,"Outside combat uses permanent deck, not combat instances");
var entropy=combat.CreateCard<Entropy>(player);player.PlayerCombatState.Hand.AddInternal(entropy,0);
Check(CardMembership.Counts(player)[Bond.Harmony]==1,"Entropy alone counts Harmony");
player.PlayerCombatState.Hand.RemoveInternal(entropy);player.PlayerCombatState.PlayPile.AddInternal(entropy,0);
Check(CardMembership.Counts(player).GetValueOrDefault(Bond.Harmony)==0,"Played power no longer counts in four piles");
var curse=combat.CreateCard<Injury>(player);player.PlayerCombatState.ExhaustPile.AddInternal(curse,0);
Check(CardMembership.Counts(player)[Bond.Curse]==1,"Exhausted curse remains a member");
var baseStrike=piles[0].Cards.First(c=>c is StrikeIronclad);
baseStrike.UpgradeInternal();Check(CardMembership.Counts(player)[Bond.Mastery]==1,"Upgrades dynamically count Mastery");
var copy=baseStrike.CreateClone();player.PlayerCombatState.Hand.AddInternal(copy,0);
Check(CardMembership.Counts(player)[Bond.Mastery]==2,"Copies independently count and retain upgrade state");
var generatedShiv=combat.CreateCard<Shiv>(player);player.PlayerCombatState.Hand.AddInternal(generatedShiv,0);
Check(CardMembership.Of(generatedShiv).Contains(Bond.Creation),"Generated playable token in a combat pile counts Creation");
var generatedDazed=combat.CreateCard<Dazed>(player);player.PlayerCombatState.Hand.AddInternal(generatedDazed,0);
Check(!CardMembership.Of(generatedDazed).Contains(Bond.Creation),"Generated status excluded from Creation");
var engineTypes=typeof(CardModel).Assembly.GetTypes().Where(t=>typeof(CardModel).IsAssignableFrom(t)&&!t.IsAbstract&&t.GetConstructor(Type.EmptyTypes)!=null).ToDictionary(t=>t.Name);
foreach(var definition in CardMembership.Definitions.Values)
{
    Check(engineTypes.ContainsKey(definition.ClassName),"Catalog class exists "+definition.ClassName);
    var native=ModelDb.GetById<CardModel>(ModelDb.GetId(engineTypes[definition.ClassName]));
    Check((definition.DynamicType||native.Type.ToString()==definition.Type)&&native.Rarity.ToString()==definition.Rarity,"Pinned identity "+definition.ClassName);
    Check(definition.Bonds.Length>0,"Card has memberships "+definition.ClassName);
    Check(definition.Trait.Amount>0&&definition.Trait.Limit>=0&&definition.Trait.Every==1,"Ordinary growth pays on each event "+definition.ClassName);
    if(native.Type==CardType.Power)Check(definition.Trait.OngoingTrigger!=null&&definition.Trait.OngoingEvery==1,"Attached power growth is smooth "+definition.ClassName);
}
var save=terminal.Ledger.Save();Check(Ledger.Load(save).Get(Bond.Poison)==56,"Ledger JSON round trip");
var usage=new Ledger();Check(usage.Claim("x",2)&&usage.Claim("x",2)&&!usage.Claim("x",2),"Source limit cannot be exceeded");
Console.WriteLine($"COVENANT_FOUNDATION_OK assertions={assertions} native-cards={CardMembership.Definitions.Count} covenants=33 native-save=pass four-piles=pass six-characters=pass");
using(new LegacyTraitIsolation())
{
await CombatChecks.Run(Check);
await MechanismChecks.Run(Check);
await TraitChecks.Run(Check);
await RewardChecks.Run(Check);
}
await MechanismChecks.RunAuthored(Check);
AffinityChecks.Run(Check);
ThresholdSimulation.Run(Check);
Console.WriteLine($"COVENANT_ALL_CHECKS_OK assertions={assertions}");

internal static class Helpers
{
    public static bool NoLog()=>false;
    public static bool NoAudio(ref MegaCrit.Sts2.Core.Audio.Debug.NDebugAudioManager? __result){__result=null;return false;}
    // Only the first-shuffle tutorial is suppressed; native shuffle and card rules still run.
    public static bool SeenTutorial(ref bool __result){__result=true;return false;}
    public static bool NoTicks(ref ulong __result){__result=0;return false;}
}
