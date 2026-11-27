using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;

namespace Covenant.Game;

[ModInitializer(nameof(Initialize))]
public static class Bootstrap
{
    public const string Version="0.6.20";
    public static void Initialize()
    {
        CovenantSettings.EnsureLoaded();
        _=CardMembership.Definitions;
        var harmony=new Harmony("20829.Covenant");
        harmony.Patch(AccessTools.Method(typeof(Player),"PopulateStartingRelics"),postfix:new HarmonyMethod(typeof(Bootstrap),nameof(GrantTerminal)));
        Presentation.Install(harmony);
        CovenantSelection.Install(harmony);
        SettingsUi.Install(harmony);
        CovenantTerminal.InstallAncientCosts(harmony);
        CovenantTerminal.InstallCreationRules(harmony);
        CovenantTerminal.InstallStatusReplacement(harmony);
        CovenantTerminal.InstallStatusRefresh(harmony);
        CovenantTerminal.InstallRandomState(harmony);
        CovenantFloorScaling.Install(harmony);
        NativeHooks.Install(harmony);
        CovenantTerminal.InstallNewBonds(harmony);
        RevisionTrace.Install(harmony);
        RevisionNativeBonuses.Install(harmony);
        CardReworkHooks.Install(harmony);
        CombatGenerationPools.Install(harmony);
        ApprovedTraitHooks.Install(harmony);
        GreedCredit.Install(harmony);
        ProfitSpending.Install(harmony);
        CurseCovenant.Install(harmony);
        CovenantRewards.Install(harmony);
        RewardPages.Install(harmony);
        MerchantHandOption.Install(harmony);
        Log.Info($"[Covenant] v{Version}; target v0.114.0; {CardMembership.Definitions.Count} card definitions; {Covenant.Core.BondCatalog.All.Count} covenants; assembly={Assembly.GetExecutingAssembly().Location}");
    }
    private static void GrantTerminal(Player __instance)
    {
        if(CovenantTerminal.For(__instance)!=null)return;
        var terminal=ModelDb.Relic<CovenantTerminal>().ToMutable();
        terminal.FloorAddedToDeck=1;
        __instance.AddRelicInternal(terminal);
        SaveManager.Instance.MarkRelicAsSeen(terminal);
    }
}
