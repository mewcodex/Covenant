using Covenant.Core;
using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

internal static class AcquisitionPreviewProbe
{
    internal static async Task Run(SceneTree tree)
    {
        async Task Wait(double seconds=.3)=>await tree.ToSignal(tree.CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
        void Check(bool result,string message)
        {
            if(!result)throw new Exception(message);
            Console.WriteLine("COVENANT_ACQUISITION_PASS "+message);
        }

        var run=RunManager.Instance.DebugOnlyGetState()!;
        var player=run.Players.Single();
        var hud=NRun.Instance!.GlobalUi.GetNode<Control>("CovenantRoot");
        var bar=hud.GetNode<Control>("BondBar");
        var items=bar.GetNode<Control>("Scroller/Items");
        var overlays=NOverlayStack.Instance!;
        Control? Badge(Bond bond)=>items.GetNodeOrNull<Control>($"{bond}/Badge");
        bool Active(Bond bond)=>Badge(bond)?.HasNode("Stage")==true;
        string[] Order()=>items.GetChildren().Select(child=>child.Name.ToString()).ToArray();
        async Task Add(CardModel card)=>await CardPileCmd.Add(card,PileType.Deck,skipVisuals:true);

        for(var wave=0;wave<8&&CombatManager.Instance.IsInProgress;wave++)
        {
            await CreatureCmd.Kill(player.Creature.CombatState!.Enemies.Where(enemy=>enemy.IsAlive).ToArray(),true);
            if(await CombatManager.Instance.CheckWinCondition())break;
        }
        for(var i=0;i<80&&overlays.Peek() is not NRewardsScreen;i++)await Wait(.1);
        Check(overlays.Peek() is NRewardsScreen,"native victory opens the rewards overlay");

        var inflame=player.RunState.CreateCard<Inflame>(player);
        await Add(inflame);
        await Add(player.RunState.CreateCard<DemonForm>(player));
        await Add(player.RunState.CreateCard<Barricade>(player));
        var duplicate=player.RunState.CreateCard<Inflame>(player);
        CardCmd.Upgrade(duplicate);
        var unique=player.RunState.CreateCard<FeelNoPain>(player);
        var screen=NCardRewardSelectionScreen.ShowScreen([new(duplicate),new(unique)],[])!;
        await Wait(.55);
        Check(Badge(Bond.Solitude)!=null&&!Active(Bond.Solitude),"real reward picker shows Solitude dim for the duplicate starter deck");

        foreach(var card in player.Deck.Cards.GroupBy(CardMembership.NameKey).SelectMany(group=>group.Skip(1)).ToArray())
            await CardPileCmd.RemoveFromDeck(card,showPreview:false);
        foreach(var card in new CardModel[]{player.RunState.CreateCard<Thunderclap>(player),player.RunState.CreateCard<Anger>(player),
            player.RunState.CreateCard<ShrugItOff>(player),player.RunState.CreateCard<TwinStrike>(player),
            player.RunState.CreateCard<PommelStrike>(player),player.RunState.CreateCard<Bloodletting>(player)})await Add(card);
        await Wait();
        Check(Active(Bond.Solitude)&&!Active(Bond.Ritual)&&CardMembership.Counts(player,deck:true).GetValueOrDefault(Bond.Ritual)==BondCatalog.Get(Bond.Ritual).Basic-1,
            "unique deck starts with Solitude active and Ritual one card below threshold");
        var liveOrder=Order();
        var deckBefore=player.Deck.Cards.ToArray();
        var hitbox=screen.GetCardHolder(duplicate).Hitbox;
        hitbox.EmitSignal(Control.SignalName.MouseEntered);
        await Wait();
        Check(Badge(Bond.Solitude)!=null&&!Active(Bond.Solitude)&&Active(Bond.Ritual),$"hovering an upgraded namesake extinguishes Solitude and lights Ritual (screen={ActiveScreenContext.Instance.GetCurrentScreen()?.GetType().Name}, offer={AcquisitionPreview.CardFor(player)?.Id}, Solitude={Active(Bond.Solitude)}, Ritual={Active(Bond.Ritual)})");
        Check(Badge(Bond.Solitude)!.Scale.X>Badge(Bond.Strike)!.Scale.X&&!Order().SequenceEqual(liveOrder),"changed icons enlarge and sort in their projected order");
        Check(player.Deck.Cards.SequenceEqual(deckBefore),"hover leaves the real deck intact");
        hitbox.EmitSignal(Control.SignalName.MouseExited);
        await Wait();
        Check(Active(Bond.Solitude)&&!Active(Bond.Ritual)&&Order().SequenceEqual(liveOrder),"leaving the reward card restores tiers and order");

        hitbox=screen.GetCardHolder(unique).Hitbox;
        hitbox.EmitSignal(Control.SignalName.MouseEntered);
        await Wait();
        var projected=Order();
        Check(Active(Bond.Solitude)&&Active(Bond.Ritual),"hovering a distinct card keeps Solitude active");
        overlays.Remove(screen);
        await Add(unique);
        await Wait();
        var acquiredOrder=Order();
        Check(acquiredOrder.SequenceEqual(projected.Take(acquiredOrder.Length))&&Active(Bond.Solitude)&&Active(Bond.Ritual),"preview order matches the deck after acquiring that card");

        await RunManager.Instance.EnterRoomDebug(RoomType.Shop).WaitAsync(TimeSpan.FromSeconds(45));
        var room=NMerchantRoom.Instance!;
        room.OpenInventory();
        await Wait(.65);
        var slot=room.Inventory.GetAllSlots().OfType<NMerchantCard>().First(card=>card.Entry.IsStocked);
        var offered=((MerchantCardEntry)slot.Entry).CreationResult!.Card;
        if(!player.Deck.Cards.Any(card=>CardMembership.NameKey(card)==CardMembership.NameKey(offered)))
            await Add(run.CreateCard(offered.CanonicalInstance,player));
        var shelf=room.Inventory.GetNode<Control>("%CharacterCards");
        var shelfPosition=shelf.Position;
        Check(!bar.Visible,"merchant starts with the covenant strip folded");
        slot.Hitbox.EmitSignal(Control.SignalName.MouseEntered);
        await Wait();
        Check(bar.Visible&&Badge(Bond.Solitude)!=null&&!Active(Bond.Solitude)&&shelf.Position==shelfPosition,"native merchant hover shows the extinguished Solitude preview without moving the shelf");
        slot.Hitbox.EmitSignal(Control.SignalName.MouseExited);
        await Wait();
        Check(!bar.Visible,"leaving the merchant card restores the fold");

        slot.Hitbox.EmitSignal(Control.SignalName.MouseEntered);
        await Wait();
        player.Gold=999;
        await RunManager.Instance.MerchantSynchronizer.PurchaseLocalEntry(slot.Entry);
        await Wait();
        Check(player.Deck.Cards.Contains(offered)&&!bar.Visible,"the native purchase clears the hover even when the pointer stays on the slot");
    }
}
