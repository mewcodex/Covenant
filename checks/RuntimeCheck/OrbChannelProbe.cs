using Covenant.Core;
using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

internal static class OrbChannelProbe
{
    internal static async Task Run(SceneTree tree,Player player)
    {
        var state=player.PlayerCombatState!;
        var combat=player.Creature.CombatState!;
        var terminal=CovenantTerminal.For(player)!;
        var queue=state.OrbQueue;
        var context=new BlockingPlayerChoiceContext();
        var enemy=combat.HittableEnemies.First();
        async Task Frame()=>await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
        void Check(bool success,string message)
        {
            if(!success)throw new Exception(message);
            Console.WriteLine("COVENANT_ORBS_PASS "+message);
        }
        async Task<T> Add<T>(PileType pile=PileType.Hand,bool upgraded=false) where T:CardModel
        {
            var card=combat.CreateCard<T>(player);
            if(upgraded)CardCmd.Upgrade(card);
            await CardPileCmd.Add(card,pile,skipVisuals:pile!=PileType.Hand);
            return card;
        }
        async Task Setup(int slots,bool overload=false)
        {
            OrbCmd.RemoveSlots(player,queue.Capacity);
            await CardPileCmd.RemoveFromCombat(state.AllCards.ToArray(),skipVisuals:true);
            foreach(var power in player.Creature.Powers.Where(power=>power is not CovenantAttritionPower).ToArray())
                await PowerCmd.Remove(power);
            terminal.ResetTransient();
            terminal.Ledger.Layers.Clear();
            await OrbCmd.AddSlots(player,slots);
            await Add<BallLightning>(PileType.Exhaust);
            await Add<ColdSnap>(PileType.Exhaust);
            await Add<Darkness>(PileType.Exhaust);
            if(overload)
            {
                await Add<Dualcast>(PileType.Exhaust);
                await Add<MultiCast>(PileType.Exhaust);
                await Add<Shatter>(PileType.Exhaust);
                await Add<Thunder>(PileType.Exhaust);
            }
            Check(terminal.Tier(Bond.Resonance)>0&&terminal.Tier(Bond.Overload)==(overload?2:0),
                $"setup activates Resonance with Overload {(overload?"II":"inactive")}");
            enemy.SetMaxHpInternal(10000);enemy.SetCurrentHpInternal(10000);
        }
        async Task<int> During(Task action, bool swap)
        {
            NSimpleCardSelectScreen? previous=null;
            var choices=0;
            for(var i=0;i<200&&!action.IsCompleted;i++)
            {
                await Frame();
                if(NOverlayStack.Instance?.Peek() is not NSimpleCardSelectScreen screen||ReferenceEquals(screen,previous))continue;
                var grid=screen.GetNode<NCardGrid>("%CardGrid");
                var holders=grid.CurrentlyDisplayedCardHolders.ToArray();
                if(holders.Length==0)continue;
                previous=screen;
                if(holders.All(holder=>holder.CardModel is CovenantOption))
                {
                    choices++;
                    Check(holders.Length==queue.Orbs.Count,"the orb swap overlays one actual choice for each occupied slot");
                }
                if(swap&&holders.Length>=2&&holders.All(holder=>holder.CardModel is CovenantOption))
                {
                    grid.EmitSignal(NCardGrid.SignalName.HolderPressed,holders[0]);
                    grid.EmitSignal(NCardGrid.SignalName.HolderPressed,holders[1]);
                    var confirm=screen.GetNode<NConfirmButton>("%Confirm");
                    confirm.EmitSignal(NClickableControl.SignalName.Released,confirm);
                }
                else
                {
                    var skip=screen.GetNode<NChoiceSelectionSkipButton>("CovenantSkip");
                    skip.EmitSignal(NClickableControl.SignalName.Released,skip);
                }
            }
            if(!action.IsCompleted)
                throw new TimeoutException($"Orb action stalled with {queue.Orbs.Count}/{queue.Capacity} slots and {NOverlayStack.Instance?.Peek()?.GetType().Name??"no overlay"}.");
            await action;
            return choices;
        }
        async Task<int> Play(CardModel card,bool swap=false)
        {
            state.Energy=8;
            var target=card.TargetType==TargetType.AnyEnemy?enemy:null;
            if(!card.TryManualPlay(target))throw new Exception($"Manual play rejected {card.Id}.");
            async Task WaitForPlay()
            {
                while(!CombatManager.Instance.History.CardPlaysFinished.Any(entry=>entry.CardPlay.Card==card)||
                    card.Pile?.Type==PileType.Play)await Frame();
            }
            return await During(WaitForPlay(),swap);
        }
        for(var i=0;i<200&&state.Phase!=PlayerTurnPhase.Play;i++)await Frame();
        Check(state.Phase==PlayerTurnPhase.Play,"the native Defect's first turn is ready");

        await Setup(3);
        Check(await Play(await Add<Zap>())==0&&queue.Orbs is [LightningOrb],
            "Zap finishes with an empty queue and never asks for a swap");
        terminal.BeginTurn();
        Check(await Play(await Add<Glacier>(),swap:true)==1&&queue.Orbs is [FrostOrb,LightningOrb,FrostOrb],
            "a real Glacier finishes both channels after the player confirms a swap");
        terminal.BeginTurn();
        Check(await Play(await Add<Rainbow>())==1&&queue.Orbs is [LightningOrb,FrostOrb,DarkOrb],
            "a real Rainbow finishes three full-queue channels after the player skips swapping");

        await Setup(2,overload:true);
        await OrbCmd.Channel<FrostOrb>(context,player);
        await OrbCmd.Channel<DarkOrb>(context,player);
        terminal.BeginTurn();
        await Play(await Add<ColdSnap>(),swap:true);
        Check(queue.Orbs.Count==2&&queue.Orbs.Any(orb=>orb is FrostOrb),"full-queue channel completes with Overload II copying the evoked orb");

        await Setup(2);
        await OrbCmd.Channel<FrostOrb>(context,player);
        await OrbCmd.Channel<DarkOrb>(context,player);
        terminal.BeginTurn();
        Check(await Play(await Add<Capacitor>(),swap:true)==1&&queue.Capacity==4&&queue.Orbs.Count==3,
            "Capacitor finishes its covenant copy when Resonance asks for a swap");

        await Setup(2);
        await OrbCmd.Channel<FrostOrb>(context,player);
        terminal.BeginTurn();
        Check(await Play(await Add<Spinner>(upgraded:true),swap:true)==1&&queue.Orbs is [GlassOrb,FrostOrb],
            "Spinner+ finishes channeling Glass even when its native context rejects player choices");
        var turn=state.TurnNumber;
        async Task WaitForNextTurn()
        {
            while(state.TurnNumber==turn||state.Phase!=PlayerTurnPhase.Play)await Frame();
        }
        NCombatRoom.Instance!.Ui.EndTurnButton.CallReleaseLogic();
        Check(await During(WaitForNextTurn(),swap:false)==1&&queue.Orbs is [FrostOrb,GlassOrb],
            "Spinner's automatic channel still accepts Skip before the next hand is drawn");

        await Setup(2,overload:true);
        await OrbCmd.Channel<FrostOrb>(context,player);
        await OrbCmd.Channel<DarkOrb>(context,player);
        await Add<ColdSnap>(PileType.Discard);
        await PowerCmd.Apply<LightningRodPower>(context,player.Creature,1,player.Creature,null);
        terminal.BeginTurn();
        Check(await During(player.Creature.GetPower<LightningRodPower>()!.AfterEnergyReset(player),swap:true)==1&&
            queue.Orbs is [DarkOrb,FrostOrb]&&!player.Creature.HasPower<LightningRodPower>(),
            "Lightning Rod finishes its full-queue channel and both covenant choices during energy reset");

        await Setup(3);
        await OrbCmd.Channel<FrostOrb>(context,player);
        await PowerCmd.Apply<TrashToTreasurePower>(context,player.Creature,1,player.Creature,null);
        terminal.BeginTurn();
        Check(await Play(await Add<Turbo>())==1&&queue.Orbs.Count==2,
            "generating a Void via Turbo lets Trash to Treasure finish its nested channel and selection");

        Console.WriteLine("COVENANT_ORBS_COMPLETE");
    }
}
