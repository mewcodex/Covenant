using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Powers;
using Covenant.Core;

namespace Covenant.Game;

internal sealed class RevisionFrame
{
    internal readonly CardModel Source;
    internal readonly Creature? Target;
    internal readonly OrbModel? RightOrb;
    internal CardModel? Subject;
    internal string Event;
    internal string Scope="own";
    internal int RuleIndex=-1;
    internal Dictionary<Bond,int>? Activation;
    internal HashSet<CardModel> Observers=new(ReferenceEqualityComparer.Instance);
    internal Dictionary<CardModel,HashSet<Creature>> ObserverTargets=new(ReferenceEqualityComparer.Instance);
    internal bool WasArmed;
    internal string NativeMethod="";
    internal PowerModel? NativePower;
    internal readonly Dictionary<string,decimal> Before=new(StringComparer.Ordinal);
    internal readonly Dictionary<string,decimal> Values=new(StringComparer.Ordinal);
    internal readonly Dictionary<string,List<CardModel>> Cards=new(StringComparer.Ordinal);
    internal readonly HashSet<Creature> HitTargets=new(ReferenceEqualityComparer.Instance);
    internal readonly HashSet<Creature> KilledTargets=new(ReferenceEqualityComparer.Instance);
    internal readonly HashSet<Creature> FirstPoisonTargets=new(ReferenceEqualityComparer.Instance);
    internal readonly HashSet<Creature> FirstDoomTargets=new(ReferenceEqualityComparer.Instance);
    internal readonly HashSet<Creature> FirstVulnerableTargets=new(ReferenceEqualityComparer.Instance);
    internal readonly HashSet<Creature> FirstWeakTargets=new(ReferenceEqualityComparer.Instance);
    internal readonly HashSet<Creature> DebuffTargets=new(ReferenceEqualityComparer.Instance);
    internal readonly Dictionary<Creature,decimal> HpDamage=new(ReferenceEqualityComparer.Instance);
    internal readonly Dictionary<Creature,Dictionary<string,decimal>> EnemyBefore=new(ReferenceEqualityComparer.Instance);
    internal readonly Dictionary<string,Dictionary<Creature,decimal>> PowerDeltas=new(StringComparer.Ordinal);
    internal int Turn;
    internal RevisionFrame(CardModel source,string eventName,Creature? target=null)
    {
        Source=source;Event=eventName;Target=target??(source.IsMutable?source.CurrentTarget:null);
        if(!source.IsMutable||source.Owner==null)return;
        var owner=source.Owner;var combat=owner.PlayerCombatState;
        Turn=combat?.TurnNumber??0;
        Before["hp"]=owner.Creature.CurrentHp;Before["maxhp"]=owner.Creature.MaxHp;Before["block"]=owner.Creature.Block;
        Before["energy"]=combat?.Energy??0;Before["stars"]=combat?.Stars??0;Before["turn"]=Turn;
        Before["hand"]=combat?.Hand.Cards.Count??0;Before["draw"]=combat?.DrawPile.Cards.Count??0;
        Before["discard"]=combat?.DiscardPile.Cards.Count??0;Before["exhaust"]=combat?.ExhaustPile.Cards.Count??0;
        Before["orb_count"]=combat?.OrbQueue.Orbs.Count??0;
        Before["orb_slots"]=combat?.OrbQueue.Capacity??0;
        if(source is MegaCrit.Sts2.Core.Models.Cards.Capacitor&&combat?.OrbQueue is {} queue&&queue.Orbs.Count==queue.Capacity)
            RightOrb=queue.Orbs.FirstOrDefault();
        Before["arrow_count"]=combat?.Arrows.Count??0;
        Before["osty_alive"]=owner.IsOstyAlive?1:0;Before["osty_hp"]=owner.Osty?.CurrentHp??0;Before["osty_maxhp"]=owner.Osty?.MaxHp??0;
        Before["osty_existed"]=owner.Osty!=null?1:0;
        Before["melody_Wah"]=combat!=null&&MelodyCmd.PlayedWahThisTurn(owner)?1:0;
        Before["melody_Cho"]=combat!=null&&MelodyCmd.PlayedChoThisTurn(owner)?1:0;
        Before["melody_Ki"]=combat!=null&&MelodyCmd.PlayedKiThisTurn(owner)?1:0;
        Before["fatal_allowed"]=Target?.Powers.All(p=>p.ShouldOwnerDeathTriggerFatal())==true?1:0;
        Before["target_hp"]=Target?.CurrentHp??0;Before["target_block"]=Target?.Block??0;
        Before["target_maxhp"]=Target?.MaxHp??0;
        Before["target_attacking"]=Target?.Monster?.IntendsToAttack==true?1:0;
        foreach(var power in owner.Creature.Powers)Before["owner_"+power.GetType().Name]=power.Amount;
        if(Target!=null)foreach(var power in Target.Powers)Before["target_"+power.GetType().Name]=power.Amount;
        if(combat!=null)
        {
            foreach(var type in new[]{PileType.Hand,PileType.Draw,PileType.Discard,PileType.Exhaust})
                Cards["before_"+type]=type.GetPile(owner)!.Cards.ToList();
            foreach(var orb in combat.OrbQueue.Orbs)Add("before_orb_"+orb.GetType().Name,1);
        }
        var enemies=owner.Creature.CombatState?.HittableEnemies.ToArray()??[];
        Before["enemy_count"]=enemies.Length;
        Before["enemy_attacking"]=enemies.Count(e=>e.Monster?.IntendsToAttack==true);
        Before["target_attacking"]=Target?.Monster?.IntendsToAttack==true?1:0;
        Before["enemy_intent_total"]=enemies.Sum(e=>e.Monster?.NextMove.Intents.OfType<AttackIntent>().Sum(intent=>intent.GetTotalDamage([owner.Creature],e))??0);
        foreach(var enemy in enemies)
        {
            EnemyBefore[enemy]=enemy.Powers.GroupBy(p=>p.GetType().Name).ToDictionary(g=>g.Key,g=>g.Sum(p=>(decimal)p.Amount),StringComparer.Ordinal);
            EnemyBefore[enemy]["hp"]=enemy.CurrentHp;
            foreach(var power in enemy.Powers)
            {Add("before_enemy_"+power.GetType().Name+"_targets",1);Add("before_enemy_"+power.GetType().Name+"_sum",power.Amount);}
        }
        Before["printed_cost"]=Math.Max(0,source.EnergyCost.GetWithModifiers(CostModifiers.None));
        Before["cost"]=Math.Max(0,source.EnergyCost.GetWithModifiers(CostModifiers.All));
        Before["upgraded"]=source.IsUpgraded?1:0;Before["generated"]=CardMembership.IsGenerated(source)?1:0;
        foreach(var (name,variable) in source.DynamicVars)Before["var_"+name]=variable.BaseValue;
    }
    internal void Add(string key,decimal value)=>Values[key]=Values.GetValueOrDefault(key)+value;
    internal void AddCard(string key,CardModel card){if(!Cards.TryGetValue(key,out var list))Cards[key]=list=new();list.Add(card);}
    internal void PowerChanged(PowerModel power,decimal amount)
    {
        var name=power.GetType().Name;
        if(!PowerDeltas.TryGetValue(name,out var targets))PowerDeltas[name]=targets=new(ReferenceEqualityComparer.Instance);
        targets[power.Owner]=targets.GetValueOrDefault(power.Owner)+amount;
        if(amount>0&&power.Type==PowerType.Debuff&&power.Owner.Side!=Source.Owner.Creature.Side)DebuffTargets.Add(power.Owner);
    }
    internal IEnumerable<CardModel> Items(string key)=>Cards.GetValueOrDefault(key)??[];
    internal decimal Resolve(string key)
    {
        if(key=="one")return 1;
        if(key.StartsWith("before_",StringComparison.Ordinal))
        {
            var tail=key[7..];
            if(Before.TryGetValue(tail,out var value))return value;
            if(Values.TryGetValue(key,out value))return value;
            if(tail.StartsWith("owner_",StringComparison.Ordinal)||tail.StartsWith("target_",StringComparison.Ordinal)||tail.StartsWith("orb_",StringComparison.Ordinal)||tail.StartsWith("enemy_",StringComparison.Ordinal))return 0;
        }
        if(Values.TryGetValue(key,out var stored))return stored;
        if(key.StartsWith("resource_",StringComparison.Ordinal))return 0;
        if(key=="hits_targets")return HitTargets.Count;
        if(key=="kills")return KilledTargets.Count;
        if(key=="first_poison_targets")return FirstPoisonTargets.Count;
        if(key=="first_doom_targets")return FirstDoomTargets.Count;
        if(key=="first_vulnerable_targets")return FirstVulnerableTargets.Count;
        if(key=="first_weak_vulnerable_targets")return FirstWeakTargets.Intersect(FirstVulnerableTargets).Count();
        if(key=="strength_lost_targets")return PowerDeltas.GetValueOrDefault("StrengthPower")?.Count(p=>p.Key.Side!=Source.Owner.Creature.Side&&p.Value<0)??0;
        if(key=="doom_crossed_10_targets")return PowerDeltas.GetValueOrDefault("DoomPower")?.Count(p=>p.Value>0&&EnemyBefore.GetValueOrDefault(p.Key)?.GetValueOrDefault("DoomPower")<10&&p.Key.Powers.Any(power=>power.GetType().Name=="DoomPower"&&power.Amount>=10))??0;
        if(key=="poison_crossed_10")return PowerDeltas.GetValueOrDefault("PoisonPower")?.Where(p=>p.Value>0).Sum(p=>Math.Floor(((EnemyBefore.GetValueOrDefault(p.Key)?.GetValueOrDefault("PoisonPower")??0)+p.Value)/10)-Math.Floor((EnemyBefore.GetValueOrDefault(p.Key)?.GetValueOrDefault("PoisonPower")??0)/10))??0;
        if(key=="doom_reached_hp_targets")return PowerDeltas.GetValueOrDefault("DoomPower")?.Count(p=>p.Value>0&&(EnemyBefore.GetValueOrDefault(p.Key)?.GetValueOrDefault("DoomPower")??0)<(EnemyBefore.GetValueOrDefault(p.Key)?.GetValueOrDefault("hp")??0)&&p.Value+(EnemyBefore.GetValueOrDefault(p.Key)?.GetValueOrDefault("DoomPower")??0)>=p.Key.CurrentHp)??0;
        if(key=="other_hit_targets")return HitTargets.Count(t=>t!=Target);
        if(key=="other_debuff_targets")return DebuffTargets.Count(t=>t!=Target);
        if(key=="native_draw_discard_different")return Items("drawn").Any()&&Items("discarded").Any()&&!Items("drawn").Select(c=>c.Type).Intersect(Items("discarded").Select(c=>c.Type)).Any()?1:0;
        if(key.StartsWith("poison_damage_targets_",StringComparison.Ordinal))return HpDamage.Count(p=>p.Value>=decimal.Parse(key[22..],System.Globalization.CultureInfo.InvariantCulture));
        if(key.StartsWith("native_var_",StringComparison.Ordinal))return Source.DynamicVars.TryGetValue(key[11..],out var variable)?variable.BaseValue:0;
        if(key.StartsWith("native_power_var_",StringComparison.Ordinal))return NativePower?.DynamicVars.TryGetValue(key[17..],out var powerVariable)==true?powerVariable.BaseValue:0;
        if(key.StartsWith("native_method_",StringComparison.Ordinal))return NativeMethod==key[14..]?1:0;
        if(key.StartsWith("after_",StringComparison.Ordinal))return After(key[6..]);
        if(key=="upgrade_level")return Source.CurrentUpgradeLevel;
        if(key=="native_battle_draws")return MegaCrit.Sts2.Core.Combat.CombatManager.Instance.History.Entries.OfType<MegaCrit.Sts2.Core.Combat.History.Entries.CardDrawnEntry>().Count(e=>e.Actor==Source.Owner.Creature);
        if(key=="native_forge_damage")return Source is MegaCrit.Sts2.Core.Models.Cards.SovereignBlade?Math.Max(0,(decimal)HarmonyLib.AccessTools.Field(Source.GetType(),"_currentDamage").GetValue(Source)!-10):0;
        if(key.StartsWith("native_type_",StringComparison.Ordinal))return Source.Type.ToString()==key[12..]?1:0;
        if(key=="subject_cost")return Subject==null?0:Math.Max(0,Subject.EnergyCost.GetWithModifiers(CostModifiers.None));
        if(key=="subject_current_cost")return Subject==null?0:Math.Max(0,CovenantTerminal.For(Subject.Owner)?.CurrentCostAtPlay(Subject)??CardMembership.CurrentPlayCost(Subject));
        if(key=="subject_upgraded")return Subject?.IsUpgraded==true?1:0;
        if(key.StartsWith("subject_",StringComparison.Ordinal)&&!key.StartsWith("subject_spent_",StringComparison.Ordinal)&&!key.StartsWith("subject_turn_",StringComparison.Ordinal)&&!key.StartsWith("subject_before_",StringComparison.Ordinal)&&!key.StartsWith("subject_native_",StringComparison.Ordinal))return Subject!=null&&Filter(Subject,key[8..])?1:0;
        if(key=="target_doom")return Target?.Powers.FirstOrDefault(p=>p.GetType().Name=="DoomPower")?.Amount??0;
        if(key=="act")return Source.IsMutable&&Source.Owner?.RunState!=null?Source.Owner.RunState.CurrentActIndex+1:1;
        foreach(var listName in new[]{"returned_from_Exhaust","returned_from_Discard","drawn","discarded","exhausted","generated","returned","played","retained","upgraded","to_top","transformed_original","transformed","discounted","removed","before_hand","before_draw","before_discard","before_exhaust"})
        {
            var storage=listName.StartsWith("before_",StringComparison.Ordinal)?"before_"+char.ToUpperInvariant(listName[7])+listName[8..]:listName;
            if(key==listName+"_count")return Items(storage).Count();
            if(key==listName+"_types")return Items(storage).Select(c=>c.Type).Distinct().Count();
            if(key==listName+"_cost_sum")return Items(storage).Sum(c=>Math.Max(0,c.EnergyCost.GetWithModifiers(CostModifiers.None)));
            if(key==listName+"_costplus_sum")return Items(storage).Sum(c=>1+Math.Max(0,c.EnergyCost.GetWithModifiers(CostModifiers.None)));
            if(key.StartsWith(listName+"_",StringComparison.Ordinal))return Items(storage).Count(c=>Filter(c,key[(listName.Length+1)..]));
        }
        if(key.StartsWith("power_",StringComparison.Ordinal)||key.StartsWith("native_",StringComparison.Ordinal)||key.StartsWith("gain_",StringComparison.Ordinal)||key.StartsWith("hit_",StringComparison.Ordinal)||key.StartsWith("orb_",StringComparison.Ordinal)||key.StartsWith("spent_",StringComparison.Ordinal)||key.StartsWith("turn_",StringComparison.Ordinal)||key.StartsWith("battle_",StringComparison.Ordinal)||key.StartsWith("transformed_into_",StringComparison.Ordinal)||key.StartsWith("observed_",StringComparison.Ordinal)||key.StartsWith("subject_",StringComparison.Ordinal))return 0;
        throw new InvalidDataException("Unknown revision metric: "+key+" ("+Source.GetType().Name+")");
    }
    private decimal After(string key)
    {
        var owner=Source.Owner;
        if(key=="target_hp")return Target?.CurrentHp??0;
        if(key=="target_block")return Target?.Block??0;
        if(key.StartsWith("owner_",StringComparison.Ordinal))return owner.Creature.Powers.FirstOrDefault(p=>p.GetType().Name==key[6..])?.Amount??0;
        if(key.StartsWith("target_",StringComparison.Ordinal))return Target?.Powers.FirstOrDefault(p=>p.GetType().Name==key[7..])?.Amount??0;
        return key switch
        {
            "block"=>owner.Creature.Block,"hp"=>owner.Creature.CurrentHp,"maxhp"=>owner.Creature.MaxHp,
            "energy"=>owner.PlayerCombatState?.Energy??0,"stars"=>owner.PlayerCombatState?.Stars??0,
            "osty_alive"=>owner.IsOstyAlive?1:0,"osty_hp"=>owner.Osty?.CurrentHp??0,"osty_maxhp"=>owner.Osty?.MaxHp??0,
            "orb_count"=>owner.PlayerCombatState?.OrbQueue.Orbs.Count??0,
            "orb_types"=>owner.PlayerCombatState?.OrbQueue.Orbs.Select(o=>o.GetType()).Distinct().Count()??0,
            "melodies"=>owner.PlayerCombatState==null?0:MelodyCmd.MelodiesThisTurn(owner),
            "target_hp"=>Target?.CurrentHp??0,
            _=>throw new InvalidDataException("Unknown revision result metric: "+key)
        };
    }
    internal static bool Filter(CardModel card,string filter)=>filter switch
    {
        "Attack"=>card.Type==CardType.Attack,"Skill"=>card.Type==CardType.Skill,"Power"=>card.Type==CardType.Power,
        "gains_block"=>card.GainsBlock,
        "Status"=>card.Type==CardType.Status,"Curse"=>card.Type==CardType.Curse,
        "waste"=>card.Type is CardType.Status or CardType.Curse,
        "emotion"=>card.GetType().Name is "Joy" or "Sadness",
        "zero"=>CardMembership.IsCurrentZero(card),"heavy"=>CardMembership.IsPrintedHeavy(card),
        "heavy_attack"=>card.Type==CardType.Attack&&CardMembership.IsPrintedHeavy(card),
        "heavy_skill"=>card.Type==CardType.Skill&&CardMembership.IsPrintedHeavy(card),
        "orbs"=>NativeCapabilities.Of(card).Orbs,"doom"=>NativeCapabilities.Of(card).Doom,"forge"=>NativeCapabilities.Of(card).Forge,
        "guard_or_stretch"=>card.GainsBlock||NativeCapabilities.Of(card).Stretch,
        "wah_ki"=>card.Tags.Any(t=>t is CardTag.Wah or CardTag.Ki),
        "Sly"=>card.IsSlyThisTurn,"Ethereal"=>card.Keywords.Contains(CardKeyword.Ethereal),"Retain"=>card.ShouldRetainThisTurn,
        "star"=>card.HasStarCostX||card.BaseStarCost>=0,"rare"=>card.Rarity==CardRarity.Rare,
        "colorless"=>card.Pool is MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool,
        "common"=>card.Rarity==CardRarity.Common,"tarot"=>TarotCards.All.Any(c=>c.Id==card.Id),
        "melody"=>card.Tags.Any(t=>t is CardTag.Wah or CardTag.Cho or CardTag.Ki),
        "Strike"=>CardMembership.IsStrikeCard(card),"Shoot"=>card.Tags.Contains(CardTag.Shoot),
        _ when filter.StartsWith("class_",StringComparison.Ordinal)=>card.GetType().Name==filter[6..],
        _=>throw new InvalidDataException("Unknown revision card filter: "+filter)
    };
}
