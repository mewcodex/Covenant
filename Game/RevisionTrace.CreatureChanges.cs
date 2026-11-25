using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Covenant.Game;

internal static partial class RevisionTrace
{
    private sealed record HealReceipt(Creature Creature,decimal Hp,RevisionFrame[] Frames);
    private static void InstallCreatureChanges(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(CreatureCmd),nameof(CreatureCmd.Heal)),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(Healing)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(Healed)));
        harmony.Patch(AccessTools.Method(typeof(CreatureCmd),nameof(CreatureCmd.LoseBlock)),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(BlockRemoved)));
    }
    private static void Healing(Creature creature,out HealReceipt __state)=>__state=new(creature,creature.CurrentHp,Current is {Suppressed:false} scope?scope.Frames:[]);
    private static void Healed(HealReceipt __state,ref Task __result)=>__result=CompleteHeal(__state,__result);
    private static async Task CompleteHeal(HealReceipt receipt,Task original)
    {
        await original;
        foreach(var frame in receipt.Frames)if(receipt.Creature==frame.Source.Owner.Osty)frame.Add("native_osty_healed",Math.Max(0,receipt.Creature.CurrentHp-receipt.Hp));
    }
    private static void BlockRemoved(Creature target,decimal amount)
    {
        if(Current is not {Suppressed:false} scope)return;
        foreach(var frame in scope.Frames)if(target.Side!=frame.Source.Owner.Creature.Side)frame.Add("native_block_removed",Math.Max(0,Math.Min(target.Block,amount)));
    }
}
