using Covenant.Core;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace Covenant.Game;

internal static class Hud
{
    private const float Spacing=5;
    private const int PreviewCapacity=10;
    private const float MoreWidth=28;
    private const float ChangedScale=1.2f;
    private readonly record struct BadgeState(int Count,int Layers,int Tier,bool Suppressed)
    {
        internal bool Visible=>Count>0||Layers>0||Tier>0;
    }
    internal static void Install(Harmony h)=>h.Patch(AccessTools.Method(typeof(NRun),nameof(NRun._Ready)),postfix:new HarmonyMethod(typeof(Hud),nameof(Create)));
    private static void Create(NRun __instance)
    {
        var run=(RunState)AccessTools.Field(typeof(NRun),"_state").GetValue(__instance)!;
        var player=LocalContext.GetMe(run);if(player==null||CovenantTerminal.For(player)==null)return;
        CompactRelics.Attach(__instance.GlobalUi.RelicInventory);
        AddTo(__instance,player);
        CovenantLibrary.WarmRunPage(__instance);
    }
    internal static Control AddTo(Node parent,Player player)
    {
        var root=new Control{Name="CovenantRoot",MouseFilter=Control.MouseFilterEnum.Ignore};
        // Share the native scene canvas: interactive beside overlays, still beneath the game's transition.
        var host=parent is NRun nativeRun?nativeRun.GlobalUi:(Node)parent;host.AddChild(root);
        if(parent is NRun orderedRun)host.MoveChild(root,orderedRun.GlobalUi.Overlays.GetIndex()+1);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var bar=new Control{Name="BondBar",MouseFilter=Control.MouseFilterEnum.Ignore};root.AddChild(bar);
        var scroll=new ScrollContainer{Name="Scroller",VerticalScrollMode=ScrollContainer.ScrollMode.Disabled,MouseFilter=Control.MouseFilterEnum.Pass};bar.AddChild(scroll);
        var items=new HBoxContainer{Name="Items",MouseFilter=Control.MouseFilterEnum.Ignore};items.AddThemeConstantOverride("separation",(int)Spacing);scroll.AddChild(items);
        var more=new Button{Name="MoreCovenants",CustomMinimumSize=new(MoreWidth,24),Flat=true,MouseDefaultCursorShape=Control.CursorShape.PointingHand};bar.AddChild(more);
        var fold=new Button{Name="FoldCovenants",CustomMinimumSize=new(32,24),Flat=true};root.AddChild(fold);
        var expanded=true;var showAll=false;object? lastRoom=null;
        fold.Draw+=()=>{var y=expanded?15:8;var middle=expanded?8:15;fold.DrawLine(new(8,y),new(16,middle),BondBadge.Cyan,2,true);fold.DrawLine(new(16,middle),new(24,y),BondBadge.Cyan,2,true);};
        more.Draw+=()=>
        {
            var x=showAll?17:10;var middle=showAll?10:17;
            more.DrawLine(new(x,5),new(middle,12),BondBadge.Cyan,2,true);
            more.DrawLine(new(middle,12),new(x,19),BondBadge.Cyan,2,true);
        };
        var traitToggle=new Control{Name="TraitToggle",Size=new(180,68),MouseFilter=Control.MouseFilterEnum.Ignore};root.AddChild(traitToggle);
        var checkbox=AddToggle(traitToggle,SettingsUi.TraitToggleName,"显示卡牌特性",0);
        var formulaCheckbox=AddToggle(traitToggle,SettingsUi.FormulaToggleName,"显示公式",34);

        var panel=new PanelContainer{Name="BondDetails",Visible=false,MouseFilter=Control.MouseFilterEnum.Stop};root.AddChild(panel);
        panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new Color(.035f,.065f,.075f,.97f),BorderColor=new Color("496660"),BorderWidthTop=1,BorderWidthBottom=1,BorderWidthLeft=1,BorderWidthRight=1,ContentMarginLeft=20,ContentMarginRight=20,ContentMarginTop=14,ContentMarginBottom=20});
        var layout=new VBoxContainer();layout.AddThemeConstantOverride("separation",16);panel.AddChild(layout);
        var title=new Label{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};title.AddThemeFontSizeOverride("font_size",25);layout.AddChild(title);
        title.UseBundledChinese();
        var effects=new VBoxContainer{Name="Effects"};effects.AddThemeConstantOverride("separation",20);layout.AddChild(effects);
        Bond? selected=null;string fingerprint="",badgeData="";
        var badgeStates=new Dictionary<Bond,BadgeState>();
        int? scrollBeforePreview=null;
        var showingFormulas=CovenantSettings.ShowFormulas;
        var shownProfitProgress=0;
        float BarTop()
        {
            if(parent is not NRun run)return 72;
            // NTopBar is a full-screen control because it also owns floating tips. Follow its visible row instead.
            var topBar=run.GlobalUi.TopBar;
            var edges=new[]{topBar.GetNode<Control>("%BgImage").GetGlobalRect().End.Y,topBar.LeftContainer.GetGlobalRect().End.Y,topBar.RightContainer.GetGlobalRect().End.Y};
            var bottom=edges.Where(y=>y>0&&y<root.GetViewportRect().Size.Y/3).DefaultIfEmpty(64).Max();
            return bottom+10;
        }
        void Hide(){panel.Visible=false;}
        fold.Pressed+=()=>{expanded=!expanded;Hide();fold.QueueRedraw();Update();};
        more.Pressed+=()=>{showAll=!showAll;scroll.ScrollHorizontal=0;Hide();more.QueueRedraw();Update();};
        void Details(Bond id)
        {
            if(!bar.Visible||!badgeStates.TryGetValue(id,out var state))return;
            selected=id;panel.Visible=true;
            var terminal=CovenantTerminal.For(player)!;var definition=BondCatalog.Get(id);
            var (count,_,tier,_)=state;
            title.Text=$"{definition.Name} ({count}{(id==Bond.Spectrum?"色":"张")})";
            title.AddThemeColorOverride("font_color",BondBadge.HasSpecialPalette(id)?BondBadge.Accent(id,tier>0):Colors.White);
            foreach(var child in effects.GetChildren()){effects.RemoveChild(child);child.QueueFree();}
            var width=Math.Min(540,root.GetViewportRect().Size.X-48);
            const int labelWidth=82;
            var descriptionWidth=width-labelWidth-54;
            var intro=BondCatalog.Intro(id);
            if(intro.Length>0)
            {
                var description=BondDescription.Create("Intro");description.CustomMinimumSize=new(width-40,0);
                description.Text=BondDescription.Format(intro,terminal);effects.AddChild(description);
            }
            void TierRow(int number,int threshold,string text)
            {
                var enabled=tier>=number;
                var row=new HBoxContainer{Name=number==1?"Basic":"Advanced"};row.AddThemeConstantOverride("separation",14);effects.AddChild(row);
                var required=new Label{Name="Threshold",Text=$"{(number==1?"I":"II")} ({count}/{threshold})",CustomMinimumSize=new(labelWidth,0)};required.AddThemeFontSizeOverride("font_size",18);required.AddThemeColorOverride("font_color",BondBadge.Accent(id,enabled));row.AddChild(required);
                var description=BondDescription.Create("Description");description.CustomMinimumSize=new(descriptionWidth,0);
                description.Modulate=enabled?Colors.White:new Color(.65f,.69f,.69f);
                description.Text=BondDescription.Format(text,terminal);row.AddChild(description);
            }
            var basicText=definition.BasicText(terminal.Scale(id));
            if(id==Bond.Profit)
            {
                shownProfitProgress=terminal.Ledger.ProfitGoldProgress;
                if(tier>0)basicText+=$"（{shownProfitProgress}/{BondCatalog.ProfitGoldThreshold}金币）";
            }
            TierRow(1,definition.Basic,basicText);
            if(definition.Advanced>0)TierRow(2,definition.Advanced,definition.AdvancedText(terminal.Scale(id)));
            foreach(var (layers,text) in BondCatalog.Milestones(id,terminal.Scale(id)))
            {
                var enabled=tier>0&&terminal.Ledger.Get(id)>=layers;
                var row=new HBoxContainer();row.AddThemeConstantOverride("separation",14);effects.AddChild(row);
                var required=new Label{Text=$"{layers}层",CustomMinimumSize=new(labelWidth,0)};required.AddThemeFontSizeOverride("font_size",18);required.AddThemeColorOverride("font_color",enabled?BondBadge.Cyan:BondBadge.Dim);row.AddChild(required);
                required.UseBundledChinese();
                var description=BondDescription.Create("Milestone");description.CustomMinimumSize=new(descriptionWidth,0);description.Modulate=enabled?Colors.White:new Color(.65f,.69f,.69f);description.Text=BondDescription.Format(text,terminal);row.AddChild(description);
            }
            panel.Size=new(width,0);panel.ResetSize();
            panel.Position=new((root.GetViewportRect().Size.X-width)/2,BarTop()+96);
        }
        Dictionary<Bond,BadgeState> Snapshot(CovenantTerminal terminal,IReadOnlyDictionary<Bond,int> counts,bool solitudeSuppressed,bool projected)
        {
            var states=new Dictionary<Bond,BadgeState>();
            foreach(var definition in BondCatalog.All)
            {
                var id=definition.Id;
                var count=counts.GetValueOrDefault(id);
                var suppressed=id==Bond.Solitude&&solitudeSuppressed;
                var tier=suppressed?0:projected?definition.Tier(count):terminal.TierAtCount(id,count);
                states.Add(id,new(count,terminal.Scale(id).Layers,tier,suppressed));
            }
            return states;
        }
        void Update()
        {
            if(!GodotObject.IsInstanceValid(root)||!root.IsInsideTree())return;
            var covered=parent is NRun currentRun&&currentRun.GlobalUi.CapstoneContainer.InUse;
            root.Visible=!covered;
            if(covered){Hide();return;}
            var terminal=CovenantTerminal.For(player);if(terminal==null){bar.Visible=false;traitToggle.Visible=false;panel.Visible=false;return;}
            var viewport=root.GetViewportRect().Size;
            var room=player.RunState?.CurrentRoom;
            if(!ReferenceEquals(lastRoom,room))
            {
                lastRoom=room;if(room is MerchantRoom){expanded=false;Hide();}else if(room is CombatRoom)expanded=true;
                showAll=false;scrollBeforePreview=null;scroll.ScrollHorizontal=0;fold.QueueRedraw();more.QueueRedraw();
            }
            var live=Snapshot(terminal,CardMembership.Counts(player,respectSuppression:false),CardMembership.SolitudeSuppressed(player),projected:false);
            var hasFateRoster=terminal.FateCards.Count>0;
            var hasOracleRoster=terminal.OracleCards.Count>0;
            var offer=AcquisitionPreview.CardFor(player);
            var previewing=offer!=null;
            if(previewing)scrollBeforePreview??=scroll.ScrollHorizontal;
            var restoreScroll=previewing?null:scrollBeforePreview;
            if(!previewing)scrollBeforePreview=null;
            badgeStates=offer==null?live:Snapshot(terminal,CardMembership.CountsAfterAcquisition(player,offer),CardMembership.SolitudeSuppressed(player,offer),projected:true);
            var visible=BondCatalog.All.Select(definition=>(Definition:definition,State:badgeStates[definition.Id],Changed:badgeStates[definition.Id]!=live[definition.Id]))
                .Where(entry=>entry.State.Visible||entry.Changed&&live[entry.Definition.Id].Visible||
                    entry.Definition.Id==Bond.Fate&&hasFateRoster||entry.Definition.Id==Bond.Oracle&&hasOracleRoster)
                .OrderByDescending(entry=>entry.State.Tier>0?entry.Definition.Id switch{Bond.Oracle=>2,Bond.Fate=>1,_=>0}:0)
                .ThenByDescending(entry=>entry.State.Tier>0).ThenByDescending(entry=>entry.State.Layers)
                .ThenBy(entry=>entry.State.Tier==0&&entry.State.Layers==0?Math.Max(0,entry.Definition.Basic-entry.State.Count):0)
                .ThenBy(entry=>entry.Definition.Id).ToArray();
            var overflow=visible.Length>PreviewCapacity;
            if(!previewing&&!overflow)showAll=false;
            var scrolling=showAll&&overflow&&!previewing;
            // Keep extinguished or newly eligible covenants visible even when their projected position is after the first ten.
            var shown=scrolling?visible:visible.Where((entry,index)=>index<PreviewCapacity||entry.Changed).ToArray();
            var badgeScale=1f;
            var side=parent is NRun layoutRun?Math.Max(48,CompactRelics.RightEdge(layoutRun.GlobalUi.RelicInventory)+12):48;
            var arrowSpace=overflow?MoreWidth+Spacing:0;
            var available=Math.Max(BondBadge.Width,viewport.X-2*(side+arrowSpace));
            var gaps=Spacing*Math.Max(0,shown.Length-1);
            var naturalWidth=shown.Sum(entry=>BondBadge.Width*(entry.Changed?ChangedScale:1));
            if(!scrolling&&naturalWidth>0)badgeScale=Math.Min(badgeScale,(available-gaps)/naturalWidth);
            var width=Math.Min(MathF.Ceiling(naturalWidth*badgeScale+gaps),available);
            var hasBadges=hasFateRoster||hasOracleRoster||live.Values.Any(state=>state.Visible);
            var statisticsVisible=expanded&&hasBadges;
            bar.Visible=statisticsVisible||previewing&&visible.Length>0;
            // Only the actual icon list participates in centering; the adjacent checkbox does not.
            var top=BarTop();bar.Position=new((viewport.X-width)/2,top);
            var maxFactor=shown.Any(entry=>entry.Changed)?ChangedScale:1;
            bar.Size=new(width,(BondBadge.Height+11)*badgeScale*maxFactor+(scrolling?15:0));scroll.Size=bar.Size;
            scroll.HorizontalScrollMode=scrolling?ScrollContainer.ScrollMode.Auto:ScrollContainer.ScrollMode.Disabled;
            scroll.FollowFocus=scrolling;
            more.Visible=overflow;more.Position=new(width+Spacing,(BondBadge.Height*badgeScale-24)/2);
            if(room is MerchantRoom&&NMerchantRoom.Instance?.Inventory is {} shop)CardTraitTip.LayoutMerchantShelves(shop,statisticsVisible);
            fold.Position=new(viewport.X/2-16,top-14);fold.Visible=hasBadges;
            traitToggle.Visible=expanded;checkbox.IsTicked=CovenantSettings.ShowCardTraits;
            formulaCheckbox.IsTicked=CovenantSettings.ShowFormulas;
            traitToggle.Position=new(Math.Min(bar.Position.X+width+arrowSpace+12,viewport.X-188),top+(width+arrowSpace>viewport.X-220?96:28));
            if(panel.Visible)panel.Position=new((viewport.X-panel.Size.X)/2,top+96);
            var currentData=string.Join(";",live.Select(entry=>$"{entry.Key}:{entry.Value}"));
            var key=badgeScale+":"+scrolling+":"+string.Join(";",shown.Select(entry=>$"{entry.Definition.Id}:{entry.State}:{entry.Changed}"));
            var formulaChanged=showingFormulas!=CovenantSettings.ShowFormulas;
            showingFormulas=CovenantSettings.ShowFormulas;
            if(currentData!=badgeData){badgeData=currentData;Presentation.RefreshCardBadges();}
            if(!bar.Visible&&panel.Visible)Hide();
            if(key==fingerprint)
            {
                if(panel.Visible&&selected is {} open&&
                    (formulaChanged||open==Bond.Profit&&shownProfitProgress!=terminal.Ledger.ProfitGoldProgress))Details(open);
                return;
            }
            fingerprint=key;
            foreach(var child in items.GetChildren()){items.RemoveChild(child);child.QueueFree();}
            foreach(var entry in shown)
            {
                var d=entry.Definition;
                var (count,layers,tier,suppressed)=entry.State;
                var scale=badgeScale*(entry.Changed?ChangedScale:1);
                var button=new Button{Name=d.Id.ToString(),CustomMinimumSize=new(BondBadge.Width*scale,(BondBadge.Height+11)*scale),Flat=true,MouseDefaultCursorShape=Control.CursorShape.PointingHand};
                button.AddThemeStyleboxOverride("normal",new StyleBoxEmpty());button.AddThemeStyleboxOverride("hover",new StyleBoxEmpty());button.AddThemeStyleboxOverride("pressed",new StyleBoxEmpty());
                items.AddChild(button);var badge=BondBadge.Create(d,count,layers,suppressed:suppressed,activeTier:tier);badge.PivotOffset=new(36,30);badge.Scale=Vector2.One*scale;badge.Position=new(36*(scale-1),41*badgeScale-30);button.AddChild(badge);
                var id=d.Id;
                void Animate(float factor){var tween=badge.CreateTween();tween.TweenProperty(badge,"scale",Vector2.One*factor*scale,.12);}
                button.MouseEntered+=()=>{Animate(1.12f);Details(id);};button.MouseExited+=()=>{Animate(1);Hide();};
                button.FocusEntered+=()=>Details(id);button.FocusExited+=Hide;
                button.Pressed+=()=>
                {
                    Hide();
                    if(parent is NRun openRun)CovenantLibrary.OpenFromHud(openRun,id);
                };
                button.GuiInput+=input=>{if(input is InputEventKey{Pressed:true,Keycode:Key.Escape}){Hide();button.AcceptEvent();}};
            }
            if(restoreScroll is {} previous)scroll.SetDeferred(ScrollContainer.PropertyName.ScrollHorizontal,previous);
            if(panel.Visible&&selected is {} chosen)
            {
                if(shown.Any(entry=>entry.Definition.Id==chosen))Details(chosen);
                else Hide();
            }
        }
        void OfferChanged()=>Callable.From(Update).CallDeferred();
        AcquisitionPreview.Changed+=OfferChanged;
        ActiveScreenContext.Instance.Updated+=OfferChanged;
        root.TreeExiting+=()=>{AcquisitionPreview.Changed-=OfferChanged;ActiveScreenContext.Instance.Updated-=OfferChanged;};
        var timer=new Godot.Timer{WaitTime=.2,Autostart=true};root.AddChild(timer);timer.Timeout+=Update;Update();TraitSidebar.AddTo(root,player);return root;
    }

    private static NFastModeTickbox AddToggle(Control parent,string name,string text,float y)
    {
        var checkbox=TraitToggle.Create(name);
        parent.AddChild(checkbox);
        checkbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        checkbox.Size=new(64,64);checkbox.Scale=Vector2.One*.45f;checkbox.Position=new(0,y);
        var visuals=checkbox.GetNode<Control>("%TickboxVisuals");
        visuals.SetAnchorsPreset(Control.LayoutPreset.TopLeft);visuals.PivotOffset=visuals.Size/2;visuals.Position=checkbox.Size/2-visuals.Size/2;
        checkbox.GetNode<Control>("SelectionReticle").SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var label=new Label{Text=text,Position=new(35,y+2),Size=new(145,30),MouseFilter=Control.MouseFilterEnum.Stop};
        label.UseBundledChinese();
        label.AddThemeFontSizeOverride("font_size",17);label.AddThemeColorOverride("font_outline_color",BondBadge.Ink);label.AddThemeConstantOverride("outline_size",4);
        parent.AddChild(label);
        label.GuiInput+=input=>{if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:false}){checkbox.ForceToggleTick();label.AcceptEvent();}};
        return checkbox;
    }
}
