using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace Covenant.Game;

internal static class NativeHooks
{
    internal static void Install(Harmony h)
    {
        CovenantMelody.Install(h);
        CovenantTerminal.InstallOracle(h);
        h.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.OnPlayWrapper)),prefix:new HarmonyMethod(typeof(NativeHooks),nameof(PlayStarting)),postfix:new HarmonyMethod(typeof(NativeHooks),nameof(PlayFinished)),finalizer:new HarmonyMethod(typeof(NativeHooks),nameof(PlayScopeEnded)));
        h.Patch(AccessTools.Method(typeof(CardPileCmd),nameof(CardPileCmd.Add),[typeof(IEnumerable<CardModel>),typeof(CardPile),typeof(CardPilePosition),typeof(AbstractModel),typeof(bool),typeof(bool)]),prefix:new HarmonyMethod(typeof(NativeHooks),nameof(BeforePileAdd)));
        h.Patch(AccessTools.Method(typeof(CardCmd),nameof(CardCmd.Exhaust)),prefix:new HarmonyMethod(typeof(NativeHooks),nameof(BeforeExhaust)),
            postfix:new HarmonyMethod(typeof(NativeHooks),nameof(AfterExhaust)));
        h.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.OnTurnEndInHandWrapper)),
            prefix:new HarmonyMethod(typeof(NativeHooks),nameof(BeforeTurnEndCard)));
        h.Patch(AccessTools.Method(typeof(CardCmd),nameof(CardCmd.AutoPlay)),
            prefix:new HarmonyMethod(typeof(NativeHooks),nameof(BeforeAutoPlay)));
        // Reset before *any* start-of-turn listener. Powers run before relics in the normal model dispatch.
        h.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.BeforeSideTurnStart)),prefix:new HarmonyMethod(typeof(NativeHooks),nameof(BeforeSide)));
        h.Patch(AccessTools.Method(typeof(MelodyCmd),nameof(MelodyCmd.PlayedThisTurn)),postfix:new HarmonyMethod(typeof(NativeHooks),nameof(PlayedMelody)));
        h.Patch(AccessTools.Method(typeof(MelodyCmd),nameof(MelodyCmd.IsFirstPlayThisTurn)),postfix:new HarmonyMethod(typeof(NativeHooks),nameof(FirstMelody)));
        h.Patch(AccessTools.Method(typeof(OrbCmd),nameof(OrbCmd.Channel),[typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),typeof(OrbModel),typeof(Player)]),
            prefix:new HarmonyMethod(typeof(NativeHooks),nameof(ChannelStarting)),postfix:new HarmonyMethod(typeof(NativeHooks),nameof(ChannelFinished)));
    }
    private static bool BeforeExhaust(PlayerChoiceContext choiceContext,CardModel card,bool causedByEthereal,ref bool skipVisuals,
        ref Task<CardPileAddResult?> __result,out bool __state)
    {
        __state=false;
        if(card.HasBeenRemovedFromState&&CovenantTerminal.TryGetCombatReplacement(card,out _))
        {
            __result=Task.FromResult<CardPileAddResult?>(null);
            return false;
        }
        if(!causedByEthereal)return true;
        // An earlier end-of-turn play can move a card out of the native Ethereal queue.
        if(card.Pile?.Type is not (PileType.Hand or PileType.Play))
        {
            __result=Task.FromResult<CardPileAddResult?>(null);
            return false;
        }
        if(card.Owner is not {} owner||owner.Creature.IsDead||CombatManager.Instance.IsOverOrEnding||
            owner.Creature.CombatState is not {} combat||CovenantTerminal.For(owner) is not {} terminal)return true;
        if(skipVisuals&&card.Pile?.Type==PileType.Play&&terminal.SolitudeCanReplaceExhaustedPlayCard(card))
        {
            // Finish the original animation before its Exhaust-pile model can be replaced.
            skipVisuals=false;
            __state=true;
        }
        var chance=terminal.EtherealAutoPlayChance();
        if(chance<=0)return true;
        if(card.Keywords.Contains(CardKeyword.Unplayable)||
            card.TargetType==TargetType.AnyAlly&&!combat.Allies.Any(ally=>ally.IsAlive&&ally.IsPlayer&&ally!=owner.Creature)||
            (card.TargetType is TargetType.AnyEnemy or TargetType.RandomEnemy)&&!combat.HittableEnemies.Any()||
            !Hook.ShouldPlay(combat,card,out _,AutoPlayType.Default))return true;
        if(terminal.RandomFor(CovenantRandom.CombatRoll).NextInt(100)>=chance)return true;
        __result=PlayInsteadOfExhausting(terminal,choiceContext,card);
        return false;
    }
    private static bool BeforeTurnEndCard(CardModel __instance,ref Task __result)
        =>BeforeAutoPlay(__instance,ref __result);
    private static bool BeforeAutoPlay(CardModel card,ref Task __result)
    {
        if(!card.HasBeenRemovedFromState||!CovenantTerminal.TryGetCombatReplacement(card,out _))return true;
        __result=Task.CompletedTask;
        return false;
    }

    private static void AfterExhaust(bool __state,ref Task<CardPileAddResult?> __result)
        =>__result=ResolveExhaustResult(__result,__state);
    private static async Task<CardPileAddResult?> ResolveExhaustResult(Task<CardPileAddResult?> original,bool animated)
    {
        var result=await original;
        if(animated||result is not {} added)return null;
        if(added.success&&CovenantTerminal.TryGetCombatReplacement(added.cardAdded,out var replacement))
            added.cardAdded=replacement;
        return added;
    }
    private static async Task<CardPileAddResult?> PlayInsteadOfExhausting(CovenantTerminal terminal,PlayerChoiceContext context,CardModel card)
    {
        await terminal.PlayForCovenant(context,card);
        return null;
    }
    private static void BeforePileAdd(ref IEnumerable<CardModel> cards,CardPile newPile,bool isChangingOwners)
        =>CovenantTerminal.ReplaceDirectStatusAdds(ref cards,newPile,isChangingOwners);
    private static void PlayStarting(CardModel __instance,PlayerChoiceContext choiceContext,bool isAutoPlay,ref ResourceInfo resources,out RevisionTrace.Scope? __state)
    {
        __state=null;if(CovenantTerminal.For(__instance.Owner) is not {} terminal)return;
        terminal.RestoreVakuuPayment(__instance,ref resources);
        if(!isAutoPlay)terminal.ObservePlayBeforeLeavingHand(__instance);
        if(terminal.ReworkFreeX(__instance))
        {
            resources=resources with{EnergyValue=__instance.Owner.PlayerCombatState!.Energy};
            __instance.EnergyCost.CapturedXValue=resources.EnergyValue;
        }
        __state=RevisionTrace.BeginCard(__instance,choiceContext);
    }
    private static void PlayFinished(CardModel __instance,MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext,bool isAutoPlay,RevisionTrace.Scope? __state,ref Task __result)
    {
        if(CovenantTerminal.For(__instance.Owner) is {} terminal)__result=AfterPlay(terminal,__instance,choiceContext,isAutoPlay,__state,__result);
        RevisionTrace.Restore(__state);
    }
    private static async Task AfterPlay(CovenantTerminal terminal,CardModel card,MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext context,bool isAutoPlay,RevisionTrace.Scope? scope,Task original)
    {try{await original;await terminal.FinishReworkPlay(card);await terminal.FinishSecondaryPlay(context,card,isAutoPlay);}finally{RevisionTrace.Restore(scope);}}
    private static void PlayScopeEnded(RevisionTrace.Scope? __state)=>RevisionTrace.Restore(__state);
    private static void ChannelStarting(Player player,ref PlayerChoiceContext choiceContext,out CovenantTerminal? __state)
    {
        __state=CovenantTerminal.For(player);
        if(__state==null)return;
        __state.BeginNativeChannel();
        // Vanilla can channel during energy reset with no action that can host a choice.
        // Resonance and Overload may open a selection while that channel is still resolving.
        choiceContext=CovenantSelection.WithChoiceSupport(choiceContext,player);
    }
    private static void ChannelFinished(CovenantTerminal? __state,ref Task __result)
    {if(__state!=null)__result=FinishChannel(__state,__result);}
    private static async Task FinishChannel(CovenantTerminal terminal,Task original)
    {
        var complete=false;
        try{await original;complete=true;}
        finally{await terminal.EndNativeChannel(complete);}
    }
    private static void BeforeSide(CombatSide side,IReadOnlyList<Creature> participants)
    {
        if(side!=CombatSide.Player)return;
        foreach(var player in participants.Where(x=>x.IsPlayer).Select(x=>x.Player))CovenantTerminal.For(player)?.BeginTurn();
    }
    private static void PlayedMelody(Player player,CardTag cardTag,ref bool __result)
    {
        if(CovenantTerminal.For(player)?.HasInheritedMelody(cardTag)==true)__result=true;
    }
    private static void FirstMelody(CardPlay cardPlay,CardTag cardTag,ref bool __result)
    {
        if(CovenantTerminal.For(cardPlay.Player)?.HasInheritedMelody(cardTag)==true)__result=false;
    }
}
