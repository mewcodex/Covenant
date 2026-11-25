using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Covenant.Game;

internal static partial class RevisionTrace
{
    private static void InstallSecondaryEvents(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Creature),nameof(Creature.AfterTurnStart)),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(BlockClearStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(BlockClearFinished)));
    }
    private static void BlockClearStarting(Creature __instance,out decimal __state)=>__state=__instance.Block;
    private static void BlockClearFinished(Creature __instance,CombatSide side,decimal __state,ref Task __result)=>__result=CompleteBlockClear(__instance,side,__state,__result);
    private static async Task CompleteBlockClear(Creature creature,CombatSide side,decimal before,Task original)
    {
        await original;
        if(side==CombatSide.Player&&creature.Player is {} player&&player.PlayerCombatState?.TurnNumber>1)
            CovenantTerminal.For(player)?.RevisionCarriedBlock(before>0&&creature.Block>0);
    }
}
