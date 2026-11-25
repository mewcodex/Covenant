using Covenant.Core;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private List<CardModel> _normalityPriority=new();
    private CardModel? _hermitCurse;
    internal bool IsHermitCurse(CardModel card)=>_hermitCurse==card;
    private CardModel? FirstNormalityPriority()=>_normalityPriority.FirstOrDefault(card=>card.Pile?.Type==PileType.Hand);

    public override bool ShouldPlay(CardModel card,AutoPlayType autoPlayType)
    {
        if(card.Owner!=Owner)return true;
        if(IsHermitCurse(card)&&autoPlayType!=AutoPlayType.None)return true;
        if(card is Normality normality&&!CurseCovenant.CanPlayNormality(normality))return false;
        return autoPlayType!=AutoPlayType.None||FirstNormalityPriority() is not {} priority||card==priority;
    }

    public override bool TryModifyKeywordsInCombat(CardModel card,ISet<CardKeyword> keywords)
        =>card.Owner==Owner&&(IsHermitCurse(card)||CurseCovenant.PlayCost(card)!=null&&CardMembership.CurseTier(Owner)>0)&&keywords.Remove(CardKeyword.Unplayable);

    internal async Task PlayHermit(PlayerChoiceContext context,TheHermit hermit)
    {
        var chosen=(await CardSelectCmd.FromHandForDiscard(context,Owner,
            new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt,1),null,hermit)).FirstOrDefault();
        if(chosen is {Type:CardType.Curse})
        {
            var previous=_hermitCurse;
            _hermitCurse=chosen;
            chosen.ExhaustOnNextPlay=true;
            try{await PlayForCovenant(context,chosen);}
            finally
            {
                chosen.ExhaustOnNextPlay=false;
                _hermitCurse=previous;
            }
        }
        else if(chosen!=null)
        {
            RevisionTrace.FindPlay(hermit)?.Add("native_hermit_discard",1);
            await CardCmd.Discard(context,chosen);
        }
        await CardPileCmd.Draw(context,hermit.DynamicVars.Cards.BaseValue,Owner);
    }

    internal async Task PlayCovenantCurse(PlayerChoiceContext context,CardModel card)
    {
        var state=Owner.PlayerCombatState!;
        var combat=Owner.Creature.CombatState!;
        var enemies=combat.HittableEnemies;
        switch(card)
        {
            case AscendersBane:
                await Power<PlatingPower>(context,2);
                await CardCmd.Exhaust(context,card);
                break;
            case Clumsy:
                await Block(3);
                if(RandomFor(CovenantRandom.CombatCard).NextItem(state.Hand.Cards) is {} discarded)
                    await CardCmd.Discard(context,discarded);
                break;
            case CurseOfTheBell: await Power<ArtifactPower>(context,1);break;
            case Debt:
                var gold=Owner.Gold/20;
                if(gold>0)
                {
                    await PlayerCmd.LoseGold(gold,Owner);
                    await Block(gold);
                    GrantLayers(Bond.Curse,gold);
                }
                await CardCmd.Exhaust(context,card);
                break;
            case Decay:
                var tier=Scale(Bond.Curse).A;
                var hits=1+tier;
                var damage=3+tier/2;
                var rng=RandomFor(CovenantRandom.CombatTarget);
                for(var i=0;i<hits&&!Owner.Creature.IsDead&&!CombatManager.Instance.IsOverOrEnding;i++)
                {
                    var target=rng.NextItem(combat.Creatures.Where(creature=>creature.IsHittable));
                    if(target==null)break;
                    await CreatureCmd.Damage(context,target,damage,ValueProp.Unpowered,Owner.Creature,card,null);
                }
                break;
            case Doubt:
                foreach(var enemy in enemies.ToArray())
                    await PowerCmd.Apply<WeakPower>(context,enemy,1,Owner.Creature,card);
                break;
            case Folly:
                await Draw(context,2);
                await CardCmd.Exhaust(context,card);
                break;
            case Enthralled: await Block(16);break;
            case Greed:
                for(var i=0;i<2&&state.Hand.Cards.Count<CardPile.MaxCardsInHand;i++)
                {
                    var drawn=RandomFor(CovenantRandom.CombatCard).NextItem(state.DiscardPile.Cards.Where(candidate=>candidate.Type==CardType.Curse));
                    if(drawn==null)break;
                    await CardPileCmd.Add(drawn,PileType.Hand);
                }
                break;
            case Normality:
                var priority=RandomFor(CovenantRandom.CombatCard).NextItem(state.Hand.Cards.Where(candidate=>
                    !CardMembership.IsXCost(candidate)&&!candidate.Keywords.Contains(CardKeyword.Unplayable)&&
                    Hook.ShouldPlay(candidate.CombatState!,candidate,out _,AutoPlayType.None)));
                if(priority!=null)
                {
                    if(!_normalityPriority.Contains(priority))_normalityPriority.Add(priority);
                    priority.EnergyCost.SetUntilPlayed(0);
                    if(priority.CurrentStarCost>=0)priority.SetStarCostUntilPlayed(0);
                }
                break;
            case Regret: await Burst(context,state.Hand.Cards.Count);break;
            case Shame:
                foreach(var enemy in enemies.Where(enemy=>enemy.Block>0).ToArray())
                {
                    var old=enemy.Block;
                    await CreatureCmd.LoseBlock(context,enemy,Math.Ceiling(old/2m),Owner.Creature);
                    if(enemy.Block<old)RevisionTrace.FindPlay(card)?.Add("native_shame_loss",1);
                }
                break;
            case SporeMind:
                var sown=ModelDb.Enchantment<Sown>();
                var unenchanted=state.DrawPile.Cards.Concat(state.DiscardPile.Cards)
                    .Where(candidate=>candidate.Enchantment==null&&sown.CanEnchant(candidate)).ToList();
                var sowCount=1+Scale(Bond.Curse).A/3;
                for(var i=0;i<sowCount&&unenchanted.Count>0;i++)
                {
                    var chosen=RandomFor(CovenantRandom.CombatCard).NextItem(unenchanted)!;
                    unenchanted.Remove(chosen);
                    CardCmd.Enchant<Sown>(chosen,1);
                }
                break;
            case Writhe:
                var seen=state.DrawPile.Cards.Take(3).ToArray();
                var taken=await Select(context,seen,"选择1张牌放入手牌。",optional:false);
                if(taken==null)break;
                await CardPileCmd.Add(taken,PileType.Hand);
                var exhaust=RandomFor(CovenantRandom.CombatCard).NextItem(seen.Where(other=>other!=taken&&other.Pile?.Type==PileType.Draw));
                if(exhaust!=null)await CardCmd.Exhaust(context,exhaust);
                break;
        }
    }
}
