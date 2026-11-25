using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Commands.Builders;

namespace Covenant.Game;

internal static partial class RevisionTrace
{
    internal sealed class Credit(CardModel? source,int units,int turn,long id)
    {
        internal sealed class Token{internal bool Used;}
        internal Token Claim=new();
        internal readonly CardModel? Source=source;
        internal int Units=units;
        internal readonly int Turn=turn;
        internal readonly long Id=id;
        internal Credit Slice(int units)=>new(Source,units,Turn,Id){Claim=Claim};
    }
    internal sealed record ResourceSpend(string Resource,Credit Credit,CardModel? Subject);
    private sealed class ResourceBook
    {internal readonly List<Credit> Energy=new(),Stars=new();internal bool Initialized;}
    private sealed class CreditList{internal readonly List<Credit> Items=new();}
    private sealed class Payment(CardModel card,string resource,Payment? parent)
    {internal readonly CardModel Card=card;internal readonly string Resource=resource;internal readonly Payment? Parent=parent;internal bool Open=true;internal readonly List<ResourceSpend> Spends=new();}
    private static readonly ConditionalWeakTable<PlayerCombatState,ResourceBook> ResourceBooks=new();
    private static readonly ConditionalWeakTable<PowerModel,CreditList> PowerCredits=new();
    private static readonly AsyncLocal<Payment?> Paying=new();
    private static long _creditId;
    private static Credit NewCredit(CardModel? card,int units)=>new(card,units,card?.Owner.PlayerCombatState?.TurnNumber??0,Interlocked.Increment(ref _creditId));
    internal static void ClearResources(PlayerCombatState state)=>ResourceBooks.Remove(state);
    private static List<Credit> Consume(List<Credit> credits,int units)
    {
        var result=new List<Credit>();
        while(units>0&&credits.Count>0)
        {
            var credit=credits[0];var taken=Math.Min(units,credit.Units);
            if(taken>0)result.Add(credit.Slice(taken));
            credit.Units-=taken;units-=taken;if(credit.Units<=0)credits.RemoveAt(0);
        }
        return result;
    }
    private static void Reconcile(List<Credit> credits,int actual)
    {
        var known=credits.Sum(c=>(long)c.Units);
        if(known>actual)Consume(credits,(int)Math.Min(int.MaxValue,known-actual));
        else if(known<actual)credits.Add(NewCredit(null,actual-(int)known));
    }
    private static ResourceBook Book(PlayerCombatState state,int? energyBefore=null,int? starsBefore=null)
    {
        var book=ResourceBooks.GetOrCreateValue(state);
        if(!book.Initialized)
        {
            book.Initialized=true;
            if((energyBefore??state.Energy)>0)book.Energy.Add(NewCredit(null,energyBefore??state.Energy));
            if((starsBefore??state.Stars)>0)book.Stars.Add(NewCredit(null,starsBefore??state.Stars));
        }
        return book;
    }
    private static IEnumerable<Credit> Partition(IEnumerable<Credit> origins,int units,bool fresh)
    {
        var inputs=origins.Where(c=>c.Units>0).ToArray();var total=inputs.Sum(c=>(decimal)c.Units);
        if(total<=0){yield return NewCredit(null,units);yield break;}
        decimal running=0;var assigned=0;
        foreach(var credit in inputs)
        {
            running+=credit.Units;var next=(int)Math.Floor(units*running/total);var amount=next-assigned;assigned=next;
            if(amount>0)yield return fresh?NewCredit(credit.Source,amount):credit.Slice(amount);
        }
    }
    private static IEnumerable<Credit> CurrentCredits(int units)
    {
        if(Current is not {Suppressed:false} scope)return [NewCredit(null,units)];
        if(scope.Model is CardModel card)return [NewCredit(card,units)];
        if(scope.Model is PowerModel power&&PowerCredits.TryGetValue(power,out var credits))return Partition(credits.Items,units,true).ToArray();
        return [NewCredit(null,units)];
    }
    private static ResourceSpend[] RecordPowerCredits(PowerModel power,CardModel? source,decimal delta,Scope? scope)
    {
        if(power.GetType().Name is not ("VigorPower" or "AimPower" or "StretchPower" or "EnergyNextTurnPower" or "FriendshipPower" or "GenesisPower" or "PrepTimePower" or "ArcheryPower" or "HiddenRootsPower" or "FreeSkillPower" or "FreeAttackPower" or "FreePowerPower" or "FreeCardPower" or "VoidFormPower" or "VeilpiercerPower" or "AccelerantPower" or "SpinnerPower"))return [];
        var credits=PowerCredits.GetOrCreateValue(power).Items;
        Reconcile(credits,Math.Max(0,power.Amount-(int)delta));
        if(delta>0)
        {
            credits.AddRange(source!=null?[NewCredit(source,(int)delta)]:CurrentCredits((int)delta));return [];
        }
        var consumed=Consume(credits,Math.Max(0,-(int)delta));
        var method=scope?.Method;
        if(scope?.Model!=power||!(power.GetType().Name is "VigorPower" or "AimPower"&&method=="AfterAttack"||power.GetType().Name=="StretchPower"&&method=="AfterCardPlayed"))return [];
        var subject=scope.Subject;
        return consumed.Select(c=>new ResourceSpend(power.GetType().Name,c,subject)).ToArray();
    }
    private static void InstallResources(Harmony harmony)
    {
        foreach(var name in new[]{"SpendEnergy","SpendStars"})
            harmony.Patch(AccessTools.Method(typeof(CardModel),name),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(PaymentStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(PaymentFinished)),finalizer:new HarmonyMethod(typeof(RevisionTrace),nameof(PaymentEnded)));
        harmony.Patch(AccessTools.Method(typeof(PlayerCombatState),nameof(PlayerCombatState.GainStars)),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(StarsStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(StarsAdded)));
        foreach(var name in new[]{nameof(PlayerCombatState.LoseEnergy),nameof(PlayerCombatState.LoseStars)})
            harmony.Patch(AccessTools.Method(typeof(PlayerCombatState),name),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(ResourceLossStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(ResourceLost)));
        foreach(var name in new[]{nameof(PlayerCombatState.ResetEnergy),nameof(PlayerCombatState.AddMaxEnergyToCurrent)})
            harmony.Patch(AccessTools.Method(typeof(PlayerCombatState),name),prefix:new HarmonyMethod(typeof(RevisionTrace),nameof(EnergyResetStarting)),postfix:new HarmonyMethod(typeof(RevisionTrace),nameof(EnergyResetFinished)));
    }
    private static void PaymentStarting(CardModel __instance,MethodBase __originalMethod,out Payment __state)
    {__state=new(__instance,__originalMethod.Name=="SpendEnergy"?"energy":"stars",Paying.Value);Paying.Value=__state;}
    private static void PaymentFinished(Payment __state,ref Task __result){__result=CompletePayment(__state,__result);Paying.Value=__state.Parent;}
    private static void PaymentEnded(Payment __state)=>Paying.Value=__state.Parent;
    private static async Task CompletePayment(Payment payment,Task original){try{await original;}finally{Paying.Value=payment.Parent;}}
    private static ResourceSpend[] ClaimPayment(string resource)
    {
        if(Paying.Value is not {} payment||payment.Resource!=resource||!payment.Open)return [];
        payment.Open=false;return payment.Spends.ToArray();
    }
    private static void StarsStarting(PlayerCombatState __instance,out int __state)=>__state=__instance.Stars;
    private static void StarsAdded(PlayerCombatState __instance,Player ____player,int __state)
    {
        var book=Book(__instance,starsBefore:__state);Reconcile(book.Stars,__state);
        var added=CurrentCredits(Math.Max(0,__instance.Stars-__state)).ToArray();
        book.Stars.AddRange(added);
        if(added.Any(credit=>credit.Source!=null))CovenantTerminal.For(____player)?.RefreshBattleStatuses();
    }
    private static void ResourceLossStarting(PlayerCombatState __instance,MethodBase __originalMethod,out int __state)=>__state=__originalMethod.Name==nameof(PlayerCombatState.LoseEnergy)?__instance.Energy:__instance.Stars;
    private static void ResourceLost(PlayerCombatState __instance,Player ____player,MethodBase __originalMethod,int __state)
    {
        var energy=__originalMethod.Name==nameof(PlayerCombatState.LoseEnergy);var resource=energy?"energy":"stars";
        var book=Book(__instance,energy?__state:null,energy?null:__state);var credits=energy?book.Energy:book.Stars;
        Reconcile(credits,__state);var spent=Consume(credits,__state-(energy?__instance.Energy:__instance.Stars));
        if(Paying.Value is {Open:true} payment&&payment.Resource==resource&&payment.Card.Owner.PlayerCombatState==__instance)
            payment.Spends.AddRange(spent.Select(c=>new ResourceSpend(resource,c,payment.Card)));
        else if(spent.Any(credit=>credit.Source!=null))CovenantTerminal.For(____player)?.RefreshBattleStatuses();
    }
    private static void EnergyResetStarting(PlayerCombatState __instance,out int __state)=>__state=__instance.Energy;
    private static void EnergyResetFinished(PlayerCombatState __instance,Player ____player,MethodBase __originalMethod,int __state)
    {
        var book=Book(__instance);var reset=__originalMethod.Name==nameof(PlayerCombatState.ResetEnergy);
        if(reset)book.Energy.Clear();else Reconcile(book.Energy,__state);
        var added=Math.Max(0,__instance.Energy-(reset?0:__state));
        var friendship=____player.Creature.Powers.FirstOrDefault(p=>p.GetType().Name=="FriendshipPower");
        var bonus=Math.Min(added,friendship?.Amount??0);
        if(added>bonus)book.Energy.Add(NewCredit(null,added-bonus));
        if(bonus>0&&friendship!=null&&PowerCredits.TryGetValue(friendship,out var credits))book.Energy.AddRange(Partition(credits.Items,bonus,true));
        else if(bonus>0)book.Energy.Add(NewCredit(null,bonus));
    }
    private static void RecordEnergyGain(PlayerCombatState state,int before)
    {
        var book=Book(state,energyBefore:before);Reconcile(book.Energy,before);
        book.Energy.AddRange(CurrentCredits(Math.Max(0,state.Energy-before)));
    }
    private static async Task ResolveResourceSpends(IEnumerable<ResourceSpend>? spends)
    {
        if(spends==null)return;
        foreach(var group in spends.Where(s=>s.Credit.Source!=null&&s.Credit.Units>0).GroupBy(s=>(s.Credit.Source,s.Resource,s.Credit.Turn,s.Subject)))
        {
            var source=group.Key.Source!;var terminal=CovenantTerminal.For(source.Owner);if(terminal==null)continue;
            var frame=new RevisionFrame(source,"resource_spent"){Subject=group.Key.Subject,Scope="resource"};
            frame.Values["resource_"+group.Key.Resource]=1;
            frame.Values["resource_units"]=group.Sum(s=>s.Credit.Units);
            frame.Values["resource_issued_turn"]=group.Key.Turn;
            frame.Values["resource_turn"]=source.Owner.PlayerCombatState?.TurnNumber??0;
            frame.Values["resource_new_grants"]=group.Select(s=>s.Credit.Claim).Distinct().Count(c=>!c.Used);
            var fired=await terminal.FireRevision(frame);
            if(fired&&RevisionDescriptions.Program(source)?.Rules.Any(r=>r.Event=="resource_spent"&&r.Parameter=="once_grant")==true)
                foreach(var token in group.Select(s=>s.Credit.Claim))token.Used=true;
            terminal.RefreshBattleStatuses();
        }
    }
    internal static bool HasResourceCredits(CardModel source,bool unusedOnly=false,string? condition=null)
        =>ResourceBalances(source,unusedOnly,condition).Any();

