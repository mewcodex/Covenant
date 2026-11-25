using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

internal static class ExtraTurnProbe
{
    private sealed class Skip:ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,int minSelect,int maxSelect)=>Task.FromResult(options.Take(minSelect));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,IReadOnlyList<CardRewardAlternative> alternatives)=>new();
    }
    private static async Task Wait(SceneTree tree,Func<bool> condition,string error)
    {
        for(int i=0;i<900;i++)
        {
            if(condition())return;
            await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
        }
        throw new Exception(error);
    }
    internal static async Task Run(SceneTree tree,Action<bool,string> check)
    {
        var player=RunManager.Instance.DebugOnlyGetState()!.Players.Single();var state=player.Creature.CombatState!;
        using var choices=RunManager.Instance.CardSelectorStack.UseSelector(player,new Skip());
        await Wait(tree,()=>player.PlayerCombatState!.Phase==PlayerTurnPhase.Play,"Native extra-turn probe never reached a playable turn");
        var original=player.RunState.CreateCard<TheHangedMan>(player);await CardPileCmd.Add(original,PileType.Deck,skipVisuals:true);
        var card=state.CreateCard<TheHangedMan>(player);card.DeckVersion=original;await CardPileCmd.Add(card,PileType.Hand,skipVisuals:false);
        var attrition=player.Creature.GetPower<CovenantAttritionPower>()!;var beforeEnemy=attrition.EnemyTurns;
        if(!card.TryManualPlay(null))throw new Exception("The native extra-turn card cannot be played");
        await Wait(tree,()=>card.Pile?.Type==PileType.Exhaust&&CombatManager.Instance.History.CardPlaysFinished.Any(e=>e.CardPlay.Card==card),"The native extra-turn card did not complete");
        var played=CombatManager.Instance.History.CardPlaysFinished.Last(e=>e.CardPlay.Card==card).CardPlay;
        check(!played.IsAutoPlay&&player.Creature.GetPowerAmount<ExtraTurnPower>()==1,"normal native The Hanged Man play grants a real extra player turn");
        var beforeTurn=player.PlayerCombatState!.TurnNumber;NCombatRoom.Instance!.Ui.EndTurnButton.CallReleaseLogic();
        await Wait(tree,()=>player.PlayerCombatState.TurnNumber>beforeTurn&&player.PlayerCombatState.Phase==PlayerTurnPhase.Play,"Native extra player turn did not start");
        check(state.CurrentSide==CombatSide.Player&&attrition.EnemyTurns==beforeEnemy,"a real extra player turn does not advance the enemy-turn attrition counter");
        check(player.Creature.GetPowerAmount<ExtraTurnPower>()==0,"the native extra-turn power is consumed after granting its extra turn");
    }
}
