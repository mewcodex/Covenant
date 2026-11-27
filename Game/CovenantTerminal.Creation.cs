using System.Runtime.CompilerServices;
using Covenant.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private static readonly ConditionalWeakTable<CardModel,CardModel> CombatCardReplacements=new();
    private HashSet<CardModel> _creationFree=new(ReferenceEqualityComparer.Instance);

    internal void QuoteZeroForPaidPlay(CardModel card)=>_paidCostQuotes[card]=CardMembership.CurrentPlayCost(card);
    internal int CurrentCostAtPlay(CardModel card)=>_playCostQuotes.TryGetValue(card,out var cost)?cost:CardMembership.CurrentPlayCost(card);
    internal bool CreationFreeThisTurn(CardModel card)=>card.IsMutable&&card.Owner==Owner&&_creationFree.Contains(card);
    private static bool IsGeneratedAttack(CardModel card)=>card.Type==CardType.Attack&&CardMembership.IsGenerated(card);
    internal static bool EtherealReturnEligible(CardModel card)=>(card.Type is CardType.Attack or CardType.Skill or CardType.Power)&&
        card.Keywords.Contains(CardKeyword.Ethereal)&&!card.Keywords.Contains(CardKeyword.Unplayable);

    private async Task ReturnRandom(IEnumerable<CardModel> cards)
    {
        if(Owner.PlayerCombatState!.Hand.Cards.Count>=CardPile.MaxCardsInHand)return;
        var selected=RandomFor(CovenantRandom.CombatCard).NextItem(cards);
        if(selected!=null)await CardPileCmd.Add(selected,PileType.Hand);
    }

    internal static void InstallCreationRules(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(CardPileCmd),nameof(CardPileCmd.AddGeneratedCardsToCombat)),
            prefix:new HarmonyMethod(typeof(CovenantTerminal),nameof(ReplaceGeneratedCards)));
        harmony.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.GetStarCostWithModifiers)),
            prefix:new HarmonyMethod(typeof(CovenantTerminal),nameof(FreeGeneratedCardStars)));
        harmony.Patch(AccessTools.Method(typeof(CardCmd),"PreviewInternal"),
            prefix:new HarmonyMethod(typeof(CovenantTerminal),nameof(PreviewCombatReplacement)));
        harmony.Patch(AccessTools.Method(typeof(ForgeCmd),"PreviewSovereignBlade"),
            prefix:new HarmonyMethod(typeof(CovenantTerminal),nameof(HasBladesToPreview)));
        harmony.Patch(AccessTools.Method(typeof(ForgeCmd),nameof(ForgeCmd.Forge)),
            postfix:new HarmonyMethod(typeof(CovenantTerminal),nameof(FilterReplacedBlades)));
    }

    // A Creation replacement can leave native Forge without a Blade to preview or return.
    private static bool HasBladesToPreview(IReadOnlyCollection<SovereignBlade> blades)=>blades.Count>0;
    private static void FilterReplacedBlades(ref Task<IEnumerable<SovereignBlade>> __result,Player player)
    {
        if(For(player)==null)return;
        __result=KeepForgedBlades(__result);
    }

    private static async Task<IEnumerable<SovereignBlade>> KeepForgedBlades(Task<IEnumerable<SovereignBlade>> forge)
        =>(await forge).Where(blade=>blade.Pile?.IsCombatPile==true).ToArray();

    private static void PreviewCombatReplacement(ref CardModel card)
    {
        if(TryGetCombatReplacement(card,out var replacement))card=replacement;
    }

    private static bool FreeGeneratedCardStars(CardModel __instance,ref int __result)
    {
        if(!__instance.IsMutable||For(__instance.Owner)?.CreationFreeThisTurn(__instance)!=true)return true;
        // A negative cost means no star icon. A waived star cost still has an icon.
        __result=__instance.HasStarCostX?0:Math.Min(0,__instance.CurrentStarCost);
        return false;
    }

    private static void ReplaceGeneratedCards(ref IEnumerable<CardModel> cards,Player? creator)
    {
        if(!CombatManager.Instance.IsInProgress||CombatManager.Instance.IsOverOrEnding)return;
        var batch=cards.ToArray();
        cards=batch;
        if(batch.Any(card=>card.Pile!=null))return;

        var terminal=For(creator);
        var chance=terminal?.Advanced(Bond.Creation,terminal._currentCard)==true
            ?BondCatalog.CreationAncientChance.At(terminal.Scale(Bond.Creation)):0;
        CardModel[][]? ancientByColor=null;
        for(var index=0;index<batch.Length;index++)
        {
            var card=batch[index];
            if(IsStatusReplacementCandidate(card))
            {
                // Generation and direct monster inserts share the native Add entry point. Roll once at acquisition.
                var status=ReplaceNewStatus(card);
                ProcessedGenerationAdds.AddOrUpdate(status,GenerationHandled);
                if(status!=card){batch[index]=status;continue;}
            }
            if(chance==0||creator?.Creature.CombatState is not {} combat||card.Owner!=creator||CardMembership.IsMarker(card))continue;
            if(!RollPercent(terminal!.RandomFor(CovenantRandom.CombatRoll),chance))continue;
            var rng=terminal.RandomFor(CovenantRandom.CombatCard);
            ancientByColor??=ModelDb.AllCards.Where(c=>c.Rarity==CardRarity.Ancient&&c.IsAllowedInRun(creator.RunState))
                .GroupBy(c=>c.VisualCardPool.IsColorless?"colorless":c.VisualCardPool.EnergyColorName)
                .Select(group=>group.ToArray()).ToArray();
            var color=rng.NextItem(ancientByColor);
            if(color==null||rng.NextItem(color) is not {} canonical)continue;
            var replacement=combat.CreateCard(canonical,creator);
            replacement.AddKeyword(CardKeyword.Ethereal);
            replacement.AddKeyword(CardKeyword.Exhaust);
            batch[index]=replacement;
            RecordCombatReplacement(card,replacement);
        }
    }

    private static void RecordCombatReplacement(CardModel card,CardModel replacement)
    {
        CombatCardReplacements.AddOrUpdate(card,replacement);
        var owner=card.Owner;
        if(owner?.Creature.CombatState is not {} combat||!combat.ContainsCard(card))return;
        combat.RemoveCard(card);
        card.Owner=owner; // The generating effect may still read its original preview model.
    }

    private void EnchantForgedBlades()
    {
        var pool=new (EnchantmentModel Model,int Amount)[]
        {
            (ModelDb.Enchantment<Spiral>(),1),(ModelDb.Enchantment<Sharp>(),10),
            (ModelDb.Enchantment<SlumberingEssence>(),1),(ModelDb.Enchantment<Instinct>(),1),
            (ModelDb.Enchantment<Inky>(),1),(ModelDb.Enchantment<Adroit>(),10),
            (ModelDb.Enchantment<Momentum>(),15)
        };
        foreach(var sword in Owner.PlayerCombatState!.AllCards.OfType<SovereignBlade>())
        {
            var (model,amount)=pool[RandomFor(CovenantRandom.CombatCard).NextInt(pool.Length)];
            CardCmd.ClearEnchantment(sword);
            if(model is Spiral)
            {
                // The native event limits Spiral to basic cards. The forged Blade uses its normal replay effect.
                var spiral=model.ToMutable();
                sword.EnchantInternal(spiral,amount);
                spiral.ModifyCard();
                sword.FinalizeUpgradeInternal();
            }
            else CardCmd.Enchant(model.ToMutable(),sword,amount);
        }
    }
}
