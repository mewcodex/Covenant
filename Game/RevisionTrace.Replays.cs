using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Combat;

namespace Covenant.Game;

internal static partial class RevisionTrace
{
    private sealed class ReplayGrant(CardModel? card,int units)
    {internal CardModel? Card=card;internal int Units=units;}
    private sealed class ReplayOrigins
    {
        internal readonly List<ReplayGrant> Grants=new();
        internal int Turn=-1,UsedThisTurn;
    }
    private sealed record ReplayReceipt(CardModel Card,int First,int Last);
    private static readonly ConditionalWeakTable<PowerModel,ReplayOrigins> ReplayBudgets=new();
    private static readonly ConditionalWeakTable<PowerModel,ReplayReceipt> ReplayReceipts=new();
    private static bool ConsumesReplayCharge(PowerModel power)=>power.GetType().Name is "BurstPower" or "OneTwoPunchPower" or "SignalBoostPower" or "KiVersePower";
    internal static void RecordReplayGrant(PowerModel power,CardModel? source,decimal amount)
    {
        if(amount<=0)return;
        if(!ConsumesReplayCharge(power)&&power.GetType().Name!="EchoFormPower")return;
        ReplayBudgets.GetOrCreateValue(power).Grants.Add(new(source,(int)Math.Min(int.MaxValue,Math.Floor(amount))));
    }
    private static bool HasReplayCharge(PowerModel power,CardModel card)=>!ConsumesReplayCharge(power)||
        ReplayBudgets.TryGetValue(power,out var budget)&&budget.Grants.Any(g=>g.Card==card&&g.Units>0);
    private static void InstallReplays(Harmony harmony)
    {
        harmony.Patch(AccessTools.PropertySetter(typeof(CardModel),nameof(CardModel.BaseReplayCount)),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(BaseReplayStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(BaseReplayFinished)));
        foreach(var type in typeof(PowerModel).Assembly.GetTypes().Where(t=>!t.IsAbstract&&t.IsSubclassOf(typeof(PowerModel))))
        {
            var method=type.GetMethod("ModifyCardPlayCount",BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly);
            if(method!=null)harmony.Patch(method,postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(ReplayModified)));
        }
        var fallback=AccessTools.Method(typeof(AbstractModel),"AfterModifyingCardPlayCount");
        harmony.Patch(fallback,prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(NativeScopeStart)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(NativeScopeTask)),finalizer:new HarmonyMethod(typeof(RevisionTrace),nameof(NativeScopeEnd)));
    }
    private static void BaseReplayStarting(CardModel __instance,out int __state)=>__state=__instance.BaseReplayCount;
    private static void BaseReplayFinished(CardModel __instance,int __state)
    {
        if(__instance.BaseReplayCount<=__state||Current is not {Suppressed:false} scope)return;
        foreach(var frame in scope.Frames)CovenantTerminal.For(frame.Source.Owner)?.RevisionAttach(frame.Source,__instance,"base_replay",firstPlay:1);
    }
    private static void ReplayModified(PowerModel __instance,CardModel card,int playCount,int __result)
    {
        if(__result<=playCount)return;
        ReplayReceipts.Remove(__instance);ReplayReceipts.Add(__instance,new(card,playCount,__result-1));
    }
    private static void AttachNativeReplays(PowerModel power,CardModel subject)
    {
        if(!ReplayReceipts.TryGetValue(power,out var receipt)||receipt.Card!=subject)return;
        ReplayReceipts.Remove(power);
        if(!ReplayBudgets.TryGetValue(power,out var budget))return;
        var turn=subject.Owner.PlayerCombatState?.TurnNumber??0;
        if(budget.Turn!=turn){budget.Turn=turn;budget.UsedThisTurn=0;}
        for(var index=receipt.First;index<=receipt.Last;index++)
        {
            ReplayGrant? selected;
            if(ConsumesReplayCharge(power))
            {
                selected=budget.Grants.FirstOrDefault(g=>g.Units>0);
                if(selected!=null)selected.Units--;
            }
            else
            {
                var skip=power.GetType().Name=="EchoFormPower"
                    ?CombatManager.Instance.History.CardPlaysStarted.Count(e=>e.Actor==subject.Owner.Creature&&e.CardPlay.IsFirstInSeries&&e.HappenedThisTurn(subject.Owner.Creature.CombatState))
                    :budget.UsedThisTurn++;
                selected=null;
                foreach(var grant in budget.Grants)
                {if(skip<grant.Units){selected=grant;break;}skip-=grant.Units;}
            }
            if(selected?.Card is {} origin)CovenantTerminal.For(origin.Owner)?.RevisionAttach(origin,subject,"replay",index,index);
        }
    }
}
