using System.Reflection;
using System.Runtime.CompilerServices;
using Covenant.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

internal static class RevisionNativeBonuses
{
    private sealed class VariableLease{internal int Held;}
    private static readonly ConditionalWeakTable<CardModel,VariableLease> VariableLeases=new();
    private sealed class VariableScope
    {
        internal readonly List<(DynamicVar Variable,decimal Delta)> Deltas=new();
        internal bool Restored;
        internal VariableLease? Lease;
        internal void Restore(){if(Restored)return;Restored=true;foreach(var (variable,delta) in Deltas)variable.BaseValue-=delta;if(Lease!=null)Interlocked.Exchange(ref Lease.Held,0);}
    }
    internal static void Install(Harmony harmony)
    {
        RevisionLateDamage.Install(harmony);
        harmony.Patch(AccessTools.Method(typeof(CalculatedVar),nameof(CalculatedVar.Calculate)),prefix:new HarmonyMethod(typeof(RevisionNativeBonuses),nameof(CalculationStarting)),postfix:new HarmonyMethod(typeof(RevisionNativeBonuses),nameof(CalculationFinished)),finalizer:new HarmonyMethod(typeof(RevisionNativeBonuses),nameof(VariablesFailed)));
        harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Models.Cards.Alchemize),"OnPlay"),prefix:new HarmonyMethod(typeof(RevisionNativeBonuses),nameof(AlchemyStarting)));
        var variableCards=new HashSet<string>(StringComparer.Ordinal);
        var additivePowers=new HashSet<string>(StringComparer.Ordinal);
        foreach(var definition in CardMembership.Definitions.Values)
        {
            if(definition.Trait.Revision is not {} revision)continue;
            foreach(var program in new[]{revision.Base,revision.Upgraded})
            {
                if(program.Rules.Any(rule=>rule.Event=="variable"))variableCards.Add(definition.ClassName);
                if(program.Rules.Any(rule=>rule.Event=="power_modifier_bonus"))additivePowers.UnionWith(program.NativePowers);
            }
        }
        var nativeTypes=typeof(CardModel).Assembly.GetTypes();
        foreach(var type in nativeTypes.Where(t=>!t.IsAbstract&&t.IsSubclassOf(typeof(CardModel))&&variableCards.Contains(t.Name)))
            foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly)
                .Where(m=>m.ReturnType==typeof(Task)&&(m.Name is "OnPlay" or "OnTurnEndInHand"||m.Name.StartsWith("After",StringComparison.Ordinal)||m.Name.StartsWith("Before",StringComparison.Ordinal))))
                harmony.Patch(method,prefix:new HarmonyMethod(typeof(RevisionNativeBonuses),nameof(VariablesStarting)),postfix:new HarmonyMethod(typeof(RevisionNativeBonuses),nameof(VariablesFinished)),finalizer:new HarmonyMethod(typeof(RevisionNativeBonuses),nameof(VariablesFailed)));
        foreach(var type in nativeTypes.Where(t=>!t.IsAbstract&&t.IsSubclassOf(typeof(PowerModel))&&additivePowers.Contains(t.Name)))
            foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)
                .Where(m=>m.ReturnType==typeof(decimal)&&m.Name is "ModifyDamageAdditive" or "ModifyBlockAdditive"))
                harmony.Patch(method,postfix:new HarmonyMethod(typeof(RevisionNativeBonuses),nameof(ModifierFinished)));
    }
    private static bool AlchemyStarting(CardModel __instance,ref Task __result)
    {
        if(__instance.Owner.HasOpenPotionSlots||RevisionDescriptions.Program(__instance)?.Rules.Any(r=>r.Event=="play_override")!=true||CovenantTerminal.For(__instance.Owner) is not {} terminal)return true;
        __result=terminal.FireRevision(new RevisionFrame(__instance,"play_override"){Activation=CardMembership.Counts(__instance.Owner,resolving:__instance)});
        return false;
    }
    private static void VariablesStarting(CardModel __instance,MethodBase __originalMethod,out VariableScope __state)
    {
        __state=new();
        FillVariables(__instance,__state);
    }
    private static void CalculationStarting(AbstractModel ____owner,out VariableScope __state)
    {__state=new();if(____owner is CardModel card)FillVariables(card,__state);}
    private static void CalculationFinished(VariableScope __state)=>__state.Restore();
    private static void FillVariables(CardModel __instance,VariableScope scope)
    {
        if(!__instance.IsMutable||CovenantTerminal.For(__instance.Owner) is not {} terminal||RevisionDescriptions.Program(__instance) is not {} program)return;
        if(!program.Rules.Any(r=>r.Event=="variable"))return;
        var lease=VariableLeases.GetOrCreateValue(__instance);if(Interlocked.CompareExchange(ref lease.Held,1,0)!=0)return;scope.Lease=lease;
        try
        {
        foreach(var rule in program.Rules.Where(r=>r.Event=="variable"))
        {
            if(rule.Parameter is not {} name||!__instance.DynamicVars.TryGetValue(name,out var variable))throw new InvalidDataException("Missing revised native variable "+rule.Parameter+" on "+__instance.Id);
            var delta=Math.Max(0,Math.Floor(RuleExpression.Evaluate(rule.Amount,key=>key=="A"?terminal.Scale(rule.Bond).A:throw new InvalidDataException(key))));
            if(delta==0)continue;
            variable.BaseValue+=delta;scope.Deltas.Add((variable,delta));
        }
        }
        catch{scope.Restore();throw;}
    }
    private static void VariablesFinished(VariableScope __state,ref Task __result)=>__result=CompleteVariables(__state,__result);
    private static async Task CompleteVariables(VariableScope scope,Task original){try{await original;}finally{scope.Restore();}}
    private static void VariablesFailed(VariableScope __state,Exception? __exception){if(__exception!=null)__state.Restore();}
    private static void ModifierFinished(PowerModel __instance,MethodBase __originalMethod,object[] __args,ref decimal __result)
    {
        if(__result<=0)return;
        __result+=PowerBonus(__instance,"power_modifier_bonus",__originalMethod.Name,__args.OfType<CardModel>().FirstOrDefault());
    }
    internal static decimal CurrentPowerBonus(string eventName)
    {
        if(RevisionTrace.Current is not {Suppressed:false,Model:PowerModel power})return 0;
        return PowerBonus(power,eventName);
    }
    internal static decimal PowerBonus(PowerModel power,string eventName,string? method=null,CardModel? subject=null,CardModel? onlySource=null,bool multiplyApplications=true)
    {
        decimal total=0;
        foreach(var source in RevisionTrace.Sources(power))
        {
            if(onlySource!=null&&source!=onlySource)continue;
            var program=RevisionDescriptions.Program(source);if(program==null||!program.NativePowers.Contains(power.GetType().Name))continue;
            var terminal=CovenantTerminal.For(source.Owner);if(terminal==null)continue;
            var frame=RevisionTrace.Current?.Frames.FirstOrDefault(f=>f.Source==source);
            foreach(var rule in program.Rules.Where(r=>r.Event==eventName&&(r.Parameter==null||r.Parameter==method)))
            {
                frame??=new RevisionFrame(source,eventName){NativePower=power};
                if(subject!=null)frame.Subject=subject;
                decimal Resolve(string key)=>key=="A"?terminal.Scale(rule.Bond).A:frame.Resolve(key);
                if(RuleExpression.Evaluate(rule.Condition,Resolve)==0)continue;
                total+=Math.Max(0,Math.Floor(RuleExpression.Evaluate(rule.Amount,Resolve)))*(multiplyApplications?RevisionTrace.PowerApplications(power,source):1);
            }
        }
        return total;
    }
}
