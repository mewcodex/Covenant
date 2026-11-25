using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace Covenant.Game;

/// <summary>Keeps native holders and their events; only the inventory's width and visible rows change.</summary>
internal sealed partial class CompactRelics:Node2D
{
    private const int Columns=5;
    private NRelicInventory _inventory=null!;
    private Button _fold=null!;
    private bool _expanded;
    private string _layout="";
    internal static void Install(Harmony harmony)=>harmony.Patch(AccessTools.Method(typeof(NRelicInventory),nameof(NRelicInventory.UpdateNavigation)),postfix:new HarmonyMethod(typeof(CompactRelics),nameof(Navigation)));
    private static void Navigation(NRelicInventory __instance)=>__instance.GetNodeOrNull<CompactRelics>("CovenantRelicLayout")?.ApplyNavigation();
    internal static void Attach(NRelicInventory inventory)
    {
        if(inventory.HasNode("CovenantRelicLayout"))return;
        var layout=new CompactRelics{Name="CovenantRelicLayout",_inventory=inventory};inventory.AddChild(layout);layout.Initialize();
    }
    internal static float RightEdge(NRelicInventory inventory)
    {
        var layout=inventory.GetNodeOrNull<CompactRelics>("CovenantRelicLayout");
        return layout==null?48:inventory.GlobalPosition.X+inventory.Size.X+32;
    }
    private void Initialize()
    {
        // Keep the native left edge when switching from a stretched inventory to a fixed-width row.
        var left=_inventory.OffsetLeft;
        _inventory.GrowHorizontal=Control.GrowDirection.End;
        _inventory.SetAnchor(Side.Right,_inventory.AnchorLeft,keepOffset:true);
        _inventory.OffsetLeft=left;
        _fold=new Button{Name="FoldRelics",Flat=true,Size=new(28,28),MouseDefaultCursorShape=Control.CursorShape.PointingHand};AddChild(_fold);
        _fold.Draw+=()=>
        {
            var edge=_expanded?17:10;var middle=_expanded?10:17;
            _fold.DrawLine(new(7,edge),new(14,middle),Colors.White,2,true);
            _fold.DrawLine(new(14,middle),new(21,edge),Colors.White,2,true);
        };
        _fold.Pressed+=()=>{_expanded=!_expanded;_layout="";Refresh();_fold.QueueRedraw();};
        var timer=new Godot.Timer{WaitTime=.1,Autostart=true};AddChild(timer);timer.Timeout+=Refresh;
        Refresh();
    }
    private void Refresh()
    {
        var holders=_inventory.RelicNodes;
        var cell=holders.Count>0?holders[0].GetCombinedMinimumSize():new Vector2(64,64);
        if(cell.X<=0)cell=new(64,64);
        var gap=_inventory.GetThemeConstant("h_separation","FlowContainer");
        var width=Columns*cell.X+(Columns-1)*gap;
        var right=_inventory.OffsetLeft+width;
        if(Math.Abs(_inventory.OffsetRight-right)>.1f)_inventory.OffsetRight=right;
        _fold.Position=new(width+2,Math.Max(0,(cell.Y-28)/2));_fold.Visible=holders.Count>Columns;
        var key=_expanded+":"+string.Join(',',holders.Select(h=>h.GetInstanceId()));
        if(key==_layout)return;_layout=key;
        for(var i=0;i<holders.Count;i++)holders[i].Visible=_expanded||i<Columns;
        _inventory.SetMeta("covenant_relic_expanded",_expanded);
        _inventory.SetMeta("covenant_relic_columns",Columns);
        _inventory.QueueSort();
        // The native method includes hidden holders. Rebuild links after it and after the flow has laid out.
        Callable.From(UpdateNavigation).CallDeferred();
    }
    private void UpdateNavigation()
    {
        if(!IsInsideTree())return;
        _inventory.UpdateNavigation();
    }
    private void ApplyNavigation()
    {
        var visible=_inventory.RelicNodes.Where(h=>h.Visible).ToArray();
        for(var i=0;i<visible.Length;i++)
        {
            var row=i/Columns;var start=row*Columns;var end=Math.Min(start+Columns,visible.Length)-1;
            visible[i].FocusNeighborLeft=visible[i==start?end:i-1].GetPath();
            visible[i].FocusNeighborRight=i==end&&_fold.Visible?_fold.GetPath():visible[i==end?start:i+1].GetPath();
            if(i>=Columns)visible[i].FocusNeighborTop=visible[i-Columns].GetPath();
            if(i+Columns<visible.Length)visible[i].FocusNeighborBottom=visible[i+Columns].GetPath();
        }
        if(visible.Length>0){_fold.FocusNeighborLeft=visible[Math.Min(Columns,visible.Length)-1].GetPath();_fold.FocusNeighborRight=visible[0].GetPath();}
    }
}
