using Covenant.Core;
using Covenant.Game;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.addons.mega_text;

internal static class LibraryProbe
{
    internal static async Task RunCompendium(NMainMenu menu)
    {
        var tree=menu.GetTree();
        var stack=menu.SubmenuStack;
        if(System.Environment.GetEnvironmentVariable("COVENANT_EXPECT_AUTOANTHONY")=="1")
        {
            var refresh=AccessTools.Method(typeof(NCardLibraryGrid),nameof(NCardLibraryGrid.RefreshVisibility));
            Check(Harmony.GetPatchInfo(refresh)?.Postfixes.Any(patch=>
                patch.PatchMethod.DeclaringType?.FullName=="AutoAnthony.Patches.ChaosCardLibraryVisibilityPatch")==true,
                "Auto-Anthonyology's visibility patch is installed");
        }

        Press(menu.GetNode<NButton>("MainMenuTextButtons/CompendiumButton"));
        var compendium=stack.Peek() as NCompendiumSubmenu;
        Check(compendium?.IsVisibleInTree()==true,"the main menu opens the visible compendium");
        foreach(var path in new[]{"%RelicCollectionButton","%PotionLabButton","%BestiaryButton"})
        {
            var entry=compendium!.GetNode<NButton>(path);
            if(!entry.IsVisibleInTree())continue;
            Press(entry);
            var child=stack.Peek();
            Check(child!=compendium&&child?.IsVisibleInTree()==true,$"{path} opens its child page");
            Press(child!.GetNode<NBackButton>("BackButton"));
            Check(stack.Peek()==compendium&&compendium.IsVisibleInTree(),$"{path} returns to the compendium");
        }

        Press(compendium!.GetNode<NButton>("%CardLibraryButton"));
        var native=stack.Peek() as NCardLibrary;
        Check(native?.IsVisibleInTree()==true,"the native card library opens");
        var button=native!.FindChild("OpenCovenantLibrary",true,false) as Button;
        Check(button?.IsVisibleInTree()==true,"the native library exposes the covenant entry");
        button!.EmitSignal(Button.SignalName.Pressed);
        var covenant=stack.Peek() as NCardLibrary;
        Check(covenant!=native&&covenant?.IsVisibleInTree()==true,"opening the covenant library keeps the menu stack usable");
        var filters=(GridContainer)covenant!.FindChild("Bonds",true,false)!;
        var order=filters.GetChildren().OfType<Button>().Select(entry=>Enum.Parse<Bond>(entry.Name.ToString())).ToArray();
        Check(order.Length==BondCatalog.All.Count&&order.Distinct().Count()==order.Length&&
            order.TakeLast(4).SequenceEqual(new[]{Bond.Vakuu,Bond.Tony,Bond.Fate,Bond.Oracle})&&
            Array.IndexOf(order,Bond.Reflow)==Array.IndexOf(order,Bond.Heavy)+1&&
            Array.IndexOf(order,Bond.Rotation)==Array.IndexOf(order,Bond.Chain)+1&&
            Array.IndexOf(order,Bond.Spectrum)==Array.IndexOf(order,Bond.Solitude)+1,
            "the library groups related covenants and ends with Fate and Oracle");
        var grid=covenant!.GetNode<NCardLibraryGrid>("%CardGrid");
        for(var i=0;i<120&&!grid.VisibleCards.Any();i++)
            await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);
        Check(grid.VisibleCards.Any()&&grid.VisibleCards.All(card=>CardMembership.Of(card).Contains(Bond.Strike)),
            "the default covenant filter displays its cards");
        Press(covenant.GetNode<NBackButton>("BackButton"));
        Check(stack.Peek()==native&&native.IsVisibleInTree(),"back restores the native card library");
        Press(native.GetNode<NBackButton>("BackButton"));
        Check(stack.Peek()==compendium&&compendium.IsVisibleInTree(),"back restores the compendium");
        Press(compendium.GetNode<NBackButton>("BackButton"));
        Check(!stack.SubmenusOpen,"back restores the main menu");
        Console.WriteLine("COVENANT_COMPENDIUM_COMPLETE");

