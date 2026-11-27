using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace Covenant.Game;

internal static class CovenantSelection
{
    private sealed record Options(bool CanSkip);
    private static readonly ConditionalWeakTable<LocString,Options> Prompts=new();

    internal static PlayerChoiceContext WithChoiceSupport(PlayerChoiceContext context,Player player)
    {
        if(context is not ThrowingPlayerChoiceContext)return context;
        if(RevisionTrace.FindChoiceContext(player) is {} parent)return parent;

        // Some native powers run outside any queued player action. The hook must wait for the selection.
        var blocking=new BlockingPlayerChoiceContext();
        if(context.ModelStack is {} models)
            foreach(var model in models.Reverse())blocking.PushModel(model);
        return blocking;
    }

    internal static CardSelectorPrefs Single(LocString prompt,bool optional=false)
    {
        Prompts.Add(prompt,new(optional));
        return new(prompt,optional?0:1,1){Cancelable=optional,RequireManualConfirmation=false};
    }

    internal static CardSelectorPrefs Pair(LocString prompt,bool optional=true)
    {
        Prompts.Add(prompt,new(optional));
        return new(prompt,2){Cancelable=optional,RequireManualConfirmation=true};
    }

    internal static async Task<bool> ConfirmStatusDiscard(PlayerChoiceContext context,CardModel card,int block,AbstractModel source)
    {
        var prompt=Presentation.Literal(BondDescription.Highlight($"你可以丢弃【{Presentation.CardTitle(card)}】。若如此做，获得{block}点格挡。"));
        var prefs=new CardSelectorPrefs(prompt,0,1);
        return (await CardSelectCmd.FromHandForDiscard(context,card.Owner,prefs,candidate=>candidate==card,source)).Contains(card);
    }

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(NSimpleCardSelectScreen),nameof(NSimpleCardSelectScreen._Ready)),
            postfix:new HarmonyMethod(typeof(CovenantSelection),nameof(Setup)));
        harmony.Patch(AccessTools.Method(typeof(NSimpleCardSelectScreen),"OnCardClicked"),
            postfix:new HarmonyMethod(typeof(CovenantSelection),nameof(RefreshSelection)));
    }

    private static void Setup(NSimpleCardSelectScreen __instance,CardSelectorPrefs ____prefs,HashSet<CardModel> ____selectedCards)
    {
        if(!Prompts.TryGetValue(____prefs.Prompt,out var options))return;
        var confirm=__instance.GetNode<NConfirmButton>("%Confirm");
        if(!____prefs.RequireManualConfirmation){confirm.Disable();confirm.Hide();}
        if(!options.CanSkip)return;

        var skip=GD.Load<PackedScene>("res://scenes/ui/choice_selection_skip_button.tscn").Instantiate<NChoiceSelectionSkipButton>();
        skip.Name="CovenantSkip";
        __instance.AddChild(skip);
        skip.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        skip.Position=new(__instance.Size.X-skip.Size.X-30,____prefs.RequireManualConfirmation?confirm.GetRect().End.Y+12:confirm.Position.Y);
        __instance.GetNode<NPeekButton>("%PeekButton").AddTargets([skip]);
        skip.Connect(NClickableControl.SignalName.Released,Callable.From<NButton>(_=>
        {
            ____selectedCards.Clear();
            confirm.EmitSignal(NClickableControl.SignalName.Released,confirm);
        }));
        skip.Enable();
    }

    private static void RefreshSelection(NSimpleCardSelectScreen __instance,CardModel card,CardSelectorPrefs ____prefs,HashSet<CardModel> ____selectedCards)
    {
        if(!____prefs.RequireManualConfirmation||!Prompts.TryGetValue(____prefs.Prompt,out _))return;
        var grid=__instance.GetNode<NCardGrid>("%CardGrid");
        var highlight=grid.GetCardNode(card)?.CardHighlight;
        if(highlight?.Material is not ShaderMaterial material)return;
        if(____selectedCards.Contains(card))
        {
            material.SetShaderParameter("width",.075f);
            highlight.AnimShow();
        }
        else highlight.AnimHideInstantly();
    }
}
