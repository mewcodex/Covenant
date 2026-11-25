using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Covenant.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private static readonly ConditionalWeakTable<CardModel,object> ProcessedGenerationAdds=new();
    private static readonly object GenerationHandled=new();
    private bool _statusStrengthOpen,_syncingStatusStrength,_statusStrengthChanged;
    private int _statusStrengthRemoved;

    internal static void InstallStatusReplacement(Harmony harmony)
    {
        var draw=AccessTools.Method(typeof(CardPileCmd),"DrawInternal").GetCustomAttribute<AsyncStateMachineAttribute>()!;
        harmony.Patch(AccessTools.Method(draw.StateMachineType,"MoveNext"),
            transpiler:new HarmonyMethod(typeof(CovenantTerminal),nameof(ReplaceStatusAtDraw)));
    }

    private static IEnumerable<CodeInstruction> ReplaceStatusAtDraw(IEnumerable<CodeInstruction> instructions)
    {
        var code=instructions.ToList();
        var readTop=code.Single(instruction=>instruction.operand is MethodInfo method&&method.DeclaringType==typeof(Enumerable)&&
            method.Name==nameof(Enumerable.FirstOrDefault)&&method.ReturnType==typeof(CardModel));
        // Replace the local before native draw history, callbacks and the caller's returned card list use it.
        code.Insert(code.IndexOf(readTop)+1,new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CovenantTerminal),nameof(ReplaceDrawnStatus))));
        return code;
    }

    private static bool IsStatusReplacementCandidate(CardModel card)
        =>card.Type==CardType.Status&&card is not DeprecatedCard&&!CardMembership.IsMarker(card);

    private static CardModel? RollStatusReplacement(CardModel card)
    {
        if(!IsStatusReplacementCandidate(card)||card.Owner is not {} owner||owner.Creature.CombatState is not {} combat||
            For(owner) is not {} terminal||terminal.Scale(Bond.Status).Layers<250||!terminal.Active(Bond.Status,terminal._currentCard))return null;

        var canonical=card.Keywords.Contains(CardKeyword.Unplayable)?(CardModel)ModelDb.Card<Dazed>():ModelDb.Card<Slimed>();
        if(canonical.Id==card.Id||terminal.RandomFor(CovenantRandom.CombatRoll).NextInt(100)>=2*terminal.Scale(Bond.Status).A)return null;
        var replacement=combat.CreateCard(canonical,owner);
        replacement.DeckVersion=card.DeckVersion;
        return replacement;
    }

    private static CardModel? ReplaceDrawnStatus(CardModel? card)
    {
        if(card?.Pile is not {Type:PileType.Draw} draw||card.Owner?.PlayerCombatState?.Hand.Cards.Count>=CardPile.MaxCardsInHand||
            RollStatusReplacement(card) is not {} replacement)return card;
        draw.RemoveInternal(card,silent:true);
        draw.AddInternal(replacement,0,silent:true);
        RecordCombatReplacement(card,replacement);
        return replacement;
    }

    private static CardModel ReplaceNewStatus(CardModel card)
    {
        if(RollStatusReplacement(card) is not {} replacement)return card;
        RecordCombatReplacement(card,replacement);
        return replacement;
    }

    internal static void ReplaceDirectStatusAdds(ref IEnumerable<CardModel> cards,CardPile newPile,bool isChangingOwners)
    {
        if(isChangingOwners||newPile.Type is not (PileType.Hand or PileType.Draw or PileType.Discard)||
            !CombatManager.Instance.IsInProgress||CombatManager.Instance.IsOverOrEnding)return;
        cards=cards.Select(card=>ProcessedGenerationAdds.Remove(card)||card.Pile!=null||card.DeckVersion!=null||
            card.HasBeenRemovedFromState?card:ReplaceNewStatus(card)).ToArray();
    }

    private void ResetStatusStrength()
    {
        _statusStrengthOpen=false;_syncingStatusStrength=false;_statusStrengthChanged=false;_statusStrengthRemoved=0;
    }
    private void BeginStatusStrengthTurn()
    {
        _statusStrengthOpen=true;_statusStrengthRemoved=0;
    }
    internal void CloseStatusStrengthTurn()=>_statusStrengthOpen=false;
    internal void RememberRemovedStatusStrength(int amount)
    {
        // A native effect such as Plow can make temporary Strength permanent. Do not grant those points again this turn.
        if(_statusStrengthOpen&&!_syncingStatusStrength)_statusStrengthRemoved+=Math.Max(0,amount);
    }
    public override Task AfterSideTurnEnd(PlayerChoiceContext context,CombatSide side,IEnumerable<Creature> participants)
    {
        if(side==CombatSide.Player&&participants.Contains(Owner.Creature))
        {
            CloseStatusStrengthTurn();
            _ritualDiscountThisTurn=false;
        }
        return Task.CompletedTask;
    }
    private async Task RefreshStatusStrength(PlayerChoiceContext context)
    {
        if(!_statusStrengthOpen||Owner.Creature.IsDead||Owner.PlayerCombatState is not {} state||CombatManager.Instance.IsOverOrEnding)return;
        if(_syncingStatusStrength){_statusStrengthChanged=true;return;}
        _syncingStatusStrength=true;
        try
        {
            do
            {
                _statusStrengthChanged=false;
                var power=Owner.Creature.GetPower<CovenantStatusStrengthPower>();
                var count=Advanced(Bond.Status)?Math.Max(0,state.Hand.Cards.Count(card=>card.Type==CardType.Status&&!CardMembership.IsMarker(card))-_statusStrengthRemoved):0;
                var delta=count-(power?.Amount??0);
                if(delta==0)continue;
                if(power==null)await PowerCmd.Apply<CovenantStatusStrengthPower>(context,Owner.Creature,count,Owner.Creature,null,silent:true);
                else await PowerCmd.ModifyAmount(context,power,delta,Owner.Creature,null,silent:true);
            }while(_statusStrengthChanged);
        }
        finally{_syncingStatusStrength=false;}
    }
}
