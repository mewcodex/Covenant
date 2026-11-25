using Covenant.Core;
using System.Globalization;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.addons.mega_text;

namespace Covenant.Game;

internal static class GreedCredit
{
    internal sealed record Quote(int Cost,int Payment,int Checks,int RiskPerCheck)
    {
        internal double TotalRisk=>100*(1-Math.Pow(1-RiskPerCheck/100d,Checks));
        internal string ShownRisk=>TotalRisk>=99.995?">99.99%":TotalRisk.ToString("0.##",CultureInfo.InvariantCulture)+"%";
    }
    private sealed record Purchase(MerchantEntry Entry,MerchantInventory? Inventory,Quote? Quote,HandOfGreed[] AtRisk,Purchase? Previous);
    private static readonly AsyncLocal<Purchase?> Current=new();

    internal static void Install(Harmony harmony)
    {
        HarmonyMethod Patch(string name)=>new(typeof(GreedCredit),name);
        harmony.Patch(AccessTools.PropertyGetter(typeof(MerchantEntry),nameof(MerchantEntry.EnoughGold)),postfix:Patch(nameof(Affordability)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(MerchantEntry),nameof(MerchantEntry.Cost)),postfix:Patch(nameof(Price)));
        harmony.Patch(AccessTools.Method(typeof(MerchantEntry),nameof(MerchantEntry.OnTryPurchaseUnsynchronized)),
            prefix:Patch(nameof(Begin)),postfix:Patch(nameof(Finish)),finalizer:Patch(nameof(Restore)));
        foreach(var type in new[]{typeof(NMerchantCard),typeof(NMerchantRelic),typeof(NMerchantPotion)})
            harmony.Patch(AccessTools.DeclaredMethod(type,"UpdateVisual"),postfix:Patch(nameof(ShowPrice)));
        harmony.Patch(AccessTools.Method(typeof(NHoverTipSet),nameof(NHoverTipSet.CreateAndShow),
            [typeof(Control),typeof(IEnumerable<IHoverTip>),typeof(HoverTipAlignment)]),prefix:Patch(nameof(ShowRisk)));
    }

    internal static Quote? Offer(MerchantEntry? entry)
    {
        if(entry is not (MerchantCardEntry or MerchantRelicEntry or MerchantPotionEntry)||entry.Player.RunState.CurrentRoom is not MerchantRoom||
            entry.Cost<=entry.Player.Gold||CovenantTerminal.For(entry.Player) is not {} terminal)return null;
        var card=CreditCards(entry).OrderByDescending(card=>card.IsUpgraded).FirstOrDefault();
        if(card==null)return null;
        var cost=entry.Cost;
        var payment=entry.Player.Gold;
        return new(cost,payment,(cost-payment)/(card.IsUpgraded?20:15),terminal.ReworkAmount(card));
    }
    private static IEnumerable<HandOfGreed> CreditCards(MerchantEntry entry)=>entry.Player.Deck.Cards
        .OfType<HandOfGreed>().Where(CovenantTerminal.HasRework);

    private static void Affordability(MerchantEntry __instance,ref bool __result)
    {if(!__result&&Offer(__instance)!=null)__result=true;}

    private static void Price(MerchantEntry __instance,ref int __result)
    {if(Current.Value is {} purchase&&purchase.Entry==__instance&&purchase.Quote is {} quote)__result=quote.Payment;}

    private static void Begin(MerchantEntry __instance,MerchantInventory? inventory,bool ignoreCost,out Purchase __state)
    {
        var quote=ignoreCost?null:Offer(__instance);
        __state=new(__instance,inventory,quote,quote==null?[]:CreditCards(__instance).ToArray(),Current.Value);
        Current.Value=__state;
    }
    private static void Finish(Purchase __state,ref Task<bool> __result)
    {
        if(__state.Quote!=null)__result=Complete(__state,__result);
        Current.Value=__state.Previous;
    }
    private static void Restore(Purchase? __state)=>Current.Value=__state?.Previous;

    private static async Task<bool> Complete(Purchase purchase,Task<bool> original)
    {
        if(!await original)return false;
        var entry=purchase.Entry;var player=entry.Player;var quote=purchase.Quote!;
        Current.Value=purchase.Previous;
        if(CovenantTerminal.For(player) is {} terminal&&TriggersCardLoss(quote,terminal.RandomFor(CovenantRandom.Shop)))
        {
            var cards=purchase.AtRisk.Where(player.Deck.Cards.Contains).ToArray();
            if(terminal.RandomFor(CovenantRandom.Shop).NextItem(cards) is {} lost)
            {
                await CardPileCmd.RemoveFromDeck(lost,showPreview:false);
                if(purchase.Inventory is {} inventory)
                    foreach(var slot in inventory.AllEntries)slot.OnMerchantInventoryUpdated();
            }
        }
        if(entry.IsStocked)entry.OnMerchantInventoryUpdated();
        return true;
    }

    internal static bool TriggersCardLoss(Quote quote,Rng rng)
    {
        var hit=false;
        for(var index=0;index<quote.Checks;index++)hit|=rng.NextInt(100)<quote.RiskPerCheck;
        return hit;
    }

    private static void ShowPrice(NMerchantSlot __instance)
    {
        if(__instance.Entry?.IsStocked==true&&Offer(__instance.Entry) is {} quote&&__instance.GetNodeOrNull<MegaLabel>("%CostLabel") is {} label)
            label.SetTextAutoSize($"{quote.Payment} · {quote.ShownRisk}");
    }

    private static void ShowRisk(Control owner,ref IEnumerable<IHoverTip> hoverTips)
    {
        if(owner is NMerchantSlot slot&&slot.Entry?.IsStocked==true&&Offer(slot.Entry) is {} quote)
            hoverTips=hoverTips.Append(new HoverTip(Presentation.Literal("贪婪之手"),
                $"原价{quote.Cost}金币，支付{quote.Payment}金币。成交后有{quote.ShownRisk}的概率失去1张贪婪之手。",Presentation.Icon(Bond.Profit)));
    }
}
