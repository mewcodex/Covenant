using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace Covenant.Game;

internal static class SettingsUi
{
    internal const string ToggleName="CovenantFullPool";
    internal const string TraitToggleName="CovenantTraitVisibility";
    internal const string FormulaToggleName="CovenantFormulaVisibility";
    internal const string MerchantToggleName="CovenantHideMerchantHand";
    internal const string FloorScalingToggleName="CovenantEnemyFloorScaling";
    internal const string FullPoolTitle="开启全卡池";
    internal const string MerchantTitle="隐藏商人的手";
    private static readonly ConditionalWeakTable<NSubmenuStack,CovenantSettingsPage> Pages=new();
    internal static void Install(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(NSettingsScreen),nameof(NSettingsScreen._Ready)),postfix:new HarmonyMethod(typeof(SettingsUi),nameof(AddEntry)));
        foreach(var type in new[]{typeof(NMainMenuSubmenuStack),typeof(NRunSubmenuStack)})h.Patch(AccessTools.Method(type,"GetSubmenuType",[typeof(Type)]),prefix:new HarmonyMethod(typeof(SettingsUi),nameof(GetPage)));
        h.Patch(AccessTools.Method(typeof(NFastModeTickbox),"SetFromSettings"),prefix:new HarmonyMethod(typeof(SettingsUi),nameof(SetToggle)));
        h.Patch(AccessTools.Method(typeof(NFastModeTickbox),"OnTick"),prefix:new HarmonyMethod(typeof(SettingsUi),nameof(Tick)));
        h.Patch(AccessTools.Method(typeof(NFastModeTickbox),"OnUntick"),prefix:new HarmonyMethod(typeof(SettingsUi),nameof(Untick)));
        h.Patch(AccessTools.Method(typeof(NFastModeHoverTip),nameof(NFastModeHoverTip._Ready)),postfix:new HarmonyMethod(typeof(SettingsUi),nameof(SetHoverTip)));
    }
    internal static bool IsOurs(NFastModeTickbox box)=>box.Name.ToString()==ToggleName||box.GetParent()?.Name.ToString()==ToggleName;
    private static bool SetToggle(NFastModeTickbox __instance)
    {
        switch(__instance.Name.ToString())
        {
            case MerchantToggleName: __instance.IsTicked=CovenantSettings.HideMerchantHand;return false;
            case TraitToggleName: __instance.IsTicked=CovenantSettings.ShowCardTraits;return false;
            case FormulaToggleName: __instance.IsTicked=CovenantSettings.ShowFormulas;return false;
            case FloorScalingToggleName: __instance.IsTicked=CovenantSettings.EnemyFloorScaling;return false;
            default:
                if(!IsOurs(__instance))return true;
                __instance.IsTicked=CovenantSettings.EnableAllCardPools;return false;
        }
    }
    private static bool Tick(NFastModeTickbox __instance)=>SetOption(__instance,true);
    private static bool Untick(NFastModeTickbox __instance)=>SetOption(__instance,false);
    private static bool SetOption(NFastModeTickbox box,bool value)
    {
        switch(box.Name.ToString())
        {
            case MerchantToggleName: CovenantSettings.SetHideMerchantHand(value);return false;
            case TraitToggleName: CovenantSettings.SetShowCardTraits(value);return false;
            case FormulaToggleName: CovenantSettings.SetShowFormulas(value);return false;
            case FloorScalingToggleName: CovenantSettings.SetEnemyFloorScaling(value);return false;
            default:
                if(!IsOurs(box))return true;
                CovenantSettings.SetAllCardPools(value);return false;
        }
    }
    private static void SetHoverTip(NFastModeHoverTip __instance,ref IHoverTip ____hoverTip)
    {
        var (title,description)=__instance.Name.ToString() switch
        {
            ToggleName=>(FullPoolTitle,"开启后，六个角色的卡池都会用于卡牌奖励和商店。此选项在开始新一局时生效。"),
            FloorScalingToggleName=>(CovenantFloorScaling.SettingTitle,"每深入一层，敌人的最大生命、格挡和部分状态效果都会按该比例累乘。此选项在开始新一局时生效。"),
            MerchantToggleName=>(MerchantTitle,"开启后隐藏商店中商人的手。切换后立即生效。"),
            _=>(null,null)
        };
        if(title!=null&&description!=null)____hoverTip=new HoverTip(Presentation.Literal(title),description);
    }
    internal static void Text(Node? node,string value)
    {
        if(node is Control control)control.UseBundledChinese();
        if(node is MegaRichTextLabel megaRich)megaRich.Text=value;
        else if(node is RichTextLabel rich)rich.Text=value;
        else if(node is MegaLabel mega)mega.SetTextAutoSize(value);
        else if(node is Label label)label.Text=value;
    }
    internal static void FixOwner(Node node,Node root){if(node!=root)node.Owner=root;foreach(var child in node.GetChildren())FixOwner(child,root);}
    private static void AddEntry(NSettingsScreen __instance)
    {
        if(__instance.GetNodeOrNull<NSettingsPanel>("%GeneralSettings") is not {} panel ||
            panel.Content.GetNodeOrNull<Control>("FastMode")==null ||
            panel.Content.GetNodeOrNull<Control>("Modding") is not {} action ||
            action.GetNodeOrNull<NOpenModdingScreenButton>("ModdingButton")==null)return;
        if(panel.Content.HasNode("CovenantSettingsEntry"))return;
        var row=(Control)action.Duplicate(6);row.Name="CovenantSettingsEntry";FixOwner(row,row);Text(row.GetNodeOrNull<Node>("Label"),"杀戮尖塔2：盟约");
        var button=row.GetNode<NOpenModdingScreenButton>("ModdingButton");button.Name="CovenantSettingsOpen";Text(button.GetNodeOrNull<Node>("Label"),"设置");
        button.Connect(NClickableControl.SignalName.Released,Callable.From<NButton>(_=>
        {
            for(Node? current=panel;current!=null;current=current.GetParent())if(current is NSubmenuStack stack){stack.PushSubmenuType<CovenantSettingsPage>();return;}
        }));
        panel.Content.AddChild(row);var divider=panel.Content.GetNodeOrNull<Node>("ModdingDivider");if(divider!=null)panel.Content.MoveChild(row,divider.GetIndex());
        Text(button.GetNodeOrNull<Node>("Label"),"设置");button.Enable();
        panel.Call(NSettingsPanel.MethodName.RefreshSize);panel.Call(NSettingsPanel.MethodName.UpdateNavigation);
    }
    private static bool GetPage(NSubmenuStack __instance,Type type,ref NSubmenu __result)
    {
        if(type!=typeof(CovenantSettingsPage))return true;
        if(!Pages.TryGetValue(__instance,out var page))
        {
            var settings=__instance.GetSubmenuType<NSettingsScreen>();
            var template=settings.GetNodeOrNull<NSettingsPanel>("%GeneralSettings")?.Content.GetNodeOrNull<Control>("FastMode");
            page=new CovenantSettingsPage{Visible=false,Size=__instance.Size,OptionTemplate=template};page.SetAnchorsPreset(Control.LayoutPreset.FullRect);__instance.AddChildSafely(page);page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);Pages.Add(__instance,page);
        }
        __result=page;return false;
    }
}

