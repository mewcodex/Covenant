using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

internal static partial class RevisionTrace
{
    internal static IEnumerable<CardModel> TraitStatusSources(Player player)
    {
        foreach(var power in LivePowers(player))
            foreach(var card in Sources(power).Where(card=>card.Owner==player))yield return card;
        if(player.PlayerCombatState is not {} state)yield break;
        if(ResourceBooks.TryGetValue(state,out var book))
            foreach(var credit in book.Energy.Concat(book.Stars).Where(credit=>credit.Units>0))
                if(credit.Source is {} source)yield return source;
        foreach(var orb in state.OrbQueue.Orbs)
            if(OrbSources.TryGetValue(orb,out var origins))
                foreach(var (source,_) in origins.Cards)yield return source;
    }
}
