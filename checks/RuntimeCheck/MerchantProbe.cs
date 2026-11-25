using System.Text.Json;
using Covenant.Core;
using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;

internal static class MerchantProbe
{
    internal static async Task Run(SceneTree tree,string output)
    {
        var passed=new List<string>();
        void Check(bool ok,string message){if(!ok)throw new Exception(message);passed.Add(message);Console.WriteLine("COVENANT_MERCHANT_PASS "+message);}
        var run=RunManager.Instance.DebugOnlyGetState()!;var player=run.Players.Single();var terminal=CovenantTerminal.For(player)!;
        for(var retry=0;retry<100&&player.PlayerCombatState!.Phase!=PlayerTurnPhase.Play;retry++)
            await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
        Check(player.PlayerCombatState!.Phase==PlayerTurnPhase.Play,"the first native player turn finishes drawing before the combat fixtures are added");
        var context=new ThrowingPlayerChoiceContext();
        async Task<T> Add<T>(PileType pile,bool upgraded=false) where T:CardModel
        {
            var card=player.Creature.CombatState!.CreateCard<T>(player);
            if(upgraded)CardCmd.Upgrade(card);
            await CardPileCmd.Add(card,pile,CardPilePosition.Top,skipVisuals:true);
            return card;
        }
        await CardPileCmd.RemoveFromCombat(player.PlayerCombatState!.AllCards.ToArray());
        for(var i=0;i<5;i++)await Add<Injury>(PileType.Exhaust);
        var laterCurse=await Add<Injury>(PileType.Draw);var firstCurse=await Add<Injury>(PileType.Draw);
        var block=player.Creature.Block;var target=player.Creature.CombatState!.HittableEnemies.First();var enemyHp=target.CurrentHp;
        var curseBonus=terminal.Scale(Bond.Curse).A;
        await CardPileCmd.Draw(context,player);
        Check(firstCurse.Pile?.Type==PileType.Exhaust&&laterCurse.Pile?.Type==PileType.Hand&&player.Creature.Block==block+4+2*curseBonus&&target.CurrentHp==enemyHp-3-curseBonus,
            $"Curse automatically exhausts the first curse, gains block and draws the advanced replacement without exhausting it twice (piles {firstCurse.Pile?.Type}/{laterCurse.Pile?.Type}, block {player.Creature.Block-block}, damage {enemyHp-target.CurrentHp})");

        await CardPileCmd.RemoveFromCombat(player.PlayerCombatState.AllCards.ToArray());
        for(var i=0;i<4;i++)await Add<Claw>(PileType.Exhaust,upgraded:true);
        var upgradeTrigger=await Add<DefendIronclad>(PileType.Hand,upgraded:true);
        var upgradeTarget=await Add<StrikeIronclad>(PileType.Hand);
        await CardCmd.AutoPlay(context,upgradeTrigger,target);
        Check(upgradeTarget.IsUpgraded,"Mastery automatically upgrades the only eligible card without a prompt");

        await CardPileCmd.RemoveFromCombat(player.PlayerCombatState.AllCards.ToArray());
        for(var i=0;i<7;i++)(await Add<StrikeIronclad>(PileType.Exhaust)).AddKeyword(CardKeyword.Retain);
        var retained=await Add<StrikeIronclad>(PileType.Hand);retained.AddKeyword(CardKeyword.Retain);
        await terminal.AfterFlush(context,player,[],[retained]);
        await terminal.AfterPlayerTurnStart(context,player);
        Check(retained.EnergyCost.GetWithModifiers(CostModifiers.All)==0,"Retain automatically discounts the only eligible retained card");

        var seen=new HashSet<CardPoolModel>();var normal=new HashSet<CardRarity>{CardRarity.Common,CardRarity.Uncommon,CardRarity.Rare};
        var samples=new List<CardModel>();var colorlessPreserved=true;
        for(int i=0;i<40;i++)
        {
            var stock=MerchantInventory.CreateForNormalMerchant(player);
            samples.AddRange(stock.CharacterCardEntries.Select(e=>e.CreationResult!.Card));
            colorlessPreserved&=stock.ColorlessCardEntries.All(e=>e.CreationResult!.Card.Pool is ColorlessCardPool);
        }
        foreach(var card in samples)seen.Add(card.Pool);
        Check(colorlessPreserved,"native inventory keeps the dedicated colorless shelves across all sampled shops");
        Check(CovenantTerminal.SixPools.All(seen.Contains),"native merchant generation samples include all six character pools");
        Check(samples.All(c=>normal.Contains(c.Rarity)&&CovenantTerminal.SixPools.Contains(c.Pool)),"six-pool colored shelves preserve native common/uncommon/rare acquisition restrictions");
        terminal.AllCardPools=false;
        try
        {
            var ownStock=MerchantInventory.CreateForNormalMerchant(player);
            Check(ownStock.CharacterCardEntries.All(e=>e.CreationResult!.Card.Pool==player.Character.CardPool),"disabling this run's full pool restores the native character shelf");
        }
        finally{terminal.AllCardPools=true;}
        foreach(var enemy in player.Creature.CombatState!.Enemies.Where(e=>!e.IsDead).ToArray())await CreatureCmd.Kill(enemy,true);
        await CombatManager.Instance.CheckWinCondition();
        for(int i=0;i<300&&CombatManager.Instance.IsEnding;i++)await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
        // Two zero-layer covenants from the same card have different distances to I.
        await CardPileCmd.Add(player.RunState.CreateCard<Furnace>(player),PileType.Deck,skipVisuals:true);
        foreach(var definition in BondCatalog.All.Where(definition=>definition.UsesLayers&&definition.Id is not (Bond.Ritual or Bond.Creation)))
            if(terminal.Ledger.Get(definition.Id)==0)terminal.Ledger.Add(definition.Id,1);
        await CardPileCmd.Add(player.RunState.CreateCard<LanternKey>(player),PileType.Deck,skipVisuals:true);
        CovenantSettings.SetShowCardTraits(true);
        // Use the game's room-entry diagnostic after the real victory; this is not a full map-route claim.
        await RunManager.Instance.EnterRoomDebug(RoomType.Shop).WaitAsync(TimeSpan.FromSeconds(45));
        var room=NMerchantRoom.Instance??throw new Exception("Native merchant room missing");room.OpenInventory();
        await tree.ToSignal(tree.CreateTimer(.7),SceneTreeTimer.SignalName.Timeout);
        var inventory=RunManager.Instance.MerchantSynchronizer.GetLocalInventory();
        Check(room.Inventory.IsOpen&&run.CurrentRoom is MerchantRoom,"the real merchant room opens its native inventory UI");
        var hand=Walk(room).OfType<MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantHand>().First();
        CovenantSettings.SetHideMerchantHand(true);Check(hand.GetParent<CanvasItem>().Visible==false,"the native settings option hides the merchant hand immediately");
        CovenantSettings.SetHideMerchantHand(false);Check(hand.GetParent<CanvasItem>().Visible,"the native settings option restores the merchant hand");
        var bar=(Control)NRun.Instance!.FindChild("CovenantRoot",true,false)!.GetNode("BondBar");
        Check(!bar.Visible,"entering the merchant automatically folds the covenant strip");
        var sidebar=bar.GetParent().GetNode<Control>("TraitSidebar");
        Check(sidebar.IsVisibleInTree()&&sidebar.Size.X>=210&&Walk(sidebar).OfType<Label>().Any(label=>label.Text==$"{ModelDb.Card<LanternKey>().Title}（{BondCatalog.Get(Bond.Encounter).Name}）"),"the merchant's outside trait list names the scaling covenant after Lantern Key");
        var shelfCards=room.Inventory.GetAllSlots().OfType<NMerchantCard>().Where(slot=>slot.Entry.IsStocked)
            .Select(slot=>(Slot:slot,Card:slot.GetNode<Control>("%CardHolder").GetChildren().OfType<NCard>().Single(card=>!card.IsQueuedForDeletion()))).ToArray();
        foreach(var (_,card) in shelfCards)
        {
            var tip=card.Body.GetNode<Control>("CovenantTraitTip");
            var frameRect=card.GetNode<Control>("%Frame").GetGlobalRect();
            var gap=card.GetNode<Control>("%EnergyIcon").GetGlobalRect().Position.Y-tip.GetGlobalRect().End.Y;
            Check(tip.IsVisibleInTree()&&tip.Scale.X>=.90f&&tip.GetGlobalRect().Size.X>=frameRect.Size.X*.89f&&gap>=0,
                $"merchant card {card.Model!.Id} has a nearly card-width trait box above its cost before hover (scale={tip.Scale.X}, gap={gap})");
        }
        var upperShelf=room.Inventory.GetNode<Control>("%CharacterCards");
        var lowerShelf=room.Inventory.GetNode<Control>("%ColorlessCards");
        var upperPrices=shelfCards.Where(item=>item.Slot.GetParent()==upperShelf).Select(item=>item.Slot.GetNode<Control>("%CostLabel").GetGlobalRect());
        var lowerTips=shelfCards.Where(item=>item.Slot.GetParent()==lowerShelf).Select(item=>item.Card.Body.GetNode<Control>("CovenantTraitTip").GetGlobalRect());
        Check(lowerTips.Any()&&upperPrices.Any()&&lowerTips.Min(rect=>rect.Position.Y)>=upperPrices.Max(rect=>rect.End.Y),"the lower shelf trait boxes clear the upper shelf prices");
        NRun.Instance.GetViewport().GetTexture().GetImage().SavePng(output+"/0.5/merchant-traits.png");
        var (hoverSlot,hoverCard)=shelfCards[0];var hoverTip=hoverCard.Body.GetNode<Control>("CovenantTraitTip");
        var description=hoverTip.GetNode<RichTextLabel>("%Description");
        var longTrait=string.Join('\n',Enumerable.Repeat("商店里很长的卡牌特性描述。",12));
        description.Text=longTrait;
        await tree.ToSignal(tree.CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
        Check(!description.FitContent&&hoverTip.Scale.X>=.90f&&description.Size.Y<description.GetContentHeight(),
            "a long persistent merchant trait clips at its regular text size");
        hoverSlot.Hitbox.EmitSignal(Control.SignalName.MouseEntered);
        description.Text=longTrait;
        await tree.ToSignal(tree.CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
        Check(description.FitContent&&hoverTip.Scale.X>=.90f&&description.Size.Y>=description.GetContentHeight()-1,
            "hover opens the full long merchant trait at the same text size");
        hoverSlot.Hitbox.EmitSignal(Control.SignalName.MouseExited);
        var extendedShelfY=lowerShelf.Position.Y;
        CovenantSettings.SetShowCardTraits(false);
        Check(!hoverTip.Visible,"the merchant respects the persistent trait toggle");
        await tree.ToSignal(tree.CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
        Check(lowerShelf.Position.Y==lowerShelf.GetMeta("covenant_shelf_base_y").AsSingle()&&lowerShelf.Position.Y<extendedShelfY,
            "the lower merchant shelf returns to its native position without persistent traits");
        hoverSlot.Hitbox.EmitSignal(Control.SignalName.MouseEntered);
        Check(hoverTip.Visible,"native merchant hover shows the trait above the card with persistence off");
        hoverSlot.Hitbox.EmitSignal(Control.SignalName.MouseExited);
        Check(!hoverTip.Visible,"leaving the merchant card hides its hover-only trait");
        CovenantSettings.SetShowCardTraits(true);
        await tree.ToSignal(tree.CreateTimer(.6),SceneTreeTimer.SignalName.Timeout);
        bar.GetParent().GetNode<Button>("FoldCovenants").EmitSignal(Button.SignalName.Pressed);await tree.ToSignal(tree.CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
        Check(bar.Visible,"the arrow button unfolds the merchant covenant strip");
        var more=bar.GetNode<Button>("MoreCovenants");
        Check(shelfCards.Where(item=>item.Slot.GetParent()==upperShelf).Select(item=>item.Card.Body.GetNode<Control>("CovenantTraitTip").GetGlobalRect())
            .All(box=>!box.Intersects(bar.GetGlobalRect())&&(!more.Visible||!box.Intersects(more.GetGlobalRect()))),
            "top shelf trait boxes clear the visible strip and its arrow");
        var lowestPrice=shelfCards.Where(item=>item.Slot.GetParent()==lowerShelf).Max(item=>item.Slot.GetNode<Control>("%CostLabel").GetGlobalRect().End.Y);
        var gapBetweenShelves=lowerTips.Min(rect=>rect.Position.Y)-upperPrices.Max(rect=>rect.End.Y);
        Check(lowestPrice<=bar.GetViewportRect().End.Y-4&&gapBetweenShelves>=0,
            $"lower shelf boxes and prices stay inside their available space with statistics open (bottom {lowestPrice}, gap {gapBetweenShelves})");
        var relicBottom=NRun.Instance.GlobalUi.RelicInventory.RelicNodes.Where(h=>h.IsVisibleInTree()).Select(h=>h.GetGlobalRect().End.Y).DefaultIfEmpty(0).Max();
        Check(bar.GetGlobalRect().Position.Y<relicBottom&&Math.Abs(bar.GetGlobalRect().GetCenter().X-bar.GetViewportRect().Size.X/2)<1,"merchant statistics share the original relic row and stay centered beside the compact inventory");
        var saleSlot=room.Inventory.GetAllSlots().OfType<NMerchantCard>().Single(s=>s.Entry is MerchantCardEntry{IsOnSale:true});
        var sale=saleSlot.GetNode<Node2D>("%SaleVisual");
        var saleCard=saleSlot.GetNode<Control>("%CardHolder").GetChildren().OfType<NCard>().Single(c=>!c.IsQueuedForDeletion());
        var frame=saleCard.GetNode<Control>("%Frame").GetGlobalRect();
        var saleBounds=MerchantSaleLayout.VisibleBounds(sale)??throw new Exception("Native sale resource has no visible texture bounds");
        Check(sale.Visible&&saleBounds.Position.X>=frame.End.X+7.9f&&saleBounds.Position.Y>=frame.End.Y+9.9f,"native discount marker remains visible beside the price and outside the card and covenant column");
        var firstBadge=saleCard.Body.GetNode<Control>("CovenantIcons").GetChildren().OfType<Control>().First();
        var energy=saleCard.GetNode<Control>("%EnergyIcon");var energyCenter=energy.GetGlobalTransform()*(energy.Size/2);
        var badgeCenter=firstBadge.GetGlobalTransform()*new Vector2(36,30);var frameCenter=frame.GetCenter();
        Check((badgeCenter-new Vector2(2*frameCenter.X-energyCenter.X,energyCenter.Y)).Length()<.1f,"the on-sale card retains the exact cost-mirrored first covenant badge");
        Check(!saleBounds.Intersects(firstBadge.GetGlobalRect()),"the on-sale marker does not overlap the first named covenant badge");
        var scroller=bar.GetNode<ScrollContainer>("Scroller");
        var items=scroller.GetNode<Control>("Items");
        var preview=items.GetChildren().OfType<Button>().Select(button=>button.Name).ToArray();
        Check(preview.Length==10&&more.IsVisibleInTree()&&more.GlobalPosition.X>=bar.GetGlobalRect().End.X&&
            scroller.HorizontalScrollMode==ScrollContainer.ScrollMode.Disabled&&!scroller.GetHScrollBar().Visible,
            "the compact strip shows ten badges and an arrow to their right without a scrollbar");
        more.EmitSignal(Button.SignalName.Pressed);await tree.ToSignal(tree.CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
        var expanded=items.GetChildren().OfType<Button>().Select(button=>button.Name).ToArray();
        Check(expanded.Length>10&&expanded.Take(10).SequenceEqual(preview)&&scroller.ScrollHorizontal==0&&
            scroller.HorizontalScrollMode==ScrollContainer.ScrollMode.Auto&&scroller.FollowFocus,
            "the arrow reveals all covenants in the same order and starts scrolling from the left");
        var ritual=items.GetNode<Control>("Ritual/Badge");
        var creation=items.GetNode<Control>("Creation/Badge");
        Check(!ritual.HasNode("Stage")&&!creation.HasNode("Stage")&&Array.IndexOf(expanded,new StringName("Ritual"))<Array.IndexOf(expanded,new StringName("Creation"))&&
            ritual.GetMeta("segments").AsInt32()==BondCatalog.Get(Bond.Ritual).Basic&&creation.GetMeta("segments").AsInt32()==BondCatalog.Get(Bond.Creation).Basic&&
            ritual.GetMeta("filled_segments").AsInt32()==1&&creation.GetMeta("filled_segments").AsInt32()==1,
            "inactive zero-layer rings measure progress to I, with fewer missing cards shown first");
        var badgeTop=saleCard.Body.GetNode<Control>("CovenantIcons").GetChildren().OfType<Control>().First().GetGlobalRect().Position.Y;
        Check(badgeTop>=bar.GetGlobalRect().End.Y,$"the expanded covenant strip clears the merchant card's top covenant badge: badge={badgeTop} stripEnd={bar.GetGlobalRect().End.Y}");
        scroller.ScrollHorizontal=12;await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
        Check(scroller.ScrollHorizontal>0&&scroller.GetHScrollBar().Visible,"overflowing covenants expose the horizontal scroll bar");
        more.EmitSignal(Button.SignalName.Pressed);await tree.ToSignal(tree.CreateTimer(.2),SceneTreeTimer.SignalName.Timeout);
        Check(items.GetChildCount()==10&&scroller.ScrollHorizontal==0&&!scroller.GetHScrollBar().Visible,
            "the arrow returns to the first ten and resets scrolling");
        var foreign=inventory.CharacterCardEntries.First(e=>e.CreationResult!.Card.Pool!=player.Character.CardPool);
        var bought=foreign.CreationResult!.Card;var cost=foreign.Cost;
        await PlayerCmd.GainGold(500,player);var gold=player.Gold;var deck=player.Deck.Cards.Count;
        NRun.Instance!.GetViewport().GetTexture().GetImage().SavePng(output+"/merchant-six-pools.png");
        var purchased=await RunManager.Instance.MerchantSynchronizer.PurchaseLocalEntry(foreign).WaitAsync(TimeSpan.FromSeconds(30));
        Check(purchased&&player.Gold==gold-cost&&player.Deck.Cards.Count==deck+1&&player.Deck.Cards.Contains(bought),"native synchronized purchase adds the foreign card to the permanent deck and spends its displayed price");
        Check(!foreign.IsStocked,"the purchased native merchant slot becomes empty");
        await tree.ToSignal(tree.CreateTimer(.5),SceneTreeTimer.SignalName.Timeout);
        NRun.Instance.GetViewport().GetTexture().GetImage().SavePng(output+"/merchant-purchased.png");
        File.WriteAllText(output+"/merchant-runtime.json",JsonSerializer.Serialize(new{status="pass",completedUtc=DateTimeOffset.UtcNow,checks=passed,sampledColoredCards=samples.Count,pools=seen.Select(p=>p.GetType().Name).Order(),purchasedCard=bought.Id.ToString(),cost,modSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CovenantTerminal).Assembly.Location)))},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("COVENANT_MERCHANT_COMPLETE checks="+passed.Count);
    }
    private static IEnumerable<Node> Walk(Node root){foreach(var child in root.GetChildren()){yield return child;foreach(var nested in Walk(child))yield return nested;}}
}
