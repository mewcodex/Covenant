using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Covenant.Game;

internal static partial class RevisionTrace
{
    private sealed class PoisonContext(PoisonPower power,PoisonContext? parent,bool forecast=false)
    {internal readonly PoisonPower Power=power;internal readonly PoisonContext? Parent=parent;internal readonly bool Forecast=forecast;internal int Iteration;}
    private static readonly AsyncLocal<PoisonContext?> PoisonTick=new();
    private static void InstallPoison(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(PoisonPower),nameof(PoisonPower.Trigger)),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(PoisonStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(PoisonFinished)),finalizer:new HarmonyMethod(typeof(RevisionTrace),nameof(PoisonEnded)));
        harmony.Patch(AccessTools.Method(typeof(PoisonPower),nameof(PoisonPower.CalculateTotalDamageNextTurn)),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(PoisonForecastStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(PoisonEnded)),finalizer:new HarmonyMethod(typeof(RevisionTrace),nameof(PoisonEnded)));
    }
    private static void PoisonStarting(PoisonPower __instance,out PoisonContext __state){__state=new(__instance,PoisonTick.Value);PoisonTick.Value=__state;}
    private static void PoisonForecastStarting(PoisonPower __instance,out PoisonContext __state){__state=new(__instance,PoisonTick.Value,true);PoisonTick.Value=__state;}
    private static void PoisonFinished(PoisonContext __state,ref Task __result){__result=CompletePoison(__state,__result);PoisonTick.Value=__state.Parent;}
    private static void PoisonEnded(PoisonContext __state)=>PoisonTick.Value=__state.Parent;
    private static async Task CompletePoison(PoisonContext context,Task original){try{await original;}finally{PoisonTick.Value=context.Parent;}}
    private static bool IsPoisonDamage(PoisonContext context,Creature? target,Creature? dealer,CardModel? source,ValueProp props)=>target==context.Power.Owner&&dealer==null&&source==null&&props.HasFlag(ValueProp.Unpowered)&&props.HasFlag(ValueProp.Unblockable);
    internal static decimal ExtraPoisonDamage(Creature? target,Creature? dealer,CardModel? source,ValueProp props)
    {
        if(PoisonTick.Value is not {} context||!IsPoisonDamage(context,target,dealer,source,props)||context.Iteration==0)return 0;
        var skip=context.Iteration-1;
        foreach(var ally in context.Power.Owner.CombatState!.GetOpponentsOf(context.Power.Owner).Where(c=>c.IsAlive))
            foreach(var accelerant in ally.Powers.Where(p=>p is AccelerantPower))
            {
                if(!PowerCredits.TryGetValue(accelerant,out var credits)){skip-=accelerant.Amount;if(skip<0)return 0;continue;}
                foreach(var credit in credits.Items)
                {
                    if(skip>=credit.Units){skip-=credit.Units;continue;}
                    return credit.Source==null?0:RevisionNativeBonuses.PowerBonus(accelerant,"power_extra_poison_bonus",onlySource:credit.Source,multiplyApplications:false);
                }
            }
        return 0;
    }
    internal static void PoisonDamageFinished(Creature? target,Creature? dealer,CardModel? source,ValueProp props,bool forecast)
    {
        if(PoisonTick.Value is {} context&&context.Forecast==forecast&&IsPoisonDamage(context,target,dealer,source,props))context.Iteration++;
    }
}
