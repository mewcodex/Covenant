using Covenant.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private static readonly CardTag[] Melodies=[CardTag.Wah,CardTag.Cho,CardTag.Ki];
    private Dictionary<Creature,(ImpaledPower Power,CardModel? Card)> _arrowHits=new(ReferenceEqualityComparer.Instance);
    private HashSet<CardTag> _melodies=new();
    private Dictionary<CardTag,CardModel> _firstMelodyCards=new();
    private CardTag? _inheritedMelody;
    private bool _inheritedMelodyPending,_nextMelodyPending;

    private void ResetFool()
    {
        _arrowHits=new(ReferenceEqualityComparer.Instance);_melodies=new();_firstMelodyCards=new();
        _inheritedMelody=null;_nextMelodyPending=false;_inheritedMelodyPending=false;
    }
    private void BeginFoolTurn()
    {
        _arrowHits.Clear();_melodies.Clear();_firstMelodyCards.Clear();
        _inheritedMelody=_nextMelodyPending?Melodies[RandomFor(CovenantRandom.CombatCard).NextInt(Melodies.Length)]:null;
        _nextMelodyPending=false;
        if(_inheritedMelody is {} inherited)_melodies.Add(inherited);
        _inheritedMelodyPending=_inheritedMelody.HasValue;
    }
    // Once acquired, this turn's melody is a granted resource. Losing the covenant's current
    // card-count tier does not erase it or permit native acquisition powers to reward it twice.
    internal bool HasInheritedMelody(CardTag tag)=>_inheritedMelody==tag;
    internal bool HasInheritedMelodyThisTurn=>_inheritedMelody.HasValue;
    private async Task StartFoolTurn(PlayerChoiceContext c)
    {
        if(!_inheritedMelodyPending)return;_inheritedMelodyPending=false;await RewardMelody(c,null);
        if(_inheritedMelody is {} inherited)await CovenantMelody.Inherit(c,Owner,inherited);
    }
    private void TrackMelodies(CardPlay play)
    {
        foreach(var tag in Melodies.Where(play.Card.Tags.Contains))
            if(!HasInheritedMelody(tag))_firstMelodyCards.TryAdd(tag,play.Card);
    }
    private async Task ResolveFoolPlay(PlayerChoiceContext c,CardPlay play)
    {
        foreach(var tag in Melodies.Where(play.Card.Tags.Contains))
            if(_firstMelodyCards.GetValueOrDefault(tag)==play.Card&&_melodies.Add(tag))await RewardMelody(c,play.Card);
        if(play.Card is not Joy)return;
        await FireTraits("emotion");
        if(Advanced(Bond.Emotion,play.Card))
        {
            await Power<CovenantStrengthPower>(c,1+Scale(Bond.Emotion).A/4);
            if(Once("joy"))await Retrieve(c,Owner.PlayerCombatState!.DiscardPile.Cards.Where(x=>x is not Joy));
        }
    }
    private async Task RewardMelody(PlayerChoiceContext c,CardModel? source)
    {
        await FireTraits("melody");if(!Active(Bond.Melody,source))return;
        await Block(2+Scale(Bond.Melody).A);
        if(_melodies.Count>=2&&Once("melody_second"))await Draw(c);
        if(_melodies.Count!=3||!Advanced(Bond.Melody,source)||!Once("melody_complete"))return;
        _nextMelodyPending=true;
        await CovenantEnergy();
    }
    private async Task EndFoolTurn(PlayerChoiceContext c)
    {
        if(!Active(Bond.Emotion))return;
        if(!Owner.PlayerCombatState!.Hand.Cards.Any(card=>card is Sadness))return;
        var prefs=CovenantSelection.Single(Presentation.Literal("选择1张悲伤，将其变化为喜悦并保留。"));
        var sadness=(await CardSelectCmd.FromHand(c,Owner,prefs,card=>card is Sadness,this)).FirstOrDefault();
        if(sadness==null)return;
        var transformed=await CardCmd.TransformTo<Joy>(sadness);
        if(transformed is not {} result)return;
        CardCmd.ApplySingleTurnRetain(result.cardAdded);await Block(3+Scale(Bond.Emotion).A);
    }

    public override Task BeforeDamageReceived(PlayerChoiceContext c,Creature target,decimal amount,ValueProp props,Creature? dealer,CardModel? source)
    {
        if(dealer!=Owner.Creature||!props.IsPoweredAttack()||!Active(Bond.Arrows,source))return Task.CompletedTask;
        var impaled=target.GetPowerInstances<ImpaledPower>().FirstOrDefault(x=>x.Applier==Owner.Creature&&x.Arrows.Count>0);
        if(impaled!=null&&Once("arrow_hit"))_arrowHits[target]=(impaled,source);
        return Task.CompletedTask;
    }
    public override Task AfterDamageReceivedLate(PlayerChoiceContext c,Creature target,DamageResult result,ValueProp props,Creature? dealer,CardModel? source)
        =>dealer==Owner.Creature&&props.IsPoweredAttack()?ResolveArrowHit(c,target,source):Task.CompletedTask;

    private async Task ResolveArrowHit(PlayerChoiceContext c,Creature target,CardModel? source)
    {
        if(!_arrowHits.TryGetValue(target,out var hit)||source!=hit.Card)return;
        _arrowHits.Remove(target);
        await Power<AimPower>(c,2+Scale(Bond.Arrows).A);
        // The native impale listener has already processed the normal hit, and may already have dislodged it.
        var power=hit.Power;if(target.IsDead||power.Arrows.Count==0||!target.Powers.Contains(power))return;
        power.HitCounter--;RefreshImpaleCounter(power);
        if(power.HitCounter>0)return;
        power.HitCounter=3;RefreshImpaleCounter(power);
        await ArrowCmd.DislodgeArrow(target.CombatState!,power,c,source);await power.RemoveIfNoArrows();
    }
    private static void RefreshImpaleCounter(ImpaledPower power)=>AccessTools.Method(typeof(PowerModel),"InvokeDisplayAmountChanged").Invoke(power,null);
    public override async Task AfterArrowShot(Player owner,ArrowSlot? arrow,PlayerChoiceContext c,CardModel? source)
    {
        if(owner==Owner&&arrow!=null)await FireTraits("arrow");
    }
    public override async Task AfterArrowDislodged(ImpaledPower power,ArrowSlot arrow,PlayerChoiceContext c,CardModel? source)
    {
        if(arrow.owner!=Owner||source!=null&&source.Owner!=Owner||!Advanced(Bond.Arrows,source))return;
        await ReturnRandom(Owner.PlayerCombatState!.DiscardPile.Cards.Where(card=>card.Tags.Contains(CardTag.Shoot)));
    }
}