    internal static string StatusResources(CardModel source,bool unusedOnly,string condition)
        =>string.Join("、",ResourceBalances(source,unusedOnly,condition));

    private static IEnumerable<string> ResourceBalances(CardModel source,bool unusedOnly,string? condition)
    {
        if(source.Owner.PlayerCombatState is not {} state)yield break;
        var turn=state.TurnNumber;
        var sameTurnOnly=condition?.Contains("resource_issued_turn==resource_turn",StringComparison.Ordinal)==true;
        bool Matches(string name)=>condition==null||condition.Contains($"resource_{name}>0",StringComparison.Ordinal);
        bool Ours(Credit c)=>c.Source==source&&c.Units>0&&(!sameTurnOnly||c.Turn==turn)&&(!unusedOnly||!c.Claim.Used);
        int Count(List<Credit> credits,int actual)
        {
            var excess=Math.Max(0,credits.Sum(credit=>(long)credit.Units)-actual);var total=0;
            foreach(var credit in credits)
            {
                var ignored=(int)Math.Min(excess,credit.Units);excess-=ignored;
                if(Ours(credit))total+=credit.Units-ignored;
            }
            return total;
        }
        if(ResourceBooks.TryGetValue(state,out var book))
        {
            if(Matches("energy")&&Count(book.Energy,state.Energy) is >0 and var energy)yield return $"{energy}点能量";
            if(Matches("stars")&&Count(book.Stars,state.Stars) is >0 and var stars)yield return $"{stars}个{{starIcon}}";
        }
        foreach(var power in source.Owner.Creature.Powers)
            if(Matches(power.GetType().Name)&&PowerCredits.TryGetValue(power,out var credits)&&Count(credits.Items,power.Amount) is >0 and var amount)
                yield return $"{amount}点{power.Title.GetFormattedText()}";
    }
}
