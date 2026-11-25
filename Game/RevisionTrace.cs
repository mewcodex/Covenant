using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Commands.Builders;

namespace Covenant.Game;

/// <summary>Tracks native causes across awaits. Covenant-generated actions have a separate, suppressed cause.</summary>
internal static partial class RevisionTrace
{
    internal sealed class Scope
    {
        internal AbstractModel Model=null!;
        internal Scope? Parent;
        internal RevisionFrame[] Frames=[];
        internal bool Suppressed;
        internal string Method="";
        internal PileType StartingPile;
        internal CardModel? Subject;
        internal PlayerChoiceContext? ChoiceContext;
        internal int OrbGenerationIndex;
        internal OrbModel? ReworkOrb;
        internal Player? ReworkPlayer;
    }
    private sealed class PowerSources
    {
        internal readonly List<CardModel> Cards=new();
        internal readonly Dictionary<CardModel,int> Applications=new(ReferenceEqualityComparer.Instance);
    }
    private static readonly ConditionalWeakTable<PowerModel,PowerSources> PowerOrigins=new();
    private static readonly AsyncLocal<Scope?> Ambient=new();
    private static readonly AsyncLocal<AutoPlayType?> AutoReason=new();
    private static readonly ConcurrentDictionary<MethodBase,Dictionary<string,int>> Arguments=new();
    internal static Scope? Current=>Ambient.Value;
    private sealed class Suppression:IDisposable
    {
        private readonly Scope? _prior;
        internal Suppression(CovenantTerminal terminal){_prior=Current;Ambient.Value=new Scope{Model=terminal,Parent=_prior,Suppressed=true};}
        public void Dispose()=>Ambient.Value=_prior;
    }
    internal static IDisposable Suppress(CovenantTerminal terminal)=>new Suppression(terminal);
    internal static IEnumerable<CardModel> Sources(PowerModel power)=>PowerOrigins.TryGetValue(power,out var sources)?sources.Cards:[];
    internal static IEnumerable<PowerModel> LivePowers(Player owner)=>(owner.Creature.CombatState?.Creatures??[owner.Creature]).SelectMany(c=>c.Powers);
    internal static bool HasLivePower(CardModel card,RevisionProgram program)=>card.IsMutable&&card.Owner!=null&&LivePowers(card.Owner)
        .Any(p=>IsPowerSource(p,card,program));
    internal static bool IsPowerSource(PowerModel power,CardModel card,RevisionProgram program)=>
        program.NativePowers.Contains(power.GetType().Name)&&Sources(power).Contains(card)&&HasReplayCharge(power,card)&&HasCostCharge(power,card);
    internal static void Associate(PowerModel power,CardModel source)
    {
        var values=PowerOrigins.GetOrCreateValue(power).Cards;if(!values.Contains(source))values.Add(source);
    }
    private static void RecordPowerApplication(PowerModel power,CardModel source)
    {
        Associate(power,source);
        var applications=PowerOrigins.GetOrCreateValue(power).Applications;
        applications[source]=(int)Math.Min(int.MaxValue,(long)applications.GetValueOrDefault(source)+1);
    }
    internal static int PowerApplications(PowerModel power,CardModel source)=>PowerOrigins.TryGetValue(power,out var origins)?origins.Applications.GetValueOrDefault(source):0;
    internal static Scope BeginCard(CardModel card,PlayerChoiceContext context)
    {
        var scope=Enter(card,"OnPlayWrapper");
        scope.StartingPile=card.Pile?.Type??PileType.None;
        scope.ChoiceContext=context;
        return scope;
    }
    internal static PlayerChoiceContext? FindChoiceContext(Player player)
    {
        for(var scope=Current;scope!=null;scope=scope.Parent)
            if(scope.ChoiceContext is {} context&&context is not ThrowingPlayerChoiceContext&&
                scope.Model is CardModel card&&card.Owner==player)
                return context;
        return null;
    }
    private static Scope Enter(AbstractModel model,string method)
    {
        var scope=new Scope{Parent=Current,Model=model,Method=method,Suppressed=model.GetType().Assembly!=typeof(CardModel).Assembly};
        IEnumerable<CardModel> sources=model is CardModel card?[card]:model is PowerModel power?Sources(power):[];
        if(!scope.Suppressed)scope.Frames=sources.Where(c=>c.IsMutable&&c.Owner!=null&&RevisionDescriptions.Program(c) is {} program&&
                (model is not PowerModel p||program.NativePowers.Contains(p.GetType().Name)||program.CausalPowers?.Contains(p.GetType().Name)==true))
            .Select(c=>new RevisionFrame(c,model is PowerModel?"native":"native_card"){NativeMethod=method,NativePower=model as PowerModel}).ToArray();
        Ambient.Value=scope;return scope;
    }
    internal static void Restore(Scope? scope){if(scope!=null)Ambient.Value=scope.Parent;}
    internal static async Task Complete(Scope scope,Task original)
    {
        try
        {
            await original;
            if(scope.ReworkOrb is {} orb&&scope.Model is LoopPower loop&&scope.ReworkPlayer is {} loopPlayer&&CovenantTerminal.For(loopPlayer) is {} looping)
                await looping.FinishReworkLoop(loop,orb);
            if(scope.Method=="BeforeHandDraw"&&scope.Model is EnnuiPower ennui&&scope.ReworkPlayer is {} player&&
                CovenantTerminal.ReworkSource<Ennui>(ennui) is {} source&&source.Owner==player&&CovenantTerminal.For(player) is {} ennuiTerminal&&ennuiTerminal.ReworkRoll(source))
                await CardPileCmd.AddGeneratedCardToCombat(player.Creature.CombatState!.CreateCard<Joy>(player),PileType.Hand,player);
            if(scope.Model is PowerModel replayPower&&scope.Method=="AfterModifyingCardPlayCount"&&scope.Frames.FirstOrDefault()?.Subject is {} affectedCard)
                AttachNativeReplays(replayPower,affectedCard);
            if(scope.Model is QuietusPower quietus&&scope.Method==nameof(QuietusPower.AfterAttack)&&!quietus.Owner.Powers.Contains(quietus))
                foreach(var frame in scope.Frames)frame.Add("native_quietus_consumed",1);
            foreach(var frame in scope.Frames)
                if(frame.Source.Owner!=null&&CovenantTerminal.For(frame.Source.Owner) is {} terminal)await terminal.FireRevision(frame);
        }
        finally{Restore(scope);}
    }
    internal static RevisionFrame? FindPlay(CardModel card)
    {
        for(var scope=Current;scope!=null;scope=scope.Parent)
            if(scope.Frames.FirstOrDefault(f=>f.Source==card&&f.Event=="play") is {} frame)return frame;
        return null;
    }
    private static void CaptureResolvedX(CardModel __instance,int __result)
    {
        if(Current is {Suppressed:false} scope&&scope.Model==__instance)
            foreach(var frame in scope.Frames)
                if(frame.Event=="play"&&frame.Source==__instance)frame.Values["x_value"]=__result;
    }
    private static object? Argument(MethodBase method,object[] args,string name)
    {
        var names=Arguments.GetOrAdd(method,m=>m.GetParameters().Select((p,i)=>(p.Name!,i)).ToDictionary(p=>p.Item1,p=>p.i));
        return names.TryGetValue(name,out var index)?args[index]:null;
    }
    private static void NativeScopeStart(object __instance,MethodBase __originalMethod,object[] __args,out Scope? __state)
    {
        __state=__instance is AbstractModel model?Enter(model,__originalMethod.Name):null;
        if(__state==null)return;
        if(__instance is LoopPower or EnnuiPower)__state.ReworkPlayer=__args.OfType<Player>().FirstOrDefault();
        if(__instance is LoopPower&&__state.Method=="AfterPlayerTurnStart"&&__state.ReworkPlayer is {} loopOwner)
            __state.ReworkOrb=loopOwner.PlayerCombatState?.OrbQueue.Orbs.FirstOrDefault();
        __state.Subject=__args.OfType<CardModel>().FirstOrDefault()??__args.OfType<CardPlay>().FirstOrDefault()?.Card??(__args.OfType<AttackCommand>().FirstOrDefault()?.ModelSource as CardModel);
        foreach(var frame in __state.Frames)
        {
            frame.Subject=__state.Subject;
            foreach(var (parameter,index) in __originalMethod.GetParameters().Select((p,i)=>(p,i)))
            {
                if(__args[index] is CardModel card)frame.Subject=card;
                else if(__args[index] is CardPlay play)frame.Subject=play.Card;
                else if(__args[index] is int integer)frame.Values["native_arg_"+parameter.Name]=integer;
                else if(__args[index] is decimal number)frame.Values["native_arg_"+parameter.Name]=number;
            }
        }
        ReadModifierReceipt(__state,__args);
        foreach(var frame in __state.Frames)
            if(frame.NativePower!=null)foreach(var (name,variable) in frame.NativePower.DynamicVars)frame.Before["power_var_"+name]=variable.BaseValue;
    }
    private static void NativeScopeTask(Scope? __state,ref Task __result)
    {if(__state!=null)__result=Complete(__state,__result);}
    private static void NativeScopeEnd(Scope? __state)=>Restore(__state);
    private static void Applying(PowerModel power,CardModel? cardSource,Creature target,ref decimal amount)
    {
        if(power is DoomPower&&amount>0&&target.IsEnemy&&Current is {Suppressed:false,Model:ReaperFormPower reaper}&&CovenantTerminal.For(reaper.Owner.Player) is {} terminal)
            amount+=terminal.ReworkDoomBonus(reaper,amount);
        if(cardSource!=null){Associate(power,cardSource);return;}
        if(Current is {Suppressed:false} scope)foreach(var frame in scope.Frames)Associate(power,frame.Source);
    }
    private static void Upgraded(CardModel __instance)
    {
        if(Current is not {Suppressed:false} scope)return;
        foreach(var frame in scope.Frames)frame.AddCard("upgraded",__instance);
    }
    internal sealed record EventRecord(string Name,object[] Args,MethodBase Method,Scope? Cause,RevisionFrame? Self=null,Dictionary<Covenant.Core.Bond,int>? Activation=null,ResourceSpend[]? Spends=null);
    private static void BeforeNativeHook(MethodBase __originalMethod,object[] __args,out EventRecord __state)
    {
        var method=__originalMethod;var name=method.Name;var scope=Current;
        T? Arg<T>(string key)=>Argument(method,__args,key) is T value?value:default;
        var card=Arg<CardModel>("card");var source=Arg<CardModel>("cardSource")??Arg<CardModel>("source");
        var spends=name==nameof(Hook.AfterEnergySpent)?ClaimPayment("energy"):name==nameof(Hook.AfterStarsSpent)?ClaimPayment("stars"):[];
        if(name==nameof(Hook.AfterEnergySpent))ClaimCosts();
        RevisionFrame? self=null;
        if(name==nameof(Hook.BeforeCardPlayed)&&Arg<CardPlay>("cardPlay") is {} play&&play.Card.IsMutable)
        {
            card=play.Card;
            self=new RevisionFrame(card,"play",play.Target);
            self.Values["spent_energy"]=play.Resources.EnergySpent;self.Values["spent_stars"]=play.Resources.StarsSpent;
            self.Values["native_autoplay"]=play.IsAutoPlay?1:0;self.Values["native_play_index"]=play.PlayIndex;self.Values["native_play_count"]=play.PlayCount;
            self.Values["x_value"]=play.Resources.EnergyValue;
            self.Values["native_sly"]=AutoReason.Value==AutoPlayType.SlyDiscard?1:0;
            self.Values["native_from_Exhaust"]=scope?.StartingPile==PileType.Exhaust?1:0;
            self.Values["native_from_Draw"]=scope?.StartingPile==PileType.Draw?1:0;
            self.Values["native_parent_endturn"]=scope?.Parent?.Method is {} methodName&&(methodName.Contains("TurnEnd",StringComparison.Ordinal)||methodName.Contains("PostPlay",StringComparison.Ordinal))?1:0;
            if(scope?.Parent is {Suppressed:false} parent)foreach(var frame in parent.Frames)frame.AddCard("played",card);
            if(scope is {Suppressed:false}&&scope.Model==card)scope.Frames=[self];
            CovenantTerminal.For(card.Owner)?.RevisionPlayStarting(self);
            AttachPaidCostOrigins(card);
        }
        var frames=scope is {Suppressed:false}?scope.Frames:[];
        if(name==nameof(Hook.BeforeCardRemoved)&&card!=null)foreach(var frame in frames)frame.AddCard("removed",card);
        if(name==nameof(Hook.BeforeCardRemoved)&&card?.IsMutable==true)
            CovenantTerminal.For(card.Owner)?.RemoveDivineMark(card);
        if(name==nameof(Hook.AfterCardDrawn)&&card!=null)
        {foreach(var frame in frames)frame.AddCard("drawn",card);self=new RevisionFrame(card,"draw");}
        else if(name==nameof(Hook.AfterCardGeneratedForCombat)&&card!=null)
        {
            if(scope!=null)CovenantTerminal.For(card.Owner)?.ReworkGenerated(card,scope);
            foreach(var frame in frames){frame.AddCard("generated",card);CovenantTerminal.For(frame.Source.Owner)?.RevisionAttach(frame.Source,card,"generated");}
        }
        else if(name==nameof(Hook.AfterCardDiscarded)&&card!=null)
        {foreach(var frame in frames)frame.AddCard("discarded",card);self=new RevisionFrame(card,"discard");self.Values["native_was_sly"]=card.IsSlyThisTurn?1:0;}
        else if(name==nameof(Hook.AfterCardExhausted)&&card!=null)
        {
            CovenantTerminal.For(card.Owner)?.RevisionCountResource("exhausted",1);
            foreach(var frame in frames)frame.AddCard("exhausted",card);
            self=new RevisionFrame(card,Arg<bool>("causedByEthereal")?"ethereal":"exhaust")
            {
                Activation=CardMembership.CountsForExhaust(card)
            };
        }
        else if(name==nameof(Hook.AfterCardChangedPiles)&&card!=null)
        {
            var old=Arg<PileType>("oldPile");var pile=card.Pile?.Type;
            if(pile==PileType.Hand&&old!=PileType.Hand)
            {
                foreach(var frame in frames){frame.AddCard("returned",card);frame.AddCard("returned_from_"+old,card);}
                foreach(var frame in frames)CovenantTerminal.For(frame.Source.Owner)?.RevisionAttach(frame.Source,card,"returned");
                self=new RevisionFrame(card,"return_hand");
                self.NativeMethod=scope?.Method??"";
                self.Values["native_return_from_"+old]=1;
                self.Values["native_return_at_start"]=scope?.Method is "BeforeHandDraw" or "BeforeTurnStart" or "AfterTurnStart" or "AfterPlayerTurnStart"?1:0;
            }
            if(pile==PileType.Draw&&old!=PileType.Draw&&card.Pile!.Cards.FirstOrDefault()==card)
                foreach(var frame in frames){frame.AddCard("to_top",card);CovenantTerminal.For(frame.Source.Owner)?.RevisionAttach(frame.Source,card,"top");}
        }
        else if(name==nameof(Hook.AfterDamageReceived)&&Arg<Creature>("target") is {} target&&Arg<DamageResult>("result") is {} damage)
        {
            var dealer=Arg<Creature>("dealer");
            PoisonDamageFinished(target,dealer,source,Arg<ValueProp>("props"),false);
            if(target.Player is {} hurt&&(dealer==target||dealer==null&&
                (source?.Owner==hurt||frames.Any(frame=>frame.Source.Owner==hurt))))
                CovenantTerminal.For(hurt)?.ReworkSelfDamage(dealer,damage.UnblockedDamage);
            foreach(var frame in frames)
            {
                if(target==frame.Source.Owner.Creature){frame.Add("hit_self_hp",damage.UnblockedDamage);frame.Add("hit_self_blocked",damage.BlockedDamage);}
                else if(target.Side!=frame.Source.Owner.Creature.Side)
                {
                    frame.HitTargets.Add(target);frame.Add("hit_count",1);frame.Add("hit_damage",damage.UnblockedDamage+damage.BlockedDamage);
                    if(damage.WasBlockBroken&&source==frame.Source&&source is Bludgeon)frame.Add("rework_broke_block",1);
                    frame.HpDamage[target]=frame.HpDamage.GetValueOrDefault(target)+damage.UnblockedDamage;
                    frame.Add("hit_unblocked",damage.UnblockedDamage);if(damage.UnblockedDamage>0)frame.Add("hit_unblocked_count",1);
                    CovenantTerminal.For(frame.Source.Owner)?.RevisionDamage(frame.Source,damage.UnblockedDamage+damage.BlockedDamage);
                }
            }
            if(source!=null&&source.Owner?.Creature==target){self=new RevisionFrame(source,"self_damage");self.Add("hit_self_hp",damage.UnblockedDamage);}
        }
        else if(name==nameof(Hook.AfterDeath)&&Arg<Creature>("creature") is {} dead&&!Arg<bool>("wasRemovalPrevented"))
        {foreach(var frame in frames){if(dead.Side!=frame.Source.Owner.Creature.Side)frame.KilledTargets.Add(dead);else if(dead==frame.Source.Owner.Osty)frame.Add("native_osty_killed",1);}}
        else if(name==nameof(Hook.AfterBlockGained)&&Arg<Creature>("creature") is {} receiver)
        {foreach(var frame in frames)if(receiver==frame.Source.Owner.Creature){frame.Add("gain_block",Arg<decimal>("amount"));frame.Add("gain_block_count",1);}}
        else if(name==nameof(Hook.AfterPowerAmountChanged)&&Arg<PowerModel>("power") is {} power)
        {
            if(source!=null)Associate(power,source);
            var amount=Arg<decimal>("amount");
            spends=RecordPowerCredits(power,source,amount,scope);
            if(amount>0)
            {
                if(source!=null)RecordPowerApplication(power,source);
                else foreach(var origin in frames.Select(f=>f.Source).Distinct())RecordPowerApplication(power,origin);
            }
            if(amount>0)RecordReplayGrant(power,source,amount);
            if(amount>0&&scope is {Suppressed:false})foreach(var frame in frames)Associate(power,frame.Source);
            foreach(var frame in frames)
            {
                var own=power.Owner==frame.Source.Owner.Creature;var prefix=own?"power_owner_":"power_";
                frame.Add(prefix+power.GetType().Name,amount);
                if(!own&&amount>0&&source==frame.Source)frame.Add("power_applications_"+power.GetType().Name,1);
                frame.PowerChanged(power,amount);
                if(own&&amount>0&&power.GetType().Name=="RetainHandPower")foreach(var retained in frame.Source.Owner.PlayerCombatState!.Hand.Cards){frame.AddCard("retained",retained);CovenantTerminal.For(frame.Source.Owner)?.RevisionAttach(frame.Source,retained,"retained");}
                if(!own&&power.Amount-amount<=0&&amount>0)
                {
                    if(power.GetType().Name=="PoisonPower")frame.FirstPoisonTargets.Add(power.Owner);
                    if(power.GetType().Name=="DoomPower")frame.FirstDoomTargets.Add(power.Owner);
                    if(power.GetType().Name=="VulnerablePower")frame.FirstVulnerableTargets.Add(power.Owner);
                    if(power.GetType().Name=="WeakPower")frame.FirstWeakTargets.Add(power.Owner);
                }
            }
            if(source!=null&&source.Owner?.Creature==power.Owner&&amount>0)
            {self=new RevisionFrame(source,"self_power");self.Add("power_owner_"+power.GetType().Name,amount);}
        }
        else if(name==nameof(Hook.AfterStarsGained))
        {foreach(var frame in frames)if(Arg<Player>("gainer")==frame.Source.Owner)frame.Add("gain_stars",Arg<int>("amount"));}
        else if(name==nameof(Hook.AfterOrbChanneled)&&Arg<OrbModel>("orb") is {} orb)
        {var origins=ChannelOrigins(scope);OrbCreated(orb,origins);foreach(var frame in frames.Where(f=>origins.Contains(f.Source))){frame.Add("orb_generated",1);frame.Add("orb_generated_"+orb.GetType().Name,1);if(frame.Resolve("before_orb_"+orb.GetType().Name)==0)frame.Add("orb_generated_newtype",1);}}
        else if(name==nameof(Hook.AfterOrbEvoked)&&Arg<OrbModel>("orb") is {} evoked)
        {foreach(var frame in frames){frame.Add("orb_evoked",1);frame.Add("orb_evoked_"+evoked.GetType().Name,1);}}
        else if(name==nameof(Hook.AfterForge))
        {foreach(var frame in frames){frame.Add("native_forges",1);frame.Add("native_forge_amount",Arg<decimal>("amount"));}}
        else if(name==nameof(Hook.AfterSummon))
        {foreach(var frame in frames){frame.Add("native_summons",1);frame.Add("native_summon_amount",Arg<decimal>("amount"));}}
        else if(name==nameof(Hook.AfterArrowShot))
        {
            if(Arg<MegaCrit.Sts2.Core.Entities.FoolArrows.ArrowSlot>("arrow")!=null&&Arg<Player>("owner") is {} archer)
            {
                foreach(var frame in frames)frame.Add("native_shots",1);
                CovenantTerminal.For(archer)?.RevisionObservedShot();
            }
        }
        else if(name==nameof(Hook.AfterAttack)&&Arg<AttackCommand>("command") is {} attack&&attack.Attacker?.PetOwner is {} petOwner)
        {foreach(var frame in frames)if(frame.Source.Owner==petOwner)frame.Add("native_osty_attacks",1);CovenantTerminal.For(petOwner)?.RevisionObservedOstyAttack();}
        else if(name==nameof(Hook.AfterArrowDislodged))
        {foreach(var frame in frames)frame.Add("native_dislodges",1);}
        else if(name==nameof(Hook.AfterArrowImpaled))
        {foreach(var frame in frames)frame.Add("native_impales",1);}
        if(self!=null&&self.Activation==null)self.Activation=CardMembership.Counts(self.Source.Owner,resolving:self.Source);
        var activation=name==nameof(Hook.AfterFlush)&&Arg<Player>("player") is {} flusher?CardMembership.Counts(flusher):null;
        __state=new(name,__args,method,scope,self,activation,spends);
    }
    private static void AfterNativeHook(EventRecord __state,ref Task __result)=>__result=CompleteEvent(__state,__result);
    private static async Task CompleteEvent(EventRecord record,Task original)
    {
        await original;
        await ResolveResourceSpends(record.Spends);
        if(record.Name==nameof(Hook.AfterOrbEvoked)&&Argument(record.Method,record.Args,"orb") is OrbModel evoked)
        {
            await OrbOriginEvent(evoked,"orb_origin_evoked");
            if(record.Cause is {Suppressed:false} cause)
                foreach(var origin in cause.Frames.Select(f=>f.Source).Distinct())
                    if(CovenantTerminal.For(origin.Owner) is {} evokingTerminal)await evokingTerminal.FireRevision(new RevisionFrame(origin,"own_orb_evoked"));
            CovenantTerminal.For(evoked.Owner)?.RefreshBattleStatuses();
        }
        if(record.Name==nameof(Hook.AfterOrbChanneled)&&Argument(record.Method,record.Args,"orb") is OrbModel channeled)
            CovenantTerminal.For(channeled.Owner)?.RefreshBattleStatuses();
        if(record.Name==nameof(Hook.AfterBlockGained)&&ActiveOrb.Value is {Orb:FrostOrb} frost&&ReferenceEquals(record.Cause,frost.Cause)&&record.Cause?.Suppressed!=true&&Argument(record.Method,record.Args,"amount") is decimal frostBlock&&frostBlock>0)
            await OrbOriginEvent(frost.Orb,"orb_origin_block");
        if(record.Self is {} frame&&frame.Source.IsMutable&&frame.Source.Owner!=null&&CovenantTerminal.For(frame.Source.Owner) is {} terminal)
        {
            if(record.Name==nameof(Hook.BeforeCardPlayed))
            {
                if(Argument(record.Method,record.Args,"cardPlay") is CardPlay play)await terminal.ReworkBeforePlay(new BlockingPlayerChoiceContext(),play);
                await terminal.RevisionObserve("observe_play",frame.Source,frame);
            }
            else
            {
                await terminal.FireRevision(frame);
                if(record.Name==nameof(Hook.AfterCardDiscarded))await terminal.RevisionAttachedEvent(frame.Source,"subject_discard",frame);
                if(record.Name==nameof(Hook.AfterCardExhausted))await terminal.RevisionAttachedEvent(frame.Source,"subject_exhaust",frame);
                if(record.Name==nameof(Hook.AfterCardDiscarded))await terminal.RevisionObserve("observe_discard",frame.Source,frame);
                if(record.Name==nameof(Hook.AfterCardExhausted))await terminal.RevisionObserve("observe_exhaust",frame.Source,frame);
                terminal.RefreshBattleStatuses();
            }
        }
        if(record.Name==nameof(Hook.AfterCardGeneratedForCombat)&&Argument(record.Method,record.Args,"card") is CardModel generated)
            if(CovenantTerminal.For(generated.Owner) is {} generatingTerminal)await generatingTerminal.RevisionObserve("observe_generate",generated);
        if(record.Name==nameof(Hook.AfterFlush)&&Argument(record.Method,record.Args,"player") is Player player&&Argument(record.Method,record.Args,"retainedCards") is IReadOnlyCollection<CardModel> retained)
        {
            if(CovenantTerminal.For(player) is {} retainingTerminal)
            {
                foreach(var card in retained)await retainingTerminal.FireRevision(new RevisionFrame(card,"retained"){Activation=record.Activation});
                await retainingTerminal.RevisionObserve("observe_retained",retained:retained,activation:record.Activation);
                retainingTerminal.RefreshBattleStatuses();
            }
        }
        if(record.Name==nameof(Hook.BeforeFlush)&&Argument(record.Method,record.Args,"player") is Player endingPlayer&&CovenantTerminal.For(endingPlayer) is {} endingTerminal)
            foreach(var held in endingPlayer.PlayerCombatState!.Hand.Cards.ToArray())await endingTerminal.RevisionAttachedEvent(held,"subject_endturn");
        if(record.Name==nameof(Hook.AfterPowerAmountChanged)&&Argument(record.Method,record.Args,"power") is PowerModel changed&&Argument(record.Method,record.Args,"amount") is decimal delta&&delta>0&&Argument(record.Method,record.Args,"applier") is Creature applier)
        {
            var owner=applier.Player??applier.PetOwner;
            if(owner!=null&&changed.Owner.Side!=owner.Creature.Side&&CovenantTerminal.For(owner) is {} applyingTerminal)
                await applyingTerminal.RevisionObserve("observe_power",observed:new Dictionary<string,decimal>{{"observed_"+changed.GetType().Name,delta}});
        }
        if(record.Name==nameof(Hook.AfterBlockGained)&&Argument(record.Method,record.Args,"creature") is Creature blocker&&blocker.Player is {} blockingPlayer&&Argument(record.Method,record.Args,"cardSource") is CardModel&&Argument(record.Method,record.Args,"amount") is decimal gainedBlock)
            if(CovenantTerminal.For(blockingPlayer) is {} blockingTerminal)await blockingTerminal.RevisionObserve("observe_card_block",observed:new Dictionary<string,decimal>{{"observed_block",gainedBlock}});
        if(record.Name==nameof(Hook.AfterEnergySpent)&&Argument(record.Method,record.Args,"card") is CardModel spendingCard&&Argument(record.Method,record.Args,"amount") is int spentEnergy)
            CovenantTerminal.For(spendingCard.Owner)?.RevisionCountResource("energy_spent",spentEnergy);
        if(record.Name==nameof(Hook.AfterStarsGained)&&Argument(record.Method,record.Args,"gainer") is Player starGainer&&Argument(record.Method,record.Args,"amount") is int gainedStars)
            CovenantTerminal.For(starGainer)?.RevisionCountResource("stars_gained",gainedStars);
        if(record.Name==nameof(Hook.AfterArrowShot)&&Argument(record.Method,record.Args,"arrow") is MegaCrit.Sts2.Core.Entities.FoolArrows.ArrowSlot&&
            Argument(record.Method,record.Args,"owner") is Player archer&&CovenantTerminal.For(archer) is {} archerTerminal)
        {
            await archerTerminal.RevisionObserve("observe_shot",Argument(record.Method,record.Args,"source") as CardModel);
            archerTerminal.RefreshBattleStatuses();
        }
        if(record.Name==nameof(Hook.AfterDamageReceived)&&Argument(record.Method,record.Args,"target") is Creature defender&&defender.Player is {} defendingPlayer&&Argument(record.Method,record.Args,"result") is DamageResult blocked&&blocked.WasFullyBlocked&&Argument(record.Method,record.Args,"props") is ValueProp properties&&properties.IsPoweredAttack())
            if(CovenantTerminal.For(defendingPlayer) is {} defendingTerminal)await defendingTerminal.RevisionObserve("observe_blocked");
    }
    internal static void Install(Harmony harmony)
    {
        InstallTransforms(harmony);
        InstallCreatureChanges(harmony);
        InstallReplays(harmony);
        InstallLocalCosts(harmony);
        InstallModifiers(harmony);
        InstallResources(harmony);
        InstallCosts(harmony);
        var captureX=new HarmonyMethod(typeof(RevisionTrace),nameof(CaptureResolvedX));
        harmony.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.ResolveEnergyXValue)),postfix:captureX);
        harmony.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.ResolveStarXValue)),postfix:captureX);
        InstallPoison(harmony);
        InstallOrbs(harmony);
        InstallSecondaryEvents(harmony);
        var native=typeof(CardModel).Assembly;
        foreach(var type in native.GetTypes().Where(t=>!t.IsAbstract&&(t.IsSubclassOf(typeof(PowerModel))||t.IsSubclassOf(typeof(CardModel)))))
            foreach(var method in type.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly).Where(m=>m.ReturnType==typeof(Task)&&(m.Name.StartsWith("After",StringComparison.Ordinal)||m.Name.StartsWith("Before",StringComparison.Ordinal))))
                harmony.Patch(method,prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(NativeScopeStart)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(NativeScopeTask)),finalizer:new HarmonyMethod(typeof(RevisionTrace),nameof(NativeScopeEnd)));
        foreach(var method in typeof(CovenantTerminal).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly).Where(m=>m.ReturnType==typeof(Task)&&(m.Name.StartsWith("After",StringComparison.Ordinal)||m.Name.StartsWith("Before",StringComparison.Ordinal))))
            harmony.Patch(method,prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(NativeScopeStart)),finalizer:new HarmonyMethod(typeof(RevisionTrace),nameof(NativeScopeEnd)));
        foreach(var name in new[]{nameof(Hook.BeforeCardPlayed),nameof(Hook.AfterCardDrawn),nameof(Hook.AfterCardGeneratedForCombat),nameof(Hook.AfterCardDiscarded),nameof(Hook.AfterCardExhausted),nameof(Hook.AfterCardChangedPiles),nameof(Hook.AfterDamageReceived),nameof(Hook.AfterDeath),nameof(Hook.AfterBlockGained),nameof(Hook.AfterPowerAmountChanged),nameof(Hook.AfterStarsGained),nameof(Hook.AfterStarsSpent),nameof(Hook.AfterOrbChanneled),nameof(Hook.AfterOrbEvoked),nameof(Hook.AfterForge),nameof(Hook.AfterSummon),nameof(Hook.AfterArrowShot),nameof(Hook.AfterArrowDislodged),nameof(Hook.AfterArrowImpaled),nameof(Hook.AfterAttack),nameof(Hook.AfterFlush),nameof(Hook.AfterEnergySpent),nameof(Hook.BeforeFlush),nameof(Hook.BeforeCardRemoved)})
            harmony.Patch(AccessTools.Method(typeof(Hook),name),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(BeforeNativeHook)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(AfterNativeHook)));
        harmony.Patch(AccessTools.Method(typeof(PowerCmd),nameof(PowerCmd.Apply),[typeof(PlayerChoiceContext),typeof(PowerModel),typeof(Creature),typeof(decimal),typeof(Creature),typeof(CardModel),typeof(bool)]),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(Applying)));
        harmony.Patch(AccessTools.Method(typeof(CardModel),nameof(CardModel.UpgradeInternal)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(Upgraded)));
        harmony.Patch(AccessTools.Method(typeof(PlayerCombatState),nameof(PlayerCombatState.GainEnergy)),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(EnergyStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(EnergyGained)));
        harmony.Patch(AccessTools.Method(typeof(PlayerCmd),nameof(PlayerCmd.GainEnergy)),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(CardEnergyStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(CardEnergyComplete)));
        harmony.Patch(AccessTools.Method(typeof(CardCmd),nameof(CardCmd.ApplySingleTurnRetain)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(Retained)));
        harmony.Patch(AccessTools.Method(typeof(CardCmd),nameof(CardCmd.ApplySingleTurnSly)),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(SlyStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(SlyApplied)));
        harmony.Patch(AccessTools.Method(typeof(CardCmd),nameof(CardCmd.ApplyKeyword)),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(KeywordsStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(KeywordsApplied)));
        harmony.Patch(AccessTools.Method(typeof(OrbCmd),nameof(OrbCmd.Passive)),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(OrbPassive)));
        harmony.Patch(AccessTools.Method(typeof(CreatureCmd),nameof(CreatureCmd.Stun),[typeof(Creature),typeof(string)]),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(Stunned)));
        harmony.Patch(AccessTools.Method(typeof(CardCmd),nameof(CardCmd.AutoPlay)),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(AutoStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(AutoFinished)),finalizer:new HarmonyMethod(typeof(RevisionTrace),nameof(AutoEnded)));
    }
    private static void EnergyStarting(PlayerCombatState __instance,out int __state)=>__state=__instance.Energy;
    private static void CardEnergyStarting(Player player,out (CardModel? Source,int Before) __state)
    {
        var source=Current is {Suppressed:false} scope?scope.Model switch
        {
            CardModel card=>card,
            PowerModel power=>Sources(power).FirstOrDefault(),
            _=>null
        }:null;
        __state=(source,source==null?0:player.PlayerCombatState?.Energy??0);
    }
    private static void CardEnergyComplete(Player player,(CardModel? Source,int Before) __state,ref Task __result)
    {
        if(__state.Source is {} source&&CovenantTerminal.For(player) is {} terminal)
            __result=AfterCardEnergy(__result,terminal,source,__state.Before);
    }
    private static async Task AfterCardEnergy(Task original,CovenantTerminal terminal,CardModel source,int before)
    {
        await original;
        await terminal.OnReflowCardEnergy(source,before);
    }
    private static void EnergyGained(PlayerCombatState __instance,int __state)
    {
        RecordEnergyGain(__instance,__state);
        if(Current is not {Suppressed:false} scope)return;
        foreach(var frame in scope.Frames)if(frame.Source.Owner.PlayerCombatState==__instance)frame.Add("gain_energy",Math.Max(0,__instance.Energy-__state));
    }
    private static void Retained(CardModel card)
    {if(Current is {Suppressed:false} scope)foreach(var frame in scope.Frames){frame.AddCard("retained",card);CovenantTerminal.For(frame.Source.Owner)?.RevisionAttach(frame.Source,card,"retained");}}
    private static void SlyStarting(CardModel card,out bool __state)=>__state=card.IsSlyThisTurn;
    private static void SlyApplied(CardModel card,bool __state){if(!__state&&card.IsSlyThisTurn)KeywordAttached(card,"Sly");}
    private static void KeywordsStarting(CardModel card,CardKeyword[] keywords,out CardKeyword[] __state)=>__state=keywords.Where(k=>!card.Keywords.Contains(k)).ToArray();
    private static void KeywordsApplied(CardModel card,CardKeyword[] __state)
    {foreach(var keyword in __state)KeywordAttached(card,keyword.ToString());}
    private static void KeywordAttached(CardModel card,string keyword)
    {
        if(Current is not {Suppressed:false} scope)return;
        foreach(var frame in scope.Frames)CovenantTerminal.For(frame.Source.Owner)?.RevisionAttach(frame.Source,card,"keyword_"+keyword);
    }
    private static void OrbPassive(OrbModel orb)
    {if(Current is {Suppressed:false} scope)foreach(var frame in scope.Frames)frame.Add("orb_passive_"+orb.GetType().Name,1);}
    private static void Stunned(Creature creature)
    {if(Current is {Suppressed:false} scope&&creature.Monster?.NextMove.Intents.Any(i=>i is MegaCrit.Sts2.Core.MonsterMoves.Intents.StunIntent)==true)foreach(var frame in scope.Frames)frame.Add("native_stunned",1);}
    private static void AutoStarting(AutoPlayType type,out AutoPlayType? __state){__state=AutoReason.Value;AutoReason.Value=type;}
    private static void AutoFinished(AutoPlayType? __state,ref Task __result){__result=CompleteAuto(__state,__result);AutoReason.Value=__state;}
    private static void AutoEnded(AutoPlayType? __state)=>AutoReason.Value=__state;
    private static async Task CompleteAuto(AutoPlayType? prior,Task task){try{await task;}finally{AutoReason.Value=prior;}}
}
