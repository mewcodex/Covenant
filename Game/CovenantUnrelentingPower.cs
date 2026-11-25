using Covenant.Core;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Covenant.Game;

public sealed class CovenantUnrelentingPower:PowerModel
{
    [SavedProperty] public int Multiplier {get;set;}=2;
    public override PowerType Type=>PowerType.Buff;
    public override PowerStackType StackType=>PowerStackType.Single;
    public override PowerInstanceType InstanceType=>PowerInstanceType.Instanced;
    public override LocString Title=>Presentation.Literal("无情猛攻");
    internal static string Effect(int multiplier)=>$"你打出下一张攻击牌时，使已激活的【重装】增加等同于该牌原始费用{multiplier}倍的层数。";
    public override LocString Description=>Presentation.Literal(BondDescription.Format(Effect(Multiplier),this));

    public override async Task BeforeCardPlayed(CardPlay play)
    {
        if(play.Card.Owner.Creature!=Owner||play.Card.Type!=CardType.Attack||play.PlayIndex!=0)return;
        if(CovenantTerminal.For(Owner.Player) is {} terminal)
        {
            var count=CardMembership.Counts(Owner.Player!,resolving:play.Card).GetValueOrDefault(Bond.Heavy);
            if(BondCatalog.Get(Bond.Heavy).Tier(count)>0)
            {
                var cost=Math.Max(0,play.Card.EnergyCost.GetWithModifiers(CostModifiers.None));
                terminal.GrantLayers(Bond.Heavy,(int)Math.Min(int.MaxValue,(long)cost*Multiplier));
            }
        }
        await PowerCmd.Remove(this);
        Presentation.RefreshCardTraits();
    }
}
