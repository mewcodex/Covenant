using Covenant.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private int _storedEnergy;
    private CardModel? _channelOpeningCard;
    private Dictionary<CardModel,int> _channelStarPayments=new(ReferenceEqualityComparer.Instance);

    public override int ModifyXValue(CardModel card,int originalValue)
    {
        if(card.Owner!=Owner||!CardMembership.IsXCost(card)||!Active(Bond.Channel,card))return originalValue;
        var scale=Scale(Bond.Channel);
        var costPerBonus=Math.Max(2,4-scale.A/5);
        var paid=card.HasStarCostX?_channelStarPayments.GetValueOrDefault(card):_playEnergy.GetValueOrDefault(card);
        return originalValue+1+scale.A/10+paid/costPerBonus;
    }

    private void StoreChannelEnergy()
    {
        if(Owner.Creature.IsDead||CombatManager.Instance.IsOverOrEnding||!Advanced(Bond.Channel)||
            Owner.PlayerCombatState is not {Energy:>0} state)return;
        var energy=state.Energy;
        _storedEnergy=Math.Min(999_999_999,_storedEnergy+energy);
        state.LoseEnergy(energy);
    }

    internal int ReleaseChannelEnergy(CardModel card)
    {
        if(_storedEnergy==0||!CardMembership.IsXCost(card)||Owner.PlayerCombatState is not {} state||
            BondCatalog.Get(Bond.Channel).Tier(CardMembership.Counts(Owner,resolving:card).GetValueOrDefault(Bond.Channel))<2)return 0;
        var energyBefore=state.Energy;
        var stored=_storedEnergy;
        _storedEnergy=0;
        state.GainEnergy(stored);
        return state.Energy-energyBefore;
    }

    private async Task SelectChannelOpeningCard(PlayerChoiceContext context)
    {
        if(Scale(Bond.Channel).Layers<250||!Active(Bond.Channel)||!Once("channel_opening"))return;
        _channelOpeningCard=await Select(context,Owner.PlayerCombatState!.DrawPile.Cards.Where(CardMembership.IsXCost),
            "选择1张X费牌，使其获得保留。每次抽到这张牌时，获得{energyPrefix:energyIcons(1)}。",optional:false);
        if(_channelOpeningCard is {} card&&!card.Keywords.Contains(CardKeyword.Retain))card.AddKeyword(CardKeyword.Retain);
    }
}
