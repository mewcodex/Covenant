using Covenant.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Covenant.Game;

public sealed class CovenantStatusStrengthPower:TemporaryStrengthPower
{
    public override AbstractModel OriginModel=>ModelDb.Relic<CovenantTerminal>();
    public override LocString Title=>Presentation.Literal(BondCatalog.Get(Bond.Status).Name);

    public override Task AfterRemoved(Creature oldOwner)
    {
        CovenantTerminal.For(oldOwner.Player)?.RememberRemovedStatusStrength(Amount);
        return base.AfterRemoved(oldOwner);
    }

    public override Task AfterSideTurnEnd(PlayerChoiceContext context,CombatSide side,IEnumerable<Creature> participants)
    {
        var ending=participants.ToArray();
        if(ending.Contains(Owner))CovenantTerminal.For(Owner.Player)?.CloseStatusStrengthTurn();
        return base.AfterSideTurnEnd(context,side,ending);
    }
}
