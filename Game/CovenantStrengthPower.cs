using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Covenant.Game;

public sealed class CovenantStrengthPower:TemporaryStrengthPower
{
    public override AbstractModel OriginModel=>ModelDb.Relic<CovenantTerminal>();
}
