using Covenant.Core;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace Covenant.Game;

internal sealed record TraitEntry(Bond Bond,string Text,string Source,string Kind,string? State=null,int? Counter=null);

internal static class TraitSidebar
{
    internal static void AddTo(Control root,Player player)
    {
        var panel=TraitListView.Panel("TraitSidebar");
        panel.Hide();root.AddChild(panel);
        var content=new VBoxContainer();content.AddThemeConstantOverride("separation",8);panel.AddChild(content);
        content.AddChild(TraitListView.Heading("局外特性",20));
        var list=TraitListView.ScrollList(content);
        var key="";
        void Refresh()
        {
            var terminal=CovenantTerminal.For(player);
            if(terminal==null||TraitDisplay.InCombat(player)){panel.Hide();return;}
            var cards=NOverlayStack.Instance?.Peek() is NCardRewardSelectionScreen reward
                ?reward.GetNode<Control>("UI/CardRow").GetChildren().OfType<NGridCardHolder>()
                    .Where(holder=>holder.Visible&&!holder.IsQueuedForDeletion()).Select(holder=>holder.CardModel)
                :Enumerable.Empty<CardModel>();
            var entries=terminal.OutsideTraitStatus(cards).ToArray();
            panel.Visible=entries.Length>0;
            if(!panel.Visible)return;
            var viewport=root.GetViewportRect().Size;
            var bar=root.GetNode<Control>("BondBar");
            var width=Math.Clamp(viewport.X*.16f,210,280);
            panel.Position=new(8,Math.Max(180,bar.Position.Y+(bar.Visible?bar.Size.Y:0)+12));
            var current=TraitListView.Key(entries,width);
            if(current!=key){key=current;TraitListView.Fill(list,entries,terminal,width-36);}
            var space=Math.Max(70,viewport.Y-panel.Position.Y-105);
            panel.Size=new(width,Math.Min(space,list.GetCombinedMinimumSize().Y+58));
        }
        var timer=new Godot.Timer{WaitTime=.2,Autostart=true};panel.AddChild(timer);timer.Timeout+=Refresh;Refresh();
    }
}
