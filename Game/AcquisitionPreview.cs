using Godot;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace Covenant.Game;

internal static class AcquisitionPreview
{
    private sealed record Offer(Control Holder,IScreenContext Screen,CardModel Card);
    private static Offer? hovered;
    internal static event Action? Changed;

    internal static void HoverReward(NGridCardHolder holder,NCardRewardSelectionScreen screen)
    {
        if(holder.CardModel is {} card)Set(new(holder,screen,card));
    }

    internal static void HoverMerchant(NMerchantCard slot)
    {
        if(slot.Entry is not MerchantCardEntry{CreationResult.Card:var card})return;
        for(var parent=slot.GetParent();parent!=null;parent=parent.GetParent())
            if(parent is NMerchantInventory inventory)
            {
                Set(new(slot,inventory,card));
                return;
            }
    }

    internal static void Exit(Control holder)
    {
        if(hovered?.Holder==holder)Set(null);
    }

    internal static CardModel? CardFor(Player player)
    {
        if(hovered is not {} offer)return null;
        if(!GodotObject.IsInstanceValid(offer.Holder)||!offer.Holder.IsVisibleInTree()||offer.Holder.IsQueuedForDeletion()
            ||!ActiveScreenContext.Instance.IsCurrent(offer.Screen)
            ||(offer.Holder is NMerchantCard slot&&(slot.Entry as MerchantCardEntry)?.CreationResult?.Card!=offer.Card)
            ||(offer.Holder is NCardHolder holder&&holder.CardNode?.Model!=offer.Card)
            ||player.Deck.Cards.Contains(offer.Card))
        {
            hovered=null;
            return null;
        }
        return offer.Card.Owner==player?offer.Card:null;
    }

    private static void Set(Offer? offer)
    {
        if(hovered==offer)return;
        hovered=offer;
        Changed?.Invoke();
    }
}
