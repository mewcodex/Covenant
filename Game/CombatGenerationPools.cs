using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Unlocks;

namespace Covenant.Game;

internal static class CombatGenerationPools
{
    internal static void Install(Harmony harmony)
    {
        var patch=new HarmonyMethod(typeof(CombatGenerationPools),nameof(UseMixedPools));
        Type[] cards=[typeof(Abundance),typeof(BlotOutTheSky),typeof(Discovery),typeof(Distraction),typeof(InfernalBlade),
            typeof(Jackpot),typeof(Metamorphosis),typeof(Stoke),typeof(WhiteNoise)];
        var generators=cards.Select(card=>AccessTools.DeclaredMethod(card,"OnPlay")).Concat([
            AccessTools.DeclaredMethod(typeof(MadScience),"ExecuteRider"),
            AccessTools.DeclaredMethod(typeof(CalamityPower),nameof(CalamityPower.AfterCardPlayed)),
            AccessTools.DeclaredMethod(typeof(CallOfTheVoidPower),nameof(CallOfTheVoidPower.BeforeHandDraw)),
            AccessTools.DeclaredMethod(typeof(CreativeAiPower),nameof(CreativeAiPower.BeforeHandDraw)),
            AccessTools.DeclaredMethod(typeof(HelloWorldPower),nameof(HelloWorldPower.BeforeHandDraw))]);
        foreach(var generator in generators)
        {
            var stateMachine=generator?.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
            if(stateMachine!=null)harmony.Patch(AccessTools.Method(stateMachine,"MoveNext"),transpiler:patch);
        }
    }

    private static IEnumerable<CodeInstruction> UseMixedPools(IEnumerable<CodeInstruction> instructions,MethodBase original)
    {
        var code=instructions.ToList();
        var character=AccessTools.PropertyGetter(typeof(Player),nameof(Player.Character));
        var pool=AccessTools.PropertyGetter(typeof(CharacterModel),nameof(CharacterModel.CardPool));
        var unlocked=AccessTools.Method(typeof(CardPoolModel),nameof(CardPoolModel.GetUnlockedCards));
        var receiver=-1;
        for(var index=0;index+1<code.Count;index++)
        {
            if(!code[index].Calls(character)||!code[index+1].Calls(pool))continue;
            receiver=index;
            break;
        }
        var candidates=receiver<0?-1:code.FindIndex(receiver+2,op=>op.Calls(unlocked));
        if(candidates<0)
        {
            Log.Warn($"[Covenant] Cannot expand combat generation pools in {original.DeclaringType}.");
            return code;
        }

        // Preserve the player on the stack so the replacement can read this player's saved setting.
        // Native restrictions and the random draw run after this candidate lookup.
        code[candidates].opcode=OpCodes.Call;
        code[candidates].operand=AccessTools.Method(typeof(CombatGenerationPools),nameof(UnlockedForCombat));
        var keepPlayer=new CodeInstruction(OpCodes.Dup);
        keepPlayer.labels.AddRange(code[receiver].labels);
        code[receiver].labels.Clear();
        code.Insert(receiver,keepPlayer);
        return code;
    }

    private static IEnumerable<CardModel> UnlockedForCombat(Player player,CardPoolModel original,UnlockState unlocks,CardMultiplayerConstraint constraint)
    {
        if(CovenantTerminal.For(player)?.AllCardPools!=true)return original.GetUnlockedCards(unlocks,constraint);
        return CovenantTerminal.SixPools.Prepend(original).Distinct()
            .SelectMany(pool=>pool.GetUnlockedCards(unlocks,constraint)).DistinctBy(card=>card.Id);
    }
}
