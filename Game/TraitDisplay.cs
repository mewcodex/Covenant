using Covenant.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace Covenant.Game;

internal static class TraitDisplay
{
    internal static bool InCombat(Player? player)=>player?.Creature.CombatState!=null&&(CombatManager.Instance.IsInProgress||CombatManager.Instance.IsStarting);
    internal static bool Own(string trigger)=>trigger is "play" or "draw" or "self_exhaust" or "own_play" or "own_draw" or "own_exhaust" or "own_discard" or "own_acquire" or "own_remove";
    internal static bool Direct(TraitEffect effect)=>Own(effect.Trigger)||effect.Kind is "self_damage" or "self_block";
    internal static bool UsesAuthored(CardTrait trait)=>trait.Effects is {Length:>0};
    internal static Bond MainBond(CardTrait trait)=>trait.Revision?.Base.Rules.FirstOrDefault()?.Bond??trait.Effects?.FirstOrDefault()?.Bond??trait.Bond;
    internal static Bond MainBond(CardModel card)=>CardMembership.TraitRevision(card)?.Base.Rules.FirstOrDefault()?.Bond
        ??MainBond(CardMembership.Definition(card)!.Trait);
    internal static Bond? SecondTraitBond(CardModel card)=>card switch
    {
        MeteorShower or Vicious=>Bond.Breach,
        MadScience=>CardMembership.TraitRevision(card)?.Base.Rules.ElementAtOrDefault(1)?.Bond,
        _=>null
    };
    internal static string TraitTitle(CardModel card,Bond first)=>SecondTraitBond(card) is {} second
        ?$"{BondCatalog.Get(first).Name} · {BondCatalog.Get(second).Name}":BondCatalog.Get(first).Name;
    internal static bool HasDirect(CardModel card)=>card is MegaCrit.Sts2.Core.Models.Cards.BootSequence or MegaCrit.Sts2.Core.Models.Cards.Reanimate or MegaCrit.Sts2.Core.Models.Cards.Bloodletting
        ||CardMembership.Definition(card) is {} d&&(RevisionDescriptions.Program(card) is {} program
        ?program.Rules.Any(RevisionDescriptions.Direct)
        :UsesAuthored(d.Trait)?d.Trait.Effects!.Any(Direct):Own(d.Trait.Trigger));
    internal static bool Active(CardModel card,Func<Player,Dictionary<Bond,int>>? countsFor=null)
    {
        var trait=CardMembership.Definition(card)?.Trait;if(trait==null)return false;
        var terminal=card.IsMutable?CovenantTerminal.For(card.Owner):null;
        bool BondActive(Bond bond)=>terminal!=null&&(countsFor==null?terminal.Tier(bond):
            terminal.TierAtCount(bond,countsFor(terminal.Owner).GetValueOrDefault(bond)))>0;
        if(RevisionDescriptions.Program(card) is {} program)return program.Rules.Any(r=>!r.RequireActive||BondActive(r.Bond));
        return UsesAuthored(trait)?trait.Effects!.Any(e=>!e.RequireActive||BondActive(e.Bond??trait.Bond)):BondActive(trait.Bond);
    }
}
