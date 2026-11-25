using System.Runtime.CompilerServices;
using Covenant.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace Covenant.Game;

/// <summary>Post-combat choices use the native reward and card-selection commands, including their synchronization.</summary>
internal static class CovenantRewards
{
    private sealed class State
    {
        internal bool Rerolled, Exchanged, AlternateCompleted, NormalClaim, PurchaseOffered;
    }
    private static readonly ConditionalWeakTable<CardReward,State> States=new();
    private static readonly ConditionalWeakTable<CardRewardAlternative,LocString> Titles=new();
    private static readonly System.Reflection.PropertyInfo Options=AccessTools.Property(typeof(CardReward),"Options");
    private static readonly System.Reflection.FieldInfo CardList=AccessTools.Field(typeof(CardReward),"_cards");
    private static readonly System.Reflection.FieldInfo OptionCount=AccessTools.Field(typeof(CardReward),"<OptionCount>k__BackingField");
    private static readonly System.Reflection.FieldInfo RerollOptions=AccessTools.Field(typeof(CardReward),"<RerollOptions>k__BackingField");
    internal static List<CardCreationResult> Cards(CardReward reward)=>(List<CardCreationResult>)CardList.GetValue(reward)!;
    private static int Tier(CovenantTerminal terminal,Bond bond)=>BondCatalog.Get(bond).Tier(terminal.Counts(deck:true).GetValueOrDefault(bond));
    internal static bool Eligible(CardReward reward)=>CovenantTerminal.For(reward.Player)!=null
        && reward.Player.RunState.CurrentRoom is CombatRoom
        && ((CardCreationOptions)Options.GetValue(reward)!).Source==CardCreationSource.Encounter
        && !((CardCreationOptions)Options.GetValue(reward)!).Flags.HasFlag(CardCreationFlags.NoCardModelModifications);

