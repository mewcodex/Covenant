using Covenant.Core;
using Covenant.Game;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.addons.mega_text;
using System.Text.Json;
using System.Text.RegularExpressions;

[ModInitializer(nameof(Initialize))]
public static class Probe
{
    private static bool _started;
    private static readonly TaskCompletionSource<MegaCrit.Sts2.Core.Assets.AssetLoadingSession> CommonAssets=new();
    public static void Initialize()
    {
        if(!Path.GetFileName(OS.GetUserDataDir()).StartsWith("CovenantSmoke-",StringComparison.Ordinal))throw new InvalidOperationException("Probe requires disposable user data.");
        new Harmony("Covenant.RuntimeCheck").Patch(AccessTools.Method(typeof(NMainMenu),nameof(NMainMenu._Ready)),postfix:new HarmonyMethod(typeof(Probe),nameof(Check)));
        new Harmony("Covenant.RuntimeCheck.Tutorial").Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Saves.SaveManager),nameof(MegaCrit.Sts2.Core.Saves.SaveManager.SeenFtue)),prefix:new HarmonyMethod(typeof(Probe),nameof(SeenTutorial)));
        new Harmony("Covenant.RuntimeCheck.Assets").Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.NAssetLoader),nameof(MegaCrit.Sts2.Core.Nodes.NAssetLoader.LoadInTheBackground)),prefix:new HarmonyMethod(typeof(Probe),nameof(Loading)));
    }
    private static void Loading(MegaCrit.Sts2.Core.Assets.AssetLoadingSession session)
    {if((string?)AccessTools.Field(typeof(MegaCrit.Sts2.Core.Assets.AssetLoadingSession),"_name").GetValue(session)=="Common")CommonAssets.TrySetResult(session);}
    private static bool SeenTutorial(ref bool __result){__result=true;return false;}
    private static async void Check(NMainMenu __instance)
    {
        if(_started)return;_started=true;
        var output=System.Environment.GetEnvironmentVariable("COVENANT_AUDIT_DIR")??"G:/sts2modding/Covenant/audits";Directory.CreateDirectory(output);
        try
        {
            if(System.Environment.GetEnvironmentVariable("COVENANT_UI_INSPECT")=="1")
            {
                File.WriteAllText(output+"/native-ui-tree.txt",__instance.GetTree().Root.GetTreeStringPretty());
                File.WriteAllText(output+"/native-ui-layers.json",JsonSerializer.Serialize(Walk(__instance.GetTree().Root).Where(n=>n is CanvasLayer||n.Name.ToString().Contains("Transition")||n.Name.ToString().Contains("HoverTipsContainer")).Select(n=>new{path=n.GetPath().ToString(),type=n.GetType().Name,layer=n is CanvasLayer canvas?canvas.Layer:0,z=n is CanvasItem item?item.ZIndex:0}),new JsonSerializerOptions{WriteIndented=true}));
                ResourceSaver.Save(GD.Load<PackedScene>("res://scenes/ui/hover_tip.tscn"),output+"/native-hover-tip.tscn");
                Console.WriteLine("COVENANT_NATIVE_UI_INSPECTED");__instance.GetTree().Quit(0);return;
            }
            // The menu's _Ready fires before its deferred background load. Wait for that exact session so the
            // probe does not synchronously request shared resources while Godot is still loading them in workers.
            var common=await CommonAssets.Task.WaitAsync(TimeSpan.FromSeconds(45));await common.Task.WaitAsync(TimeSpan.FromSeconds(45));
            LocManager.Instance.SetLanguage("zhs");
            MegaCrit.Sts2.Core.Saves.SaveManager.Instance.PrefsSave.FastMode=MegaCrit.Sts2.Core.Settings.FastModeType.Fast;
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new(1280,720));
            var revisionFocus=System.Environment.GetEnvironmentVariable("COVENANT_REVISION_FOCUS");
            if(revisionFocus=="performance")
            {
                var performanceTree=__instance.GetTree();
                await PerformanceProbe.Run(performanceTree,output+"/0.5");
                performanceTree.Quit(0);return;
            }
            if(revisionFocus=="random-resume")
            {
                CovenantRandomProbe.Resume(output);__instance.GetTree().Quit(0);return;
            }
            if (revisionFocus=="scaling")
            {
                var scalingTree=__instance.GetTree();
                await FloorScalingProbe.Run(scalingTree);
                scalingTree.Quit(0);
                return;
            }
            if (revisionFocus=="library")
            {
                var libraryTree = __instance.GetTree();
                await LibraryProbe.Run(__instance, output);
                libraryTree.Quit(0);
                return;
            }
            if (revisionFocus=="compendium")
            {
                await LibraryProbe.RunCompendium(__instance);
                __instance.GetTree().Quit(0);
                return;
            }
            if (revisionFocus=="oracle")
            {
                var oracleTree=__instance.GetTree();
                await OracleProbe.Run(oracleTree);
                oracleTree.Quit(0);
                return;
            }
            if (revisionFocus=="shop-library-ui")
            {
                await ShopLibraryProbe.Run(__instance.GetTree(),output);
                return;
            }
            if(System.Environment.GetEnvironmentVariable("COVENANT_REVISION_FOCUS")=="badge")
            {
                await LayoutProbe.BadgePreview(__instance.GetTree(),output);__instance.GetTree().Quit(0);return;
            }
            if(System.Environment.GetEnvironmentVariable("COVENANT_REVISION_FOCUS")=="resume")
            {
                await SaveProbe.Resume(__instance,output);__instance.GetTree().Quit(0);return;
            }
            if(System.Environment.GetEnvironmentVariable("COVENANT_UI_LAYOUT")=="1")
            {
                var layoutTree=__instance.GetTree();await CombatProbe.Prepare(layoutTree);
                await LayoutProbe.Run(layoutTree,output);
                var runPlayer=RunManager.Instance.DebugOnlyGetState()!.Players.Single();
                var pile=NCardPileScreen.ShowScreen(runPlayer.PlayerCombatState!.ExhaustPile,[]);
                await LayoutProbe.VerifyGrid(layoutTree,pile.GetNode<NCardGrid>("CardGrid"),"combat exhaust pile");
                NCapstoneContainer.Instance!.Close();
                var previousFocus=System.Environment.GetEnvironmentVariable("COVENANT_REVISION_FOCUS");
                try{System.Environment.SetEnvironmentVariable("COVENANT_REVISION_FOCUS","descriptions");await RevisionProbe.Run(layoutTree,output);}
                finally{System.Environment.SetEnvironmentVariable("COVENANT_REVISION_FOCUS",previousFocus);}
                layoutTree.Quit(0);return;
            }
            if(System.Environment.GetEnvironmentVariable("COVENANT_REVISION_ONLY")=="1")
            {
                var revisionTree=__instance.GetTree();
                if(revisionFocus=="ritual-latch")
                {
                    await ReworkProbe.RunRitualLatch(revisionTree);
                    revisionTree.Quit(0);return;
                }
                await CombatProbe.Prepare(revisionTree,character:revisionFocus=="orbs"?ModelDb.Character<Defect>():null);
                if(revisionFocus=="compatibility")
                {
                    await CompatibilityProbe.Run(revisionTree,RunManager.Instance.DebugOnlyGetState()!.Players.Single());
                    System.Environment.SetEnvironmentVariable("COVENANT_REVISION_FOCUS","descriptions");
                    await RevisionProbe.Run(revisionTree,output);
                    var nativePath=typeof(CardModel).Assembly.Location;
                    var modPath=typeof(CovenantTerminal).Assembly.Location;
                    string Digest(string path)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
                    Console.WriteLine($"COVENANT_COMPAT_COMPLETE mod={Digest(modPath)} native={Digest(nativePath)}");
                    revisionTree.Quit(0);return;
                }
                if(revisionFocus=="growth-triggers")
                {
                    await ReworkProbe.RunGrowthTriggers(revisionTree,RunManager.Instance.DebugOnlyGetState()!.Players.Single());
                    revisionTree.Quit(0);return;
                }
                if(revisionFocus=="envenom")
                {
                    await ReworkProbe.RunEnvenomPenetration(revisionTree,RunManager.Instance.DebugOnlyGetState()!.Players.Single());
                    revisionTree.Quit(0);return;
                }
                if(revisionFocus=="orbs")
                {
                    await OrbChannelProbe.Run(revisionTree,RunManager.Instance.DebugOnlyGetState()!.Players.Single());
                    revisionTree.Quit(0);return;
                }
                if(revisionFocus=="status-selection")
                {
                    await ReworkProbe.RunStatusSelection(revisionTree,RunManager.Instance.DebugOnlyGetState()!.Players.Single());
                    revisionTree.Quit(0);return;
                }
                if(revisionFocus=="generation-pools")
                {
                    await GenerationPoolsProbe.Run(revisionTree,RunManager.Instance.DebugOnlyGetState()!.Players.Single());
                    revisionTree.Quit(0);return;
                }
                if(revisionFocus=="relics")
                {
                    await LayoutProbe.Relics(revisionTree);revisionTree.Quit(0);return;
                }
                if(revisionFocus=="attrition")
                {
                    await AttritionProbe.Run(revisionTree,RunManager.Instance.DebugOnlyGetState()!.Players.Single());
                    revisionTree.Quit(0);return;
                }
                if(revisionFocus=="random")
                {
                    await CovenantRandomProbe.Run(output);revisionTree.Quit(0);return;
                }
                if(revisionFocus=="settlement")
                {
                    await ReworkProbe.RunSettlement(RunManager.Instance.DebugOnlyGetState()!.Players.Single());
                    revisionTree.Quit(0);return;
                }
                if(revisionFocus=="profit")
                {
                    await RewardProbe.ProfitSpending(RunManager.Instance.DebugOnlyGetState()!.Players.Single());
                    revisionTree.Quit(0);return;
                }
                if(revisionFocus=="approved-traits")
                {
                    await ApprovedTraitsProbe.Run(revisionTree);
                    System.Environment.SetEnvironmentVariable("COVENANT_REVISION_FOCUS","descriptions");
                    await RevisionProbe.Run(revisionTree,output);
                    revisionTree.Quit(0);return;
                }
                if(revisionFocus is "reworks" or "balance-058")
                {
                    try{System.Environment.SetEnvironmentVariable("COVENANT_REVISION_FOCUS","descriptions");await RevisionProbe.Run(revisionTree,output);}
                    finally{System.Environment.SetEnvironmentVariable("COVENANT_REVISION_FOCUS",revisionFocus);}
                    await ReworkProbe.Run(revisionTree,RunManager.Instance.DebugOnlyGetState()!.Players.Single(),balanceOnly:revisionFocus=="balance-058");
                    revisionTree.Quit(0);return;
                }
                if(revisionFocus=="traits")
                    await RevisionProbe.TraitSelectionUi(revisionTree,RunManager.Instance.DebugOnlyGetState()!.Players.Single());
                if(revisionFocus=="event")
                {
                    await RewardProbe.Run(revisionTree,output,victoryOnly:true,verifyTraitSidebar:true);
                    var runPlayer=RunManager.Instance.DebugOnlyGetState()!.Players.Single();
                    await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Amalgamator>(),showTransition:false);
                    var deck=NDeckViewScreen.ShowScreen(runPlayer)!;
                    await LayoutProbe.VerifyGrid(revisionTree,deck.GetNode<NCardGrid>("CardGrid"),"deck opened from an event",output+"/0.5/event-deck-traits.png");
                    Console.WriteLine("COVENANT_EVENT_COMPLETE");revisionTree.Quit(0);return;
                }
                if(System.Environment.GetEnvironmentVariable("COVENANT_REVISION_FOCUS")=="rewards")
                {
                    await RewardProbe.Run(revisionTree,output);await SaveProbe.Write(output);
                    Console.WriteLine("COVENANT_REWARDS_SAVE_COMPLETE");revisionTree.Quit(0);return;
                }
                if(System.Environment.GetEnvironmentVariable("COVENANT_REVISION_FOCUS")=="merchant")
                {await MerchantProbe.Run(revisionTree,output);revisionTree.Quit(0);return;}
                if(revisionFocus=="acquisition-preview")
                {await AcquisitionPreviewProbe.Run(revisionTree);revisionTree.Quit(0);return;}
                if(System.Environment.GetEnvironmentVariable("COVENANT_REVISION_FOCUS")=="release")
                {
                    await RevisionProbe.Run(revisionTree,output);await LayoutProbe.Run(revisionTree,output);
                    await RewardProbe.Run(revisionTree,output);await SaveProbe.Write(output);
                    File.WriteAllText(output+"/runtime-latest.json",JsonSerializer.Serialize(new{status="pass",completedUtc=DateTimeOffset.UtcNow,modSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CovenantTerminal).Assembly.Location)))},new JsonSerializerOptions{WriteIndented=true}));
                    Console.WriteLine("COVENANT_RELEASE_COMPLETE");revisionTree.Quit(0);return;
                }
                await RevisionProbe.Run(revisionTree,output);
                if(revisionFocus=="traits")await RewardProbe.Run(revisionTree,output,victoryOnly:true,verifyTraitSidebar:true);
                revisionTree.Quit(0);return;
            }
            if(System.Environment.GetEnvironmentVariable("COVENANT_CLIMB")=="1")
            {ClimbProbe.Start(output+"/0.5");return;}
            if(System.Environment.GetEnvironmentVariable("COVENANT_SAVE_RESUME")=="1")
            {
                var resumeTree=__instance.GetTree();await SaveProbe.Resume(__instance,output);
                if(File.Exists(output+"/probe-failure.txt"))File.Delete(output+"/probe-failure.txt");
                resumeTree.Quit(0);return;
            }
            CovenantSettings.SetShowCardTraits(true);
            var player=Player.CreateForNewRun(ModelDb.Character<Ironclad>(),UnlockState.all,7801);
            var run=RunState.CreateForTest([player],[],[],seed:"covenant-ui");
            var terminal=CovenantTerminal.For(player)??throw new Exception("Terminal missing from native new run");
            terminal.Ledger.Add(Bond.Strike,42);terminal.Ledger.Add(Bond.Poison,56);terminal.Ledger.Add(Bond.Guard,18);terminal.Ledger.Add(Bond.Mastery,20);
            var scene=GD.Load<PackedScene>("res://scenes/cards/card.tscn");
            var viewport=new SubViewport{Size=new(1440,850),TransparentBg=false,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};__instance.GetTree().Root.AddChild(viewport);
            viewport.AddChild(new ColorRect{Color=new("172128"),Size=new(1440,850)});
            var hud=Hud.AddTo(viewport,player);
            var cards=new CardModel[]{ModelDb.Card<StrikeIronclad>().ToMutable(),ModelDb.Card<Entropy>().ToMutable(),ModelDb.Card<DeadlyPoison>().ToMutable(),ModelDb.Card<WahChoKi>().ToMutable()};
            var nodes=new List<NCard>();var results=new List<object>();
            for(int i=0;i<cards.Length;i++)
            {
                var card=cards[i];card.Owner=player;if(i is 1 or 3)card.UpgradeInternal();
                var node=scene.Instantiate<NCard>();node.OnInstantiated();node.Model=card;node.Visibility=ModelVisibility.Visible;viewport.AddChild(node);node.Position=new(210+i*340,575);node.UpdateVisuals(PileType.Deck,CardPreviewMode.Normal);nodes.Add(node);
            }
            // Match the real scene: GlobalUi is above the combat/card scene on the
            // same canvas. The standalone specimen must preserve that child order.
            viewport.MoveChild(hud,-1);
            for(int i=0;i<12;i++)await __instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var specimenTrait=CardMembership.Definition(cards[0])!.Trait;var specimenBefore=terminal.Ledger.Get(specimenTrait.Bond);
            var specimenAmount=specimenTrait.Effects!.Single().Amount;
            terminal.Ledger.Add(specimenTrait.Bond,specimenAmount);Presentation.RefreshCardTraits();
            if(nodes[0].Body.GetNode<Control>("CovenantTraitTip").GetNode<MegaRichTextLabel>("%Description").Text.Contains("至多"))throw new Exception("The single-trait display invents a growth limit");
            await __instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            foreach(var node in nodes)
            {
                var cardText=node.GetNode<MegaRichTextLabel>("%DescriptionLabel").Text;
                if(cardText.Contains("每场战斗至多"))throw new Exception("Card trait still occupies the native card description: "+node.Model!.Id);
                var traitTip=node.Body.GetNode<Control>("CovenantTraitTip");var traitText=traitTip.GetNode<MegaRichTextLabel>("%Description").Text;
                if(string.IsNullOrWhiteSpace(traitText)||!traitText.Contains("增加")||traitTip.SceneFilePath!="res://scenes/ui/hover_tip.tscn")throw new Exception("Persistent trait tip does not reuse the native keyword-tip resource");
                var frameTop=node.Body.GetGlobalTransform().AffineInverse()*(node.GetNode<Control>("%Frame").GetGlobalTransform()*Vector2.Zero);
                var costTop=node.Body.GetGlobalTransform().AffineInverse()*(node.GetNode<Control>("%EnergyIcon").GetGlobalTransform()*Vector2.Zero);
                if(Math.Abs(Math.Min(frameTop.Y,costTop.Y)-traitTip.Position.Y-traitTip.Size.Y*traitTip.Scale.Y-CardTraitTip.Gap)>.1f)throw new Exception("Persistent trait tip uses the wrong card-and-cost-to-tip gap");
                var icons=node.Body.GetNode<Control>("CovenantIcons");var first=icons.GetChild<Control>(0);var energy=node.GetNode<Control>("%EnergyIcon");
                var energyCenter=node.Body.GetGlobalTransform().AffineInverse()*(energy.GetGlobalTransform()*(energy.Size/2));
                var frame=node.GetNode<Control>("%Frame");var frameCenter=node.Body.GetGlobalTransform().AffineInverse()*(frame.GetGlobalTransform()*(frame.Size/2));
                var logoCenter=first.Position+new Vector2(36,30)*first.Scale;
                if(Math.Abs(energyCenter.Y-logoCenter.Y)>.1f||Math.Abs(energyCenter.X+logoCenter.X-2*frameCenter.X)>.1f)throw new Exception("Card icon is not symmetric with energy icon: "+node.Model!.Id);
                if(icons.GetChildCount()!=CardMembership.Of(node.Model!).Count)throw new Exception("Missing card bond icon");
                foreach(var badge in icons.GetChildren().OfType<Control>())
                {
                    var caption=badge.GetNode<Label>("Caption");
                    if(caption.Text!=BondCatalog.Get(Enum.Parse<Bond>(badge.Name.ToString())).Name)throw new Exception("Card badge does not name its covenant");
                    if(caption.GetIndex()<=badge.GetNode<Control>("Logo").GetIndex())throw new Exception("Card caption is behind logo");
                }
                results.Add(new{card=node.Model!.Id.Entry,energyCenter=new[]{energyCenter.X,energyCenter.Y},logoCenter=new[]{logoCenter.X,logoCenter.Y},frameCenter=new[]{frameCenter.X,frameCenter.Y},logos=icons.GetChildCount()});
            }
            nodes[0].UpdateVisuals(PileType.Deck,CardPreviewMode.Upgrade);
            if(!nodes[0].Body.GetNode<Control>("CovenantTraitTip").GetNode<MegaRichTextLabel>("%Description").Text.Contains("增加2层"))throw new Exception("Upgrade preview does not show the upgraded single trait amount");
            nodes[0].UpdateVisuals(PileType.Deck,CardPreviewMode.Normal);
            var zero=ModelDb.Card<BattleTrance>().ToMutable();zero.Owner=player;
            if(!BondDescription.Format("获得{energyPrefix:energyIcons(1)}。",zero).Contains("[img]res://images/packed/sprite_fonts/ironclad_energy_icon.png[/img]"))throw new Exception("Card trait formatting does not support the native energy icon");
            var traitDescriptions=0;var missingNativeDescriptions=new List<string>();var traitTextAudit=new List<object>();
            var nativeTypes=typeof(CardModel).Assembly.GetTypes().Where(t=>CardMembership.Definitions.ContainsKey(t.Name)).ToDictionary(t=>t.Name);
            foreach(var definition in CardMembership.Definitions.Values)
            {
                var card=ModelDb.GetById<CardModel>(ModelDb.GetId(nativeTypes[definition.ClassName])).ToMutable();card.Owner=player;
                if(!card.Description.Exists()){missingNativeDescriptions.Add(definition.ClassName);continue;}
                var description=BondDescription.Format(Presentation.TraitText(card),card);
                if(string.IsNullOrWhiteSpace(description)||description.Contains("energyPrefix")||definition.Trait.Effects is not {Length:1 or 2})throw new Exception("Invalid single-trait card text: "+definition.ClassName);
                var plain=Regex.Replace(Regex.Replace(description,@"\[img[^\]]*\].*?\[/img\]","●"),@"\[[^\]]+\]","").Trim();
                traitTextAudit.Add(new{card=definition.ClassName,name=card.Title,traits=definition.Trait.Effects.Length,text=plain,length=plain.Length});
                traitDescriptions++;
            }
            File.WriteAllText(output+"/single-trait-catalog.json",JsonSerializer.Serialize(new{source="Native Chinese titles and rendered description text; not complete play coverage for all native card effects.",cards=traitTextAudit},new JsonSerializerOptions{WriteIndented=true}));
            var image=viewport.GetTexture().GetImage();if(image.SavePng(output+"/cards-and-hud.png")!=Error.Ok)throw new Exception("Cannot save screenshot");
            var items=hud.GetNode("BondBar/Scroller/Items");
            var buttons=items.GetChildren().OfType<Button>().ToArray();
            var listCenter=(buttons.First().GetGlobalRect().Position.X+buttons.Last().GetGlobalRect().End.X)/2;
            if(Math.Abs(listCenter-viewport.Size.X/2)>.5f)throw new Exception("HUD icons are not centered: "+listCenter);
            if(hud.HasNode("TurnLimit"))throw new Exception("The old turn-limit label remains");
            var visibilityToggle=hud.GetNode<NFastModeTickbox>("TraitToggle/"+SettingsUi.TraitToggleName);
            File.WriteAllText(output+"/trait-toggle-layout.json",JsonSerializer.Serialize(Walk(visibilityToggle).OfType<Control>().Select(c=>new{node=c.Name.ToString(),position=new[]{c.Position.X,c.Position.Y},size=new[]{c.Size.X,c.Size.Y},scale=new[]{c.Scale.X,c.Scale.Y},pivot=new[]{c.PivotOffset.X,c.PivotOffset.Y}}),new JsonSerializerOptions{WriteIndented=true}));
            var nativeTickVisuals=visibilityToggle.GetNode<Control>("%TickboxVisuals");
            if((nativeTickVisuals.GetGlobalRect().GetCenter()-visibilityToggle.GetGlobalRect().GetCenter()).Length()>.2f)throw new Exception("Native checkbox visuals are not centered in their clickable area");
            if(Walk(visibilityToggle).Count(n=>n.Name.ToString()=="Ticked")!=1||visibilityToggle.Size.X>65)throw new Exception("Native checkbox retained duplicate indicators or its original settings-row minimum width");
            var speed=MegaCrit.Sts2.Core.Saves.SaveManager.Instance.PrefsSave.FastMode;
            visibilityToggle.ForceToggleTick();
            if(nativeTickVisuals.GetNode<Control>("Ticked").Visible||!nativeTickVisuals.GetNode<Control>("NotTicked").Visible)throw new Exception("Native checkbox visuals did not switch to unchecked");
            if(CovenantSettings.ShowCardTraits||nodes[0].Body.GetNode<Control>("CovenantTraitTip").Visible)throw new Exception("Native trait toggle did not hide persistent tips");
            if(cards[0].HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().Any(t=>t.Description.Contains("使已激活的")))throw new Exception("A normal hover still puts the trait beside keywords");
            nodes[0].SetMeta("covenant_hover",true);CardTraitTip.Update(nodes[0]);
            if(!nodes[0].Body.GetNode<Control>("CovenantTraitTip").Visible)throw new Exception("Hover must show the above-card trait with visibility disabled");
            nodes[0].SetMeta("covenant_hover",false);CardTraitTip.Update(nodes[0]);
            CardTraitTip.Inspecting=true;
            if(!cards[0].HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().Any(t=>t.Description.Contains("使已激活的")))throw new Exception("Inspection must include the side trait");
            CardTraitTip.Inspecting=false;
            visibilityToggle.ForceToggleTick();
            if(!CovenantSettings.ShowCardTraits||!nodes[0].Body.GetNode<Control>("CovenantTraitTip").Visible||MegaCrit.Sts2.Core.Saves.SaveManager.Instance.PrefsSave.FastMode!=speed)throw new Exception("Native trait checkbox changed an unrelated game preference");
            foreach(var item in buttons)
            {
                var badge=item.GetNode<Control>("Badge");var id=Enum.Parse<Bond>(item.Name.ToString());
                var glyph=badge.GetNode<Control>("Logo");
                if((glyph.Position+glyph.Size/2-new Vector2(36,30)).Length()>.01f)throw new Exception($"Visible logo is not at the circle center: {item.Name} position={glyph.Position} size={glyph.Size}");
                if(glyph is not TextureRect{Texture:AtlasTexture})throw new Exception("Visible artwork padding has not been removed");
                var definition=BondCatalog.Get(id);
                if(badge.GetMeta("segments").AsInt32()!=(badge.HasNode("Stage")?definition.Segments:definition.Basic))throw new Exception("Missing per-card ring segments");
                if(badge.GetNode<Label>("Caption").GetIndex()<=badge.GetNode<Control>("Logo").GetIndex())throw new Exception("HUD layers are behind logo");
            }
            var button=buttons.First();button.EmitSignal(Button.SignalName.Pressed);
            for(int i=0;i<5;i++)await __instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            if(!hud.GetNode<Control>("BondDetails").Visible)throw new Exception("Click did not open details");
            var descriptions=Walk(hud.GetNode("BondDetails")).OfType<MegaRichTextLabel>().ToArray();
            if(descriptions.Length!=2)throw new Exception("Details must contain exactly two native card description widgets");
            var text=string.Join("\n",descriptions.Select(x=>x.Text));
            if(!text.Contains("[gold]")||!text.Contains("[img]res://images/packed/sprite_fonts/ironclad_energy_icon.png[/img]"))throw new Exception("Native keyword or energy formatting missing: "+text);
            if(text.Contains("层数公式")||text.Contains("当前计入")||text.Contains("特性来源")||text.Contains("未激活"))throw new Exception("Redundant detail information remains");
            viewport.GetTexture().GetImage().SavePng(output+"/bond-details.png");
            var alphaViewport=new SubViewport{Size=new(72,84),TransparentBg=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always};
            __instance.GetTree().Root.AddChild(alphaViewport);alphaViewport.AddChild(BondBadge.Create(BondCatalog.Get(Bond.Strike),5,42));
            for(int i=0;i<3;i++)await __instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var badgeImage=alphaViewport.GetTexture().GetImage();
            // Sample the middle of the gap, away from the ring's antialiased inner edge and the foreground caption.
            var gapAlpha=new[]{badgeImage.GetPixel(58,30).A,badgeImage.GetPixel(13,30).A,badgeImage.GetPixel(20,14).A}.Max();
            badgeImage.SavePng(output+"/badge-alpha.png");
            if(gapAlpha>.01f)throw new Exception("The gap between the inner disc and outer ring is not transparent: "+gapAlpha);
            alphaViewport.QueueFree();
            var layoutChecks=new List<object>();
            foreach(var size in new[]{new Vector2I(1024,640),new Vector2I(1280,720),new Vector2I(1920,1080)})
            {
                viewport.Size=size;await __instance.ToSignal(__instance.GetTree().CreateTimer(.25),SceneTreeTimer.SignalName.Timeout);
                var current=items.GetChildren().OfType<Control>().ToArray();var centerX=(current.First().GetGlobalRect().Position.X+current.Last().GetGlobalRect().End.X)/2;
                if(Math.Abs(centerX-size.X/2)>.5f)throw new Exception("HUD is not centered after resize: "+size);
                layoutChecks.Add(new{width=size.X,height=size.Y,centerX});
            }
            viewport.Size=new(1280,720);
            foreach(var d in BondCatalog.All)if(terminal.Ledger.Get(d.Id)==0)terminal.Ledger.Add(d.Id,1);
            await __instance.ToSignal(__instance.GetTree().CreateTimer(.25),SceneTreeTimer.SignalName.Timeout);
            float maxPageHeight=0;
            foreach(var item in items.GetChildren().OfType<Button>())
            {
                item.EmitSignal(Button.SignalName.Pressed);
                if(!hud.GetNode<Control>("BondDetails").Visible)item.EmitSignal(Button.SignalName.Pressed);
                for(int i=0;i<3;i++)await __instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                var detail=hud.GetNode<Control>("BondDetails");maxPageHeight=Math.Max(maxPageHeight,detail.Size.Y);
                if(detail.GetGlobalRect().End.Y>720)throw new Exception("Long covenant description runs below viewport: "+item.Name+" "+detail.Size);
                if(Walk(detail).OfType<MegaRichTextLabel>().Count()!=2)throw new Exception("Incorrect native description count: "+item.Name);
            }
            var formattedDescriptions=0;
            foreach(var character in ModelDb.AllCharacters)
                foreach(var d in BondCatalog.All)
                    foreach(var raw in new[]{d.BasicText(new(170)),d.AdvancedText(new(170))})
                    {
                        var formatted=BondDescription.Format(raw,character.CardPool);
                        if(formatted.Contains("energyPrefix")||formatted.Contains("energyIcons"))throw new Exception("Unresolved native energy token");
                        foreach(Match match in Regex.Matches(formatted,@"\[img\](.*?)\[/img\]"))if(!ResourceLoader.Exists(match.Groups[1].Value))throw new Exception("Missing energy sprite: "+match.Groups[1].Value);
                        formattedDescriptions++;
                    }
            File.WriteAllText(output+"/ui-runtime.json",JsonSerializer.Serialize(new{game="v0.113.1",status="pass",cards=results,cardTraitsInNativeDescription=false,persistentNativeTraitTips=true,traitGap=CardTraitTip.Gap,traitDescriptions,missingNativeDescriptions,traitUpgradePreview=true,detailsClick=true,hudCenter=listCenter,ringSegments=true,foregroundCaptions=true,visibleGlyphCentered=true,gapAlpha,layoutChecks,maxPageHeight,formattedDescriptions,nativeDescriptionWidgets=descriptions.Length,nativeEnergyAndKeywords=true,userData=OS.GetUserDataDir()},new JsonSerializerOptions{WriteIndented=true}));
            if(File.Exists(output+"/ui-failure.txt"))File.Delete(output+"/ui-failure.txt");
            Console.WriteLine("COVENANT_UI_OK native-cards=4 named-round-badges=pass centered-hud=pass segmented-rings=pass foreground-captions=pass native-descriptions=pass energy-keywords=pass details-click=pass");
            if(System.Environment.GetEnvironmentVariable("COVENANT_UI_ONLY")=="1"){__instance.GetTree().Quit(0);return;}
            viewport.QueueFree();
            var settings=__instance.SubmenuStack.PushSubmenuType<NSettingsScreen>();
            for(int i=0;i<8;i++)await __instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var entry=settings.FindChild("CovenantSettingsOpen",true,false)??throw new Exception("Settings entry missing");
            entry.EmitSignal(NClickableControl.SignalName.Released,entry);
            for(int i=0;i<8;i++)await __instance.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            var page=__instance.SubmenuStack.Peek() as CovenantSettingsPage??throw new Exception("Settings entry did not open dedicated page");
            var checkbox=Walk(page).OfType<NFastModeTickbox>().Single(x=>x.Name.ToString()=="CovenantFullPool");
            var before=CovenantSettings.EnableAllCardPools;
            checkbox.ForceToggleTick();
            if(CovenantSettings.EnableAllCardPools==before)throw new Exception("Settings option failed to persist toggle");
            checkbox.ForceToggleTick();
            await __instance.ToSignal(__instance.GetTree().CreateTimer(1.5),SceneTreeTimer.SignalName.Timeout);
            __instance.GetViewport().GetTexture().GetImage().SavePng(output+"/settings-page.png");
            Console.WriteLine("COVENANT_SETTINGS_OK entry=pass dedicated-submenu=pass native-tickbox=pass preference-toggle=pass");
            var tree=__instance.GetTree();__instance.SubmenuStack.Pop();__instance.SubmenuStack.Pop();
            CovenantSettings.SetAllCardPools(true);
            if(System.Environment.GetEnvironmentVariable("COVENANT_AUTHORED_ONLY")=="1")await CombatProbe.Prepare(tree);else await CombatProbe.Run(tree,output);
            await AuthoredTraitProbe.Run(tree,output);await RewardProbe.Run(tree,output);await SaveProbe.Write(output);
            File.WriteAllText(output+"/runtime-latest.json",JsonSerializer.Serialize(new{status="pass",completedUtc=DateTimeOffset.UtcNow,modSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CovenantTerminal).Assembly.Location)))},new JsonSerializerOptions{WriteIndented=true}));
            if(File.Exists(output+"/probe-failure.txt"))File.Delete(output+"/probe-failure.txt");
            Console.WriteLine("COVENANT_RUNTIME_COMPLETE");tree.Quit(0);
        }
        catch(Exception ex){File.WriteAllText(output+"/probe-failure.txt",ex.ToString());Console.Error.WriteLine("COVENANT_RUNTIME_FAIL "+ex);var failedTree=(SceneTree)Engine.GetMainLoop();failedTree.Root.GetTexture().GetImage().SavePng(output+"/runtime-failure.png");failedTree.Quit(1);}
    }
    private static IEnumerable<Node> Walk(Node root){foreach(var child in root.GetChildren()){yield return child;foreach(var next in Walk(child))yield return next;}}
}
