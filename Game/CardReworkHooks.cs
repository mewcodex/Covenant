using Covenant.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace Covenant.Game;

internal static class CardReworkHooks
{
    private static readonly AsyncLocal<PowerModel?> PreparedPower=new();
    private static readonly AsyncLocal<CardPlay?> DamagePlay=new();

    internal static void Install(Harmony harmony)
    {
        HarmonyMethod Patch(string name)=>new(typeof(CardReworkHooks),name);
        harmony.Patch(AccessTools.PropertyGetter(typeof(Clash),"IsPlayable"),postfix:Patch(nameof(ClashPlayable)));
        harmony.Patch(AccessTools.Method(typeof(NoxiousFumesPower),nameof(NoxiousFumesPower.AfterSideTurnStart)),prefix:Patch(nameof(BeforeFumes)));
        harmony.Patch(AccessTools.Method(typeof(EcstasyPower),nameof(EcstasyPower.AfterPlayerTurnStart)),prefix:Patch(nameof(BeforeEcstasy)));
        harmony.Patch(AccessTools.Method(typeof(CardFactory),nameof(CardFactory.GetDistinctForCombat),
            [typeof(Player),typeof(IEnumerable<CardModel>),typeof(int),typeof(Rng)]),prefix:Patch(nameof(FilterGeneratedRare)));
        harmony.Patch(AccessTools.Method(typeof(CreatureCmd),nameof(CreatureCmd.Damage),
            [typeof(PlayerChoiceContext),typeof(IEnumerable<DamageTargetData>),typeof(decimal),typeof(ValueProp),typeof(Creature),typeof(CardModel),typeof(CardPlay),typeof(ModifyDamageHookType)]),
            prefix:Patch(nameof(BeforeDamage)),postfix:Patch(nameof(AfterDamage)),finalizer:Patch(nameof(RestoreDamage)));
        harmony.Patch(AccessTools.Method(typeof(Creature),"DamageBlockInternal"),prefix:Patch(nameof(PierceBlock)));
    }

    private static void ClashPlayable(Clash __instance,ref bool __result)
    {
        if(__instance.IsMutable&&CovenantTerminal.For(__instance.Owner)!=null&&CovenantTerminal.HasRework(__instance))__result=true;
    }

