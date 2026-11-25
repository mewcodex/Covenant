using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace Covenant.Game;

internal static class CovenantFonts
{
    private static readonly Lazy<HashSet<string>> PowerTipIds=new(()=>new[]
    {
        typeof(CovenantAttritionPower), typeof(CovenantEffectPower),
        typeof(CovenantStatusStrengthPower), typeof(CovenantStrengthPower), typeof(CovenantUnrelentingPower),
        typeof(CovenantSynthesisPower), typeof(CovenantTerraformingPower)
    }.Select(type=>ModelDb.GetId(type).ToString()).ToHashSet(StringComparer.Ordinal));

    internal static void Install(Harmony harmony) => harmony.Patch(AccessTools.Method(typeof(NHoverTipSet),"Init"),
        prefix:new HarmonyMethod(typeof(CovenantFonts),nameof(IdentifyTips)),
        postfix:new HarmonyMethod(typeof(CovenantFonts),nameof(StyleTips)));

    internal static void UseBundledChinese(this Control control)
    {
        // The native rich-text widgets apply their locale font during _Ready.
        if(control is (MegaLabel or MegaRichTextLabel)&&!control.IsNodeReady())
            control.Ready+=()=>Apply(control);
        else
            Apply(control);
    }

    private static void Apply(Control control)
    {
        if(control is RichTextLabel)
        {
            Use(control,"normal_font",FontType.Regular);
            Use(control,"bold_font",FontType.Bold);
            Use(control,"italics_font",FontType.Italic);
            Use(control,"bold_italics_font",FontType.Bold);
            Use(control,"mono_font",FontType.Regular);
        }
        else
            Use(control,"font",FontType.Regular);
    }

    private static void Use(Control control,string name,FontType type)
    {
        // Covenant text is written in Chinese even when the game's UI uses a different language.
        if(FontManager.GetSubstituteFont("zhs",type) is {} font)control.AddThemeFontOverride(name,font);
    }

    private static void IdentifyTips(ref IEnumerable<IHoverTip> hoverTips,out bool[] __state)
    {
        // Native text nodes are created in this same order, separately from card preview nodes.
        hoverTips=IHoverTip.RemoveDupes(hoverTips);
        __state=hoverTips.OfType<HoverTip>().Select(tip=>tip.CanonicalModel is CovenantTerminal||
            tip.Id.Contains("relics.COVENANT_LITERAL_",StringComparison.Ordinal)||PowerTipIds.Value.Contains(tip.Id)).ToArray();
    }

    private static void StyleTips(VFlowContainer ____textHoverTipContainer,bool[] __state)
    {
        for(var i=0;i<__state.Length;i++)
        {
            if(!__state[i])continue;
            var tip=____textHoverTipContainer.GetChild<Control>(i);
            var title=tip.GetNode<MegaLabel>("%Title");
            title.UseBundledChinese();
            title.SetTextAutoSize(title.Text);
            tip.GetNode<MegaRichTextLabel>("%Description").UseBundledChinese();
            tip.ResetSize();
        }
    }
}
