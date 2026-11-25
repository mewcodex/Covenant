using Covenant.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private int _ritualCombatTier;

    private void RememberRitualTier(int count)
    {
        if(TraitDisplay.InCombat(Owner))_ritualCombatTier=Math.Max(_ritualCombatTier,BondCatalog.Get(Bond.Ritual).Tier(count));
    }

    public override async Task BeforeHandDraw(Player player,PlayerChoiceContext choiceContext,ICombatState combatState)
    {
        if(player!=Owner||player.PlayerCombatState!.TurnNumber!=1)return;
        await SelectChannelOpeningCard(choiceContext);
        if(Scale(Bond.Ritual).Layers<BondCatalog.RitualDrawLayers||
            !Active(Bond.Ritual)||!Once("ritual_opening"))return;

        var play=Scale(Bond.Ritual).Layers>=BondCatalog.RitualPlayLayers;
        var card=await Select(choiceContext,player.PlayerCombatState.DrawPile.Cards.Where(card=>card.Type==CardType.Power),
            play?"选择1张能力牌，将其打出。":"选择1张能力牌，将其放入手牌。",optional:false);
        if(card==null)return;
        if(play)await PlayForCovenant(choiceContext,card);
        else await CardPileCmd.Add(card,PileType.Hand);
    }

    private async Task GenerateRitualPowers()
    {
        if(!Advanced(Bond.Ritual)||Owner.Creature.CombatState is not {} combat||CombatManager.Instance.IsOverOrEnding)return;
        var scale=Scale(Bond.Ritual);
        var rng=RandomFor(CovenantRandom.CombatRoll);
        var ordinary=RollPercent(rng,BondCatalog.RitualPowerChance.At(scale));
        var rare=RollPercent(rng,BondCatalog.RitualRarePowerChance.At(scale));
        if(!ordinary&&!rare)return;

        var candidates=ModelDb.AllCards.Where(card=>card.Type==CardType.Power&&card.CanBeGeneratedInCombat&&
            card.Keywords.Contains(CardKeyword.Ethereal)&&card.IsAllowedInRun(Owner.RunState)).ToArray();
        async Task AddPower(IEnumerable<CardModel> pool)
        {
            if(RandomFor(CovenantRandom.CombatCard).NextItem(pool) is not {} choice)return;
            var card=combat.CreateCard(choice,Owner);
            card.EnergyCost.AddThisCombat(-1,reduceOnly:true);
            var result=await CardPileCmd.AddGeneratedCardToCombat(card,PileType.Hand,Owner);
            if(result.success&&result.cardAdded!=card)result.cardAdded.EnergyCost.AddThisCombat(-1,reduceOnly:true);
        }
        if(ordinary)await AddPower(candidates);
        if(rare)await AddPower(candidates.Where(card=>card.Rarity==CardRarity.Rare));
    }
}
