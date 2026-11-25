using Covenant.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private int _reflowTriggers;

    private int ReflowDamage
    {
        get
        {
            var a = Scale(Bond.Reflow).A;
            return (int)Math.Min(int.MaxValue, 1L + a + (1L + a / 4) * _reflowTriggers);
        }
    }

    internal async Task OnReflowCardEnergy(CardModel source, int energyBefore)
    {
        if (Owner.PlayerCombatState is not { } state || state.Energy <= energyBefore ||
            Owner.Creature.IsDead || CombatManager.Instance.IsOverOrEnding ||
            _turn.GetValueOrDefault("reflow_energy") > 0 || !Active(Bond.Reflow, source.Owner == Owner ? source : null)) return;
        Once("reflow_energy");
        using var suppressed = RevisionTrace.Suppress(this);
        await Draw(Context);
        await Power<StretchPower>(Context, 2 + Scale(Bond.Reflow).A / 2);
    }

    private async Task ResolveReflowDamage()
    {
        var enemies = Owner.Creature.CombatState!.HittableEnemies.ToArray();
        if (enemies.Length == 0) return;
        using var suppressed = RevisionTrace.Suppress(this);
        var damage = ReflowDamage;
        if (Scale(Bond.Reflow).Layers >= 250)
        {
            await Burst(Context, damage, enemies);
            if (enemies.Length == 1)
                await Burst(Context, damage / 2, enemies);
        }
        else
        {
            await Burst(Context, damage, [RandomFor(CovenantRandom.ReflowTarget).NextItem(enemies)!]);
        }
        _reflowTriggers++;
    }

    private async Task ResolveReflowEnd(PlayerChoiceContext context)
    {
        if (!CombatManager.Instance.IsOverOrEnding && !Owner.Creature.IsDead &&
            Scale(Bond.Reflow).Layers >= 150 && Active(Bond.Reflow) && Owner.PlayerCombatState is { } state)
        {
            var affordable = state.DiscardPile.Cards.Where(card =>
                Math.Max(0, card.EnergyCost.GetWithModifiers(CostModifiers.All)) <= state.Energy && card.CanPlay());
            if (RandomFor(CovenantRandom.ReflowCard).NextItem(affordable) is { } card)
                await PlayForCovenant(context, card);
        }
        _reflowTriggers = 0;
    }
}
