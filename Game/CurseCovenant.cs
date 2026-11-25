using System.Reflection.Emit;
using Covenant.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace Covenant.Game;

internal static class CurseCovenant
{
    private static readonly Func<Normality,bool> NormalityLimit=AccessTools.MethodDelegate<Func<Normality,bool>>(
        AccessTools.PropertyGetter(typeof(Normality),"ShouldPreventCardPlay"));
    [ThreadStatic] private static (Player Player,CardCreationOptions Options)? _reward;
    [ThreadStatic] private static (Player Player,IEnumerable<CardModel> Blacklist)? _merchant;
    private const int StandardWeight=50;

    internal static int? PlayCost(CardModel card)=>CardMembership.Definition(card)?.CursePlayCost;
    internal static bool IsActive(CardModel card)=>card.IsMutable&&CovenantTerminal.For(card.Owner)!=null&&CardMembership.CurseTier(card.Owner)>0;
    internal static bool CanPlayNormality(Normality card)=>IsActive(card)&&NormalityLimit(card);
    internal static IEnumerable<CardModel> DiceCurses(Player player)=>ModelDb.CardPool<CurseCardPool>()
        .GetUnlockedCards(player.UnlockState,player.RunState.CardMultiplayerConstraint)
        .Where(card=>card.CanBeGeneratedByModifiers).OrderBy(card=>card.Id);

    internal static void Install(Harmony harmony)
    {
        HarmonyMethod Method(string name)=>new(typeof(CurseCovenant),name);
        harmony.Patch(AccessTools.Method(typeof(CardEnergyCost),nameof(CardEnergyCost.GetWithModifiers)),prefix:Method(nameof(ConditionalCost)));
        harmony.Patch(AccessTools.Method(typeof(Normality),nameof(Normality.ShouldPlay)),postfix:Method(nameof(AllowNormality)));
        harmony.Patch(AccessTools.Method(typeof(CardFactory),nameof(CardFactory.CreateForReward),[typeof(Player),typeof(int),typeof(CardCreationOptions)]),
            prefix:Method(nameof(BeginReward)),finalizer:Method(nameof(EndReward)));
        harmony.Patch(AccessTools.Method(typeof(CardFactory),nameof(CardFactory.CreateForReward),[typeof(Player),typeof(IEnumerable<CardModel>),typeof(CardCreationOptions)]),
            transpiler:Method(nameof(PatchRewardPicker)));
        harmony.Patch(AccessTools.Method(typeof(CardFactory),nameof(CardFactory.CreateForMerchant),[typeof(Player),typeof(IEnumerable<CardModel>),typeof(IEnumerable<CardModel>),typeof(CardType)]),
            prefix:Method(nameof(BeginMerchant)),finalizer:Method(nameof(EndMerchant)),transpiler:Method(nameof(PatchMerchantPicker)));
        harmony.Patch(AccessTools.Method(typeof(MerchantCardEntry),"GetCost"),postfix:Method(nameof(MerchantPrice)));
        harmony.Patch(AccessTools.Method(typeof(CardCreationOptions),nameof(CardCreationOptions.GetPossibleCards)),postfix:Method(nameof(AddCandidates)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(CardModel),nameof(CardModel.Rarity)),postfix:Method(nameof(RewardRarity)));
    }

    private static bool ConditionalCost(CardModel ____card,ref int __result)
    {
        if(PlayCost(____card)==null||IsActive(____card))return true;
        __result=-1;
        return false;
    }

    private static void AllowNormality(CardModel card,ref bool __result)
    {
        if(!__result&&(card is Normality normality&&CanPlayNormality(normality)||
            CovenantTerminal.For(card.Owner)?.IsHermitCurse(card)==true))__result=true;
    }

    private static CovenantTerminal? ActiveForAcquisition(Player player)
        =>CovenantTerminal.For(player) is {} terminal&&CardMembership.CurseTier(player,deck:true)>0?terminal:null;

    private static void BeginReward(Player player,CardCreationOptions options,out (Player Player,CardCreationOptions Options)? __state)
    {
        __state=_reward;
        if(IsCharacterReward(options)&&ActiveForAcquisition(player)!=null)
            _reward=(player,options);
        else _reward=null;
    }

    private static bool IsCharacterReward(CardCreationOptions options)
    {
        const CardCreationFlags excluded=CardCreationFlags.NoModifyHooks|CardCreationFlags.NoCardPoolModifications;
        return (options.Flags&excluded)==0&&options.Flags.HasFlag(CardCreationFlags.IsCardReward)&&
            options.CardPools.Any(CovenantTerminal.SixPools.Contains);
    }

    private static void EndReward((Player Player,CardCreationOptions Options)? __state)=>_reward=__state;

