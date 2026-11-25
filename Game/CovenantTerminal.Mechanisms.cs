using Covenant.Core;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private bool _boneRevivePending;
    private bool _boneSummonNextTurn,_boneSummonAtStart;
    private int _nativeChannelDepth;
    private bool _drainingOverload;
    private Queue<(PlayerChoiceContext Context,OrbModel Orb)> _pendingOverload=new();

    private void ResetMechanisms(){_boneRevivePending=_boneSummonNextTurn=_boneSummonAtStart=false;_nativeChannelDepth=0;_drainingOverload=false;_pendingOverload=new();ResetFool();ResetSpecials();}
    internal void BeginNativeChannel()=>_nativeChannelDepth++;
    internal async Task EndNativeChannel(bool complete)
    {
        _nativeChannelDepth=Math.Max(0,_nativeChannelDepth-1);
        if(!complete){_pendingOverload.Clear();return;}
        if(_nativeChannelDepth!=0||_drainingOverload)return;
        _drainingOverload=true;
        try
        {
            while(_pendingOverload.TryDequeue(out var pending))
            {
                if(!_executing.Add("overload"))continue;
                try{await OrbCmd.Channel(pending.Context,pending.Orb,Owner);}
                finally{_executing.Remove("overload");}
            }
        }
        finally{_drainingOverload=false;}
    }

    private async Task ApplyCovenantPoison(PlayerChoiceContext c,Creature target,decimal amount,bool transferred=false)
    {
        var previousAmount=target.GetPowerAmount<PoisonPower>();
        var previous=_augmentingPoison;_augmentingPoison=true;
        try{await PowerCmd.Apply<PoisonPower>(c,target,amount,Owner.Creature,null);}
        finally{_augmentingPoison=previous;}
        if(transferred&&target.GetPowerAmount<PoisonPower>()>previousAmount&&Scale(Bond.Poison).Layers>=250&&Once("poison_weak"))
            await PowerCmd.Apply<WeakPower>(c,target,1,Owner.Creature,null);
    }

    public override async Task AfterDamageReceived(PlayerChoiceContext c,Creature target,DamageResult result,ValueProp props,Creature? dealer,CardModel? source)
    {
        ObserveBreachHit(target,props,dealer,source);
        MarkKnockoutTarget(target,source);
        if(target!=Owner.Creature||Owner.Creature.IsDead||result.UnblockedDamage<=0)return;
        // Legacy card traits retain their own voluntary self-damage condition.
        var selfCard=source?.Owner==Owner&&source.Type is CardType.Attack or CardType.Skill or CardType.Power;
        if(selfCard||dealer==Owner.Creature&&source==null&&props.HasFlag(ValueProp.Unblockable))await FireTraits("self_loss");
        if(dealer?.IsEnemy==true&&props.IsPoweredAttack())return;
        if(Active(Bond.Blood,source)&&Once("blood"))
        {
            await Block(4+Scale(Bond.Blood).A);
            await Draw(c);
            if(Scale(Bond.Blood).Layers>=250)await CovenantEnergy();
        }
    }

    public override async Task AfterDeath(PlayerChoiceContext c,Creature creature,CardModel? source,bool wasRemovalPrevented,float deathAnimLength)
    {
        await ResolveArrowHit(c,creature,source);
        if(wasRemovalPrevented)return;
        await ResolveKnockoutDeath(creature,source);
        if(creature.Monster is Osty&&creature.PetOwner==Owner)
        {
            var firstDown=!_ostyWasDowned;
            _ostyWasDowned=true;
            if(firstDown&&Owner.PlayerCombatState is {} state)
                foreach(var card in state.Hand.Cards.OfType<Reanimate>())card.InvokeEnergyCostChanged();
            if(Advanced(Bond.Bones))_boneRevivePending=true;
            RefreshBattleStatuses();
        }
        if(creature.Side==Owner.Creature.Side||!Advanced(Bond.Poison))return;
        var poison=creature.GetPowerAmount<PoisonPower>();
        var percent=BondCatalog.PoisonTransferBasePercent+BondCatalog.PoisonTransferPercentPerTier*Scale(Bond.Poison).A;
        var transferred=Math.Floor(poison*percent/100m);
        if(transferred<=0)return;
        var targets=Owner.Creature.CombatState?.HittableEnemies.Where(x=>x!=creature).ToArray()??[];
        if(targets.Length==0)return;
        var recipient=RandomFor(CovenantRandom.CombatTarget).NextItem(targets);
        if(recipient!=null)await ApplyCovenantPoison(c,recipient,transferred,transferred:true);
    }

    public override async Task AfterOrbChanneled(PlayerChoiceContext c,Player player,OrbModel orb)
    {
        if(player!=Owner)return;await FireTraits("channel");
        if(!Active(Bond.Resonance,_currentCard)||!Once("channel"))return;
        await Block(3+Scale(Bond.Resonance).A);
        await ChooseOrbSwap(c);
    }

    private async Task ChooseOrbSwap(PlayerChoiceContext c)
    {
        var queue=Owner.PlayerCombatState!.OrbQueue;var orbs=queue.Orbs.ToArray();if(orbs.Length<2)return;
        var options=orbs.Select((orb,index)=>
        {
            var option=(CovenantOption)ModelDb.Card<CovenantOption>().ToMutable();option.Owner=Owner;option.OptionValue=index;
            option.OptionTitle=$"从右数第{index+1}个：{orb.Title.GetFormattedText()}";
            option.OptionDescription=$"选择两个充能球，交换它们的位置。\n这个充能球的被动数值为{orb.PassiveVal}，激发数值为{orb.EvokeVal}。";
            option.OptionPortrait=ImageHelper.GetImagePath("orbs/"+orb.Id.Entry.ToLowerInvariant()+".png");return (CardModel)option;
        }).ToArray();
        var selection=(await CardSelectCmd.FromSimpleGrid(c,options,Owner,CovenantSelection.Pair(Presentation.Literal("你可以选择两个充能球，交换它们的位置。")))).OfType<CovenantOption>().ToArray();
        if(selection.Length!=2)return;
        SwapOrbs(orbs[selection[0].OptionValue],orbs[selection[1].OptionValue]);
    }

    internal bool SwapOrbs(OrbModel first,OrbModel second)
    {
        var queue=Owner.PlayerCombatState!.OrbQueue;var orbs=queue.Orbs.ToList();var a=orbs.IndexOf(first);var b=orbs.IndexOf(second);
        if(a<0||b<0||a==b)return false;
        if(a>b){(a,b)=(b,a);(first,second)=(second,first);}
        queue.Remove(first);queue.Remove(second);queue.Insert(a,second);queue.Insert(b,first);
        var manager=NCombatRoom.Instance?.GetCreatureNode(Owner.Creature)?.OrbManager;
        if(manager!=null)
        {
            // Snapshot the slot nodes before replacing either model; replacing by model twice would replace both.
            foreach(var node in manager.FindChildren("*","",true,false).OfType<NOrb>().Where(x=>x.Model==first||x.Model==second).ToArray())
                node.ReplaceOrb(node.Model==first?second:first);
        }
        return true;
    }

    public override async Task AfterOrbEvoked(PlayerChoiceContext c,OrbModel orb,IEnumerable<Creature> targets)
    {
        if(orb.Owner!=Owner)return;await FireTraits("evoke");
        if(!Active(Bond.Overload,_currentCard)||!_executing.Add("overload"))return;
        try
        {
        await Burst(c,3+Scale(Bond.Overload).A);
        if(Once("evoke_retrieve"))await Retrieve(c,Owner.PlayerCombatState!.DiscardPile.Cards.Where(x=>CardMembership.Of(x).Contains(Bond.Resonance)),PileType.Draw);
        if(Advanced(Bond.Overload,_currentCard))
        {
            // Start from the canonical orb so Dark charge and other accumulated state are not copied.
            var replacement=ModelDb.GetById<OrbModel>(orb.Id).ToMutable();
            // A full native Channel is still reserving its freed slot. Append the covenant
            // orb only after that native operation has enqueued its requested orb.
            if(_nativeChannelDepth>0)_pendingOverload.Enqueue((c,replacement));
            else await OrbCmd.Channel(c,replacement,Owner);
            await Block(4+Scale(Bond.Overload).A/2);
        }
        }
        finally{_executing.Remove("overload");}
    }

    public override async Task AfterStarsSpent(int amount,Player spender)
    {
        if(spender!=Owner||amount<=0)return;
        await ApplyStarSpending(amount);
        await FireTraits("stars");
    }

    public override bool ShouldPayExcessEnergyCostWithStars(Player player)
        =>player==Owner&&Scale(Bond.Stars).Layers>=500&&Active(Bond.Stars);

    public override async Task AfterForge(decimal amount,Player forger,AbstractModel? source)
    {
        if(forger!=Owner||amount<=0)return;
        await FireTraits("forge");if(source==this)return;
        var count=Inc("forge");
        if(!Active(Bond.Forge,_currentCard))return;
        var advanced=Advanced(Bond.Forge,_currentCard);
        if((count==1||advanced)&&Owner.PlayerCombatState!.AllCards.OfType<SovereignBlade>().Any(sword=>!sword.IsDupe&&sword.Pile?.Type!=PileType.Exhaust))
            await ForgeCmd.Forge(1+Scale(Bond.Forge).A/2,Owner,this);
        if(count==2)await Retrieve(Context,Owner.PlayerCombatState!.DiscardPile.Cards.OfType<SovereignBlade>(),optional:false);
        if(advanced)EnchantForgedBlades();
    }

    public override async Task AfterCardGeneratedForCombat(CardModel card,Player? creator)
    {
        if(card.Owner==Owner&&(creator==null||creator==Owner))await AcquireTraits(card);
        if(card.Owner==Owner&&card is Sadness)await FireTraits("emotion");
        if(card.Owner!=Owner||creator!=null&&creator!=Owner)return;
        if(card.Type is not (CardType.Status or CardType.Curse))await FireTraits("generate");
        if(creator!=Owner||CardMembership.IsMarker(card))return;
        if(Advanced(Bond.Creation,_currentCard)&&card.Pile?.IsCombatPile==true&&
            RollPercent(RandomFor(CovenantRandom.CombatRoll),BondCatalog.CreationFreeChance.At(Scale(Bond.Creation))))
        {
            _creationFree.Add(card);
            if(!card.EnergyCost.CostsX&&card.EnergyCost.GetWithModifiers(CostModifiers.None)>=0)card.EnergyCost.SetThisTurnOrUntilPlayed(0);
        }
        if(card.Type!=CardType.Status)
        {
            var count=Inc("generated");
            if(Active(Bond.Creation,_currentCard))
            {
                var scale=Scale(Bond.Creation);
                if(count==1)await Block(3+scale.A);
                if(count==2)await CovenantEnergy();
                if(scale.Layers>=250&&count<=6)await Block(Math.Max(1,scale.A/7));
                if(scale.Layers>=250&&count==7&&_ledger.Claim("creation_heal"))
                    await CreatureCmd.Heal(Owner.Creature,7);
            }
        }
        RefreshBattleStatuses();
    }

    public override Task AfterAttack(PlayerChoiceContext c,AttackCommand command)
    {
        if(command.Attacker?.Monster is Osty&&command.Attacker.PetOwner==Owner&&Active(Bond.Bones,command.ModelSource as CardModel)&&Once("osty_attack"))
        {
            _boneBonus=true;_boneSummonNextTurn=true;
            RefreshBattleStatuses();
        }
        return Task.CompletedTask;
    }

    // A preview may query this repeatedly. Only the real AfterSummon callback consumes the bonus.
    public override decimal ModifySummonAmount(Player summoner,decimal amount,AbstractModel? source)
        =>summoner==Owner&&_boneBonus&&Active(Bond.Bones,source as CardModel)?amount+2+Scale(Bond.Bones).A:amount;
    public override async Task AfterSummon(PlayerChoiceContext c,Player summoner,decimal amount)
    {
        if(summoner!=Owner)return;_boneBonus=false;await FireTraits("summon");
    }

    public override bool ShouldClearBlock(Creature creature)
    {
        if(creature!=Owner.Creature||!Advanced(Bond.Guard))return true;
        // Let full retention effects (such as Barricade and Blur) keep their own, stronger behavior.
        return creature.CombatState!.IterateHookListeners().Any(x=>x!=this&&!x.ShouldClearBlock(creature));
    }
    public override async Task AfterPreventingBlockClear(AbstractModel preventer,Creature creature)
    {
        if(preventer!=this||creature!=Owner.Creature)return;
        await CreatureCmd.LoseBlock(Context,creature,Math.Max(0,creature.Block-3-2*Scale(Bond.Guard).A),null);
    }
}