        static void Press(NButton button)=>button.EmitSignal(NClickableControl.SignalName.Released,button);
        static void Check(bool result,string description)
        {
            if(!result)throw new Exception(description);
            Console.WriteLine("COVENANT_COMPENDIUM_PASS "+description);
        }
    }

    internal static async Task Run(NMainMenu menu, string output)
    {
        var tree = menu.GetTree();
        async Task Draw() => await tree.ToSignal(tree.CreateTimer(.8), SceneTreeTimer.SignalName.Timeout);
        void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
            Console.WriteLine("COVENANT_LIBRARY_PASS " + message);
        }
        CovenantSettings.SetShowCardTraits(true);
        CovenantSettings.SetShowFormulas(false);
        var accelerant = ModelDb.Card<Accelerant>();
        var traitText = RevisionDescriptions.Text(accelerant, annotateScaling: true);
        string Red(string number) => BondDescription.ScalingColorTag + number + "[/color]";
        string[] EffectLines(Bond id, bool advanced)
        {
            var bond = BondCatalog.Get(id);
            var scale = new Scale(300, bond.LayerUnit);
            return BondDescription.Format((advanced ? bond.AdvancedText : bond.BasicText)(scale), accelerant, showFormulas: true).Split('\n');
        }
        Check(EffectLines(Bond.Ember, false) is [_, var ember] && ember.Contains("概率") &&
            EffectLines(Bond.Chain, true) is [var chain] && chain.Contains("[gold]活力[/gold]") && chain.Contains("[gold]拉伸[/gold]"),
            "Ember describes the return chance and Chain highlights both native power names");
        Check(EffectLines(Bond.Stars, false) is [var stars, var end] && stars.Contains("star_icon.png") && end.Contains("至少花费") &&
            EffectLines(Bond.Stars, true) is [var free, var progress] && free.Contains("不再消耗") && progress.Contains("每累计消耗"),
            "Stars separates its opening grant, refund, energy discount and cumulative reward");
        Check(EffectLines(Bond.Profit, true) is [var exchange, var victory] &&
            exchange.StartsWith("兑换卡牌奖励时") && victory.StartsWith("每场战斗胜利时"),
            "Profit describes exchange and battle victory as independent triggers");
        var zeroFormula = $"{Red("0")} (={Red("0")}/50)";
        Check(BondDescription.Format(traitText, accelerant).Contains($"增加{Red("0")}点"),
            "Accelerant's zero bonus stays red with formulas hidden");
        var player = Player.CreateForNewRun(ModelDb.Character<Ironclad>(), UnlockState.all, 7801);
        _ = RunState.CreateForTest([player], [], [], seed: "covenant-formula-ui");
        var starterCounts = CardMembership.Counts(player, deck: true);
        Check(starterCounts[Bond.Strike] == 1 && starterCounts[Bond.Guard] == 1,
            "all starter Strikes count as one name and all starter Defends count as one name");
        var viewport = new SubViewport { Size = new(1280,720) };
        tree.Root.AddChild(viewport);
        VerifyTraitMembership(player, viewport, await CovenantLibrary.PrepareMembership(tree), Check);
        var hud = Hud.AddTo(viewport, player);
        var traitToggle = hud.GetNode<NFastModeTickbox>("TraitToggle/" + SettingsUi.TraitToggleName);
        var formulaToggle = hud.GetNode<NFastModeTickbox>("TraitToggle/" + SettingsUi.FormulaToggleName);
        var originalSpeed = SaveManager.Instance.PrefsSave.FastMode;
        Check(!formulaToggle.IsTicked && formulaToggle.Position.X == traitToggle.Position.X &&
            formulaToggle.Position.Y > traitToggle.Position.Y, "native formula checkbox sits immediately below the trait checkbox");
        formulaToggle.ForceToggleTick();
        Check(CovenantSettings.ShowFormulas && BondDescription.Format(traitText, accelerant).Contains(zeroFormula) &&
            SaveManager.Instance.PrefsSave.FastMode == originalSpeed && CovenantSettings.ShowCardTraits,
            "formula toggle changes only formula visibility");
        var sample = ScalingText.Amount(new Scale(100, 2), "A");
        Check(BondDescription.Format(sample, accelerant) == $"{Red("50")} (={Red("100")}/2)",
            "formula renders only the result and current layer count in red");
        CovenantTerminal.For(player)!.Ledger.Add(Bond.Poison, 175);
        var liveAccelerant = accelerant.ToMutable();
        liveAccelerant.Owner = player;
        var liveText = RevisionDescriptions.Text(liveAccelerant, annotateScaling: true);
        Check(BondDescription.Format(liveText, liveAccelerant).Contains($"{Red("3")} (={Red("175")}/50)"),
            "card formula uses the actual layer count without truncation symbols");
        Check(EffectLines(Bond.Guard, true).Single().Contains($"(=3+{Red("300")}/25)"),"Guard reduces the layer fraction");
        formulaToggle.ForceToggleTick();
        Check(!CovenantSettings.ShowFormulas, "formula checkbox can be turned off again");
        viewport.QueueFree();
        var originalLanguage=LocManager.Instance.Language;
        LocManager.Instance.SetLanguage("eng");
        var nativeSettings = menu.SubmenuStack.PushSubmenuType<NSettingsScreen>();
        var generalRows = nativeSettings.GetNode<NSettingsPanel>("%GeneralSettings").Content;
        var settingsEntry = generalRows.GetNode<NOpenModdingScreenButton>("CovenantSettingsEntry/CovenantSettingsOpen");
        Check(settingsEntry.IsVisibleInTree(), "the first native settings page shows the Covenant entry");
        settingsEntry.EmitSignal(NClickableControl.SignalName.Released,settingsEntry);
        var settingsPage = menu.SubmenuStack.Peek() as CovenantSettingsPage ?? throw new Exception("The native settings entry did not open the Covenant options.");
        var settingsRows = settingsPage.GetNode<ScrollContainer>("CovenantSettingsScroll").GetChild<VBoxContainer>(0);
        var poolRow = settingsRows.GetNode<Control>(SettingsUi.ToggleName);
        var scalingRow = settingsRows.GetNode<Control>(SettingsUi.FloorScalingToggleName);
        var handRow = settingsRows.GetNode<Control>(SettingsUi.MerchantToggleName);
        var bundledFont=FontManager.GetSubstituteFont("zhs",FontType.Regular)!;
        Check(bundledFont.ResourcePath.StartsWith("res://themes/fonts/zhs/")&&bundledFont.HasChar('盟')&&
            settingsRows.GetChild<Label>(0).GetThemeFont("font")==bundledFont,
            "Chinese settings headings use a glyph-bearing game asset even with English game language");
        var activeTips=(Dictionary<Control,NHoverTipSet>)AccessTools.Field(typeof(NHoverTipSet),"_activeHoverTips").GetValue(null)!;
        foreach(var (row,title,expected) in new[]
        {
            (poolRow,SettingsUi.FullPoolTitle,"六个角色的卡池"),
            (scalingRow,CovenantFloorScaling.SettingTitle,"最大生命、格挡和部分状态效果"),
            (handRow,SettingsUi.MerchantTitle,"切换后立即生效")
        })
        {
            row.EmitSignal(Control.SignalName.MouseEntered);
            var tip=activeTips[row];
            var heading=tip.FindChildren("Title","",true,false).OfType<MegaLabel>().Single();
            var tipDescription=tip.FindChildren("Description","",true,false).OfType<MegaRichTextLabel>().Single();
            Check(heading.Text==title&&tipDescription.Text.Contains(expected)&&
                heading.GetThemeFont("font")==bundledFont&&tipDescription.GetThemeFont("normal_font")==bundledFont,
                $"{row.Name} shows its own native tooltip using the bundled Chinese font");
            row.EmitSignal(Control.SignalName.MouseExited);
            Check(!activeTips.ContainsKey(row),$"{row.Name} dismisses its tooltip on mouse exit");
        }
        Check(settingsRows.GetChild<Label>(poolRow.GetIndex() + 1).Text == "难度" &&
            settingsRows.GetChild<Label>(scalingRow.GetIndex() + 1).Text == "显示" &&
            scalingRow.FindChildren("*", "Label", true, false).Count == poolRow.FindChildren("*", "Label", true, false).Count,
            "the difficulty section contains one native checkbox row without an extra note");
        var scalingToggle = scalingRow.FindChildren("*", "", true, false).OfType<NFastModeTickbox>().Single();
        scalingToggle.ForceToggleTick();
        Check(scalingToggle.IsTicked && CovenantSettings.EnemyFloorScaling && SaveManager.Instance.PrefsSave.FastMode == originalSpeed,
            "difficulty checkbox updates the Covenant preference and keeps native acceleration unchanged");
        scalingToggle.ForceToggleTick();
        menu.SubmenuStack.Pop();
        menu.SubmenuStack.Pop();
        LocManager.Instance.SetLanguage("rus");
        var russianUiDescription=BondDescription.Create("CovenantFontProbe");
        menu.AddChild(russianUiDescription);
        Check(russianUiDescription.GetThemeFont("normal_font")==bundledFont,
            "native rich text keeps the Covenant font after the game's Russian font substitution would have run");
        russianUiDescription.QueueFree();
        LocManager.Instance.SetLanguage(originalLanguage);
        CovenantSettings.SetShowCardTraits(false);
        var native = menu.SubmenuStack.PushSubmenuType<NCardLibrary>();
        await Draw();
        var grid = native.GetNode<NCardLibraryGrid>("%CardGrid");
        var entry = (Button)native.FindChild("OpenCovenantLibrary", true, false)!;
        Check(entry.IsVisibleInTree() && grid.VisibleCards.Any(), "normal entry and native cards are visible");
        var originalColumns = Measure(grid);
        tree.Root.GetTexture().GetImage().SavePng(output + "/0.5/card-library.png");
        var hoverTips = NGame.Instance!.HoverTipsContainer!;
        var discovered = (HashSet<ModelId>)SaveManager.Instance.Progress.DiscoveredCards;
        var newlyDiscovered = discovered.Add(accelerant.Id);
        grid.RefreshVisibility();
        ((HashSet<ModelId>)AccessTools.Field(typeof(NCardLibraryGrid), "_unlockedCards").GetValue(grid)!).Add(accelerant.Id);
        grid.FilterCards(card => card.Id == accelerant.Id);
        await Draw();
        var accelerantHolder = grid.GetCardHolder(grid.VisibleCards.Single())!;
        void VerifyAccelerant(NCardHolder holder, string page)
        {
            var box = holder.CardNode!.Body.GetNode<Control>("CovenantTraitTip");
            var description=box.GetNode<MegaRichTextLabel>("%Description");
            Check(box.IsVisibleInTree()&&description.Text.Contains(zeroFormula), page + " always shows the trait and formula above the card with both toggles off");
        }
        VerifyAccelerant(accelerantHolder, "normal card library");
        var accelerantNode=accelerantHolder.CardNode!;
        var originalBadges=accelerantNode.Body.GetNode<Control>("CovenantIcons");
        var follower=accelerantNode.GetNode<Godot.Timer>("CovenantTraitFollower");
        accelerantNode.Body.GetNode<Control>("CovenantTraitTip").Hide();
        Check(follower.IsStopped(),"a hidden trait panel stops its layout timer");
        accelerantNode.UpdateVisuals(accelerantNode.DisplayingPile,CardPreviewMode.Normal);
        Check(!follower.IsStopped()&&accelerantNode.Body.GetNode<Control>("CovenantIcons")==originalBadges,
            "an unchanged card reuses its badges while a visible trait resumes layout");
        grid.EmitSignal(NCardGrid.SignalName.HolderPressed, accelerantHolder);
        await Draw();
        bool InspectShowsFormula() => hoverTips.FindChildren("Description", "", true, false).OfType<MegaRichTextLabel>()
            .Any(label => label.Text.Contains(zeroFormula));
        Check(InspectShowsFormula(), "normal card library inspection always shows the formula");
        var inspect = NGame.Instance.GetInspectCardScreen();
        inspect.Close();
        await Draw();
        if (newlyDiscovered) discovered.Remove(accelerant.Id);
        entry.EmitSignal(Button.SignalName.Pressed);
        await Draw();
        var library = (NCardLibrary)menu.SubmenuStack.Peek()!;
        var covenants = (GridContainer)library.FindChild("Bonds", true, false)!;
        grid = library.GetNode<NCardLibraryGrid>("%CardGrid");
        Check(library != native && covenants.GetChildCount() == BondCatalog.All.Count, "native submenu opens a separate page with all bonds");
        Check(Measure(grid) == originalColumns, "both grids have native horizontal spacing, with the same column count and extra vertical space");
        Check(grid.VisibleCards.Any() && grid.VisibleCards.All(card => CardMembership.Of(card).Contains(Bond.Strike)), "default Strike list uses actual membership");
        var button = covenants.GetNode<Button>("Poison");
        Choose(button);
        button.EmitSignal(Control.SignalName.MouseEntered);
        await Draw();
        Check(grid.VisibleCards.Any() && grid.VisibleCards.All(card => CardMembership.Of(card).Contains(Bond.Poison)), "Poison filters across pools");
        var poisonBond = BondCatalog.Get(Bond.Poison);
        var description = hoverTips.FindChildren("Description", "", true, false).OfType<MegaRichTextLabel>()
            .Single(label => label.Text.Contains($"I (0/{poisonBond.Basic})"));
        Check(description.Text.Contains($"II (0/{poisonBond.Advanced})") && !description.Text.Contains(" · "), "hover displays both revised activation ratios");
        var highlighted = System.Text.RegularExpressions.Regex.Matches(description.Text,
            System.Text.RegularExpressions.Regex.Escape(BondDescription.ScalingColorTag) + "(.*?)\\[/color\\]")
            .Select(match => match.Groups[1].Value);
        Check(highlighted.SequenceEqual(new[] { "1", "0", "0", "0", "25", "0" }) &&
            description.Text.Contains($"{Red("1")} (=1+{Red("0")}/100)"),
            "covenant hover always shows formulas with the result and current layers highlighted");
        var topCard = grid.GetTopRowOfCardNodes()!.First().CardNode!;
        var traitTip = topCard.Body.GetNode<Control>("CovenantTraitTip");
        Check(traitTip.GetNode<TextureRect>("%Icon") is {Visible:true,Texture:not null} icon &&
            icon.GetGlobalRect().Position.X >= traitTip.GetNode<Control>("%Title").GetGlobalRect().End.X,
            "card trait icon is visibly on the title row after the covenant name");
        tree.Root.GetTexture().GetImage().SavePng(output + "/0.5/covenant-library-hover.png");
        button.EmitSignal(Control.SignalName.MouseExited);
        grid.FilterCards(card => card.Id == accelerant.Id);
        await Draw();
        accelerantHolder = grid.GetCardHolder(grid.VisibleCards.Single())!;
        VerifyAccelerant(accelerantHolder, "covenant library");
        grid.EmitSignal(NCardGrid.SignalName.HolderPressed, accelerantHolder);
        await Draw();
        Check(InspectShowsFormula(), "covenant library inspection always shows the formula");
        inspect.Close();
        await Draw();
        foreach (var bond in new[] { Bond.Harmony, Bond.Tony, Bond.Mastery })
        {
            Choose(covenants.GetNode<Button>(bond.ToString()));
            await Draw();
            var cards = grid.VisibleCards.ToArray();
            Check(cards.Length > 0 && cards.All(card => CardMembership.ReferenceBonds(card).Contains(bond)), bond + " lists its eligible cards");
            if (bond == Bond.Harmony)
                Check(cards.Length == 1 && cards[0].GetType().Name == "Entropy", "Harmony is limited to Entropy");
            if (bond == Bond.Tony)
                Check(cards.Length == 12 && !cards.Any(c => c is Tempest or Entrench), "Tony only lists the 12 remaining entries");
            if (bond == Bond.Mastery)
                Check(cards.Length > 500 && cards.Where(c => !c.IsUpgraded).Select(c => c.GetType()).ToHashSet().SetEquals(
                    [typeof(Aggression), typeof(Apotheosis), typeof(Armaments), typeof(DrainPower), typeof(MadScience)]) &&
                    grid.GetTopRowOfCardNodes()!.Where(holder => holder.CardModel!.IsUpgraded).All(holder =>
                        !holder.CardNode!.Body.GetNode<Control>("CovenantIcons").HasNode("Mastery")),
                    "Mastery lists cards that upgrade existing cards and upgraded cards, and hides the badge on upgraded cards");
        }
        var scroll = grid.GetNode<Control>("ScrollContainer");
        var dragTarget = AccessTools.Field(typeof(NCardGrid), "_targetDrag");
        dragTarget.SetValue(grid, grid.Size.Y - scroll.Size.Y);
        var last = grid.VisibleCards.Last();
        for (var i = 0; i < 15 && grid.GetCardHolder(last)?.IsVisibleInTree() != true; i++)
            await Draw();
        Check(grid.GetCardHolder(last)?.IsVisibleInTree() == true,
            $"the expanded grid scrolls to its final card (scroll {scroll.Position.Y}/{grid.Size.Y - scroll.Size.Y})");
        dragTarget.SetValue(grid, 0f);
        for (var i = 0; i < 15 && Math.Abs(scroll.Position.Y) > 1; i++)
            await Draw();
        var holder = grid.GetTopRowOfCardNodes()!.First();
        Check(holder.CardNode!.Visibility == ModelVisibility.Visible, "all covenant member cards show their details");
        grid.EmitSignal(NCardGrid.SignalName.HolderPressed, holder);
        await Draw();
        Check(NGame.Instance.GetInspectCardScreen().IsVisibleInTree(), "a click opens native card inspection");
        var expectedIcon = Presentation.Icon(TraitDisplay.MainBond(CardMembership.Definition(holder.CardModel!)!.Trait));
        Check(hoverTips.FindChildren("Icon", "TextureRect", true, false).OfType<TextureRect>().Any(icon => icon.Texture == expectedIcon),
            "inspection uses the native title icon for the card trait");
        NGame.Instance.GetInspectCardScreen().Close();
        menu.SubmenuStack.Pop();
        Check(menu.SubmenuStack.Peek() == native && native.IsVisibleInTree(), "back returns to original card library");
        Console.WriteLine("COVENANT_LIBRARY_COMPLETE");
    }

    private static void VerifyTraitMembership(Player player, SubViewport viewport, Dictionary<Bond,CardModel[]> members, Action<bool,string> check)
    {
        var groups = new Dictionary<Bond,Type[]>
        {
            [Bond.Blood] = [typeof(CrimsonMantle), typeof(Inferno), typeof(Spite)],
            [Bond.Heavy] = [typeof(BorrowedTime), typeof(Cruelty), typeof(DanseMacabre), typeof(Friendship)],
            [Bond.Arrows] = [typeof(Honing), typeof(LockedIn)],
            [Bond.Guard] = [typeof(HiddenRoots), typeof(Juggernaut)],
            [Bond.Creation] = [typeof(Haunt)],
            [Bond.Profit] = [typeof(Pillage)],
            [Bond.Foresight] = [typeof(TheHunt)],
            [Bond.Ritual] = [typeof(ImitationLearning), typeof(SignalBoost)],
            [Bond.Mastery] = [typeof(Aggression), typeof(Apotheosis), typeof(Armaments), typeof(DrainPower)],
        };
        var counts = CardMembership.Counts(player, deck: true);
        foreach (var (bond, types) in groups)
        foreach (var type in types)
        {
            var card = ModelDb.GetById<CardModel>(ModelDb.GetId(type));
            var upgraded = (CardModel)card.MutableClone();
            upgraded.UpgradeInternal();
            upgraded.FinalizeUpgradeInternal();
            check(CardMembership.Of(card).Contains(bond) && CardMembership.Of(upgraded).Count(value => value == bond) == 1 &&
                CardMembership.CountsAfterAcquisition(player, card).GetValueOrDefault(bond) == counts.GetValueOrDefault(bond) + 1 &&
                members[bond].Contains(card), $"{type.Name} counts once for {bond} at either upgrade level and appears in the library");
        }

        var drain = ModelDb.Card<DrainPower>();
        var preview = NCard.Create(drain)!;
        viewport.AddChild(preview);
        preview.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
        check(preview.Body.HasNode("CovenantIcons/Mastery"), "unupgraded Drain Power shows its Mastery emblem");
        var upgradedDrain = (CardModel)drain.MutableClone();
        upgradedDrain.UpgradeInternal();
        upgradedDrain.FinalizeUpgradeInternal();
        preview.Model = upgradedDrain;
        preview.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
        check(!preview.Body.HasNode("CovenantIcons/Mastery"), "upgraded Drain Power follows the hidden Mastery badge convention");
        preview.QueueFree();
    }

    private static int Measure(NCardLibraryGrid grid)
    {
        var holders = grid.GetTopRowOfCardNodes()!.ToArray();
        var size = (Vector2)AccessTools.Field(typeof(NCardGrid), "_cardSize").GetValue(grid)!;
        var rows = (List<List<NGridCardHolder>>)AccessTools.Field(typeof(NCardGrid), "_cardRows").GetValue(grid)!;
        if (holders.Length < 2 || rows.Count < 2 ||
            Math.Abs(holders[1].Position.X - holders[0].Position.X - size.X - 40) > .1 ||
            Math.Abs(rows[1][0].Position.Y - holders[0].Position.Y - size.Y - 40 - LibraryGridSpacing.ExtraRow) > .1 ||
            Math.Abs(holders[0].Position.Y - size.Y / 2 - 80 - grid.YOffset - LibraryGridSpacing.ExtraTop) > .1)
            throw new Exception($"Incorrect native catalog spacing. Size={size}, first={holders[0].Position}, nextRow={rows[1][0].Position}, offset={grid.YOffset}.");
        return holders.Length;
    }

    private static void Choose(Button button)
    {
        button.ButtonPressed = true;
        button.EmitSignal(Button.SignalName.Pressed);
    }
}