    private static bool BeforeFumes(NoxiousFumesPower __instance,CombatSide side,IReadOnlyList<Creature> participants,ICombatState combatState,ref Task __result)
    {
        if(PreparedPower.Value==__instance||side!=CombatSide.Player||!participants.Contains(__instance.Owner)||
            CovenantTerminal.ReworkSource<NoxiousFumes>(__instance) is not {} source||CovenantTerminal.For(source.Owner) is not {} terminal)return true;
        __result=RunPrepared(__instance,async ()=>{
            var targets=combatState.HittableEnemies.Where(enemy=>enemy.GetPowerAmount<PoisonPower>()>0)
                .OrderByDescending(enemy=>enemy.GetPowerAmount<PoisonPower>()).Take(terminal.ReworkAmount(source)).ToArray();
            foreach(var target in targets)
                if(target.IsAlive&&target.GetPower<PoisonPower>() is {} poison)
                {
                    await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(),target,poison.Amount,ValueProp.Unpowered|ValueProp.Unblockable,null,null,null);
                    if(target.IsAlive&&target.Powers.Contains(poison))await PowerCmd.Decrement(poison);
                }
        },()=>CombatManager.Instance.IsOverOrEnding?Task.CompletedTask:__instance.AfterSideTurnStart(side,participants,combatState));
        return false;
    }

    private static bool BeforeEcstasy(EcstasyPower __instance,PlayerChoiceContext choiceContext,Player player,ref Task __result)
    {
        if(PreparedPower.Value==__instance||__instance.Owner.Player!=player||CovenantTerminal.ReworkSource<Ecstasy>(__instance) is not {} source||CovenantTerminal.For(player) is not {} terminal)return true;
        __result=RunPrepared(__instance,async ()=>{
            var state=player.PlayerCombatState!;
            if(state.Hand.Cards.Count>=CardPile.MaxCardsInHand||!source.IsUpgraded&&state.Hand.Cards.Any(card=>card is Sadness))return;
            var pool=state.DiscardPile.Cards.OfType<Sadness>().ToArray();
            if(pool.Length==0)pool=state.ExhaustPile.Cards.OfType<Sadness>().ToArray();
            var selected=terminal.RandomFor(CovenantRandom.CombatCard).NextItem(pool);
            if(selected!=null)await CardPileCmd.Add(selected,PileType.Hand);
        },()=>__instance.AfterPlayerTurnStart(choiceContext,player));
        return false;
    }

    private static async Task RunPrepared(PowerModel power,Func<Task> prepare,Func<Task> original)
    {
        await prepare();
        var previous=PreparedPower.Value;PreparedPower.Value=power;
        try{await original();}
        finally{PreparedPower.Value=previous;}
    }

    private static void FilterGeneratedRare(Player player,ref IEnumerable<CardModel> cards,ref Rng rng)
    {
        if(RevisionTrace.Current is not {Suppressed:false,Model:CallOfTheVoidPower power}||
            CovenantTerminal.ReworkSource<CallOfTheVoid>(power) is not {} source||player!=source.Owner||CovenantTerminal.For(player) is not {} terminal)return;
        if(!terminal.ReworkRoll(source))return;
        var available=cards.ToArray();
        var rare=CardFactory.FilterForCombat(available)
            .Where(card=>card.Rarity==CardRarity.Rare&&card.IsAllowedInRun(player.RunState)).ToArray();
        if(rare.Length==0){cards=available;return;}
        cards=rare;
        rng=terminal.RandomFor(CovenantRandom.CombatCard);
    }

    private sealed record DamageScope(CardPlay? Previous,RevisionFrame? Overflow);

    private static bool BeforeDamage(PlayerChoiceContext choiceContext,IEnumerable<DamageTargetData>? targetData,decimal amount,ValueProp props,
        Creature? dealer,CardModel? cardSource,CardPlay? cardPlay,ModifyDamageHookType modifyDamageHookType,out DamageScope __state,ref Task<IEnumerable<DamageResult>> __result)
    {
        var previous=DamagePlay.Value;
        DamagePlay.Value=props.IsPoweredAttack()?cardPlay:null;
        var terminal=cardSource?.IsMutable==true?CovenantTerminal.For(cardSource.Owner):null;
        __state=new(previous,null);
        if(!props.IsPoweredAttack()||cardSource is not SovereignBlade||terminal==null||targetData==null)return true;
        if(cardPlay is {PlayIndex:>0}&&terminal.LiveRework<SwordSage>() is {} sage)
        {
            __result=ReplayedHpLoss(choiceContext,targetData.ToArray(),amount,props,dealer,cardSource,cardPlay,modifyDamageHookType,terminal.ReworkAmount(sage));
            return false;
        }
        if(terminal.LiveRework<SeekingEdge>()!=null)__state=__state with{Overflow=RevisionTrace.FindPlay(cardSource)};
        return true;
    }

    private static async Task<IEnumerable<DamageResult>> ReplayedHpLoss(PlayerChoiceContext context,DamageTargetData[] targets,decimal amount,ValueProp props,
        Creature? dealer,CardModel card,CardPlay play,ModifyDamageHookType hookType,int percent)
    {
        var results=new List<DamageResult>();
        foreach(var target in targets.Select(entry=>entry.target).Where(target=>target.IsAlive))
        {
            var damage=Hook.ModifyDamage(card.Owner.RunState,target.CombatState,target,dealer,amount,props,card,play,hookType,CardPreviewMode.None,out _);
            results.AddRange(await CreatureCmd.Damage(context,[new DamageTargetData(target)],Math.Floor(damage*percent/100m),
                ValueProp.Unpowered|ValueProp.Unblockable,dealer,card,play,ModifyDamageHookType.Cap));
        }
        return results;
    }

    private static void AfterDamage(DamageScope __state,ref Task<IEnumerable<DamageResult>> __result)
    {
        if(__state.Overflow is {} frame)__result=CaptureGroupHit(frame,__result);
        DamagePlay.Value=__state.Previous;
    }
    private static void RestoreDamage(DamageScope? __state)=>DamagePlay.Value=__state?.Previous;
    private static async Task<IEnumerable<DamageResult>> CaptureGroupHit(RevisionFrame frame,Task<IEnumerable<DamageResult>> original)
    {
        var results=(await original).ToArray();
        frame.Add("rework_overkill",results.Where(result=>result.Receiver.IsEnemy).Sum(result=>result.OverkillDamage));
        return results;
    }
    private static void PierceBlock(Creature __instance,ref decimal amount,ValueProp props)
    {
        if(amount<=0||props.HasFlag(ValueProp.Unblockable)||DamagePlay.Value is not {} play)return;
        if(CovenantTerminal.For(play.Player) is not {} terminal)return;
        var blockable=Math.Min(amount,__instance.Block);
        var penetration=terminal.ReworkPoisonPenetration(play,__instance,blockable);
        if(penetration>0)amount=blockable-penetration;
    }
}
