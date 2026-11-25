using Covenant.Core;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Random;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private HashSet<(CardModel,int)> _runningTraits=new();
    private Dictionary<CardModel,int> _growthBoosts=new(ReferenceEqualityComparer.Instance);
    private HashSet<(CardModel,int)> _authoredTurnUses=new();
    private int _traitPaidEnergy;
    internal static bool MatchesTraitFilter(CardModel card,string? filter)=>filter switch
    {
        null or "any"=>true,"Attack"=>card.Type==CardType.Attack,"Skill"=>card.Type==CardType.Skill,"Power"=>card.Type==CardType.Power,
        "zero"=>CardMembership.IsCurrentZero(card),"heavy"=>CardMembership.IsPrintedHeavy(card),"upgraded"=>card.IsUpgraded,
        "generated"=>CardMembership.IsGenerated(card),"exhaust"=>card.Keywords.Contains(CardKeyword.Exhaust),"retained"=>card.ShouldRetainThisTurn,
        _=>filter.StartsWith("name:",StringComparison.Ordinal)?card.GetType().Name==filter[5..]:Enum.TryParse<Bond>(filter,out var bond)&&CardMembership.Of(card).Contains(bond)
    };
    internal bool TraitCondition(CardModel source,TraitEffect effect)
    {
        if((effect.Trigger is "attached_passive" or "attached_turn"||source.Type==CardType.Power&&!TraitDisplay.Own(effect.Trigger)&&effect.Scope!="outside")&&!_powerTraits.ContainsKey(source))return false;
        if(effect.RequireActive&&!Active(effect.Bond??CardMembership.Definition(source)!.Trait.Bond,source))return false;
        return effect.Condition switch
        {
            null or "first"=>true,"low_hp"=>Owner.Creature.CurrentHp*2<=Owner.Creature.MaxHp,"blocked"=>Owner.Creature.Block>0,
            "unblocked"=>Owner.Creature.Block==0,"upgraded"=>source.IsUpgraded,"retained"=>source.ShouldRetainThisTurn,
            "generated"=>CardMembership.IsGenerated(source),"poisoned_enemy"=>Owner.Creature.CombatState?.HittableEnemies.Any(e=>e.GetPowerAmount<PoisonPower>()>0)==true,
            "weak_enemy"=>Owner.Creature.CombatState?.HittableEnemies.Any(e=>e.GetPowerAmount<WeakPower>()>0)==true,
            "empty_hand"=>Owner.PlayerCombatState?.Hand.Cards.Count==0,"paid_two"=>_traitPaidEnergy>=2,
            "zero_play"=>_currentCard!=null&&CurrentCostAtPlay(_currentCard)==0,"upgraded_play"=>_currentCard?.IsUpgraded==true,
            "generated_play"=>_currentCard!=null&&CardMembership.IsGenerated(_currentCard),
            _=>throw new InvalidDataException("Unknown trait condition: "+effect.Condition)
        };
    }
    private bool TraitEvent(CardModel source,TraitEffect effect,string trigger,CardModel? specific)=>effect.Trigger switch
    {
        "own_play"=>trigger=="play"&&specific==source,"own_draw"=>trigger=="draw"&&specific==source,
        "own_exhaust"=>trigger=="self_exhaust"&&specific==source,"own_discard"=>trigger=="self_discard"&&specific==source,
        "own_acquire"=>trigger=="acquire"&&specific==source,"own_remove"=>trigger=="remove"&&specific==source,
        "attack_play"=>trigger=="any_play"&&_currentCard?.Type==CardType.Attack,
        "skill_play"=>trigger=="any_play"&&_currentCard?.Type==CardType.Skill,
        "power_play"=>trigger=="any_play"&&_currentCard?.Type==CardType.Power,
        "attached_turn"=>trigger=="power_turn"&&_powerTraits.ContainsKey(source),
        _=>effect.Trigger==trigger
    };
    private int TraitAmount(CardModel card,TraitEffect effect)=>effect.Amount+(card.IsUpgraded?effect.UpgradeAmount:0);
    private void GrantTraitGrowth(CardModel source,Bond bond,int amount)
    {
        if(_growthBoosts.Remove(source,out var boost))amount+=boost;
        foreach(var supporter in CardMembership.Cards(Owner).Concat(_powerTraits.Keys).Append(_currentCard).OfType<CardModel>().Distinct())
            foreach(var effect in CardMembership.Definition(supporter)?.Trait.Effects??[])
                if(effect.Kind=="growth_aura"&&supporter!=source&&TraitDisplay.InCombat(Owner)&&MatchesTraitFilter(source,effect.Filter)&&TraitCondition(supporter,effect))amount+=TraitAmount(supporter,effect);
        GrantLayers(bond,amount);
    }
    private async Task FireAuthoredTraits(string trigger,CardModel? specific)
    {
        if(trigger is not ("victory" or "acquire" or "remove")&&!TraitDisplay.InCombat(Owner))return;
        var cards=trigger is "acquire" or "remove"?new[]{specific!}:trigger=="victory"?Owner.Deck.Cards.ToArray():CardMembership.Cards(Owner).Concat(_powerTraits.Keys).Append(specific).Append(_currentCard).OfType<CardModel>().Distinct().ToArray();
        foreach(var source in cards)
        {
            var definition=CardMembership.Definition(source);if(definition==null)continue;
            if(definition.Trait.Revision!=null)continue;
            var effects=definition.Trait.Effects??[];
            for(var index=0;index<effects.Length;index++)
            {
                var effect=effects[index];var key=(source,index);
                if(trigger is not ("victory" or "acquire" or "remove")&&!TraitDisplay.InCombat(Owner))break;
                if(effect.Kind is "growth_aura" or "shop_discount"||!TraitEvent(source,effect,trigger,specific)||!TraitCondition(source,effect))continue;
                if(effect.Condition=="first"&&!_authoredTurnUses.Add(key))continue;
                if(!_runningTraits.Add(key))continue;
                try{await ExecuteTrait(source,effect,TraitAmount(source,effect));}finally{_runningTraits.Remove(key);}
            }
        }
        Presentation.RefreshCardTraits();
    }
    private async Task ExecuteTrait(CardModel source,TraitEffect effect,int amount)
    {
        var context=Context;var hand=Owner.PlayerCombatState?.Hand.Cards.Where(c=>c!=source&&MatchesTraitFilter(c,effect.Filter)).ToArray()??[];
        var targetsHand=effect.Kind is "grow_other" or "boost_next" or "retain" or "discount" or "enhance" or "upgrade";
        var target=!targetsHand||hand.Length==0?null:hand[RandomFor(CovenantRandom.CombatCard).NextInt(hand.Length)];var bond=effect.Bond??CardMembership.Definition(source)!.Trait.Bond;
        switch(effect.Kind)
        {
            case "grow":GrantTraitGrowth(source,bond,amount);break;
            case "grow_memberships":foreach(var member in CardMembership.Of(source))GrantTraitGrowth(source,member,amount);break;
            case "grow_other":
                if(target!=null&&CardMembership.Definition(target) is {} targetDefinition)GrantTraitGrowth(target,targetDefinition.Trait.Bond,amount);
                break;
            case "boost_next":if(target!=null)_growthBoosts[target]=_growthBoosts.GetValueOrDefault(target)+amount;break;
            case "block":await Block(amount);break;
            case "aegis_block":await Block(amount+2*Scale(bond).A);break;
            case "damage":await Burst(context,amount,Owner.Creature.CombatState?.HittableEnemies.Take(1));break;
            case "aoe":await Burst(context,amount);break;
            case "poison":case "doom":case "weak":case "vulnerable":
                foreach(var enemy in (Owner.Creature.CombatState?.HittableEnemies??[]).ToArray())
                {
                    var power=effect.Kind switch{"poison"=>ModelDb.Power<PoisonPower>().ToMutable(),"doom"=>ModelDb.Power<DoomPower>().ToMutable(),"weak"=>ModelDb.Power<WeakPower>().ToMutable(),_=>ModelDb.Power<VulnerablePower>().ToMutable()};
                    await PowerCmd.Apply(context,power,enemy,amount,Owner.Creature,source);
                }break;
            case "strength":await Power<StrengthPower>(context,amount);break;
            case "temp_strength":await Power<CovenantStrengthPower>(context,amount);break;
            case "dexterity":await Power<DexterityPower>(context,amount);break;
            case "vigor":await Power<VigorPower>(context,amount);break;
            case "focus":await Power<FocusPower>(context,amount);break;
            case "plating":await Power<PlatingPower>(context,amount);break;
            case "thorns":await Power<ThornsPower>(context,amount);break;
            case "artifact":await Power<ArtifactPower>(context,amount);break;
            case "draw":await Draw(context,amount);break;
            case "energy":await PlayerCmd.GainEnergy(amount,Owner);break;
            case "stars":await PlayerCmd.GainStars(amount,Owner);break;
            case "heal":await CreatureCmd.Heal(Owner.Creature,amount);break;
            case "gold":await PlayerCmd.GainGold(amount,Owner);break;
            case "retain":if(target!=null)CardCmd.ApplySingleTurnRetain(target);break;
            case "discount":if(target!=null)target.EnergyCost.AddThisTurn(-amount);break;
            case "enhance":if(target!=null)_nextBonus[target]=_nextBonus.GetValueOrDefault(target)+amount;break;
            case "recycle":
                var options=Owner.PlayerCombatState?.DiscardPile.Cards.Where(c=>c!=source&&MatchesTraitFilter(c,effect.Filter)).ToArray()??[];
                var recycled=options.Length==0?null:options[RandomFor(CovenantRandom.CombatCard).NextInt(options.Length)];
                if(recycled!=null){await CardPileCmd.Add(recycled,PileType.Hand);CardCmd.ApplySingleTurnRetain(recycled);}break;
            case "upgrade":if(target is {IsUpgradable:true})CardCmd.Upgrade(target);break;
            case "purge":
                var waste=Owner.PlayerCombatState?.Hand.Cards.FirstOrDefault(c=>c.Type is CardType.Status or CardType.Curse);
                if(waste!=null)await CardCmd.Exhaust(context,waste);break;
            case "shiv":
                var shiv=Owner.Creature.CombatState!.CreateCard<Shiv>(Owner);await CardPileCmd.AddGeneratedCardToCombat(shiv,PileType.Hand,Owner);break;
            case "soul":
                var soul=Owner.Creature.CombatState!.CreateCard<Soul>(Owner);await CardPileCmd.AddGeneratedCardToCombat(soul,PileType.Hand,Owner);break;
            case "forge":await ForgeCmd.Forge(amount,Owner,source);break;
            case "frost":await OrbCmd.Channel<FrostOrb>(context,Owner);break;
            case "lightning":await OrbCmd.Channel<LightningOrb>(context,Owner);break;
            case "summon":await OstyCmd.Summon(context,Owner,amount,source);break;
            default:throw new InvalidDataException("Unknown authored trait action: "+effect.Kind);
        }
    }
    public override decimal ModifyMerchantPrice(Player player,MerchantEntry entry,decimal cost)
    {
        if(player!=Owner)return cost;
        var service=entry is MerchantCardRemovalEntry or BlitzMerchantCardRemovalEntry&&Tier(Bond.Profit)>0&&Scale(Bond.Profit).Layers>=BondCatalog.ProfitRemovalLayers?25:0;
        var revised=entry is MerchantCardRemovalEntry or BlitzMerchantCardRemovalEntry?0:RevisionShopDiscount();
        return Math.Max(0,cost-service-revised-Owner.Deck.Cards.Sum(c=>(CardMembership.Definition(c)?.Trait.Revision==null?CardMembership.Definition(c)?.Trait.Effects??[]:[]).Where(e=>e.Kind=="shop_discount"&&TraitCondition(c,e)).Sum(e=>TraitAmount(c,e))));
    }
    internal async Task AcquireTraits(CardModel card)
    {
        await FireAuthoredTraits("acquire",card);
        await FireRevision(new RevisionFrame(card,"acquire"));
    }
    private async Task AcquireDeckTraits(CardModel card)
    {
        await AcquireTraits(card);
        if(card.Type==CardType.Curse&&CardMembership.CurseTier(Owner,deck:true)>0)
            await CreatureCmd.GainMaxHp(Owner.Creature,1+Scale(Bond.Curse).A);
    }
    public override Task AfterCardChangedPiles(CardModel card,PileType oldPileType,AbstractModel? clonedBy)
    {
        if(card.Owner!=Owner)return Task.CompletedTask;
        if(card.Type==CardType.Power&&_ritualCombatTier<2&&card.Pile?.Type is PileType.Hand or PileType.Draw or PileType.Discard or PileType.Exhaust)
            RememberRitualTier(Counts().GetValueOrDefault(Bond.Ritual));
        return card.Pile?.Type==PileType.Deck&&oldPileType!=PileType.Deck?AcquireDeckTraits(card):Task.CompletedTask;
    }
    public override async Task BeforeCardRemoved(CardModel card)
    {
        if(card.Owner!=Owner)return;
        await FireAuthoredTraits("remove",card);
        await FireRevision(new RevisionFrame(card,"remove"){Activation=CardMembership.Counts(Owner,deck:!TraitDisplay.InCombat(Owner))});
    }
}
