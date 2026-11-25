using Covenant.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private HashSet<CardModel> _encounterToTop=new(ReferenceEqualityComparer.Instance);
    private HashSet<CardModel> _encounterDiscount=new(ReferenceEqualityComparer.Instance);
    private readonly record struct AncientFreePlay(int Turn);
    private Dictionary<CardModel,AncientFreePlay> _ancientFree=new(ReferenceEqualityComparer.Instance);
    private Dictionary<CardModel,IReadOnlyList<Bond>> _ancientPlayBonds=new(ReferenceEqualityComparer.Instance);
    private void ResetSpecials(){_encounterToTop=new(ReferenceEqualityComparer.Instance);_encounterDiscount=new(ReferenceEqualityComparer.Instance);_ancientFree=new(ReferenceEqualityComparer.Instance);_ancientPlayBonds=new(ReferenceEqualityComparer.Instance);}

    internal static void InstallAncientCosts(Harmony harmony)
        =>harmony.Patch(AccessTools.Method(typeof(CardEnergyCost),nameof(CardEnergyCost.GetAmountToSpend)),postfix:new HarmonyMethod(typeof(CovenantTerminal),nameof(AncientXCost)));

    private static void AncientXCost(CardEnergyCost __instance,CardModel ____card,ref int __result)
    {
        if(__instance.CostsX&&____card.IsMutable&&CovenantTerminal.For(____card.Owner) is {} terminal&&
            (terminal.AncientFreeThisTurn(____card)||terminal.ReworkFreeX(____card)||terminal.CreationFreeThisTurn(____card)))__result=0;
    }

    private bool AncientFreeThisTurn(CardModel card)=>_ancientFree.TryGetValue(card,out var play)&&play.Turn==Owner.PlayerCombatState?.TurnNumber;

    private async Task ResolveAncientPlay(CardModel card)
    {
        if(card.Rarity!=CardRarity.Ancient||!Active(Bond.Ancient,card))return;
        var bonds=_ancientPlayBonds.TryGetValue(card,out var startingBonds)?startingBonds:CardMembership.Of(card);
        foreach(var bond in bonds.Where(bond=>BondCatalog.Get(bond).UsesLayers&&Active(bond,card)))GrantLayers(bond,4);
        if(!Advanced(Bond.Ancient,card)||MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsOverOrEnding||Owner.Creature.CombatState is not {} combat)return;
        var pool=card.VisualCardPool.IsColorless?ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>():card.VisualCardPool;
        var canonical=RandomFor(CovenantRandom.CombatCard).NextItem(pool.GetUnlockedCards(Owner.UnlockState,Owner.RunState.CardMultiplayerConstraint)
            .Where(candidate=>candidate.Rarity==CardRarity.Rare&&candidate.IsAllowedInRun(Owner.RunState)));
        if(canonical==null)return;
        var generated=combat.CreateCard(canonical,Owner);
        generated.AddKeyword(CardKeyword.Ethereal);generated.AddKeyword(CardKeyword.Exhaust);
        generated.SetToFreeThisTurn();
        if(generated.EnergyCost.CostsX)_ancientFree[generated]=new(Owner.PlayerCombatState!.TurnNumber);
        await CardPileCmd.AddGeneratedCardToCombat(generated,PileType.Hand,Owner);
    }

    private async Task StartSpecialTurn(PlayerChoiceContext c)
    {
        if(!Advanced(Bond.Retain))return;
        var card=await Select(c,_retained.Where(x=>x.Pile?.Type==PileType.Hand),$"选择1张上回合保留的手牌，使其能量消耗减少{{energyPrefix:energyIcons(1)}}直到打出，并使其下次打出时造成的伤害和获得的格挡增加{2+Scale(Bond.Retain).A}点。",optional:false);
        if(card==null)return;
        card.EnergyCost.AddUntilPlayed(-1);QueueCardBonus(Bond.Retain,card,2+Scale(Bond.Retain).A);
    }

    private async Task ResolveSpecialPlay(PlayerChoiceContext c,CardPlay play)
    {
        var card=play.Card;
        if(CardMembership.IsEncounter(card)&&Active(Bond.Encounter,card)&&Once("once:encounter"))
        {
            await Retrieve(c,Owner.PlayerCombatState!.DrawPile.Cards.Where(x=>x!=card&&CardMembership.IsEncounter(x)));
            if(Advanced(Bond.Encounter,card)&&play.ResultPile==PileType.Discard)
            {
                var selected=await Select(c,[card],"你可以将此牌改为放到抽牌堆顶部，使其下次打出时的能量消耗减少{energyPrefix:energyIcons(1)}。");
                if(selected!=null)_encounterToTop.Add(card);
            }
        }
    }

    public override async Task AfterCardChangedPilesLate(CardModel card,PileType oldPileType,AbstractModel? clonedBy)
    {
        if(card.Owner!=Owner)return;
        ActivateTopTraits(card,oldPileType);
        if(card.Pile?.Type==PileType.Discard&&_encounterToTop.Remove(card))
        {
            // Apply after the native result-pile move so the discount survives the current play's cleanup.
            _encounterDiscount.Add(card);
            await CardPileCmd.Add(card,PileType.Draw,CardPilePosition.Top);
        }
        await RefreshStatusStrength(Context);
        RefreshBattleStatuses();
    }
}