    private static void AddCandidates(CardCreationOptions __instance,Player player,ref IEnumerable<CardModel> __result)
    {
        if(_reward is not {} scope||scope.Player!=player||scope.Options!=__instance)return;
        var curses=DiceCurses(player).Append(ModelDb.Card<Greed>());
        if(__instance.CardPoolFilter is {} filter)curses=curses.Where(filter);
        __result=__result.Concat(curses).Distinct();
    }

    private static void RewardRarity(CardModel __instance,ref CardRarity __result)
    {
        if(_reward!=null&&__result==CardRarity.Curse)
        {
            if(__instance is Greed)__result=CardRarity.Uncommon;
            else if(__instance.CanBeGeneratedByModifiers)__result=CardRarity.Common;
        }
    }

    private static IEnumerable<CodeInstruction> PatchRewardPicker(IEnumerable<CodeInstruction> instructions)
        =>ReplacePicker(instructions,nameof(PickReward));

    private static IEnumerable<CodeInstruction> PatchMerchantPicker(IEnumerable<CodeInstruction> instructions)
        =>ReplacePicker(instructions,nameof(PickMerchant));

    private static IEnumerable<CodeInstruction> ReplacePicker(IEnumerable<CodeInstruction> instructions,string method)
    {
        var native=AccessTools.Method(typeof(Rng),nameof(Rng.NextItem)).MakeGenericMethod(typeof(CardModel));
        var replacement=AccessTools.Method(typeof(CurseCovenant),method);
        var found=0;
        foreach(var instruction in instructions)
        {
            if(instruction.Calls(native))
            {
                found++;
                instruction.opcode=OpCodes.Call;
                instruction.operand=replacement;
            }
            yield return instruction;
        }
        if(found!=1)throw new InvalidOperationException($"Expected one native card draw for {method}, found {found}.");
    }

    private static CardModel? PickReward(Rng native,IEnumerable<CardModel> items)
        =>_reward is {} scope&&CovenantTerminal.For(scope.Player) is {} terminal
            ?PickWeighted(native,items.ToArray(),terminal,CovenantRandom.CurseReward):native.NextItem(items);

    private static void BeginMerchant(Player player,IEnumerable<CardModel> blacklist,out (Player Player,IEnumerable<CardModel> Blacklist)? __state)
    {
        __state=_merchant;
        _merchant=ActiveForAcquisition(player)!=null?(player,blacklist):null;
    }

    private static void EndMerchant((Player Player,IEnumerable<CardModel> Blacklist)? __state)=>_merchant=__state;

    private static CardModel? PickMerchant(Rng native,IEnumerable<CardModel> items)
    {
        if(_merchant is not {} scope||CovenantTerminal.For(scope.Player) is not {} terminal)return native.NextItem(items);
        var normal=items.ToArray();
        if(normal.Length==0||!CovenantTerminal.SixPools.Contains(normal[0].Pool))return native.NextItem(normal);
        IEnumerable<CardModel> extra=normal[0].Rarity switch
        {
            CardRarity.Common=>DiceCurses(scope.Player),
            CardRarity.Uncommon=>[ModelDb.Card<Greed>()],
            _=>Array.Empty<CardModel>()
        };
        var candidates=normal.Concat(extra.Where(card=>card.IsAllowedInRun(scope.Player.RunState)).Except(scope.Blacklist)).ToArray();
        return PickWeighted(native,candidates,terminal,CovenantRandom.CurseShop);
    }

    private static void MerchantPrice(CardModel card,ref int __result)
    {
        if(card is Greed&&card.IsMutable&&CovenantTerminal.For(card.Owner)!=null)__result=75;
    }

    // Integer tickets preserve the exact ratio even between multiples of 50 layers.
    internal static int CurseWeight(int layers)=>StandardWeight+Math.Clamp(layers,0,2*StandardWeight);

    private static CardModel? PickWeighted(Rng native,CardModel[] items,CovenantTerminal terminal,CovenantRandom stream)
    {
        var curses=items.Where(card=>card.Type==CardType.Curse).ToArray();
        if(curses.Length==0)return native.NextItem(items);
        var ordinary=items.Where(card=>card.Type!=CardType.Curse).ToArray();
        // Keep the native draw for the ordinary card and use the mod stream for the bonus candidates.
        var standardPick=native.NextItem(ordinary);
        var curseWeight=CurseWeight(terminal.Ledger.Get(Bond.Curse));
        var curseTickets=curses.Length*curseWeight;
        var ticket=terminal.RandomFor(stream).NextInt(curseTickets+ordinary.Length*StandardWeight);
        return ticket<curseTickets?curses[ticket/curseWeight]:standardPick;
    }
}
