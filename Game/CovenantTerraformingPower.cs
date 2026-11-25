using Covenant.Core;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

public sealed class CovenantTerraformingPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    public override LocString Title => Presentation.Literal("地形改造");
    public override LocString Description => Presentation.Literal(BondDescription.Format(
        $"你下次打出攻击牌时，使该牌所属的已激活盟约各增加{Amount}层。", this));

    public override async Task BeforeCardPlayed(CardPlay play)
    {
        if (play.Player.Creature != Owner || play.Card.Type != CardType.Attack || play.PlayIndex != 0) return;
        if (CovenantTerminal.For(play.Player) is { } terminal)
        {
            var counts = CardMembership.Counts(play.Player, resolving:play.Card);
            foreach (var bond in terminal.ActiveLayeredBonds(play.Card, counts)) terminal.GrantLayers(bond, Amount);
        }
        await PowerCmd.Remove(this);
        Presentation.RefreshCardTraits();
    }
}
