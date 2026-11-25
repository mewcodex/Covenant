using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace Covenant.Game;

internal static partial class RevisionTrace
{
    private sealed class TransformCall
    {
        internal TransformCall? Parent;
        internal readonly Scope? Cause=Current;
        internal readonly List<(CardModel Original,CardModel Replacement)> Pairs=new();
    }
    private static readonly AsyncLocal<TransformCall?> TransformAmbient=new();
    private static void InstallTransforms(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.AfterThisWasTransformedInto)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(CardTransformed)));
        harmony.Patch(AccessTools.Method(typeof(CardCmd),nameof(CardCmd.Transform),[typeof(IEnumerable<CardTransformation>),typeof(Rng),typeof(CardPreviewStyle)]),
            prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(TransformStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(TransformFinished)),finalizer:new HarmonyMethod(typeof(RevisionTrace),nameof(TransformEnded)));
    }
    private static void TransformStarting(out TransformCall __state)
    {__state=new(){Parent=TransformAmbient.Value};TransformAmbient.Value=__state;}
    private static void CardTransformed(CardModel __instance,CardModel card)
    {
        TransformAmbient.Value?.Pairs.Add((__instance,card));
        if(Current is not {Suppressed:false} scope)return;
        foreach(var frame in scope.Frames)
        {
            frame.AddCard("transformed",card);frame.AddCard("transformed_original",__instance);
            CovenantTerminal.For(frame.Source.Owner)?.RevisionAttach(frame.Source,card,"transformed");
        }
    }
    private static void TransformFinished(TransformCall __state,ref Task<IEnumerable<CardPileAddResult>> __result)
    {__result=CompleteTransform(__state,__result);TransformAmbient.Value=__state.Parent;}
    private static void TransformEnded(TransformCall __state)=>TransformAmbient.Value=__state.Parent;
    private static async Task<IEnumerable<CardPileAddResult>> CompleteTransform(TransformCall call,Task<IEnumerable<CardPileAddResult>> original)
    {
        try
        {
            var result=await original;
            foreach(var (oldCard,newCard) in call.Pairs)
                if(CovenantTerminal.For(oldCard.Owner) is {} terminal)
                {
                    await terminal.RevisionTransformed(oldCard,newCard);
                    if(newCard.Pile?.IsCombatPile==true && call.Cause is {Model:EntropyPower,Suppressed:false} cause &&
                        cause.Frames.FirstOrDefault(frame=>frame.Source.Owner==oldCard.Owner) is {} entropy)
                        await terminal.FireRevision(new RevisionFrame(entropy.Source,"entropy_transform"){Subject=newCard,Scope="live"});
                    terminal.RefreshBattleStatuses();
                }
            return result;
        }
        finally{TransformAmbient.Value=call.Parent;}
    }
}
