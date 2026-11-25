using Covenant.Core;
using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Stopwatch = System.Diagnostics.Stopwatch;

internal static class ShopLibraryProbe
{
    internal static async Task Run(SceneTree tree,string output)
    {
        await CombatProbe.Prepare(tree);
        var run=RunManager.Instance.DebugOnlyGetState()!;
        var player=run.Players.Single();
        var terminal=CovenantTerminal.For(player)!;
        await Wait(()=>player.PlayerCombatState?.Phase==PlayerTurnPhase.Play);
        foreach(var enemy in player.Creature.CombatState!.HittableEnemies.ToArray())
            await CreatureCmd.Kill(enemy,true);
        await CombatManager.Instance.CheckWinCondition();
        await Wait(()=>!CombatManager.Instance.IsEnding);

        foreach(var model in new CardModel[]{run.CreateCard<DefendSilent>(player),run.CreateCard<DefendDefect>(player),
            run.CreateCard<DefendRegent>(player),run.CreateCard<PanicButton>(player)})
            await CardPileCmd.Add(model,PileType.Deck,skipVisuals:true);
        terminal.Ledger.Add(Bond.Ritual,125);
        foreach(var bond in BondCatalog.All.Where(b=>b.UsesLayers).Take(15))
            terminal.Ledger.Add(bond.Id,1);
        await RunManager.Instance.EnterRoomDebug(RoomType.Shop).WaitAsync(TimeSpan.FromSeconds(45));
        var merchant=NMerchantRoom.Instance!;
        merchant.OpenInventory();
        var native=NRun.Instance!;
        var hud=native.GlobalUi.GetNode<Control>("CovenantRoot");
        var bar=hud.GetNode<Control>("BondBar");
        var items=bar.GetNode<Control>("Scroller/Items");
        await Wait(()=>merchant.Inventory.IsOpen&&!bar.Visible);
        hud.GetNode<Button>("FoldCovenants").EmitSignal(Button.SignalName.Pressed);
        await Wait(()=>bar.IsVisibleInTree()&&items.HasNode("Ritual"));
        var inactive=items.GetNode<Control>("Ritual/Badge");
        var name=inactive.GetNode<Label>("BondName");
        Check(inactive.Scale.X>=.95f&&!inactive.HasNode("Stage")&&name.GetThemeColor("font_color")==new Color("bdcecb")&&
            name.GetThemeConstant("outline_size")==4&&name.GetThemeColor("font_shadow_color").A>.6f,
            $"inactive shop badges use normal scale ({inactive.Scale.X:F2}) and have visible text and shadow");
        await tree.ToSignal(tree.CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
        var upperShelf=merchant.Inventory.GetNode<Control>("%CharacterCards");
        var lowerShelf=merchant.Inventory.GetNode<Control>("%ColorlessCards");
        var upper=Stock(upperShelf);
        var lower=Stock(lowerShelf);
        var arrow=bar.GetNode<Control>("MoreCovenants");
        var toggles=hud.GetNode<Control>("TraitToggle");
        native.GetViewport().GetTexture().GetImage().SavePng(output+"/0.5/shop-library-badges.png");
        var controls=new[]{bar,arrow,toggles}.Where(control=>control.IsVisibleInTree()).ToArray();
        var overlaps=upper.SelectMany(slot=>controls.Where(control=>Tip(slot)?.GetGlobalRect().Intersects(control.GetGlobalRect())==true)
            .Select(control=>$"{slot.Name} {Tip(slot)!.GetGlobalRect()} vs {control.Name} {control.GetGlobalRect()}")).ToArray();
        Check(Math.Abs(bar.GetGlobalRect().GetCenter().X-hud.GetViewportRect().Size.X/2)<1&&upper.Length>0&&
            upper.All(slot=>Tip(slot) is {Visible:true})&&overlaps.Length==0,
            "the full-size shop strip is centered and the settled upper shelf traits clear it "+string.Join(" | ",overlaps));
        Check(lower.Length>0&&lower.All(slot=>Price(slot).End.Y<=hud.GetViewportRect().End.Y-4)&&
            lower.All(slot=>Tip(slot) is {Visible:true} tip&&upper.All(other=>!tip.GetGlobalRect().Intersects(Price(other)))),
            $"lower shelf traits clear upper prices and lower prices remain in the viewport (lower end {lower.Max(slot=>Price(slot).End.Y)}, viewport {hud.GetViewportRect().End.Y})");
        var goods=merchant.Inventory.GetAllSlots().Where(slot=>slot is not NMerchantCard&&slot.Entry.IsStocked).ToArray();
        Check(goods.Length>0&&upper.All(slot=>goods.All(item=>!item.Hitbox.GetGlobalRect().Intersects(CardFrame(slot).Merge(Price(slot))))),
            "relics, potions and card removal sit below the upper cards and prices");

        var timer=Stopwatch.StartNew();
        var prepared=native.GlobalUi.SubmenuStack.Stack.GetNode<NCardLibrary>("CovenantLibrary");
        Check(!prepared.Visible&&!prepared.GetNode<NCardLibraryGrid>("%CardGrid").VisibleCards.Any(),"the run prebuilds the hidden shell without building a card grid");
        items.GetNode<Button>("Spectrum").EmitSignal(Button.SignalName.Pressed);
        var ui=native.GlobalUi;
        var page=(NCardLibrary)ui.SubmenuStack.Stack.Peek()!;
        var grid=page.GetNode<NCardLibraryGrid>("%CardGrid");
        Check(page.IsVisibleInTree()&&!grid.VisibleCards.Any(),
            $"the selected library shell opens before building the card grid ({timer.ElapsedMilliseconds} ms)");
        await Wait(()=>grid.VisibleCards.Count()>500&&grid.GetTopRowOfCardNodes()?.FirstOrDefault()?.CardNode!=null);
        Check(grid.VisibleCards.All(card=>CardMembership.SpectrumColor(card)!=0)&&
            grid.GetTopRowOfCardNodes()!.First().CardNode!.Body.HasNode("CovenantTraitTip"),
            $"the large selected page contains the expected cards and native trait panels ({timer.ElapsedMilliseconds} ms)");
        var buttons=(GridContainer)page.FindChild("Bonds",true,false)!;
        var filter=buttons.GetNode<Button>("Guard");
        filter.ButtonPressed=true;
        filter.EmitSignal(Button.SignalName.Pressed);
        await Wait(()=>grid.VisibleCards.Any()&&grid.VisibleCards.All(card=>CardMembership.ReferenceBonds(card).Contains(Bond.Guard)));
        ui.SubmenuStack.Stack.Pop();
        await Wait(()=>hud.IsVisibleInTree()&&merchant.Inventory.IsOpen);
        Check(!ui.CapstoneContainer.InUse,"returning from the library restores the open merchant");
        Console.WriteLine("COVENANT_SHOP_LIBRARY_COMPLETE");
        tree.Quit(0);

        static NMerchantCard[] Stock(Control shelf)=>shelf.GetChildren().OfType<NMerchantCard>().Where(slot=>slot.Entry.IsStocked).ToArray();
        static Rect2 Price(NMerchantCard slot)=>slot.GetNode<Control>("%CostLabel").GetGlobalRect();
        static NCard? Card(NMerchantCard slot)=>slot.GetNode<Control>("%CardHolder").GetChildren().OfType<NCard>()
            .FirstOrDefault(card=>!card.IsQueuedForDeletion());
        static Rect2 CardFrame(NMerchantCard slot)=>Card(slot)!.GetNode<Control>("%Frame").GetGlobalRect();
        static Control? Tip(NMerchantCard slot)=>Card(slot)?.Body.GetNodeOrNull<Control>("CovenantTraitTip");

        async Task Wait(Func<bool> condition)
        {
            for(var i=0;i<200&&!condition();i++)
                await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
            if(!condition())throw new Exception("Timed out in the native shop/library probe.");
        }
    }

    private static void Check(bool condition,string description)
    {
        if(!condition)throw new Exception(description);
        Console.WriteLine("COVENANT_SHOP_LIBRARY_PASS "+description);
    }
}
