using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

internal static partial class RevisionTrace
{
    private static void InstallLocalCosts(Harmony harmony)
    {
        foreach(var name in new[]{nameof(CardEnergyCost.SetThisCombat),nameof(CardEnergyCost.SetThisTurnOrUntilPlayed)})
            harmony.Patch(AccessTools.Method(typeof(CardEnergyCost),name),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(LocalCostStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(LocalCostFinished)));
    }
    private static void LocalCostStarting(CardEnergyCost __instance,out int __state)=>__state=__instance.GetWithModifiers(CostModifiers.All);
    private static void LocalCostFinished(CardEnergyCost __instance,CardModel ____card,int __state)
    {
        if(Current is not {Suppressed:false} scope||__instance.GetWithModifiers(CostModifiers.All)>=__state)return;
        foreach(var frame in scope.Frames)frame.AddCard("discounted",____card);
    }
}
