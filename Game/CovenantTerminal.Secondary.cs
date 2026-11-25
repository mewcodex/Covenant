using Covenant.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private bool _secondaryFirstPlaySeen,_vakuuControlled,_vakuuRunning;
    private int _vakuuTurn,_vakuuPlayed,_tonyPendingPenalty;
    private CardModel? _vakuuPaidCard,_vakuuPendingCard;
    private (int Energy,int Stars) _vakuuPaid;
    private void ResetSecondary()
    {
        _secondaryFirstPlaySeen=false;_vakuuPendingCard=null;_vakuuControlled=false;_vakuuRunning=false;
        _vakuuTurn=0;_vakuuPlayed=0;_tonyPendingPenalty=0;_vakuuPaidCard=null;_ledger.Layers.Remove(Bond.Tony);
    }
    internal void ObservePlayBeforeLeavingHand(CardModel card)
    {
        if(_secondaryFirstPlaySeen||card.Pile?.Type!=PileType.Hand)return;
        _secondaryFirstPlaySeen=true;
        if(Owner.PlayerCombatState?.TurnNumber==1&&Tier(Bond.Vakuu)>0&&Owner.PlayerCombatState.Hand.Cards.FirstOrDefault()==card)
            _vakuuPendingCard=card;
    }
    internal async Task FinishSecondaryPlay(PlayerChoiceContext context,CardModel card,bool isAutoPlay)
    {
        if(!isAutoPlay&&_vakuuPendingCard==card){_vakuuPendingCard=null;_vakuuControlled=true;}
        if(_vakuuControlled&&!_vakuuRunning)await RunVakuu(context);
    }
    internal void RestoreVakuuPayment(CardModel card,ref ResourceInfo resources)
    {
        if(_vakuuPaidCard!=card)return;_vakuuPaidCard=null;
        resources=new ResourceInfo{EnergySpent=_vakuuPaid.Energy,EnergyValue=resources.EnergyValue,StarsSpent=_vakuuPaid.Stars,StarValue=resources.StarValue};
    }
    private async Task RunVakuu(PlayerChoiceContext context)
    {
        if(_vakuuRunning||!_vakuuControlled||CombatManager.Instance.IsOverOrEnding)return;
        _vakuuRunning=true;
        using var suppressed=RevisionTrace.Suppress(this);
        try
        {
            var turn=Owner.PlayerCombatState!.TurnNumber;
            if(turn!=_vakuuTurn){_vakuuTurn=turn;_vakuuPlayed=0;}
            using var selector=RunManager.Instance.CardSelectorStack.PushSelector(Owner,new VakuuCardSelector());
            while(_vakuuPlayed<13&&!CombatManager.Instance.IsOverOrEnding&&Owner.PlayerCombatState.TurnNumber==turn&&!CombatManager.Instance.IsPlayerReadyToEndTurn(Owner))
            {
                var card=Owner.PlayerCombatState.Hand.Cards.FirstOrDefault(c=>c.CanPlay());if(card==null)break;
                var state=Owner.Creature.CombatState!;
                Creature? target=card.TargetType switch
                {
                    TargetType.AnyEnemy=>state.HittableEnemies.FirstOrDefault(),
                    TargetType.AnyAlly=>RandomFor(CovenantRandom.CombatTarget).NextItem(state.Allies.Where(c=>c.IsAlive&&c.IsPlayer&&c!=Owner.Creature)),
                    TargetType.AnyPlayer=>Owner.Creature,_=>null
                };
                _vakuuPlayed++;_vakuuPaid=await card.SpendResources();_vakuuPaidCard=card;
                await PlayForCovenant(context,card,target,skipXCapture:true);
                _vakuuPaidCard=null;
            }
            if(!CombatManager.Instance.IsOverOrEnding&&Owner.PlayerCombatState.TurnNumber==turn)PlayerCmd.EndTurn(Owner,canBackOut:false);
        }
        finally{_vakuuRunning=false;}
    }
    public override async Task AfterAutoPrePlayPhaseEnteredLate(PlayerChoiceContext context,Player player)
    {
        if(player==Owner&&_vakuuControlled)await RunVakuu(context);
    }
    public override Task AfterEnergyReset(Player player)
    {
        if(player==Owner&&_tonyPendingPenalty>0)
        {
            var state=Owner.PlayerCombatState!;
            state.Energy=Math.Max(0,state.Energy-Math.Min(_tonyPendingPenalty,state.MaxEnergy));
            _tonyPendingPenalty=0;
        }
        return Task.CompletedTask;
    }
    public override async Task AfterSideTurnEndLate(PlayerChoiceContext context,CombatSide side,IEnumerable<Creature> participants)
    {
        if(side!=CombatSide.Player||!participants.Contains(Owner.Creature))return;
        await ReworkEndTurn(context);
        await ResolveRotationEnd(context);
        await ResolveReflowEnd(context);
        if(Tier(Bond.Tony)>0&&Owner.Creature.Block==16&&!Owner.Creature.IsDead&&!CombatManager.Instance.IsOverOrEnding)
        {
            _ledger.Add(Bond.Tony,1);_tonyPendingPenalty=_ledger.Get(Bond.Tony);
            await PowerCmd.Apply<ExtraTurnPower>(context,Owner.Creature,1,Owner.Creature,null);
        }
        else{_ledger.Layers.Remove(Bond.Tony);_tonyPendingPenalty=0;}
        StoreChannelEnergy();
        EndStarTurn();
        RefreshBattleStatuses();
    }
}
