using Covenant.Core;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    public override bool TryModifyCardRewardOptions(Player player,List<CardCreationResult> rewards,CardCreationOptions options)
    {
        const CardCreationFlags prohibited=CardCreationFlags.NoModifyHooks|CardCreationFlags.NoCardPoolModifications|CardCreationFlags.NoCardModelModifications;
        if(player!=Owner||options.Source!=CardCreationSource.Encounter||
            !options.Flags.HasFlag(CardCreationFlags.IsCardReward)||(options.Flags&prohibited)!=0||rewards.Count==0)return false;
        if(Counts(deck:true).GetValueOrDefault(Bond.Foresight)>=BondCatalog.Get(Bond.Foresight).Basic&&Scale(Bond.Foresight).Layers>=BondCatalog.ForesightBossLayers&&player.RunState.CurrentRoom is CombatRoom{RoomType:RoomType.Boss})
            return GuaranteeBossFlow(player,rewards,options);
        if(!AllCardPools)return false;

        var held=Counts(deck:true).Where(x=>x.Key<=Bond.Emotion&&x.Value>0).OrderBy(x=>x.Key).ToArray();
        if(held.Length==0)return false;
        var existing=rewards.Select(r=>r.Card.Id).ToHashSet();
        var pools=SixPools.ToHashSet();
        // Use the final native pool/filter/unlocks/run constraints. Never introduce
        // event, token, ancient or otherwise unavailable cards into this reward.
        var available=options.GetPossibleCards(player).Where(c=>pools.Contains(c.Pool)&&c.IsAllowedInRun(player.RunState)&&
            c.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare).DistinctBy(c=>c.Id).ToArray();
        var memberships=available.ToDictionary(c=>c.Id,CardMembership.Of);
        var candidates=available.Where(c=>!existing.Contains(c.Id)).ToArray();
        var plans=held.Select(h=>(Bond:h.Key,Weight:Math.Max(1,h.Value*available.Length/(Math.Max(13,available.Count(c=>memberships[c.Id].Contains(h.Key)))*(h.Value==1?2:1))),Slots:rewards.Select((reward,index)=>(reward,index))
            .Where(s=>!s.reward.HasBeenModified&&s.reward.Card.Type!=CardType.Curse&&candidates.Any(c=>c.Rarity==s.reward.Card.Rarity&&memberships[c.Id].Contains(h.Key))).Select(s=>s.index).ToArray()))
            .Where(p=>p.Slots.Length>0).ToArray();
        if(plans.Length==0)return false;

        var rng=RandomFor(CovenantRandom.Rewards);
        if(rng.NextInt(100)>=35)return false;
        var ticket=rng.NextInt(plans.Sum(p=>p.Weight));
        var plan=plans[0];
        foreach(var possible in plans){ticket-=possible.Weight;if(ticket<0){plan=possible;break;}}
        var slot=plan.Slots[rng.NextInt(plan.Slots.Length)];
        var old=rewards[slot].Card;
        var eligible=candidates.Where(c=>c.Rarity==old.Rarity&&memberships[c.Id].Contains(plan.Bond)).ToArray();
        var card=player.RunState.CreateCard(eligible[rng.NextInt(eligible.Length)],player);
        // Preserve the native upgrade roll. Egg/enchantment hooks run afterward in
        // the game's late phase and apply to the final card as usual.
        for(int i=0;i<old.CurrentUpgradeLevel&&card.IsUpgradable;i++)
        {card.UpgradeInternal();card.FinalizeUpgradeInternal();}
        rewards[slot].ModifyCard(card,this);
        return true;
    }
    private bool GuaranteeBossFlow(Player player,List<CardCreationResult> rewards,CardCreationOptions options)
    {
        var counts=Counts(deck:true);var flow=CardMembership.HighestActiveFlow(player,counts);if(flow==null)return false;
        if(rewards.Any(r=>CardMembership.Of(r.Card).Contains(flow.Value)))return false;
        var existing=rewards.Select(r=>r.Card.Id).ToHashSet();
        var available=options.GetPossibleCards(player).Where(c=>c.IsAllowedInRun(player.RunState)&&!existing.Contains(c.Id)&&CardMembership.Of(c).Contains(flow.Value)).DistinctBy(c=>c.Id).ToArray();
        if(available.Length==0)return false;
        var slots=rewards.Select((reward,index)=>(reward,index)).Where(s=>!s.reward.HasBeenModified&&s.reward.Card.Type!=CardType.Curse).ToArray();if(slots.Length==0)return false;
        var slot=slots.FirstOrDefault(s=>available.Any(c=>c.Rarity==s.reward.Card.Rarity),slots[0]);
        var matching=available.Where(c=>c.Rarity==slot.reward.Card.Rarity).ToArray();if(matching.Length>0)available=matching;
        var rng=RandomFor(CovenantRandom.Rewards);var card=player.RunState.CreateCard(available[rng.NextInt(available.Length)],player);
        for(var level=0;level<slot.reward.Card.CurrentUpgradeLevel&&card.IsUpgradable;level++){card.UpgradeInternal();card.FinalizeUpgradeInternal();}
        rewards[slot.index].ModifyCard(card,this);return true;
    }
}
