using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

internal static partial class RevisionTrace
{
    private sealed class CostCapture(CardModel card,CostCapture? parent)
    {internal readonly CardModel Card=card;internal readonly CostCapture? Parent=parent;internal bool Open=true;internal readonly List<PowerModel> Powers=new();}
    private sealed class CostOrigins{internal readonly List<CardModel> Cards=new();}
    private static readonly AsyncLocal<CostCapture?> CostPaying=new();
    private static readonly ConditionalWeakTable<CardModel,CostOrigins> PaidCostOrigins=new();
    private static void InstallCosts(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.SpendResources)),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(CostStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(CostFinished)),finalizer:new HarmonyMethod(typeof(RevisionTrace),nameof(CostEnded)));
        foreach(var type in typeof(PowerModel).Assembly.GetTypes().Where(t=>!t.IsAbstract&&t.IsSubclassOf(typeof(PowerModel))))
            foreach(var method in type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly).Where(m=>m.Name is "TryModifyEnergyCostInCombat" or "TryModifyEnergyCostInCombatLate" or "TryModifyStarCost"))
                harmony.Patch(method,postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(CostModified)));
    }
    private static void CostStarting(CardModel __instance,out CostCapture __state)
    {
        if(CovenantTerminal.For(__instance.Owner) is {} terminal)
        {
            terminal.ReleaseChannelEnergy(__instance);
            terminal.QuoteZeroForPaidPlay(__instance);
        }
        __state=new(__instance,CostPaying.Value);CostPaying.Value=__state;PaidCostOrigins.Remove(__instance);
    }
    private static void CostFinished(CostCapture __state,ref Task<(int,int)> __result)
    {__result=CompleteCost(__state,__result);CostPaying.Value=__state.Parent;}
    private static void CostEnded(CostCapture __state)=>CostPaying.Value=__state.Parent;
    private static async Task<(int,int)> CompleteCost(CostCapture capture,Task<(int,int)> original)
    {try{return await original;}finally{CostPaying.Value=capture.Parent;}}
    private static void CostModified(PowerModel __instance,CardModel card,decimal originalCost,decimal modifiedCost,bool __result)
    {
        if(!__result||modifiedCost!=0||originalCost<=0||CostPaying.Value is not {Open:true} capture||capture.Card!=card)return;
        if(!capture.Powers.Contains(__instance))capture.Powers.Add(__instance);
    }
    private static void ClaimCosts()
    {
        if(CostPaying.Value is not {Open:true} capture)return;capture.Open=false;
        var origins=PaidCostOrigins.GetOrCreateValue(capture.Card);
        foreach(var power in capture.Powers)
        {
            if(!PowerCredits.TryGetValue(power,out var credits))continue;
            var skip=0;
            if(power.GetType().Name=="VoidFormPower")
            {
                var dataType=power.GetType().GetNestedType("Data",BindingFlags.NonPublic)!;
                var data=AccessTools.Method(typeof(PowerModel),"GetInternalData").MakeGenericMethod(dataType).Invoke(power,null)!;
                skip=(int)AccessTools.Field(dataType,"cardsPlayedThisTurn").GetValue(data)!;
            }
            foreach(var credit in credits.Items)
            {
                if(skip>=credit.Units){skip-=credit.Units;continue;}
                if(credit.Source is {} origin&&!origins.Cards.Contains(origin))origins.Cards.Add(origin);
                break;
            }
        }
    }
    private static void AttachPaidCostOrigins(CardModel subject)
    {
        if(!PaidCostOrigins.TryGetValue(subject,out var origins))return;
        PaidCostOrigins.Remove(subject);
        foreach(var origin in origins.Cards)CovenantTerminal.For(origin.Owner)?.RevisionAttach(origin,subject,"free");
    }
    private static bool HasCostCharge(PowerModel power,CardModel source)=>power.GetType().Name is not ("FreeSkillPower" or "FreeAttackPower" or "FreePowerPower" or "FreeCardPower" or "VeilpiercerPower")||PowerCredits.TryGetValue(power,out var credits)&&credits.Items.Any(c=>c.Source==source&&c.Units>0);
}
