using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private sealed record RevisionAttachment(CardModel Origin,int RuleIndex,string Scope,int Turn,string Lifetime,int FirstPlay,int LastPlay);
    private Dictionary<CardModel,List<RevisionAttachment>> _revisionAttachments=new(ReferenceEqualityComparer.Instance);
    private Dictionary<CardModel,HashSet<CardModel>> _revisionPendingTop=new(ReferenceEqualityComparer.Instance);

    internal void RevisionQueueTop(CardModel origin,CardModel subject)
    {
        if(!_revisionPendingTop.TryGetValue(subject,out var origins))_revisionPendingTop[subject]=origins=new(ReferenceEqualityComparer.Instance);
        origins.Add(origin);
    }

    private void ActivateTopTraits(CardModel subject,PileType from)
    {
        // The result location is decided before the play. Arm the trait only after that play actually moves the card.
        if(from!=PileType.Play||!_revisionPendingTop.Remove(subject,out var origins)||subject.Pile?.Type!=PileType.Draw)return;
        foreach(var origin in origins)RevisionAttach(origin,subject,"top");
    }
    internal void RevisionAttach(CardModel origin,CardModel subject,string scope,int firstPlay=0,int lastPlay=int.MaxValue)
    {
        var program=Program(origin);if(program==null||origin.Owner!=subject.Owner)return;
        for(var index=0;index<program.Rules.Length;index++)
        {
            var rule=program.Rules[index];if(rule.Scope!=scope)continue;
            if(rule.Parameter is {} filter&&!RevisionFrame.Filter(subject,filter))continue;
            if(!_revisionAttachments.TryGetValue(subject,out var entries))_revisionAttachments[subject]=entries=new();
            entries.Add(new(origin,index,scope,Owner.PlayerCombatState?.TurnNumber??0,rule.Lifetime,firstPlay,lastPlay));
        }
    }
    private bool RevisionAttachmentCurrent(RevisionAttachment entry)
    {
        var turn=Owner.PlayerCombatState?.TurnNumber??0;
        return entry.Lifetime!="turn"||entry.Turn==turn;
    }
    private Task RevisionAttachedPlay(CardModel subject)=>RevisionAttachedEvent(subject,"subject_play",_revisionPlays.GetValueOrDefault(subject));
    internal async Task RevisionAttachedEvent(CardModel subject,string eventName,RevisionFrame? subjectFrame=null)
    {
        if(!_revisionAttachments.TryGetValue(subject,out var entries))return;
        foreach(var entry in entries.ToArray())
        {
            if(!RevisionAttachmentCurrent(entry))continue;
            if(eventName=="subject_play"&&(subject.CurrentPlayIndex<entry.FirstPlay||subject.CurrentPlayIndex>entry.LastPlay))continue;
            var rule=Program(entry.Origin)?.Rules.ElementAtOrDefault(entry.RuleIndex);if(rule==null)continue;
            if(rule.Event==eventName)
            {
                var frame=new RevisionFrame(entry.Origin,eventName,subject.CurrentTarget){Subject=subject,Scope=entry.Scope,RuleIndex=entry.RuleIndex,Activation=subjectFrame?.Activation};
                frame.Values["attachment_turn"]=entry.Turn;
                if(subjectFrame!=null)
                {
                    foreach(var (key,value) in subjectFrame.Values)frame.Values["subject_"+key]=value;
                    foreach(var (key,value) in subjectFrame.Before)frame.Values["subject_before_"+key]=value;
                }
                await FireRevision(frame);
            }
            if(eventName=="subject_play"&&entry.Lifetime is "next" or "next_replay")entries.Remove(entry);
        }
        if(entries.Count==0)_revisionAttachments.Remove(subject);
    }
    private decimal RevisionAttachedBonus(CardModel subject,bool block,MegaCrit.Sts2.Core.Entities.Creatures.Creature? target)
    {
        if(!_revisionAttachments.TryGetValue(subject,out var entries))return 0;decimal total=0;
        foreach(var entry in entries.ToArray())
        {
            if(!RevisionAttachmentCurrent(entry)||subject.CurrentPlayIndex<entry.FirstPlay||subject.CurrentPlayIndex>entry.LastPlay)continue;
            var rule=Program(entry.Origin)?.Rules.ElementAtOrDefault(entry.RuleIndex);
            if(rule==null||rule.Event!=(block?"block_bonus":"damage_bonus"))continue;
            var frame=new RevisionFrame(entry.Origin,rule.Event,target){Subject=subject,Scope=entry.Scope};
            if(rule.RequireActive&&!RevisionActive(frame,rule.Bond))continue;
            if(EvaluateRevision(frame,rule,rule.Condition)!=0)total+=Math.Max(0,Math.Floor(EvaluateRevision(frame,rule,rule.Amount)));
        }
        return total;
    }
    private int RevisionGrowthBonus(CardModel subject)
    {
        if(!_revisionAttachments.TryGetValue(subject,out var entries))return 0;long total=0;
        foreach(var entry in entries.Where(RevisionAttachmentCurrent))
        {
            var rule=Program(entry.Origin)?.Rules.ElementAtOrDefault(entry.RuleIndex);if(rule?.Event!="growth_bonus")continue;
            var frame=new RevisionFrame(entry.Origin,"growth_bonus"){Subject=subject,Scope=entry.Scope};
            if(EvaluateRevision(frame,rule,rule.Condition)!=0)total+=(int)Math.Max(0,Math.Floor(EvaluateRevision(frame,rule,rule.Amount)));
        }
        return (int)Math.Min(int.MaxValue,total);
    }
    internal bool RevisionHasAttachments(CardModel source)=>_revisionAttachments.Any(pair=>(pair.Key.Pile?.Type is PileType.Hand or PileType.Draw or PileType.Discard or PileType.Exhaust)&&
        pair.Value.Any(e=>e.Origin==source&&RevisionAttachmentCurrent(e)));
    internal async Task RevisionTransformed(CardModel original,CardModel replacement)
    {
        // A self-transform during OnPlay still occupies Play until the wrapper
        // moves the replacement to its result pile. Preserve that play's count
        // snapshot instead of temporarily dropping the transforming component.
        var own=new RevisionFrame(original,"transform"){Subject=original};
        own.Values["transformed_into_"+replacement.GetType().Name]=1;
        await FireRevision(own);
        if(replacement.CombatState!=null)await RevisionObserve("observe_transform",replacement);
        if(replacement.Pile?.Type==PileType.Deck)await AcquireDeckTraits(replacement);
        if(!_revisionAttachments.Remove(original,out var entries))return;
        foreach(var entry in entries.ToArray())
        {
            if(!RevisionAttachmentCurrent(entry))continue;
            var rule=Program(entry.Origin)?.Rules.ElementAtOrDefault(entry.RuleIndex);
            if(rule?.Event!="subject_transform")continue;
            var frame=new RevisionFrame(entry.Origin,"subject_transform"){Subject=original,Scope=entry.Scope,RuleIndex=entry.RuleIndex};
            frame.Values["transformed_into_"+replacement.GetType().Name]=1;
            await FireRevision(frame);
        }
    }
}
