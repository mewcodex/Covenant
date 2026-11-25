using System.Text.Json;
using Covenant.Core;
using Covenant.Game;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using MegaCrit.Sts2.addons.mega_text;

internal static class LayoutProbe
{
    internal static async Task Relics(SceneTree tree)
    {
        var player=RunManager.Instance.DebugOnlyGetState()!.Players.Single();
        var inventory=NRun.Instance!.GlobalUi.RelicInventory;
        var fold=inventory.GetNode<Button>("CovenantRelicLayout/FoldRelics");
        var viewport=inventory.GetViewportRect();
        static void Check(bool ok,string message)
        {
            if(!ok)throw new Exception("Relic layout: "+message);
            Console.WriteLine("COVENANT_RELIC_LAYOUT_PASS "+message);
        }
        async Task Settle()
        {
            await tree.ToSignal(tree.CreateTimer(.6),SceneTreeTimer.SignalName.Timeout);
            await tree.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        }
        void CheckFirstRow(int count)
        {
            var holders=inventory.RelicNodes;
            var first=holders[0].GetGlobalRect();
            var previousRight=first.Position.X;
            for(var i=0;i<count;i++)
            {
                var holder=holders[i];
                var rect=holder.GetGlobalRect();
                var icon=holder.Relic.Icon;
                Console.WriteLine($"COVENANT_RELIC_LAYOUT_SLOT {i+1} holder={rect} icon={icon.GetGlobalRect()}");
                Check(holder.IsVisibleInTree() && icon.IsVisibleInTree() && icon.Texture!=null &&
                    icon.Modulate.A>.95f && viewport.Encloses(rect) && viewport.Encloses(icon.GetGlobalRect()),
                    $"slot {i+1} shows a complete relic inside the viewport");
                Check(Math.Abs(rect.Position.Y-first.Position.Y)<1 && rect.Position.X>=previousRight-.5f,
                    $"slot {i+1} is in the first row without overlap");
                previousRight=rect.End.X;
            }
            if(fold.Visible)Check(fold.GetGlobalRect().Position.X>=previousRight && viewport.Encloses(fold.GetGlobalRect()),
                "the expand button is outside the five relic slots and on screen");
        }
        void CheckTerminalIcon()
        {
            var relics=inventory.RelicNodes.Select(holder=>holder.Relic).ToArray();
            var starter=relics.Single(relic=>relic.Model is BurningBlood);
            var terminal=relics.Single(relic=>relic.Model is CovenantTerminal);
            Check(starter.Icon.Texture==ModelDb.Relic<BurningBlood>().Icon &&
                terminal.Icon.Texture==Presentation.Icon(Bond.Harmony) &&
                terminal.Outline.Texture==Presentation.Icon(Bond.Harmony),
                "Burning Blood and Covenant Terminal show their own icon and the Terminal uses its custom outline");
        }
        await Settle();
        Check(player.Relics.First() is BurningBlood && inventory.RelicNodes.Count==2,"native starter and Covenant Terminal are both present");
        CheckFirstRow(2);
        CheckTerminalIcon();
        foreach(var relic in new RelicModel[]{ModelDb.Relic<FishingRod>(),ModelDb.Relic<FestivePopper>(),ModelDb.Relic<LizardTail>()})
            await RelicCmd.Obtain(relic.ToMutable(),player);
        await Settle();
        Check(inventory.RelicNodes.Count==5 && !fold.Visible,"five relics require no expand button");
        CheckFirstRow(5);
        await RelicCmd.Obtain(ModelDb.Relic<Anchor>().ToMutable(),player);
        await Settle();
        Check(inventory.RelicNodes.Count==6 && !inventory.RelicNodes[5].Visible && fold.Visible,"the sixth relic exposes the expand button");
        CheckFirstRow(5);
        fold.EmitSignal(Button.SignalName.Pressed);
        await Settle();
        var first=inventory.RelicNodes[0].GetGlobalRect();
        var sixth=inventory.RelicNodes[5];
        Check(sixth.IsVisibleInTree() && sixth.Relic.Icon.IsVisibleInTree() && sixth.Relic.Icon.Modulate.A>.95f &&
            viewport.Encloses(sixth.GetGlobalRect()) && sixth.GlobalPosition.Y>=first.End.Y &&
            Math.Abs(sixth.GlobalPosition.X-first.Position.X)<1,"expanding shows the sixth relic at the start of the second row");
        fold.EmitSignal(Button.SignalName.Pressed);
        await Settle();
        Check(!sixth.Visible,"collapsing hides the second row");
        CheckFirstRow(5);
        await SaveManager.Instance.SaveRun(null);
        await NGame.Instance!.ReturnToMainMenu().WaitAsync(TimeSpan.FromSeconds(45));
        var menu=NGame.Instance.MainMenu!;
        await ((Task)AccessTools.Method(typeof(NMainMenu),"OnContinueButtonPressedAsync").Invoke(menu,[])!).WaitAsync(TimeSpan.FromSeconds(60));
        inventory=NRun.Instance!.GlobalUi.RelicInventory;
        fold=inventory.GetNode<Button>("CovenantRelicLayout/FoldRelics");
        await Settle();
        Check(inventory.RelicNodes.Count==6 && fold.Visible && !inventory.RelicNodes[5].Visible,
            "native Continue restores all six relics and defaults to a collapsed row");
        CheckTerminalIcon();
        CheckFirstRow(5);
        Console.WriteLine("COVENANT_RELIC_LAYOUT_COMPLETE");
    }

