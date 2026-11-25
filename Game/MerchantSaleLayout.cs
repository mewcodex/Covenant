using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace Covenant.Game;

internal static class MerchantSaleLayout
{
    private sealed record Original(Vector2 Position);
    private static readonly ConditionalWeakTable<NMerchantCard,Original> Originals=new();
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(NMerchantCard),"UpdateVisual"),postfix:new HarmonyMethod(typeof(MerchantSaleLayout),nameof(Update)));
        harmony.Patch(AccessTools.Method(typeof(NMerchantCard),nameof(NMerchantCard.OnInventoryOpened)),postfix:new HarmonyMethod(typeof(MerchantSaleLayout),nameof(Update)));
    }
    internal static void UpdateForCard(NCard card)
    {
        for(var parent=card.GetParent();parent!=null;parent=parent.GetParent())
            if(parent is NMerchantCard merchant){Update(merchant);return;}
    }
    private static void Update(NMerchantCard __instance)
    {
        var sale=__instance.GetNodeOrNull<Node2D>("%SaleVisual");
        if(sale==null)return;
        var original=Originals.GetValue(__instance,_=>new(sale.Position));sale.Position=original.Position;
        if(!sale.Visible)return;
        var card=__instance.GetNode<Control>("%CardHolder").GetChildren().OfType<NCard>().FirstOrDefault(c=>!c.IsQueuedForDeletion());
        var badges=card?.Body?.GetNodeOrNull<Control>("CovenantIcons");
        if(badges==null||badges.GetChildCount()==0||VisibleBounds(sale) is not {} bounds)return;
        // Keep the native resource, color and canvas order. Put the discount next to
        // the price, clearing the card and its fixed cost-mirrored covenant column.
        var frame=card!.GetNode<Control>("%Frame").GetGlobalRect();
        sale.GlobalPosition+=frame.End+new Vector2(8,10)-bounds.Position;
    }
    internal static Rect2? VisibleBounds(Node node)
    {
        Rect2? result=null;
        if(node is Sprite2D{Visible:true} sprite&&sprite.Texture!=null)
        {
            var rect=sprite.GetRect();var transform=sprite.GetGlobalTransform();
            var points=new[]{rect.Position,rect.Position+new Vector2(rect.Size.X,0),rect.End,rect.Position+new Vector2(0,rect.Size.Y)}.Select(p=>transform*p).ToArray();
            var min=new Vector2(points.Min(p=>p.X),points.Min(p=>p.Y));var max=new Vector2(points.Max(p=>p.X),points.Max(p=>p.Y));
            result=new Rect2(min,max-min);
        }
        else if(node is TextureRect{Visible:true,Texture:not null} texture)result=texture.GetGlobalRect();
        foreach(var child in node.GetChildren())
            if(VisibleBounds(child) is {} childBounds)result=result is {} current?current.Merge(childBounds):childBounds;
        return result;
    }
}
