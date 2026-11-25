using Covenant.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private const int AllSpectrumColors = (1 << 7) - 1;
    private static readonly string[] SpectrumColorNames = ["战士", "猎手", "故障机器人", "储君", "死灵", "愚者", "无色"];
    private int _spectrumPlayedColors;

    private void ResolveSpectrumPlay(CardModel card)
    {
        if (CombatManager.Instance.IsOverOrEnding || Owner.PlayerCombatState is not {} combat) return;
        var color = CardMembership.SpectrumColor(card);
        if (color == 0 || (_spectrumPlayedColors & color) != 0) return;
        var tier = EffectiveTier(Bond.Spectrum, card);
        if (tier == 0) return;

        var candidates = combat.Hand.Cards.Where(candidate =>
        {
            var other = CardMembership.SpectrumColor(candidate);
            return other != 0 && other != color && !candidate.EnergyCost.CostsX &&
                candidate.EnergyCost.GetWithModifiers(CostModifiers.All) > 0;
        });
        RandomFor(CovenantRandom.SpectrumDiscount).NextItem(candidates)?.EnergyCost.AddUntilPlayed(-1, reduceOnly:true);

        _spectrumPlayedColors |= color;
        if (tier == 2 && _spectrumPlayedColors == AllSpectrumColors) _spectrumPlayedColors = 0;
    }

    private string SpectrumProgress()
    {
        var remaining = SpectrumColorNames.Where((_, index) => (_spectrumPlayedColors & (1 << index)) == 0).ToArray();
        return remaining.Length == 0
            ? "本场战斗已触发全部7种颜色。"
            : $"本轮还可通过{string.Join("、", remaining)}的牌触发。";
    }
}