    internal static async Task BadgePreview(SceneTree tree,string output)
    {
        var specimen=new SubViewport{Size=new(360,210),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};tree.Root.AddChild(specimen);
        specimen.AddChild(new ColorRect{Color=new("172128"),Size=new(360,210)});
        var strike=BondCatalog.Get(Bond.Strike);
        var one=BondBadge.Create(strike,strike.Basic,250);one.Position=new(20,20);one.Scale=Vector2.One*2;specimen.AddChild(one);
        var two=BondBadge.Create(strike,strike.Advanced,500);two.Position=new(196,20);two.Scale=Vector2.One*2;specimen.AddChild(two);
        for(var i=0;i<8;i++)await tree.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        if(specimen.GetTexture().GetImage().SavePng(output+"/0.5/stage-ring-current.png")!=Error.Ok)throw new Exception("Cannot save badge preview");
        Console.WriteLine("COVENANT_BADGE_PREVIEW_COMPLETE");specimen.QueueFree();
    }
    internal static async Task Run(SceneTree tree,string output)
    {
        var checks=new List<string>();
        void Check(bool ok,string text){if(!ok)throw new Exception("Layout: "+text);checks.Add(text);Console.WriteLine("COVENANT_LAYOUT_PASS "+text);}
        async Task Frames(int count=8){for(var i=0;i<count;i++)await tree.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);}
        var run=NRun.Instance!;var player=LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState())!;
        var terminal=CovenantTerminal.For(player)!;CovenantSettings.SetShowCardTraits(true);
        var combat=player.Creature.CombatState!;
        var deckBefore=terminal.Counts(deck:true);
        var foreignStrike=combat.CreateCard<StrikeSilent>(player);
        var foreignDefend=combat.CreateCard<DefendFool>(player);
        foreach(var starter in new CardModel[]{foreignStrike,foreignDefend})
        {
            starter.UpgradeInternal();starter.FinalizeUpgradeInternal();
            await CardPileCmd.Add(starter,PileType.Exhaust,skipVisuals:true);
        }
        var liveCards=CardMembership.Cards(player).ToArray();
        var liveCounts=terminal.Counts();
        Check(liveCounts[Bond.Strike]==(liveCards.OfType<StrikeIronclad>().Count()+1)/2 &&
            liveCounts[Bond.Guard]==Math.Ceiling((liveCards.OfType<DefendIronclad>().Count()+1)/2m),
            "cross-color and upgraded starter copies share the same halved live count across combat piles");
        Check(liveCounts[Bond.Mastery]==liveCards.Count(c=>c.IsUpgraded) && terminal.Counts(deck:true).SequenceEqual(deckBefore),
            "halving only affects Strike and Guard and does not move combat copies into the deck count");
        Check(Presentation.TraitText(foreignStrike).Contains("同名基础牌计入【打击】的张数减半，向下取整。") &&
            Presentation.TraitText(foreignDefend).Contains("同名基础牌计入【坚守】的张数减半，向上取整。"),
            "both starter variants describe their counting trait");
        for(var i=0;i<8;i++)await CardPileCmd.Add(player.Creature.CombatState!.CreateCard<StrikeIronclad>(player),PileType.Exhaust,skipVisuals:true);
        for(var i=0;i<2;i++)await CardPileCmd.Add(player.Creature.CombatState!.CreateCard<DeadlyPoison>(player),PileType.Exhaust,skipVisuals:true);
        for(var i=0;i<5;i++)
        {
            await CardPileCmd.Add(player.Creature.CombatState!.CreateCard<DemonForm>(player),PileType.Exhaust,skipVisuals:true);
            await CardPileCmd.Add(player.Creature.CombatState!.CreateCard<Bulwark>(player),PileType.Exhaust,skipVisuals:true);
        }
        await CardPileCmd.Add(player.Creature.CombatState!.CreateCard<Entropy>(player),PileType.Exhaust,skipVisuals:true);
        await CardPileCmd.Add(combat.CreateCard<Break>(player),PileType.Exhaust,skipVisuals:true);
        Check(DisplayServer.WindowGetMode()==DisplayServer.WindowMode.Windowed,"isolated game stays windowed");
        Check(SaveManager.Instance.PrefsSave.FastMode==FastModeType.Fast,"native fast mode is enabled");
        var safe=new[]{"Vajra","Anchor","BagOfMarbles","BronzeScales","Orichalcum","OddlySmoothStone","PenNib","HappyFlower","BloodVial","Lantern","ArtOfWar","HornCleat"};
        foreach(var name in safe)
        {
            var model=ModelDb.AllRelics.FirstOrDefault(r=>r.GetType().Name==name);
            if(model!=null&&!player.Relics.Any(r=>r.Id==model.Id))await RelicCmd.Obtain(model.ToMutable(),player);
        }
        foreach(var definition in BondCatalog.All.Where(b=>b.UsesLayers).Take(6))terminal.Ledger.Add(definition.Id,250);
        await tree.ToSignal(tree.CreateTimer(3),SceneTreeTimer.SignalName.Timeout);await Frames(20);
        var inventory=run.GlobalUi.RelicInventory;
        var fold=inventory.GetNode<Button>("CovenantRelicLayout/FoldRelics");
        Check(inventory.RelicNodes.Count>10,"real native relic acquisition supplies multiple rows");
        Check(inventory.RelicNodes.Count(h=>h.Visible)==5,"relic inventory defaults to five visible holders");
        var hud=run.GlobalUi.GetNode<Control>("CovenantRoot");var bar=hud.GetNode<Control>("BondBar");var y=bar.GlobalPosition.Y;
        Check(Math.Abs(bar.GetGlobalRect().GetCenter().X-hud.GetViewportRect().Size.X/2)<1,"covenant statistics remain centered");
        Check(bar.GlobalPosition.Y<inventory.RelicNodes[0].GlobalPosition.Y+inventory.RelicNodes[0].Size.Y,"statistics occupy the original relic row");
        await Snapshot("layout-collapsed.png");
        fold.EmitSignal(Button.SignalName.Pressed);await Frames();
        Check(inventory.RelicNodes.All(h=>h.Visible),"expanding shows every native relic holder");
        var first=inventory.RelicNodes[0];var sixth=inventory.RelicNodes[5];
        Check(sixth.GlobalPosition.Y>first.GlobalPosition.Y&&Math.Abs(sixth.GlobalPosition.X-first.GlobalPosition.X)<1,"relics wrap downward at five columns");
        Check(Math.Abs(bar.GlobalPosition.Y-y)<1,"expanded relics do not push statistics down");
        await Snapshot("layout-expanded.png");
        fold.EmitSignal(Button.SignalName.Pressed);await Frames();
        Check(inventory.RelicNodes.Count(h=>h.Visible)==5,"relic inventory collapses back to one row");
        var card=player.Creature.CombatState!.CreateCard<StrikeIronclad>(player);await CardPileCmd.Add(card,PileType.Hand);
        await Frames();var node=NCard.FindOnTable(card)!;node.SetMeta("covenant_hover",true);CardTraitTip.Update(node);
        var tip=node.Body.GetNode<Control>("CovenantTraitTip");Check(tip.Visible,"hovered hand card shows its trait above the card");
        node.AnimCardToPlayPile();Check(!tip.Visible,"trait disappears at the beginning of the outgoing card animation");
        node.SetMeta("covenant_hover",true);CardTraitTip.Update(node);Check(!tip.Visible,"a stale hover cannot restore an outgoing trait");
        node.PlayPileTween?.Kill();node.UpdateVisuals(PileType.Hand,CardPreviewMode.Normal);Check(tip.Visible,"returning to a hand context restores the trait");
        foreach(var definition in BondCatalog.All.Where(b=>b.UsesLayers))terminal.Ledger.Add(definition.Id,250);
        await Frames(20);
        var scroll=bar.GetNode<ScrollContainer>("Scroller");
        bar.GetNode<Button>("MoreCovenants").EmitSignal(Button.SignalName.Pressed);await Frames(5);
        Check(scroll.GetHScrollBar().MaxValue>scroll.Size.X,"the expanded covenant strip uses horizontal scrolling within the reserved central width");
        Check(scroll.ScrollHorizontal==0,"covenant scrolling starts at the left edge");
        var buttons=bar.GetNode("Scroller/Items").GetChildren().OfType<Button>().ToArray();
        Check(buttons.Length==BondCatalog.All.Count,"all covenant badges, including both secondary covenants, are available");
        foreach(var button in buttons)
        {
            button.EmitSignal(Control.SignalName.MouseEntered);await Frames(2);
            var detail=hud.GetNode<Control>("BondDetails");
            Check(detail.Visible&&detail.GetGlobalRect().End.Y<=hud.GetViewportRect().Size.Y,"native covenant details fit the viewport: "+button.Name);
            button.EmitSignal(Control.SignalName.MouseExited);
            if(button.Name!="Strike")continue;
            var title=Find(detail).OfType<Label>().First();
            var advanced=Find(detail).OfType<MegaRichTextLabel>().First(label=>label.GetParent().Name=="Advanced").GetParsedText();
            Check(title.Text==$"打击 ({terminal.Counts()[Bond.Strike]}张)" && advanced.Contains("，并使你的下一张打击牌每次") &&
                !advanced.Contains('\n') && !Find(detail).OfType<MegaRichTextLabel>().Any(label=>label.Text.Contains("普通战斗胜利")),
                "Strike uses matching parentheses and keeps the next-Strike bonus in the triggering sentence");
        }
        await Snapshot("layout-many-covenants.png");
        var specimen=new SubViewport{Size=new(360,210),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};tree.Root.AddChild(specimen);
        specimen.AddChild(new ColorRect{Color=new("172128"),Size=new(360,210)});
        var strike=BondCatalog.Get(Bond.Strike);
        var stageOne=BondBadge.Create(strike,strike.Basic,250);stageOne.Position=new(20,20);stageOne.Scale=Vector2.One*2;specimen.AddChild(stageOne);
        var stageTwo=BondBadge.Create(strike,strike.Advanced,500);stageTwo.Position=new(196,20);stageTwo.Scale=Vector2.One*2;specimen.AddChild(stageTwo);
        await Frames();specimen.GetTexture().GetImage().SavePng(output+"/0.5/stage-ring-specimen.png");specimen.QueueFree();
        File.WriteAllText(output+"/0.5/layout-native.json",JsonSerializer.Serialize(new{status="pass",checks,completedUtc=DateTimeOffset.UtcNow,
            modSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CovenantTerminal).Assembly.Location))),
            viewport=hud.GetViewportRect().Size.ToString(),window=DisplayServer.WindowGetSize().ToString(),inventoryWidth=inventory.Size.X,statisticsTop=bar.GlobalPosition.Y},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("COVENANT_LAYOUT_COMPLETE checks="+checks.Count);
        async Task Snapshot(string file){await Frames();if(tree.Root.GetTexture().GetImage().SavePng(output+"/0.5/"+file)!=Error.Ok)throw new Exception("Cannot save "+file);}
    }

    internal static async Task VerifyGrid(SceneTree tree, NCardGrid grid, string context, string? screenshot=null)
    {
        await tree.ToSignal(tree.CreateTimer(.7),SceneTreeTimer.SignalName.Timeout);
        var rows=(List<List<NGridCardHolder>>)AccessTools.Field(typeof(NCardGrid),"_cardRows").GetValue(grid)!;
        var cardSize=(Vector2)AccessTools.Field(typeof(NCardGrid),"_cardSize").GetValue(grid)!;
        var first=rows[0][0];
        if(rows.Count<2||rows[0].Count<2 || Math.Abs(first.Position.Y-cardSize.Y/2-80-LibraryGridSpacing.TopOffset(grid))>.1 ||
            Math.Abs(rows[1][0].Position.Y-first.Position.Y-cardSize.Y-40-LibraryGridSpacing.ExtraRow)>.1 ||
            Math.Abs(rows[0][1].Position.X-first.Position.X-cardSize.X-40)>.1)
            throw new Exception(context+" grid has incorrect top or row spacing");
        var card=first.CardNode!;
        var tip=card.Body.GetNode<Control>("CovenantTraitTip");
        var icon=tip.GetNode<Control>("%Icon");var title=tip.GetNode<Control>("%Title");
        var gap=icon.Position.X-title.Position.X-title.Size.X;
        var rowGap=rows[1][0].CardNode!.Body.GetNode<Control>("CovenantTraitTip").GetGlobalRect().Position.Y-card.GetNode<Control>("%Frame").GetGlobalRect().End.Y;
        if(!tip.Visible||tip.Scale.X<.55f||gap<9.5f||rowGap<0)
            throw new Exception($"{context} trait is crowded before hover (scale={tip.Scale.X}, icon gap={gap}, row gap={rowGap}, first holder scale={first.Scale})");
        if(grid.GetNodeOrNull<Control>("ScrollContainer/SortingOptions") is {} sorting &&
            tip.GetGlobalRect().Position.Y < sorting.GetGlobalRect().End.Y + 6)
            throw new Exception(context+" trait box overlaps the sorting controls");
        var naturalScale=tip.Scale.X;
        grid.SetScrollPosition(-first.Position.Y+120);
        await tree.ToSignal(tree.CreateTimer(.15),SceneTreeTimer.SignalName.Timeout);
        if(Math.Abs(tip.Scale.X-naturalScale)>.02)
            throw new Exception(context+" trait shrinks at the viewport edge");
        grid.SetScrollPosition(0);
        await tree.ToSignal(tree.CreateTimer(.15),SceneTreeTimer.SignalName.Timeout);
        if(screenshot!=null)tree.Root.GetTexture().GetImage().SavePng(screenshot);
        Console.WriteLine("COVENANT_GRID_PASS "+context+" keeps normal horizontal spacing and visible, separated trait boxes before hover");
        CovenantSettings.SetShowCardTraits(false);
        try
        {
            await tree.ToSignal(tree.CreateTimer(.15),SceneTreeTimer.SignalName.Timeout);
            if(first!=rows[0][0]||tip.Visible||Math.Abs(first.Position.Y-cardSize.Y/2-80-grid.YOffset)>.1||
                Math.Abs(rows[1][0].Position.Y-first.Position.Y-cardSize.Y-40)>.1||
                Math.Abs(rows[0][1].Position.X-first.Position.X-cardSize.X-40)>.1)
                throw new Exception(context+" did not restore the native grid positions and hide its persistent traits");
            Console.WriteLine("COVENANT_GRID_PASS "+context+" restores original spacing in place when persistent traits are disabled");
        }
        finally{CovenantSettings.SetShowCardTraits(true);}
        if(!tip.Visible||Math.Abs(rows[1][0].Position.Y-first.Position.Y-cardSize.Y-40-LibraryGridSpacing.ExtraRow)>.1)
            throw new Exception(context+" did not restore the extra row space with persistent traits enabled");
    }
    private static IEnumerable<Node> Find(Node root){foreach(var child in root.GetChildren()){yield return child;foreach(var nested in Find(child))yield return nested;}}
}
