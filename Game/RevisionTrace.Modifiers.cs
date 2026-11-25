using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

internal static partial class RevisionTrace
{
    private sealed class BlockReceipts
    {internal readonly Dictionary<(CardModel?,CardPlay?),decimal> Values=new();}
    private static readonly ConditionalWeakTable<PowerModel,BlockReceipts> ExtraBlockReceipts=new();
    private static void InstallModifiers(Harmony harmony)
    {
        foreach(var type in typeof(PowerModel).Assembly.GetTypes().Where(t=>!t.IsAbstract&&t.IsSubclassOf(typeof(PowerModel))))
        {
            var method=type.GetMethod("ModifyBlockMultiplicative",BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly);
            if(method!=null)harmony.Patch(method,postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(BlockMultiplied)));
        }
        var callback=AccessTools.Method(typeof(AbstractModel),nameof(AbstractModel.AfterModifyingBlockAmount));
        harmony.Patch(callback,prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(NativeScopeStart)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(NativeScopeTask)),finalizer:new HarmonyMethod(typeof(RevisionTrace),nameof(NativeScopeEnd)));
    }
    private static void BlockMultiplied(PowerModel __instance,decimal block,CardModel? cardSource,CardPlay? cardPlay,decimal __result)
    {
        if(__result<=1)return;
        ExtraBlockReceipts.GetOrCreateValue(__instance).Values[(cardSource,cardPlay)]=Math.Max(0,block*(__result-1));
    }
    private static void ReadModifierReceipt(Scope scope,object[] args)
    {
        if(scope.Model is not PowerModel power)return;
        var subject=args.OfType<CardModel>().FirstOrDefault();var play=args.OfType<CardPlay>().FirstOrDefault();
        if(scope.Method==nameof(AbstractModel.AfterModifyingBlockAmount)&&ExtraBlockReceipts.TryGetValue(power,out var receipts)&&receipts.Values.Remove((subject,play),out var extra))
            foreach(var frame in scope.Frames)frame.Values["native_extra_block"]=extra;
        if(scope.Method==nameof(AbstractModel.AfterModifyingCardPlayResultLocation)&&subject!=null)
            foreach(var origin in Sources(power))
            {
                var terminal=CovenantTerminal.For(origin.Owner);if(terminal==null)continue;
                if(power.GetType().Name=="CorruptionPower"&&!subject.Keywords.Contains(CardKeyword.Exhaust))terminal.RevisionAttach(origin,subject,"forced_exhaust");
                if(power is MegaCrit.Sts2.Core.Models.Powers.ReboundPower or MegaCrit.Sts2.Core.Models.Powers.NostalgiaPower&&
                   args.OfType<CardLocation>().Any(location=>location.pileType==PileType.Draw&&location.position==CardPilePosition.Top))
                    terminal.RevisionQueueTop(origin,subject);
            }
    }
}
