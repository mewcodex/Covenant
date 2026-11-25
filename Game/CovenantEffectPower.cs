using Covenant.Core;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

public sealed class CovenantEffectPower : PowerModel
{
    internal Bond IconBond { get; set; }
    internal string EffectName { get; set; } = "";
    internal string EffectText { get; set; } = "";
    private int? _counter;
    internal void SetCounter(int? value)
    {
        if(_counter==value)return;
        _counter=value;
        InvokeDisplayAmountChanged();
    }

    public override PowerType Type => PowerType.None;
    public override PowerStackType StackType => _counter.HasValue?PowerStackType.Counter:PowerStackType.Single;
    public override int DisplayAmount => _counter??base.DisplayAmount;
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    public override bool ShouldReceiveCombatHooks => false;
    public override bool ShouldPlayVfx => false;
    public override LocString Title => Presentation.Literal(EffectName);
    public override LocString Description => Presentation.Literal(BondDescription.Format(EffectText,
        (AbstractModel?)CovenantTerminal.For(IsMutable?Owner.Player:null)??this));
}
