using Covenant.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private Dictionary<CardModel,RevisionFrame> _revisionPlays=new(ReferenceEqualityComparer.Instance);
    private Dictionary<(CardModel,int),int> _revisionTurnUses=new(),_revisionBattleUses=new();
    private HashSet<(CardModel,int)> _revisionRunning=new();
    private Dictionary<string,int> _revisionTurnCounts=new(StringComparer.Ordinal),_revisionBattleCounts=new(StringComparer.Ordinal);
    private Dictionary<CardModel,decimal> _revisionDamageTotals=new(ReferenceEqualityComparer.Instance);
    private HashSet<CardModel> _revisionPlayedCards=new(ReferenceEqualityComparer.Instance);
    private HashSet<CardModel> _revisionExhaustAtEnd=new(ReferenceEqualityComparer.Instance);
    private Dictionary<CardModel,int> _revisionCardTurnCounts=new(ReferenceEqualityComparer.Instance);
    private Dictionary<CardModel,int> _revisionCardBattleCounts=new(ReferenceEqualityComparer.Instance);
    private Dictionary<(CardModel,int),decimal> _revisionTurnAccumulations=new(),_revisionBattleAccumulations=new();
    private Dictionary<CardModel,int> _revisionBankedGold=new(ReferenceEqualityComparer.Instance);
    private Dictionary<CardModel,int> _revisionSourceBonuses=new(ReferenceEqualityComparer.Instance);
    private static RevisionProgram? Program(CardModel card)=>RevisionDescriptions.Program(card);
    private void ResetRevisions()
    {
        if(IsMutable&&Owner?.PlayerCombatState is {} combat)RevisionTrace.ClearResources(combat);
        _revisionPlays=new(ReferenceEqualityComparer.Instance);_revisionTurnUses=new();_revisionBattleUses=new();_revisionRunning=new();
        _revisionTurnCounts=new(StringComparer.Ordinal);_revisionBattleCounts=new(StringComparer.Ordinal);_revisionDamageTotals=new(ReferenceEqualityComparer.Instance);_revisionPlayedCards=new(ReferenceEqualityComparer.Instance);
        _revisionCardTurnCounts=new(ReferenceEqualityComparer.Instance);
        _revisionCardBattleCounts=new(ReferenceEqualityComparer.Instance);
        _revisionAttachments=new(ReferenceEqualityComparer.Instance);
        _revisionPendingTop=new(ReferenceEqualityComparer.Instance);
        _revisionTurnAccumulations=new();_revisionBattleAccumulations=new();
        _revisionExhaustAtEnd=new(ReferenceEqualityComparer.Instance);
        _revisionBankedGold=new(ReferenceEqualityComparer.Instance);
        _revisionSourceBonuses=new(ReferenceEqualityComparer.Instance);
    }
    private static CardModel Original(CardModel card)=>card.DeckVersion??card;
    internal void RevisionPlayStarting(RevisionFrame frame)
    {
        frame.Activation=CardMembership.Counts(Owner,resolving:frame.Source);
        frame.Observers=RevisionTrace.LivePowers(Owner).SelectMany(RevisionTrace.Sources).Distinct().Where(c=>Program(c)?.Rules.Any(r=>r.Event=="after_play")==true).ToHashSet<CardModel>(ReferenceEqualityComparer.Instance);
        foreach(var power in RevisionTrace.LivePowers(Owner).Where(p=>p.GetType().Name=="ConquerorPower"))
            foreach(var origin in RevisionTrace.Sources(power))
            {
                if(!frame.ObserverTargets.TryGetValue(origin,out var targets))frame.ObserverTargets[origin]=targets=new(ReferenceEqualityComparer.Instance);
                targets.Add(power.Owner);
            }
        var card=frame.Source;_revisionPlays[card]=frame;_revisionPlayedCards.Add(card);_revisionPlayedCards.Add(Original(card));
        if(card is MegaCrit.Sts2.Core.Models.Cards.Spite && Owner.Creature.CombatState is {} combat)
            frame.Before["owner_lost_hp_this_turn"]=CombatManager.Instance.History.Entries.OfType<DamageReceivedEntry>()
                .Any(entry=>entry.Receiver==Owner.Creature&&entry.Result.UnblockedDamage>0&&entry.HappenedThisTurn(combat))?1:0;
        foreach(var key in new[]{"play",card.Type.ToString(),card.GetType().Name})
        {
            frame.Values["turn_before_"+key]=_revisionTurnCounts.GetValueOrDefault(key);
            frame.Values["battle_before_"+key]=_revisionBattleCounts.GetValueOrDefault(key);
            _revisionTurnCounts[key]=_revisionTurnCounts.GetValueOrDefault(key)+1;
            _revisionBattleCounts[key]=_revisionBattleCounts.GetValueOrDefault(key)+1;
        }
        frame.Values["turn_before_zero"]=_revisionTurnCounts.GetValueOrDefault("zero");
        if(CurrentCostAtPlay(card)==0)_revisionTurnCounts["zero"]=_revisionTurnCounts.GetValueOrDefault("zero")+1;
        frame.Values["native_retained"]=_retained.Contains(card)?1:0;
        frame.Values["turn_before_source"]=_revisionCardTurnCounts.GetValueOrDefault(card);
        frame.Values["battle_before_source"]=_revisionCardBattleCounts.GetValueOrDefault(card);
        frame.Values["turn_before_shot"]=_revisionTurnCounts.GetValueOrDefault("shot");
        frame.Values["turn_before_osty_attack"]=_revisionTurnCounts.GetValueOrDefault("osty_attack");
        frame.Values["turn_energy_spent"]=_revisionTurnCounts.GetValueOrDefault("energy_spent");
        frame.Values["turn_stars_gained"]=_revisionTurnCounts.GetValueOrDefault("stars_gained");
        frame.Values["turn_exhausted"]=_revisionTurnCounts.GetValueOrDefault("exhausted");
        frame.Values["turn_carried_block"]=_revisionTurnCounts.GetValueOrDefault("carried_block");
        _revisionCardTurnCounts[card]=_revisionCardTurnCounts.GetValueOrDefault(card)+1;
        _revisionCardBattleCounts[card]=_revisionCardBattleCounts.GetValueOrDefault(card)+1;
    }
    internal void RevisionObservedShot()=>_revisionTurnCounts["shot"]=_revisionTurnCounts.GetValueOrDefault("shot")+1;
    internal void RevisionObservedOstyAttack()=>_revisionTurnCounts["osty_attack"]=_revisionTurnCounts.GetValueOrDefault("osty_attack")+1;
    internal void RevisionCountResource(string name,int amount)=>_revisionTurnCounts[name]=(int)Math.Clamp((long)_revisionTurnCounts.GetValueOrDefault(name)+amount,0,int.MaxValue);
    internal void RevisionCarriedBlock(bool carried)=>_revisionTurnCounts["carried_block"]=carried?1:0;
    internal int RevisionSourceBonus(CardModel card)=>_revisionSourceBonuses.GetValueOrDefault(card);
    internal void RevisionDamage(CardModel card,decimal damage)=>_revisionDamageTotals[Original(card)]=_revisionDamageTotals.GetValueOrDefault(Original(card))+damage;
    private decimal EvaluateRevision(RevisionFrame frame,RevisionInstruction rule,string expression)=>RuleExpression.Evaluate(expression,key=>key switch
    {
        "A"=>Scale(rule.Bond).A,
        "battle_damage"=>_revisionDamageTotals.GetValueOrDefault(Original(frame.Source)),
        "battle_played"=>_revisionPlayedCards.Contains(Original(frame.Source))?1:0,
        "battle_exhausted"=>_revisionExhaustAtEnd.Contains(Original(frame.Source))?1:0,
        "source_bonus"=>_revisionSourceBonuses.GetValueOrDefault(frame.Source),
        _=>frame.Resolve(key)
    });
    internal async Task<bool> FireRevision(RevisionFrame frame)
    {
        var program=Program(frame.Source);if(program==null)return false;
        var fired=false;
        using var suppressed=RevisionTrace.Suppress(this);
        for(var index=0;index<program.Rules.Length;index++)
        {
            var rule=program.Rules[index];
            if(frame.RuleIndex>=0&&frame.RuleIndex!=index)continue;
            if(rule.Event!=frame.Event&&!(rule.Event=="exhaust"&&frame.Event=="ethereal"))continue;
            if(rule.Scope is not ("own" or "hand")&&rule.Scope!=frame.Scope)continue;
            if(rule.Scope=="hand"&&frame.Source.Pile?.Type!=PileType.Hand)continue;
            if(rule.Scope=="live"&&!frame.WasArmed&&!RevisionTrace.HasLivePower(frame.Source,program))continue;
            if(rule.RequireActive&&!RevisionActive(frame,rule.Bond))continue;
            var key=(frame.Source,index);
            if(rule.Accumulate is {} accumulated)
            {
                var totals=rule.Lifetime=="turn"?_revisionTurnAccumulations:_revisionBattleAccumulations;
                frame.Values["sum_before"]=totals.GetValueOrDefault(key);
                frame.Values["sum_after"]=frame.Values["sum_before"]+Math.Max(0,EvaluateRevision(frame,rule,accumulated));
                totals[key]=frame.Values["sum_after"];
            }
            if(EvaluateRevision(frame,rule,rule.Condition)==0)continue;
            var amount=(int)Math.Clamp(Math.Floor(EvaluateRevision(frame,rule,rule.Amount)),0,int.MaxValue);
            if(amount==0)continue;
            var limits=rule.Limit=="turn"?_revisionTurnUses:_revisionBattleUses;
            if(rule.Limit is "turn" or "battle"&&limits.ContainsKey(key))continue;
            if(!_revisionRunning.Add(key))continue;
            try
            {
                if(rule.Limit is "turn" or "battle")limits[key]=1;
                await ExecuteRevision(frame,rule,amount);
                fired=true;
            }
            finally{_revisionRunning.Remove(key);}
        }
        Presentation.QueueCardTraitRefresh();
        return fired;
    }
    private bool RevisionActive(RevisionFrame frame,Bond bond)
    {
        if(frame.Event=="victory")return BondCatalog.Get(bond).Tier(Counts(deck:true).GetValueOrDefault(bond))>0;
        var subject=frame.Subject??frame.Source;
        var snapshot=subject.Owner==Owner?RevisionTrace.FindPlay(subject)?.Activation:null;
        snapshot??=frame.Activation;
        if(snapshot!=null)return TierAtCount(bond,snapshot.GetValueOrDefault(bond))>0;
        if(subject.Owner==Owner&&subject.Pile?.Type==PileType.Play)return TierAtCount(bond,CardMembership.Counts(Owner,resolving:subject).GetValueOrDefault(bond))>0;
        return Active(bond,subject.Owner==Owner?subject:null);
    }
    internal IEnumerable<Bond> ActiveLayeredBonds(CardModel card,IReadOnlyDictionary<Bond,int> counts)
        =>CardMembership.Of(card).Where(bond=>BondCatalog.Get(bond).UsesLayers&&TierAtCount(bond,counts.GetValueOrDefault(bond))>0);

    private async Task ExecuteRevision(RevisionFrame frame,RevisionInstruction rule,int amount)
    {
        if(amount<=0)return;
        switch(rule.Action)
        {
            case "covenant_trait": await ResolveNewTrait(frame,amount);break;
            case "queue_star_growth":_nextStarGrowth.Add((frame.Source,rule.Bond,amount,rule.Lifetime=="turn"));break;
            case "queue_shiv_growth":_nextShivGrowth.Add((frame.Source,rule.Bond,amount));break;
            case "neutron_meter":_neutronMeters.Add((frame.Source,0));break;
            case "unrelenting_power":
                var unrelenting=(CovenantUnrelentingPower)ModelDb.Power<CovenantUnrelentingPower>().ToMutable();
                unrelenting.Multiplier=amount;
                await PowerCmd.Apply(Context,unrelenting,Owner.Creature,1,Owner.Creature,frame.Source);
                break;
            case "synthesis_power":
                var synthesis=(CovenantSynthesisPower)ModelDb.Power<CovenantSynthesisPower>().ToMutable();
                synthesis.Multiplier=amount;
                await PowerCmd.Apply(Context,synthesis,Owner.Creature,1,Owner.Creature,frame.Source);
                break;
            case "terraforming_power":
                var terraforming=(CovenantTerraformingPower)ModelDb.Power<CovenantTerraformingPower>().ToMutable();
                await PowerCmd.Apply(Context,terraforming,Owner.Creature,amount,Owner.Creature,frame.Source);
                break;
            case "grow_upgraded_highest_bond":
                var upgradedCounts=Counts();
                foreach(var upgradedCard in frame.Items("upgraded"))
                {
                    var highest=ActiveLayeredBonds(upgradedCard,upgradedCounts).OrderByDescending(bond=>Scale(bond).Layers)
                        .ThenBy(bond=>bond).Select(bond=>(Bond?)bond).FirstOrDefault();
                    if(highest is {} recipient)GrantLayers(recipient,amount);
                }
                break;
            case "grow_transformed_from_bonds":
                var beforeTransform=frame.Activation??Counts();
                foreach(var transformedOriginal in frame.Items("transformed_original"))
                    foreach(var bond in ActiveLayeredBonds(transformedOriginal,beforeTransform))
                        GrantLayers(bond,amount);
                break;
            case "grow":GrantLayers(rule.Bond,(int)Math.Min(int.MaxValue,(long)amount+RevisionGrowthBonus(frame.Source)));break;
            case "grow_entropy_bonds":
                if(frame.Subject is not {} transformed)break;
                var counts=Counts();
                var active=BondCatalog.All.Where(def=>TierAtCount(def.Id,counts.GetValueOrDefault(def.Id))>0)
                    .Select(def=>def.Id).ToHashSet();
                foreach(var bond in CardMembership.Of(transformed))
                    if(active.Contains(bond)&&BondCatalog.Get(bond).UsesLayers)GrantLayers(bond,active.Count);
                break;
            case "grow_subject_bonds":case "grow_returned_bonds":
                foreach(var subjectCard in rule.Action=="grow_subject_bonds"?(frame.Subject==null?[]:new[]{frame.Subject}):frame.Items("returned"))
                    foreach(var bond in CardMembership.Of(subjectCard).Where(b=>BondCatalog.Get(b).UsesLayers&&RevisionActive(frame,b)))GrantLayers(bond,amount);
                break;
            case "block":await Block(amount);break;
            case "stars":await PlayerCmd.GainStars(amount,Owner);break;
            case "energy":
                var beforeEnergy=Owner.PlayerCombatState!.Energy;
                await PlayerCmd.GainEnergy(amount,Owner);
                await OnReflowCardEnergy(frame.Source,beforeEnergy);
                break;
            case "draw":await Draw(Context,amount);break;
            case "gold":await PlayerCmd.GainGold(amount,Owner);break;
            case "bank_gold":_revisionBankedGold[frame.Source]=(int)Math.Min(int.MaxValue,(long)_revisionBankedGold.GetValueOrDefault(frame.Source)+amount);break;
            case "source_bonus":_revisionSourceBonuses[frame.Source]=(int)Math.Min(int.MaxValue,(long)_revisionSourceBonuses.GetValueOrDefault(frame.Source)+amount);break;
            case "absorb_damage_bonus":
                frame.Source.DynamicVars.Damage.BaseValue+=amount;
                var absorbed=HarmonyLib.AccessTools.Field(frame.Source.GetType(),"_extraDamage");
                absorbed.SetValue(frame.Source,(decimal)absorbed.GetValue(frame.Source)!+amount);
                break;
            case "discount_attack":
                var attacks=Owner.PlayerCombatState!.Hand.Cards.Where(card=>card.Type==CardType.Attack&&!card.EnergyCost.CostsX)
                    .Select(card=>(Card:card,Cost:card.EnergyCost.GetWithModifiers(CostModifiers.None))).ToArray();
                if(attacks.Length>0)
                {
                    var highest=attacks.Max(attack=>attack.Cost);
                    RandomFor(CovenantRandom.CombatCard).NextItem(attacks.Where(attack=>attack.Cost==highest).Select(attack=>attack.Card))?.EnergyCost.AddThisTurn(-amount);
                }
                break;
            case "discount_subject":
                if(frame.Subject is {} discounted)
                {
                    if(rule.Lifetime=="next")discounted.EnergyCost.AddUntilPlayed(-amount);
                    else discounted.EnergyCost.AddThisTurn(-amount);
                }
                break;
            case "discount_cards":
                foreach(var discountedCard in frame.Items(rule.Parameter??"returned"))
                {
                    if(rule.Lifetime=="next")discountedCard.EnergyCost.AddUntilPlayed(-amount);
                    else discountedCard.EnergyCost.AddThisTurn(-amount);
                }
                break;
            case "heal":await CreatureCmd.Heal(Owner.Creature,amount);break;
            case "vulnerable_target":if(frame.Target is {} targetEnemy)await PowerCmd.Apply<VulnerablePower>(Context,targetEnemy,amount,Owner.Creature,frame.Source);break;
            case "damage":await Burst(Context,amount);break;
            case "damage_target":if(frame.Target is {IsAlive:true} hitTarget)await CreatureCmd.Damage(Context,hitTarget,amount,MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered,Owner.Creature,frame.Source,null);break;
            case "weak_random":case "vulnerable_random":
                var targets=Owner.Creature.CombatState?.HittableEnemies.ToArray()??[];
                if(targets.Length>0)
                {
                    var target=RandomFor(CovenantRandom.CombatTarget).NextItem(targets);
                    if(target==null)break;
                    if(rule.Action=="weak_random")await PowerCmd.Apply<WeakPower>(Context,target,amount,Owner.Creature,frame.Source);
                    else await PowerCmd.Apply<VulnerablePower>(Context,target,amount,Owner.Creature,frame.Source);
                }break;
            default:throw new InvalidDataException($"Unimplemented revision action {rule.Action} on {frame.Source.GetType().Name}");
        }
    }
    internal async Task RevisionObserve(string eventName,CardModel? subject=null,RevisionFrame? subjectFrame=null,IEnumerable<CardModel>? retained=null,IReadOnlyDictionary<string,decimal>? observed=null,Dictionary<Bond,int>? activation=null)
    {
        var deck=eventName=="victory";
        var sources=deck?Owner.Deck.Cards:CardMembership.Cards(Owner).Concat(_powerTraits.Keys).Concat(RevisionTrace.LivePowers(Owner).SelectMany(RevisionTrace.Sources)).Concat(subjectFrame?.Observers??[]).Distinct();
        foreach(var source in sources.ToArray())
        {
            var program=Program(source);if(program==null||!program.Rules.Any(r=>r.Event==eventName))continue;
            var frame=new RevisionFrame(source,eventName,subjectFrame?.Target){Subject=subject,Scope="live",Activation=activation??subjectFrame?.Activation,WasArmed=subjectFrame?.Observers.Contains(source)==true};
            if(subjectFrame!=null)
            {
                foreach(var (key,value) in subjectFrame.Values)frame.Values["subject_"+key]=value;
                foreach(var (key,value) in subjectFrame.Before)frame.Values["subject_before_"+key]=value;
                frame.Values["subject_hits_targets"]=subjectFrame.HitTargets.Count;
                frame.Values["subject_hit_count"]=subjectFrame.Values.GetValueOrDefault("hit_count");
                frame.Values["subject_kills"]=subjectFrame.KilledTargets.Count;
                frame.Values["subject_hit_weak_targets"]=subjectFrame.HitTargets.Count(t=>subjectFrame.EnemyBefore.GetValueOrDefault(t)?.GetValueOrDefault("WeakPower")>0);
                frame.Values["subject_hit_impaled_targets"]=subjectFrame.HitTargets.Count(t=>subjectFrame.EnemyBefore.GetValueOrDefault(t)?.GetValueOrDefault("ImpaledPower")>0);
                frame.Values["subject_hit_conquered_targets"]=subjectFrame.HitTargets.Count(t=>subjectFrame.ObserverTargets.GetValueOrDefault(source)?.Contains(t)==true);
            }
            if(retained!=null)foreach(var card in retained)frame.AddCard("retained",card);
            if(observed!=null)foreach(var (key,value) in observed)frame.Values[key]=value;
            await FireRevision(frame);
        }
    }
    private async Task RevisionOwnPlay(CardModel card)
    {
        var frame=RevisionTrace.FindPlay(card)??_revisionPlays.GetValueOrDefault(card);if(frame!=null)await FireRevision(frame);
        await RevisionAttachedPlay(card);
        if(frame!=null)await RevisionObserve("after_play",card,frame);
    }
    internal decimal RevisionBonus(CardModel card,bool block,Creature? target=null)
    {
        var program=Program(card);
        var eventName=block?"block_bonus":"damage_bonus";decimal total=RevisionAttachedBonus(card,block,target);
        if(program==null)return total;
        RevisionFrame? frame=null;
        foreach(var rule in program.Rules.Where(r=>r.Event==eventName&&r.Scope=="own"))
        {
            if(rule.RequireActive&&!Active(rule.Bond,card))continue;
            if(frame==null)
            {
                var play=RevisionTrace.FindPlay(card);
                frame=play?.Target==target?play:new RevisionFrame(card,eventName,target);
                if(frame==null)frame=new RevisionFrame(card,eventName,target);
                if(play!=null&&frame!=play)
                {
                    foreach(var (key,value) in play.Before.Where(p=>!p.Key.StartsWith("target_",StringComparison.Ordinal)))frame.Before[key]=value;
                    foreach(var (key,value) in play.Values)frame.Values.TryAdd(key,value);
                    foreach(var (key,value) in play.Cards.Where(p=>p.Key.StartsWith("before_",StringComparison.Ordinal)))frame.Cards[key]=value;
                }
            }
            if(EvaluateRevision(frame,rule,rule.Condition)!=0)total+=Math.Max(0,Math.Floor(EvaluateRevision(frame,rule,rule.Amount)));
        }
        return total;
    }
    private decimal RevisionShopDiscount()
    {
        decimal total=0;
        foreach(var card in Owner.Deck.Cards)
        {
            var program=Program(card);if(program==null)continue;
            foreach(var rule in program.Rules.Where(r=>r.Event=="shop_discount"))
            {
                var frame=new RevisionFrame(card,"shop_discount");
                if(rule.RequireActive&&!Active(rule.Bond))continue;
                if(EvaluateRevision(frame,rule,rule.Condition)!=0)total+=Math.Max(0,Math.Floor(EvaluateRevision(frame,rule,rule.Amount)));
            }
        }
        return total;
    }
    private bool RevisionSupportEffective(CardModel source,RevisionProgram program,RevisionInstruction rule,bool combat)
    {
        if(rule.Event is "victory" or "shop_discount")
        {
            if(combat&&rule.Event=="shop_discount")return false;
            var frame=new RevisionFrame(source,rule.Event);
            return (!rule.RequireActive||RevisionActive(frame,rule.Bond))&&EvaluateRevision(frame,rule,rule.Condition)!=0;
        }
        if(!combat||rule.RequireActive&&!Active(rule.Bond))return false;
        if(rule.Scope=="orb")return RevisionTrace.HasOrbCredits(source)||rule.Event=="orb_origin_evoked"&&RevisionTrace.HasLivePower(source,program);
        if(rule.Event=="resource_spent")return RevisionTrace.HasResourceCredits(source)||RevisionTrace.HasLivePower(source,program);
        if(rule.Scope=="free")return RevisionHasAttachments(source)||RevisionTrace.HasLivePower(source,program);
        if(rule.Scope=="hand")return source.Pile?.Type==PileType.Hand;
        if(rule.Scope=="replay")return RevisionHasAttachments(source)||RevisionTrace.HasLivePower(source,program);
        if(rule.Event.StartsWith("subject_",StringComparison.Ordinal)||rule.Scope is "generated" or "top" or "returned" or "keyword")return RevisionHasAttachments(source);
        return (rule.Event=="native"||rule.Event.StartsWith("power_",StringComparison.Ordinal)||rule.Scope=="live")&&RevisionTrace.HasLivePower(source,program);
    }
}