    internal static void Install(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(CardRewardAlternative),nameof(CardRewardAlternative.Generate)),prefix:new HarmonyMethod(typeof(CovenantRewards),nameof(Alternatives)));
        h.Patch(AccessTools.PropertyGetter(typeof(CardRewardAlternative),nameof(CardRewardAlternative.Title)),prefix:new HarmonyMethod(typeof(CovenantRewards),nameof(Title)));
        h.Patch(AccessTools.Method(typeof(CardReward),"OnSelect"),prefix:new HarmonyMethod(typeof(CovenantRewards),nameof(Selecting)),postfix:new HarmonyMethod(typeof(CovenantRewards),nameof(Selected)));
    }
    private static State Get(CardReward reward)=>States.GetValue(reward,_=>new());
    private static bool Title(CardRewardAlternative __instance,ref LocString __result)
    {
        if(!Titles.TryGetValue(__instance,out var title))return true;__result=title;return false;
    }
    private static CardRewardAlternative Named(string id,string text,Func<Task> action,PostAlternateCardRewardAction after)
    {
        var alternative=new CardRewardAlternative(id,action,after);Titles.Add(alternative,Presentation.Literal(text));
        // Leave Escape/Back as the native skip action and avoid sharing the sacrifice shortcut.
        AccessTools.Field(typeof(CardRewardAlternative),"<Hotkeys>k__BackingField").SetValue(alternative,new[]{id=="REROLL"?"mega_view_deck_and_tab_left":"mega_view_draw_pile"});
        return alternative;
    }
    private static bool Alternatives(CardReward cardReward,ref IReadOnlyList<CardRewardAlternative> __result)
    {
        if(!Eligible(cardReward))return true;
        var terminal=CovenantTerminal.For(cardReward.Player)!;var state=Get(cardReward);
        var profit=Tier(terminal,Bond.Profit);var foresight=Tier(terminal,Bond.Foresight);
        if(profit==0&&foresight==0)return true;
        // Preserve native model hooks. Reroll stays last so removing it cannot change other alternatives' indices
        // in CardReward.OnSelect, which retains the original list throughout the selection loop.
        var options=new List<CardRewardAlternative>();
        if(cardReward.CanSkip)options.Add(new("Skip",PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward));
        Hook.ModifyCardRewardAlternatives(cardReward.Player.RunState,cardReward.Player,cardReward,options);
        for(var i=0;i<options.Count;i++)
        {
            var original=options[i];
            if(original.AfterSelected==PostAlternateCardRewardAction.EndSelectionAndCompleteReward)
                options[i]=new(original.OptionId,async()=>{state.AlternateCompleted=true;await original.OnSelect();},original.AfterSelected);
        }
        if(profit>0)
        {
            var scale=terminal.Scale(Bond.Profit);var gold=10+scale.A+(profit>=2?5+scale.A/2:0);
            var text=$"兑换为{gold}金币。";
            options.Add(Named("COVENANT_EXCHANGE",text,()=>Exchange(cardReward,terminal,profit,gold),PostAlternateCardRewardAction.EndSelectionAndCompleteReward));
        }
        if(cardReward.CanReroll||foresight>0&&!state.Rerolled)
        {
            options.Add(Named("REROLL","重新抽取候选卡牌。",()=>Reroll(cardReward,terminal,foresight),PostAlternateCardRewardAction.DoNothing));
        }
        __result=options;return false;
    }
    private static async Task<CardModel?> Choose(CardReward reward,string text)
    {
        var cards=reward.Cards.ToArray();if(cards.Length==0)return null;
        var selection=await CardSelectCmd.FromSimpleGrid(new BlockingPlayerChoiceContext(),cards,reward.Player,
            CovenantSelection.Single(Presentation.Literal(text),optional:true));
        return selection.FirstOrDefault();
    }
    private static async Task<bool> Obtain(CardReward reward,CardModel card,bool recordChoice)
    {
        var result=await CardPileCmd.Add(card,PileType.Deck);
        if(!result.success)return false;
        Cards(reward).RemoveAll(c=>c.Card==card);
        if(recordChoice)reward.Player.RunState.CurrentMapPointHistoryEntry?.GetEntry(reward.Player.NetId).CardChoices.Add(new(result.cardAdded,wasPicked:true));
        return true;
    }
    private static async Task Exchange(CardReward reward,CovenantTerminal terminal,int tier,int gold)
    {
        var state=Get(reward);if(state.Exchanged)return;
        state.Exchanged=true;state.AlternateCompleted=true;
        await PlayerCmd.GainGold(gold,reward.Player);
        terminal.GrantLayers(Bond.Profit,8);
    }
    private static Task Reroll(CardReward reward,CovenantTerminal terminal,int tier)
    {
        // Consume the native charge first. Its refresh then still exposes the
        // independent covenant charge, without restoring flags after the UI refresh.
        if(reward.CanReroll){reward.Reroll();return Task.CompletedTask;}
        var state=Get(reward);if(tier<=0||state.Rerolled)return Task.CompletedTask;
        state.Rerolled=true;
        var original=(CardCreationOptions)RerollOptions.GetValue(reward)!;
        var available=original.GetPossibleCards(reward.Player).Where(c=>c.IsAllowedInRun(reward.Player.RunState)).DistinctBy(c=>c.Id).Count();
        var count=(int)OptionCount.GetValue(reward)!;
        var extra=tier>=2?Math.Min(Math.Max(0,available-count),1+terminal.Scale(Bond.Foresight).A/4):0;
        var options=new CardCreationOptions(original.CardPools,original.Source,original.RarityOdds,original.CardPoolFilter)
            .WithFlags(original.Flags).WithRngOverride(terminal.RandomFor(CovenantRandom.RewardReroll));
        var odds=reward.Player.PlayerOdds.CardRarity;
        var previousOdds=odds.CurrentValue;
        // Both backing fields are used by native Populate. Limit the overrides to this synchronous reroll.
        try
        {
            OptionCount.SetValue(reward,count+extra);
            RerollOptions.SetValue(reward,options);
            reward.Reroll();
        }
        finally
        {
            odds.OverrideCurrentValue(previousOdds);
            RerollOptions.SetValue(reward,original);
            OptionCount.SetValue(reward,count);
        }
        if(tier>0)terminal.GrantLayers(Bond.Foresight,8);
        return Task.CompletedTask;
    }
    private static void Selecting(CardReward __instance,ref CardModel[]? __state)
    {
        if(!Eligible(__instance))return;
        var state=Get(__instance);state.AlternateCompleted=false;state.NormalClaim=false;__state=__instance.Player.Deck.Cards.ToArray();
    }
    private static void Selected(CardReward __instance,CardModel[]? __state,ref Task<bool> __result)
    {
        if(__state!=null)__result=FinishSelection(__instance,__state,__result);
    }
    private static async Task<bool> FinishSelection(CardReward reward,CardModel[] before,Task<bool> original)
    {
        var success=await original;var state=Get(reward);
        state.NormalClaim=success&&!state.AlternateCompleted&&reward.Player.Deck.Cards.Except(before).Any();return success;
    }
    internal static async Task AfterTaken(CovenantTerminal terminal,CardReward reward)
    {
        if(!Eligible(reward)||!States.TryGetValue(reward,out var state)||!state.NormalClaim||state.PurchaseOffered||Tier(terminal,Bond.Foresight)==0||terminal.Scale(Bond.Foresight).Layers<BondCatalog.ForesightPurchaseLayers)return;
        state.PurchaseOffered=true;var price=Math.Max(10,40-terminal.Scale(Bond.Foresight).A);
        if(reward.Player.Gold<price)return;
        var card=await Choose(reward,$"你可以支付{price}金币，购买1张剩余的候选卡牌。");
        if(card!=null&&reward.Player.Gold>=price&&await Obtain(reward,card,true))
        {
            var history=reward.Player.RunState.CurrentMapPointHistoryEntry?.GetEntry(reward.Player.NetId).CardChoices;
            var declined=history?.FindLastIndex(c=>!c.wasPicked&&c.Card.Id==card.Id)??-1;
            if(declined>=0)history!.RemoveAt(declined);
            await PlayerCmd.LoseGold(price,reward.Player,GoldLossType.Spent);
        }
    }
}

public sealed partial class CovenantTerminal
{
    public override Task AfterRewardTaken(Player player,Reward reward)=>player==Owner&&reward is CardReward card?CovenantRewards.AfterTaken(this,card):Task.CompletedTask;
}