public sealed class CovenantSettingsPage:NSubmenu
{
    internal Control? OptionTemplate { private get; init; }
    private Control? _focus;
    protected override Control? InitialFocusedControl=>_focus;
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var back=GD.Load<PackedScene>("res://scenes/ui/back_button.tscn").Instantiate<NBackButton>();
        back.Name="BackButton";AddChild(back);ConnectSignals();
        var title=new Label{Text="杀戮尖塔2：盟约",HorizontalAlignment=HorizontalAlignment.Center};title.UseBundledChinese();AddChild(title);title.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);title.OffsetTop=45;title.OffsetBottom=100;title.AddThemeFontSizeOverride("font_size",34);
        var scroll=new ScrollContainer{Name="CovenantSettingsScroll",AnchorLeft=.19f,AnchorRight=.81f,AnchorBottom=1,OffsetTop=135,OffsetBottom=-110,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};AddChild(scroll);
        var options=new VBoxContainer{CustomMinimumSize=new(800,0),SizeFlagsHorizontal=SizeFlags.ExpandFill};scroll.AddChild(options);
        var category=Category("卡池");options.AddChild(category);
        if(OptionTemplate is not {} template||!GodotObject.IsInstanceValid(template))
        {
            var unavailable=new Label{Text="当前设置界面无法显示盟约选项。"};unavailable.UseBundledChinese();options.AddChild(unavailable);
            _focus=back;
            return;
        }
        var row=(Control)template.Duplicate(6);row.Name=SettingsUi.ToggleName;SettingsUi.FixOwner(row,row);SettingsUi.Text(row.GetNodeOrNull<Node>("Label"),SettingsUi.FullPoolTitle);
        var box=Descendants(row).OfType<NFastModeTickbox>().Single();box.Name=SettingsUi.ToggleName;options.AddChild(row);box.IsTicked=CovenantSettings.EnableAllCardPools;_focus=box;
        options.AddChild(Category("难度"));
        var difficultyRow=(Control)template.Duplicate(6);difficultyRow.Name=SettingsUi.FloorScalingToggleName;SettingsUi.FixOwner(difficultyRow,difficultyRow);SettingsUi.Text(difficultyRow.GetNodeOrNull<Node>("Label"),CovenantFloorScaling.SettingTitle);
        var difficultyBox=Descendants(difficultyRow).OfType<NFastModeTickbox>().Single();difficultyBox.Name=SettingsUi.FloorScalingToggleName;options.AddChild(difficultyRow);difficultyBox.IsTicked=CovenantSettings.EnemyFloorScaling;
        options.AddChild(Category("显示"));
        var handRow=(Control)template.Duplicate(6);handRow.Name=SettingsUi.MerchantToggleName;SettingsUi.FixOwner(handRow,handRow);SettingsUi.Text(handRow.GetNodeOrNull<Node>("Label"),SettingsUi.MerchantTitle);
        var handBox=Descendants(handRow).OfType<NFastModeTickbox>().Single();handBox.Name=SettingsUi.MerchantToggleName;options.AddChild(handRow);handBox.IsTicked=CovenantSettings.HideMerchantHand;
        MoveChild(back,-1);
        for(Node? current=GetParent();current!=null;current=current.GetParent())if(current is NRunSubmenuStack){box.Disable();difficultyBox.Disable();row.Modulate=difficultyRow.Modulate=new(1,1,1,.45f);_focus=handBox;break;}
    }
    private static Label Category(string text)
    {
        var label=new Label{Text=text,CustomMinimumSize=new(800,55)};
        label.UseBundledChinese();
        label.AddThemeFontSizeOverride("font_size",25);
        return label;
    }
    private static IEnumerable<Node> Descendants(Node root){foreach(var child in root.GetChildren()){yield return child;foreach(var inner in Descendants(child))yield return inner;}}
}
