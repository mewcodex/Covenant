using Covenant.Core;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Covenant.Game;

public sealed class CovenantSynthesisPower : PowerModel
{
    [SavedProperty] public int Multiplier { get; set; } = 2;
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;
    public override LocString Title => Presentation.Literal("人工合成");
    public override LocString Description => Presentation.Literal(BondDescription.Format(
        $"你打出下一张能力牌时，使已激活的【仪式】增加层数，数量为该牌原始耗能加1的{Multiplier}倍。", this));

    public override async Task BeforeCardPlayed(CardPlay play)
    {
        if (play.Player.Creature != Owner || play.Card.Type != CardType.Power || play.PlayIndex != 0) return;
        if (CovenantTerminal.For(play.Player) is { } terminal &&
            terminal.TierAtCount(Bond.Ritual, CardMembership.Counts(play.Player, resolving:play.Card).GetValueOrDefault(Bond.Ritual)) > 0)
        {
            var cost = Math.Max(0, play.Card.EnergyCost.GetWithModifiers(CostModifiers.None));
            terminal.GrantLayers(Bond.Ritual, (int)Math.Min(int.MaxValue, (1L + cost) * Multiplier));
        }
        await PowerCmd.Remove(this);
        Presentation.RefreshCardTraits();
    }
}
