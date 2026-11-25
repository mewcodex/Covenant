using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    [SavedProperty] public bool AllCardPools {get;set;}=true;
    public static IEnumerable<CardPoolModel> SixPools=>new CharacterModel[]{ModelDb.Character<Ironclad>(),ModelDb.Character<Silent>(),ModelDb.Character<Defect>(),ModelDb.Character<Regent>(),ModelDb.Character<Necrobinder>(),ModelDb.Character<Fool>()}.Select(c=>c.CardPool);
    public override CardCreationOptions ModifyCardRewardCreationOptions(Player player,CardCreationOptions options)
    {
        if(player!=Owner||!AllCardPools||options.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications)||!options.Flags.HasFlag(CardCreationFlags.IsCardReward))return options;
        return options.WithCardPools(options.CardPools.Union(SixPools));
    }
    public override IEnumerable<CardModel> ModifyMerchantCardPool(Player player,IEnumerable<CardModel> options)
    {
        var original=options.ToArray();if(player!=Owner||!AllCardPools)return original;
        var pools=SixPools.ToHashSet();
        // Preserve dedicated colorless/event shelves. Expand all colored shelves in one pass, unlike repeated CharacterCards modifiers.
        if(original.Length==0||original.Any(c=>!pools.Contains(c.Pool)))return original;
        return original.Concat(pools.SelectMany(p=>p.GetUnlockedCards(player.UnlockState,player.RunState!.CardMultiplayerConstraint))).Distinct();
    }
}
