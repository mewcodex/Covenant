using Covenant.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private async Task ApplySolitudeOpening()
    {
        var amount=Owner.PlayerCombatState!.DrawPile.Cards
            .Select(card=>card.EnergyCost.CostsX?(int?)null:card.EnergyCost.GetWithModifiers(CostModifiers.All))
            .Where(cost=>cost is null or >=0).Distinct().Count()*2;
        if(amount==0)return;
        await Power<VigorPower>(Context,amount);
        await Power<StretchPower>(Context,amount);
        await Power<PlatingPower>(Context,amount);
    }

    internal bool SolitudeCanReplaceExhaustedPlayCard(CardModel card)
        =>!CardMembership.SolitudeSuppressed(Owner)&&CardMembership.CountsForExhaust(card).GetValueOrDefault(Bond.Solitude)>=BondCatalog.Get(Bond.Solitude).Advanced;

    private async Task TransformSolitudeMatches(CardModel source)
    {
        if(CombatManager.Instance.IsOverOrEnding||Owner.Creature.CombatState is not {} combat)return;
        var name=CardMembership.NameKey(source);
        // Native bulk transform records indexes as it removes cards. Visiting the back of each pile first keeps positions stable.
        var matches=CardMembership.Cards(Owner).Where(card=>CardMembership.NameKey(card)==name).Reverse().ToArray();
        if(matches.Length==0)return;
        var pools=SixPools.Append(ModelDb.CardPool<ColorlessCardPool>())
            .Select(pool=>pool.GetUnlockedCards(Owner.UnlockState,Owner.RunState.CardMultiplayerConstraint)
                .Where(card=>card.CanBeGeneratedInCombat&&card.IsAllowedInRun(Owner.RunState)&&CardMembership.NameKey(card)!=name)
                .ToArray())
            .Where(cards=>cards.Length>0).ToArray();
        if(pools.Length==0)return;

        var rng=RandomFor(CovenantRandom.CombatCard);
        var changes=new CardTransformation[matches.Length];
        for(var i=0;i<matches.Length;i++)
        {
            var original=matches[i];
            var pool=pools[rng.NextInt(pools.Length)];
            var replacement=combat.CreateCard(pool[rng.NextInt(pool.Length)],Owner);
            changes[i]=new CardTransformation(original,replacement);
            CombatCardReplacements.AddOrUpdate(original,replacement);
        }
        await CardCmd.Transform(changes,null,CardPreviewStyle.None);
    }

    internal static bool TryGetCombatReplacement(CardModel card,out CardModel replacement)
    {
        replacement=card;
        while(CombatCardReplacements.TryGetValue(replacement,out var next))replacement=next;
        return replacement!=card;
    }
}
