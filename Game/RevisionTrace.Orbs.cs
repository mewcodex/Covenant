using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;

namespace Covenant.Game;

internal static partial class RevisionTrace
{
    private sealed class OrbOrigins{internal readonly List<(CardModel Source,int Turn)> Cards=new();}
    private sealed class OrbContext(OrbModel orb,Scope? cause,OrbContext? parent)
    {internal readonly OrbModel Orb=orb;internal readonly Scope? Cause=cause;internal readonly OrbContext? Parent=parent;}
    private static readonly ConditionalWeakTable<OrbModel,OrbOrigins> OrbSources=new();
    private static readonly AsyncLocal<OrbContext?> ActiveOrb=new();
    private static void InstallOrbs(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(OrbCmd),nameof(OrbCmd.RemoveSlots)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(OrbSlotsChanged)));
        foreach(var type in typeof(OrbModel).Assembly.GetTypes().Where(t=>!t.IsAbstract&&t.IsSubclassOf(typeof(OrbModel))))
        {
            var passive=type.GetMethod(nameof(OrbModel.Passive),BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly);
            if(passive!=null)harmony.Patch(passive,prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(OrbStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(OrbFinished)),finalizer:new HarmonyMethod(typeof(RevisionTrace),nameof(OrbEnded)));
            var evoke=type.GetMethod(nameof(OrbModel.Evoke),BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly);
            if(evoke!=null)harmony.Patch(evoke,prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(OrbStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(OrbEvokeFinished)),finalizer:new HarmonyMethod(typeof(RevisionTrace),nameof(OrbEnded)));
        }
    }
    private static void OrbSlotsChanged(Player player)=>CovenantTerminal.For(player)?.RefreshBattleStatuses();
    private static void OrbStarting(OrbModel __instance,out OrbContext __state){__state=new(__instance,Current,ActiveOrb.Value);ActiveOrb.Value=__state;}
    private static void OrbEnded(OrbContext __state)=>ActiveOrb.Value=__state.Parent;
    private static void OrbFinished(OrbContext __state,ref Task __result){__result=CompleteOrb(__state,__result);ActiveOrb.Value=__state.Parent;}
    private static async Task CompleteOrb(OrbContext context,Task original){try{await original;}finally{ActiveOrb.Value=context.Parent;}}
    private static void OrbEvokeFinished(OrbContext __state,ref Task<IEnumerable<Creature>> __result){__result=CompleteOrbEvoke(__state,__result);ActiveOrb.Value=__state.Parent;}
    private static async Task<IEnumerable<Creature>> CompleteOrbEvoke(OrbContext context,Task<IEnumerable<Creature>> original){try{return await original;}finally{ActiveOrb.Value=context.Parent;}}
    private static CardModel[] ChannelOrigins(Scope? scope)
    {
        if(scope is not {Suppressed:false})return [];
        if(scope.Model is PowerModel power&&PowerCredits.TryGetValue(power,out var credits))
        {
            var skip=scope.OrbGenerationIndex++;
            foreach(var credit in credits.Items)
            {if(skip>=credit.Units){skip-=credit.Units;continue;}return credit.Source==null?[]:[credit.Source];}
        }
        return scope.Frames.Select(f=>f.Source).Distinct().ToArray();
    }
    private static void OrbCreated(OrbModel orb,IEnumerable<CardModel> sources)
    {
        var origins=OrbSources.GetOrCreateValue(orb).Cards;
        foreach(var source in sources)if(!origins.Any(c=>c.Source==source))origins.Add((source,source.Owner.PlayerCombatState?.TurnNumber??0));
    }
    private static async Task OrbOriginEvent(OrbModel orb,string eventName)
    {
        if(!OrbSources.TryGetValue(orb,out var origins))return;
        foreach(var (source,turn) in origins.Cards)
        {
            var terminal=CovenantTerminal.For(source.Owner);if(terminal==null)continue;
            var frame=new RevisionFrame(source,eventName){Scope="orb"};
            frame.Values["orb_created_turn"]=turn;frame.Values["orb_turn"]=source.Owner.PlayerCombatState?.TurnNumber??0;
            await terminal.FireRevision(frame);
        }
    }
    internal static bool HasOrbCredits(CardModel source)=>source.Owner.PlayerCombatState?.OrbQueue.Orbs.Any(orb=>OrbSources.TryGetValue(orb,out var origins)&&origins.Cards.Any(o=>o.Source==source))==true;
    internal static string StatusOrbs(CardModel source,string condition)
    {
        if(source.Owner.PlayerCombatState is not {} state)return "";
        var sameTurn=condition.Contains("orb_created_turn==orb_turn",StringComparison.Ordinal);
        return string.Join("、",state.OrbQueue.Orbs.Select((orb,index)=>(orb,index))
            .Where(pair=>OrbSources.TryGetValue(pair.orb,out var origins)&&origins.Cards.Any(origin=>origin.Source==source&&(!sameTurn||origin.Turn==state.TurnNumber)))
            .Select(pair=>$"第{pair.index+1}槽的{pair.orb.Title.GetFormattedText()}"));
    }
    internal static decimal OrbPassiveBonus(Player owner)
    {
        if(ActiveOrb.Value is not {Orb:LightningOrb} active||active.Orb.Owner!=owner||!ReferenceEquals(Current,active.Cause)||active.Cause is not {Suppressed:false} cause)return 0;
        decimal total=0;
        foreach(var frame in cause.Frames)
            foreach(var rule in RevisionDescriptions.Program(frame.Source)?.Rules.Where(r=>r.Event=="orb_passive_bonus")??[])
                total+=Math.Max(0,Math.Floor(Covenant.Core.RuleExpression.Evaluate(rule.Amount,key=>key=="A"?CovenantTerminal.For(owner)?.Scale(rule.Bond).A??0:frame.Resolve(key))));
        return total;
    }
}
