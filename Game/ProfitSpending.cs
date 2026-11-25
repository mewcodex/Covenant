using Covenant.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;

namespace Covenant.Game;

internal static class ProfitSpending
{
    private static readonly AsyncLocal<bool> EventPurchase = new();

    internal static void Install(Harmony harmony)
    {
        HarmonyMethod Patch(string name)=>new(typeof(ProfitSpending),name);
        harmony.Patch(AccessTools.Method(typeof(PlayerCmd),nameof(PlayerCmd.LoseGold)),
            prefix:Patch(nameof(BeforeGoldLoss)),postfix:Patch(nameof(AfterGoldLoss)));
        harmony.Patch(AccessTools.Method(typeof(EventOption),nameof(EventOption.Chosen)),
            prefix:Patch(nameof(BeforeEventChoice)),finalizer:Patch(nameof(RestoreEventChoice)));
    }

    private static void BeforeEventChoice(EventOption __instance,out bool __state)
    {
        __state=EventPurchase.Value;
        // These purchase options use LoseGold's default Lost classification.
        EventPurchase.Value=__instance.TextKey is "THERMAE_NONUPEIPE.pages.INITIAL.options.THERMAL_BATHS"
            or "THERMAE_NONUPEIPE.pages.INITIAL.options.PRIVATE_OASIS"
            or "WATERFALL_OF_OBVIOUS_SECRETS.pages.ENTER.options.NEGOTIATE";
    }

    private static void RestoreEventChoice(bool __state)=>EventPurchase.Value=__state;

    private static void BeforeGoldLoss(decimal amount,Player player,GoldLossType goldLossType,
        out (CovenantTerminal? Terminal,int Balance) __state)
    {
        __state=default;
        if(amount<1||player.Gold<=0||goldLossType!=GoldLossType.Spent&&
            !(goldLossType==GoldLossType.Lost&&EventPurchase.Value))return;
        if(CovenantTerminal.For(player) is {} terminal&&terminal.Tier(Bond.Profit)>0)
            __state=(terminal,player.Gold);
    }

    private static void AfterGoldLoss(Player player,(CovenantTerminal? Terminal,int Balance) __state,ref Task __result)
    {
        if(__state.Terminal is {} terminal&&__state.Balance>player.Gold)
            __result=Reward(__result,terminal,player,__state.Balance-player.Gold);
    }

    private static async Task Reward(Task payment,CovenantTerminal terminal,Player player,int spent)
    {
        await payment;
        var completed=terminal.Ledger.RecordProfitSpending(spent);
        if(completed==0)return;
        terminal.Flash();
        await CreatureCmd.GainMaxHp(player.Creature,(decimal)completed*(1+terminal.Scale(Bond.Profit).A/2));
    }
}
