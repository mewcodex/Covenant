using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace Covenant.Game;

internal static class MerchantHandOption
{
    private static readonly List<WeakReference<NMerchantHand>> Hands=new();
    internal static void Install(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(NMerchantHand),nameof(NMerchantHand._Ready)),postfix:new HarmonyMethod(typeof(MerchantHandOption),nameof(Ready)));
        h.Patch(AccessTools.Method(typeof(NMerchantHand),nameof(NMerchantHand._Process)),prefix:new HarmonyMethod(typeof(MerchantHandOption),nameof(Process)));
    }
    private static void Ready(NMerchantHand __instance){Hands.Add(new(__instance));Apply(__instance);}
    private static bool Process(NMerchantHand __instance){Apply(__instance);return !CovenantSettings.HideMerchantHand;}
    private static void Apply(NMerchantHand hand)
    {
        if(hand.GetParent() is CanvasItem visual)visual.Visible=!CovenantSettings.HideMerchantHand;
    }
    internal static void Refresh(){Hands.RemoveAll(h=>!h.TryGetTarget(out var hand)||!GodotObject.IsInstanceValid(hand));foreach(var item in Hands)if(item.TryGetTarget(out var hand))Apply(hand);}
}
