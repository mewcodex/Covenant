using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace Covenant.Game;

// Retain the native card holders, selection indices and input signals; only paginate their presentation.
internal sealed partial class RewardPages : Control
{
    private NGridCardHolder[] _cards=[];
    private NCardRewardAlternativeButton _previous=null!,_next=null!;
    private Label _caption=null!;
    private int _page;
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(NCardRewardSelectionScreen),nameof(NCardRewardSelectionScreen.RefreshOptions)),
            postfix:new HarmonyMethod(typeof(RewardPages),nameof(Refresh)));
        harmony.Patch(AccessTools.Method(typeof(NCard),nameof(NCard.FlashRelicOnCard)),
            prefix:new HarmonyMethod(typeof(RewardPages),nameof(ShowCardRelicFlash)));
    }
    private static bool ShowCardRelicFlash(NCard __instance,RelicModel relic)
    {
        if(relic is not CovenantTerminal)return true;
        for(Node? parent=__instance.GetParent();parent!=null;parent=parent.GetParent())
            if(parent is NCardRewardSelectionScreen)return false;
        return true;
    }
    private static void Refresh(NCardRewardSelectionScreen __instance)
    {
        var row=__instance.GetNode<Control>("UI/CardRow");
        var cards=row.GetChildren().OfType<NGridCardHolder>().Where(c=>!c.IsQueuedForDeletion()).ToArray();
        foreach(var holder in cards)
        {
            holder.Hitbox.MouseEntered+=()=>AcquisitionPreview.HoverReward(holder,__instance);
            holder.Hitbox.MouseExited+=()=>AcquisitionPreview.Exit(holder);
        }
        var pages=__instance.GetNodeOrNull<RewardPages>("UI/CovenantRewardPages");
        if(cards.Length<=5)
        {
            if(pages!=null)
            {
                pages._previous.Disable();
                pages._next.Disable();
                pages.Hide();
            }
            return;
        }
        if(pages==null)
        {
            pages=new RewardPages{Name="CovenantRewardPages",MouseFilter=MouseFilterEnum.Ignore};
            __instance.GetNode<Control>("UI").AddChild(pages);
            pages._previous=NCardRewardAlternativeButton.Create("上一页",MegaInput.altLeft.ToString());pages._previous.Name="Previous";
            pages._next=NCardRewardAlternativeButton.Create("下一页",MegaInput.altRight.ToString());pages._next.Name="Next";
            pages._previous.Position=new(-345,0);pages._next.Position=new(145,0);
            pages.AddChild(pages._previous);pages.AddChild(pages._next);
            pages._caption=new Label{Name="Page",HorizontalAlignment=HorizontalAlignment.Center,MouseFilter=MouseFilterEnum.Ignore};
            pages._caption.AddThemeFontSizeOverride("font_size",24);pages.AddChild(pages._caption);
            var current=pages;
            pages._previous.Connect(NClickableControl.SignalName.Released,Callable.From<NButton>(_=>current.Change(-1)));
            pages._next.Connect(NClickableControl.SignalName.Released,Callable.From<NButton>(_=>current.Change(1)));
        }
        pages._cards=cards;pages._page=0;pages.Show();
        // The native entrance tween assumes one long row. Stop it before positioning a page.
        (AccessTools.Field(typeof(NCardRewardSelectionScreen),"_cardTween").GetValue(__instance) as Tween)?.Kill();
        pages.Position=row.Position+new Vector2(0,335);
        pages._previous.Position=new(-345,0);pages._next.Position=new(145,0);
        pages._caption.Position=new(-65,8);pages._caption.Size=new(130,50);
        pages.Layout();
    }
    private void Change(int delta)
    {
        _page=Math.Clamp(_page+delta,0,(_cards.Length-1)/5);Layout();
        _cards[_page*5].TryGrabFocus();
    }
    private void Layout()
    {
        var start=_page*5;var count=Math.Min(5,_cards.Length-start);
        for(var i=0;i<_cards.Length;i++)
        {
            var card=_cards[i];var visible=i>=start&&i<start+count;
            if(!visible&&card.Visible)NHoverTipSet.Remove(card);
            card.Visible=visible;
            if(!card.Visible)continue;
            card.Modulate=Colors.White;card.Position=new((i-start-(count-1)/2f)*350,0);
            card.FocusNeighborLeft=(i==start?(Control)_previous:_cards[i-1]).GetPath();
            card.FocusNeighborRight=(i==start+count-1?(Control)_next:_cards[i+1]).GetPath();
        }
        if(_page==0)_previous.Disable();else _previous.Enable();
        if(start+count==_cards.Length)_next.Disable();else _next.Enable();
        _caption.Text=$"{_page+1} / {(_cards.Length+4)/5}";
    }
}
