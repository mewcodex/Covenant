using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace Covenant.Game;

internal static class ApprovedTraitHooks
{
    internal static void Install(Harmony harmony)
    {
        HarmonyMethod Hook(string name)=>new(typeof(ApprovedTraitHooks),name);
        harmony.Patch(AccessTools.Constructor(typeof(CardModel),[typeof(int),typeof(CardType),typeof(CardRarity),typeof(TargetType),typeof(bool)]),
            prefix:Hook(nameof(QuestCosts)));
        foreach(var type in new[]{typeof(Guilty),typeof(ByrdonisEgg)})
            harmony.Patch(AccessTools.PropertyGetter(type,"CanonicalKeywords"),prefix:Hook(nameof(PlayableKeywords)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(GrandFinale),"IsPlayable"),postfix:Hook(nameof(FinalePlayable)));
        harmony.Patch(AccessTools.Method(typeof(GrandFinale),"OnPlay"),prefix:Hook(nameof(SkipFatalFinale)));
        harmony.Patch(AccessTools.Method(typeof(CardModel),"OnPlay"),prefix:Hook(nameof(PlayQuestOrCurse)));
        harmony.Patch(AccessTools.Method(typeof(TheHermit),"OnPlay"),prefix:Hook(nameof(PlayHermit)));
        harmony.Patch(AccessTools.Method(typeof(Snakebite),"OnPlay"),prefix:Hook(nameof(BeginSnakebite)),postfix:Hook(nameof(FinishSnakebite)));
        harmony.Patch(AccessTools.Method(typeof(CardTransformation),nameof(CardTransformation.GetReplacement)),postfix:Hook(nameof(TransferExperience)));
        harmony.Patch(AccessTools.Method(typeof(AbstractModel),nameof(AbstractModel.MutableClone)),postfix:Hook(nameof(CloneExperience)));
        harmony.Patch(AccessTools.Method(typeof(Player),nameof(Player.FromSerializable)),postfix:Hook(nameof(RestoreExperience)));
    }

    private static void QuestCosts(CardModel __instance,ref int canonicalEnergyCost)
    {
        if(__instance is Guilty)canonicalEnergyCost=1;
        if(__instance is ByrdonisEgg)canonicalEnergyCost=0;
        if(CurseCovenant.PlayCost(__instance) is {} cost)canonicalEnergyCost=cost;
    }

    private static bool PlayableKeywords(CardModel __instance,ref IEnumerable<CardKeyword> __result)
    {__result=__instance is Guilty?[CardKeyword.Exhaust]:[];return false;}

    private static void FinalePlayable(GrandFinale __instance,ref bool __result)
    {
        if(__instance.IsMutable&&CovenantTerminal.For(__instance.Owner)!=null)__result=true;
    }

    private static bool SkipFatalFinale(GrandFinale __instance,ref Task __result)
    {
        if(!__instance.Owner.Creature.IsDead)return true;
        __result=Task.CompletedTask;return false;
    }

    private static bool PlayQuestOrCurse(CardModel __instance,PlayerChoiceContext choiceContext,ref Task __result)
    {
        if(CovenantTerminal.For(__instance.Owner) is not {} terminal)return true;
        if(__instance is Guilty or ByrdonisEgg)
            __result=terminal.PlayApprovedSpecial(choiceContext,__instance);
        else if(__instance is SporeMind||
            (CurseCovenant.PlayCost(__instance)!=null||__instance is Enthralled)&&CurseCovenant.IsActive(__instance)&&
            (__instance is not Normality normality||CurseCovenant.CanPlayNormality(normality)))
            __result=terminal.PlayCovenantCurse(choiceContext,__instance);
        else return true;
        return false;
    }

    private static bool PlayHermit(TheHermit __instance,PlayerChoiceContext choiceContext,ref Task __result)
    {
        if(CovenantTerminal.For(__instance.Owner) is not {} terminal)return true;
        __result=terminal.PlayHermit(choiceContext,__instance);
        return false;
    }

    private static void BeginSnakebite(Snakebite __instance,out (Snakebite Card,decimal Amount)? __state)
    {
        __state=null;
        var bonus=CovenantTerminal.For(__instance.Owner)?.TakeSnakebiteBonus(__instance)??0;
        if(bonus==0)return;
        var poison=__instance.DynamicVars.Poison;
        __state=(__instance,poison.BaseValue);
        poison.BaseValue+=bonus;
    }

    private static void FinishSnakebite((Snakebite Card,decimal Amount)? __state,ref Task __result)
    {
        if(__state is {} state)__result=RestoreSnakebite(__result,state);
    }

    private static async Task RestoreSnakebite(Task original,(Snakebite Card,decimal Amount) state)
    {
        try{await original;}
        finally{state.Card.DynamicVars.Poison.BaseValue=state.Amount;}
    }

    private static void TransferExperience(CardTransformation __instance,CardModel? __result)
    {
        if(__result!=null)CovenantTerminal.TransferBirdExperience(__instance.Original,__result);
    }

    private static void CloneExperience(AbstractModel __instance,AbstractModel __result)
    {
        if(__instance is CardModel card&&CardMembership.IsBird(card)&&__result is CardModel clone)
            CovenantTerminal.CopyBirdExperience(card,clone);
    }

    private static void RestoreExperience(Player __result)=>CovenantTerminal.For(__result)?.RestoreBirdExperience();

}
