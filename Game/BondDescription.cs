using System.Text.RegularExpressions;
using Covenant.Core;
using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

internal static class BondDescription
{
    // Use the native card-text tags and formatter, including character-specific energy sprite fonts.
    private static readonly Regex Keywords=new("衍生攻击牌|衍生牌|君王之剑|攻击牌|技能牌|能力牌|先古牌|奇遇牌|弃牌堆|抽牌堆|手牌|充能球|人工制品|活力|拉伸|覆甲|敏捷|荆棘|镀层|易伤|打击牌|打击|激发|格挡|力量|集中|中毒|虚弱|保留|虚无|瞄准|灾厄|锻造|召唤|发射|扎穿|松脱|旋律|悲伤|喜悦|升级|变化|辉星|诅咒|复制|状态|消耗(?!减少|至少|为|增加)",RegexOptions.Compiled);
    private static readonly Regex EnergyAmounts=new(@"(?<label>能量消耗(?:各?减少|为|不高于)|原始费用(?:至少为|为))(?<amount>[0-9]+)(?![0-9{])(?:点能量|点)?|(?<amount>[0-9]+)点能量",RegexOptions.Compiled);
    internal static string Highlight(string text)=>string.Concat(Regex.Split(text,@"(【[^】]+】)").Select(part=>part.StartsWith('【')?"[gold]"+part+"[/gold]":Keywords.Replace(part,m=>"[gold]"+m.Value+"[/gold]")));
    internal const string ScalingColorTag="[color=#ff777b]";
    internal static string Format(string text,AbstractModel model,bool showFormulas=false,bool splitSentences=true)
    {
        var energyText=EnergyAmounts.Replace(text,match=>$"{match.Groups["label"].Value}{{energyPrefix:energyIcons({match.Groups["amount"].Value})}}");
        var rich=Highlight(ScalingText.Render(energyText,showFormulas||CovenantSettings.ShowFormulas,ScalingColorTag));
        if(splitSentences)rich=rich.Replace("。","。\n");
        var description=Presentation.Literal(rich.TrimEnd());
        var prefix=model is CovenantTerminal{IsMutable:true} terminal
            ? terminal.Owner.Character.CardPool.EnergyColorName : EnergyIconHelper.GetPrefix(model);
        description.Add("energyPrefix",prefix);
        description.Add("starIcon",MegaCrit.Sts2.Core.Localization.Formatters.StarIconsFormatter.starIconSprite);
        return description.GetFormattedText().TrimEnd();
    }

    internal static MegaRichTextLabel Create(string name)
    {
        // Clone the actual native description widget so all fonts, effects and inline-image handling are retained.
        var template=GD.Load<PackedScene>("res://scenes/ui/hover_tip.tscn").Instantiate<Control>();
        var label=(MegaRichTextLabel)template.GetNode<MegaRichTextLabel>("%Description").Duplicate(6);
        template.Free();
        label.Name=name;label.UniqueNameInOwner=false;
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        label.Position=Vector2.Zero;label.Size=Vector2.Zero;label.CustomMinimumSize=Vector2.Zero;
        label.AutoSizeEnabled=false;label.FitContent=true;label.BbcodeEnabled=true;label.ScrollActive=false;
        label.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;label.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        label.MouseFilter=Control.MouseFilterEnum.Ignore;
        label.UseBundledChinese();
        foreach(var font in new[]{"normal_font_size","bold_font_size","italics_font_size","bold_italics_font_size","mono_font_size"})label.AddThemeFontSizeOverride(font,20);
        label.AddThemeConstantOverride("line_separation",5);
        return label;
    }
}
