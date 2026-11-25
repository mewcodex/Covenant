using Covenant.Core;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.addons.mega_text;

namespace Covenant.Game;

internal static class CardTraitTip
{
    internal const float Gap=10;
    internal static bool Inspecting;
    internal static bool InspectionInLibrary;
    private const string LibraryInspection = "covenant_library_inspection";
    private const string ShelfBaseY = "covenant_shelf_base_y";
    private const float ShelfTraitSpace = 88;
    private static readonly string[] CostIcons=["%EnergyIcon","%StarIcon","%StarCostIcon"];
    private static readonly string[] MerchantRows=["%CharacterCards","%ColorlessCards","%Relics","%Potions","%MerchantCardRemoval"];
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(NCardHolder),"CreateHoverTips"),postfix:new HarmonyMethod(typeof(CardTraitTip),nameof(Hover)));
        harmony.Patch(AccessTools.Method(typeof(NHoverTipSet),nameof(NHoverTipSet.Remove)),postfix:new HarmonyMethod(typeof(CardTraitTip),nameof(RemovedHoverTips)));
        harmony.Patch(AccessTools.Method(typeof(NMerchantCard),nameof(NMerchantCard._Ready)),postfix:new HarmonyMethod(typeof(CardTraitTip),nameof(ConnectMerchantHover)));
        harmony.Patch(AccessTools.Method(typeof(NMerchantInventory),nameof(NMerchantInventory._Ready)),postfix:new HarmonyMethod(typeof(CardTraitTip),nameof(SpaceMerchantShelves)));
        harmony.Patch(AccessTools.Method(typeof(NCard),nameof(NCard.AnimCardToPlayPile)),prefix:new HarmonyMethod(typeof(CardTraitTip),nameof(Playing)));
        harmony.Patch(AccessTools.Method(typeof(NCard),nameof(NCard.UpdateVisuals)),prefix:new HarmonyMethod(typeof(CardTraitTip),nameof(VisualContext)));
        harmony.Patch(AccessTools.Method(typeof(NCard),nameof(NCard.OnReturnedFromPool)),postfix:new HarmonyMethod(typeof(CardTraitTip),nameof(ResetFlight)));
        harmony.Patch(AccessTools.Method(typeof(NInspectCardScreen),nameof(NInspectCardScreen.Open)),prefix:new HarmonyMethod(typeof(CardTraitTip),nameof(SetInspectionContext)));
        harmony.Patch(AccessTools.Method(typeof(NInspectCardScreen),"UpdateCardDisplay"),prefix:new HarmonyMethod(typeof(CardTraitTip),nameof(InspectStart)),finalizer:new HarmonyMethod(typeof(CardTraitTip),nameof(InspectEnd)));
    }
    private static void SetInspectionContext(NInspectCardScreen __instance) => __instance.SetMeta(LibraryInspection,CovenantLibrary.OpeningInspection);
    private static void InspectStart(NInspectCardScreen __instance,out (bool Inspecting,bool InLibrary) __state)
    {
        __state=(Inspecting,InspectionInLibrary);
        Inspecting=true;
        InspectionInLibrary=__instance.HasMeta(LibraryInspection)&&__instance.GetMeta(LibraryInspection).AsBool();
    }
    private static void InspectEnd(NInspectCardScreen __instance,(bool Inspecting,bool InLibrary) __state,Exception? __exception)
    {
        (Inspecting,InspectionInLibrary)=__state;
        if(__exception!=null||NGame.Instance?.HoverTipsContainer==null)return;
        var card=(AccessTools.Field(typeof(NInspectCardScreen),"_card").GetValue(__instance) as NCard)?.Model;if(card==null)return;
        if(CardMembership.Definition(card)==null)return;
        var texture=Presentation.Icon(TraitDisplay.MainBond(card));
        foreach(var tip in Descendants(NGame.Instance.HoverTipsContainer).OfType<Control>().Where(c=>c.SceneFilePath=="res://scenes/ui/hover_tip.tscn"))
            if(tip.GetNodeOrNull<TextureRect>("%Icon") is {} icon&&icon.Texture==texture){SizeIcon(icon);Tone(tip,TraitDisplay.Active(card));}
    }
    private static void Hover(NCardHolder __instance)=>SetHover(__instance.CardNode,true);
    // Hand holders and the card-drag UI both reach this native cleanup, even when ClearHoverTips is inlined.
    private static void RemovedHoverTips(Control owner)
    {
        if(owner is NCardHolder holder)SetHover(holder.CardNode,false);
    }
    private static void ConnectMerchantHover(NMerchantCard __instance)
    {
        var holder=__instance.GetNode<Control>("%CardHolder");
        NCard? Card()=>holder.GetChildren().OfType<NCard>().FirstOrDefault(card=>!card.IsQueuedForDeletion());
        __instance.Hovered+=_=>SetHover(Card(),true);
        __instance.Unhovered+=_=>SetHover(Card(),false);
        __instance.Hitbox.MouseEntered+=()=>AcquisitionPreview.HoverMerchant(__instance);
        __instance.Hitbox.MouseExited+=()=>AcquisitionPreview.Exit(__instance);
    }
    private static void SpaceMerchantShelves(NMerchantInventory __instance)
    {
        foreach(var name in MerchantRows)
            if(__instance.GetNodeOrNull<Control>(name) is {} shelf)shelf.SetMeta(ShelfBaseY,shelf.Position.Y);
        LayoutMerchantShelves(__instance,false);
    }
    internal static void LayoutMerchantShelves(NMerchantInventory shop,bool statisticsVisible)
    {
        foreach(var name in MerchantRows)
            if(shop.GetNodeOrNull<Control>(name) is {} shelf&&shelf.HasMeta(ShelfBaseY))
            {
                var traits=CovenantSettings.ShowCardTraits;
                var offset=name switch
                {
                    "%CharacterCards"=>statisticsVisible?(traits?148f:58f):0,
                    "%ColorlessCards"=>(statisticsVisible?(traits?90f:42f):0)+(traits?ShelfTraitSpace:0),
                    _=>statisticsVisible?(traits?96f:36f):0
                };
                shelf.Position=new(shelf.Position.X,shelf.GetMeta(ShelfBaseY).AsSingle()+offset);
            }
    }
    private static void SetHover(NCard? card,bool hovered)
    {
        if(card==null||!GodotObject.IsInstanceValid(card))return;
        card.SetMeta("covenant_hover",hovered);Update(card);
    }
    private static void Playing(NCard __instance)
    {
        __instance.SetMeta("covenant_playing",true);__instance.SetMeta("covenant_hover",false);
        var tip=__instance.Body?.GetNodeOrNull<Control>("CovenantTraitTip");if(tip!=null)tip.Hide();
    }
    private static void ResetFlight(NCard __instance){__instance.SetMeta("covenant_playing",false);__instance.SetMeta("covenant_hover",false);}
    private static void VisualContext(NCard __instance,PileType pileType)
    {
        // A new visual context (including a real return to hand) ends the outgoing animation's lease.
        __instance.SetMeta("covenant_playing",pileType==PileType.Play);
    }
    private static bool IsPlaying(NCard node)=>node.DisplayingPile==PileType.Play||node.HasMeta("covenant_playing")&&node.GetMeta("covenant_playing").AsBool();
    private static bool IsHovered(NCard node)=>node.HasMeta("covenant_hover")&&node.GetMeta("covenant_hover").AsBool();
    internal static bool IsInspection(Node node)
    {
        for(Node? parent=node;parent!=null;parent=parent.GetParent())if(parent is NInspectCardScreen)return true;
        return false;
    }
    private static NCardGrid? FindGrid(Node node)
    {
        for(Node? parent=node;parent!=null;parent=parent.GetParent())if(parent is NCardGrid grid)return grid;
        return null;
    }
    internal static void Update(NCard node,CardPreviewMode? preview=null,Func<Player,Dictionary<Bond,int>>? countsFor=null)
    {
        if(!node.IsNodeReady()||node.Body==null)return;
        if(preview.HasValue)node.SetMeta("covenant_trait_preview",(int)preview.Value);
        var tip=node.Body.GetNodeOrNull<Control>("CovenantTraitTip");var card=node.Model;
        if(card==null||node.Visibility!=ModelVisibility.Visible||CardMembership.Definition(card)==null)
        {if(tip!=null)tip.Visible=false;return;}
        var hovered=IsHovered(node);
        var grid=FindGrid(node);
        var combat=grid==null&&TraitDisplay.InCombat(card.IsMutable?card.Owner:null);
        if(IsInspection(node)||IsPlaying(node)||!(grid is NCardLibraryGrid||hovered||CovenantSettings.ShowCardTraits&&(!combat||TraitDisplay.HasDirect(card))))
        {tip?.Hide();return;}
        if(tip==null)
        {
            tip=GD.Load<PackedScene>("res://scenes/ui/hover_tip.tscn").Instantiate<Control>();tip.Name="CovenantTraitTip";
            node.Body.AddChild(tip);
            foreach(var item in Descendants(tip).OfType<Control>().Prepend(tip))item.MouseFilter=Control.MouseFilterEnum.Ignore;
            tip.GetNode<Control>("%Bg").Visible=false;
            tip.GetNode<MegaLabel>("%Title").UseBundledChinese();
            var tipDescription=tip.GetNode<MegaRichTextLabel>("%Description");
            tipDescription.UseBundledChinese();
            tipDescription.ScrollActive=false;
            var container=tip;
            // Rich text recomputes its minimum after wrapping. Shrink the outer panel when that happens.
            tipDescription.MinimumSizeChanged+=()=>container.CallDeferred(Control.MethodName.ResetSize);
            var margins=tip.GetNode<MarginContainer>("TextContainer");
            margins.AddThemeConstantOverride("margin_left",15);margins.AddThemeConstantOverride("margin_right",15);
            margins.AddThemeConstantOverride("margin_top",12);margins.AddThemeConstantOverride("margin_bottom",14);
            SizeIcon(tip.GetNode<TextureRect>("%Icon"));
            var tone=new Panel{Name="ToneFrame",MouseFilter=Control.MouseFilterEnum.Ignore};tip.AddChild(tone);tip.MoveChild(tone,1);
            var follower=new Godot.Timer{Name="CovenantTraitFollower",WaitTime=1d/60};
            node.AddChild(follower);follower.Timeout+=()=>Layout(node);
            var panel=tip;
            void SyncFollower()
            {
                if(!follower.IsInsideTree())return;
                if(panel.IsVisibleInTree()){if(follower.IsStopped())follower.Start();}
                else follower.Stop();
            }
            panel.VisibilityChanged+=SyncFollower;
            follower.TreeEntered+=SyncFollower;
            SyncFollower();
        }
        tip.Show();
        var mainBond=TraitDisplay.MainBond(card);
        var title=tip.GetNode<MegaLabel>("%Title");
        var titleText=TraitDisplay.TraitTitle(card,mainBond);
        if(title.Text!=titleText)title.SetTextAutoSize(titleText);
        var icon=tip.GetNode<TextureRect>("%Icon");icon.Texture=Presentation.Icon(mainBond);
        var iconRow=icon.GetParent();
        var secondIcon=iconRow.GetNodeOrNull<TextureRect>("SecondCovenantIcon");
        if(TraitDisplay.SecondTraitBond(card) is {} secondBond)
        {
            if(secondIcon==null)
            {
                secondIcon=new TextureRect{Name="SecondCovenantIcon",MouseFilter=Control.MouseFilterEnum.Ignore};
                SizeIcon(secondIcon);
                iconRow.AddChild(secondIcon);
                iconRow.MoveChild(secondIcon,icon.GetIndex()+1);
            }
            secondIcon.Texture=Presentation.Icon(secondBond);secondIcon.Show();
        }
        else secondIcon?.Hide();
        var mode=preview??(node.HasMeta("covenant_trait_preview")?(CardPreviewMode)node.GetMeta("covenant_trait_preview").AsInt32():CardPreviewMode.Normal);
        var text=Presentation.TraitText(card,mode==CardPreviewMode.Upgrade,directOnly:combat&&!hovered,annotateScaling:true);
        var description=tip.GetNode<MegaRichTextLabel>("%Description");description.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var formatted=BondDescription.Format(text,card,showFormulas:grid is NCardLibraryGrid,splitSentences:false);
        if(description.Text!=formatted){description.Text=formatted;tip.ResetSize();}
        Tone(tip,TraitDisplay.Active(card,countsFor));
        Layout(node);
    }
    private static void SizeIcon(TextureRect icon)
    {
        icon.ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize;
        icon.StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered;
        icon.CustomMinimumSize=new(22,22);
        icon.SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
        icon.SizeFlagsVertical=Control.SizeFlags.ShrinkCenter;
    }
    private static void Tone(Control tip,bool enabled)
    {
        if(!tip.HasMeta("tone_enabled"))tip.GetNode<Control>("%Title").GetParent<HBoxContainer>().AddThemeConstantOverride("separation",10);
        if(tip.GetNodeOrNull<Panel>("ToneFrame")==null)
        {
            var background=tip.GetNodeOrNull<Control>("%Bg");if(background!=null)background.Visible=false;
            var panel=new Panel{Name="ToneFrame",MouseFilter=Control.MouseFilterEnum.Ignore};tip.AddChild(panel);tip.MoveChild(panel,1);
        }
        if(!tip.HasMeta("tone_enabled")||tip.GetMeta("tone_enabled").AsBool()!=enabled)
        {
            tip.SetMeta("tone_enabled",enabled);
            tip.GetNode<Panel>("ToneFrame").AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=enabled?new Color("153b38f2"):new Color("172627f2"),BorderColor=enabled?BondBadge.Cyan:BondBadge.Dim,BorderWidthTop=2,BorderWidthBottom=2,BorderWidthLeft=2,BorderWidthRight=2,CornerRadiusTopLeft=4,CornerRadiusTopRight=4,CornerRadiusBottomLeft=4,CornerRadiusBottomRight=4});
        }
    }
    internal static void Layout(NCard node)
    {
        if(!GodotObject.IsInstanceValid(node)||node.Body==null)return;
        var tip=node.Body.GetNodeOrNull<Control>("CovenantTraitTip");if(tip==null||!tip.IsVisibleInTree())return;
        if(IsPlaying(node)){tip.Hide();return;}
        var frame=node.GetNode<Control>("%Frame");var inverse=node.Body.GetGlobalTransform().AffineInverse();
        Vector2 Point(Control control,Vector2 point)=>inverse*(control.GetGlobalTransform()*point);
        var left=Point(frame,Vector2.Zero);var right=Point(frame,new(frame.Size.X,0));
        var frameTop=Math.Min(left.Y,right.Y);var top=frameTop;
        foreach(var name in CostIcons)
        {
            var cost=node.GetNodeOrNull<Control>(name);if(cost==null||!cost.IsVisibleInTree())continue;
            top=Math.Min(top,Math.Min(Point(cost,Vector2.Zero).Y,Point(cost,new(cost.Size.X,0)).Y));
        }
        var width=Math.Abs(right.X-left.X);
        NMerchantCard? merchant=null;
        for(var parent=node.GetParent();parent!=null;parent=parent.GetParent())
            if(parent is NMerchantCard slot){merchant=slot;break;}
        var grid=FindGrid(node);
        if(merchant!=null)
            width=LayoutMerchantTip(node,merchant,(MarginContainer)tip,frame,inverse,top,width);
        else if(tip.HasMeta("covenant_shop_width"))
        {
            var description=tip.GetNode<MegaRichTextLabel>("%Description");
            description.FitContent=true;description.ScrollActive=false;description.CustomMinimumSize=new(300,0);
            tip.RemoveMeta("covenant_shop_width");tip.ResetSize();
        }
        var scale=Math.Min(1,width/Math.Max(1,tip.Size.X));
        if(merchant==null&&grid==null)
        {
            var bounds=frame.GetGlobalRect();
            var minimum=(inverse*new Vector2(bounds.GetCenter().X,8)).Y;
            scale=Math.Min(scale,Math.Max(30,top-minimum-Gap)/Math.Max(1,tip.Size.Y));
        }
        else if(grid!=null&&LibraryGridSpacing.ReservesTraitSpace(grid))
        {
            // Use the stable row spacing while cards move through the scrolling viewport.
            var energyOverhang=frameTop-top;
            var tallFrame=Math.Max(0,frame.Size.Y-NCard.defaultSize.Y);
            var available=(40+LibraryGridSpacing.ExtraRow)/NCardHolder.smallScale.Y-Gap-energyOverhang-tallFrame-12;
            scale=Math.Min(scale,Math.Max(30,available)/Math.Max(1,tip.Size.Y));
        }
        tip.Scale=Vector2.One*scale;
        tip.Position=new((left.X+right.X-tip.Size.X*scale)/2,top-Gap-tip.Size.Y*scale);
        tip.SetMeta("actual_gap",Gap);
    }
    private static float LayoutMerchantTip(NCard node,NMerchantCard merchant,MarginContainer tip,Control frame,Transform2D inverse,float top,float frameWidth)
    {
        var width=MathF.Floor(frameWidth*1.18f);
        var bounds=frame.GetGlobalRect();
        var halfWidth=bounds.Size.X*.59f;
        var center=bounds.GetCenter();
        bool IntersectsColumn(Rect2 rect)=>center.X-halfWidth<rect.End.X&&center.X+halfWidth>rect.Position.X;
        var ceiling=8f;
        if(NRun.Instance?.GlobalUi is {} ui)
        {
            var topBarY=ui.TopBar.GetNode<Control>("%BgImage").GetGlobalRect().End.Y;
            if(topBarY>0&&topBarY<node.GetViewportRect().Size.Y/3)ceiling=topBarY+4;
            if(ui.GetNodeOrNull<Control>("CovenantRoot/BondBar") is {Visible:true} bar)
            {
                var barRect=bar.GetGlobalRect();
                if(IntersectsColumn(barRect))ceiling=Math.Max(ceiling,barRect.End.Y+4);
                var arrow=bar.GetNode<Control>("MoreCovenants");
                var arrowRect=arrow.GetGlobalRect();
                if(arrow.Visible&&IntersectsColumn(arrowRect))ceiling=Math.Max(ceiling,arrowRect.End.Y+4);
            }
        }
        if(merchant.GetParent()?.Name=="ColorlessCards"&&NMerchantRoom.Instance?.Inventory.GetNodeOrNull<Control>("%CharacterCards") is {} upperShelf)
            foreach(var slot in upperShelf.GetChildren().OfType<NMerchantCard>().Where(slot=>slot.IsVisibleInTree()))
            {
                var price=slot.GetNode<Control>("%CostLabel").GetGlobalRect();
                if(IntersectsColumn(price))ceiling=Math.Max(ceiling,price.End.Y+4);
            }

        var text=tip.GetNode<MarginContainer>("TextContainer");
        var description=tip.GetNode<MegaRichTextLabel>("%Description");
        var title=tip.GetNode<Control>("%Title").GetParent<Control>();
        int Margins(string first,string second)=>tip.GetThemeConstant(first)+tip.GetThemeConstant(second)+
            text.GetThemeConstant(first)+text.GetThemeConstant(second);
        var space=top-(inverse*new Vector2(center.X,ceiling)).Y-Gap-Margins("margin_top","margin_bottom")-title.GetCombinedMinimumSize().Y;
        // Keep native text size even in a tight shelf. Hover opens the full description.
        var line=description.GetThemeFont("normal_font").GetHeight(description.GetThemeFontSize("normal_font_size"));
        var heightLimit=Math.Max(line,MathF.Floor(space));
        var clipped=!IsHovered(node)&&description.GetContentHeight()>heightLimit;
        var minimum=new Vector2(Math.Max(1,width-Margins("margin_left","margin_right")),clipped?heightLimit:0);
        description.ScrollActive=false;
        if(description.CustomMinimumSize!=minimum||description.FitContent==clipped)
        {
            description.FitContent=!clipped;
            description.CustomMinimumSize=minimum;
            tip.ResetSize();
        }
        tip.SetMeta("covenant_shop_width",true);
        return width;
    }
    private static IEnumerable<Node> Descendants(Node root)
    {foreach(var child in root.GetChildren()){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
}
