using System.Runtime.CompilerServices;
using Stopwatch = System.Diagnostics.Stopwatch;
using Covenant.Core;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;

namespace Covenant.Game;

internal static class CovenantLibrary
{
    private sealed class PageState
    {
        internal Bond Selected = Bond.Strike;
        internal readonly Dictionary<Bond, Button> Buttons = new();
        internal Button? Hovered;
        internal bool Loading;
        internal int Request;
    }

    private static readonly ConditionalWeakTable<NCardLibrary, PageState> Pages = new();
    private static readonly ConditionalWeakTable<NSubmenuStack, NCardLibrary> StackPages = new();
    private static Task<Dictionary<Bond, CardModel[]>>? _membership;
    private static readonly System.Reflection.FieldInfo CardSource = AccessTools.Field(typeof(NCardLibraryGrid), "_allCards");
    private static readonly System.Reflection.MethodInfo ConnectGrid = AccessTools.Method(typeof(NCardGrid), "ConnectSignals");
    private static readonly System.Reflection.FieldInfo Filter = AccessTools.Field(typeof(NCardLibrary), "_filter");
    private static readonly System.Reflection.FieldInfo TypeFilters = AccessTools.Field(typeof(NCardLibrary), "_cardTypeFilters");
    private static readonly System.Reflection.FieldInfo RarityFilters = AccessTools.Field(typeof(NCardLibrary), "_rarityFilters");
    private static readonly System.Reflection.FieldInfo CostFilters = AccessTools.Field(typeof(NCardLibrary), "_costFilters");
    private static readonly System.Reflection.MethodInfo NativeUpdate = AccessTools.Method(typeof(NCardLibrary), "UpdateFilter");
    private static readonly System.Reflection.MethodInfo Display = AccessTools.Method(typeof(NCardLibrary), "DisplayCards");
    private static readonly System.Reflection.MethodInfo DisplayDelayed = AccessTools.Method(typeof(NCardLibrary), "DisplayCardsAfterShortDelay");
    private static readonly System.Reflection.MethodInfo SetCapstoneType =
        AccessTools.PropertySetter(typeof(NCapstoneSubmenuStack), nameof(NCapstoneSubmenuStack.Type));
    internal static bool OpeningInspection { get; private set; }
    internal static Player? LocalRunPlayer => RunManager.Instance.DebugOnlyGetState() is {} run ? LocalContext.GetMe(run) : null;

    internal static void Install(Harmony harmony)
    {
        LibraryGridSpacing.Install(harmony);
        harmony.Patch(AccessTools.Method(typeof(PreloadManager), nameof(PreloadManager.LoadCommonAndMainMenuAssets)),
            prefix: new HarmonyMethod(typeof(CovenantLibrary), nameof(IncludeCommonAssets)));
        harmony.Patch(AccessTools.Method(typeof(NCardLibrary), nameof(NCardLibrary._Ready)),
            postfix: new HarmonyMethod(typeof(CovenantLibrary), nameof(Setup)));
        harmony.Patch(AccessTools.Method(typeof(NMainMenu), nameof(NMainMenu._Ready)),
            postfix: new HarmonyMethod(typeof(CovenantLibrary), nameof(WarmMembership)));
        harmony.Patch(AccessTools.Method(typeof(NCardLibrary), nameof(NCardLibrary.OnSubmenuOpened)),
            prefix: new HarmonyMethod(typeof(CovenantLibrary), nameof(BeforeOpen)),
            postfix: new HarmonyMethod(typeof(CovenantLibrary), nameof(AfterOpen)));
        harmony.Patch(AccessTools.Method(typeof(NCardLibraryGrid), nameof(NCardLibraryGrid._Ready)),
            prefix: new HarmonyMethod(typeof(CovenantLibrary), nameof(SetupGrid)));
        harmony.Patch(NativeUpdate, prefix: new HarmonyMethod(typeof(CovenantLibrary), nameof(UpdateFilter)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(NCardLibrary), "InitialFocusedControl"),
            postfix: new HarmonyMethod(typeof(CovenantLibrary), nameof(InitialFocus)));
        harmony.Patch(AccessTools.Method(typeof(NCardLibraryGrid), "GetCardVisibility"),
            prefix: new HarmonyMethod(typeof(CovenantLibrary), nameof(ShowEntries)));
        harmony.Patch(AccessTools.Method(typeof(NCardLibrary), "ShowCardDetail"),
            prefix: new HarmonyMethod(typeof(CovenantLibrary), nameof(Inspect)),
            finalizer: new HarmonyMethod(typeof(CovenantLibrary), nameof(EndInspect)));
    }

    private static void IncludeCommonAssets()
    {
        if (AssetSets.CommonAssets is not HashSet<string> assets) return;
        assets.UnionWith(NCardLibrary.AssetPaths);
        assets.Add("res://scenes/screens/card_library/card_library_stats.tscn");
        assets.Add("res://images/packed/sprite_fonts/star_icon.png");
    }

    private static void WarmMembership(NMainMenu __instance) => TaskHelper.RunSafely(PrepareMembership(__instance.GetTree()));

    internal static Task<Dictionary<Bond, CardModel[]>> PrepareMembership(SceneTree tree)
        => _membership ??= BuildMembership(tree);

    private static async Task<Dictionary<Bond, CardModel[]>> BuildMembership(SceneTree tree)
    {
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        var members = BondCatalog.All.ToDictionary(bond => bond.Id, _ => new List<CardModel>());
        var cards = typeof(CardModel).Assembly.GetTypes()
            .Where(type => CardMembership.Definitions.ContainsKey(type.Name))
            .Select(type => ModelDb.GetById<CardModel>(ModelDb.GetId(type)))
            .OrderBy(card => card.Pool.Id.Entry, StringComparer.Ordinal)
            .ThenBy(card => card.Id.Entry, StringComparer.Ordinal);
        var frameBudget = Stopwatch.StartNew();
        foreach (var canonical in cards)
        {
            if (frameBudget.ElapsedMilliseconds >= 2)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                frameBudget.Restart();
            }
            if(!CardMembership.IsMarker(canonical))members[Bond.Solitude].Add(canonical);
            if(CardMembership.SpectrumColor(canonical)!=0)members[Bond.Spectrum].Add(canonical);
            var baseBonds = CardMembership.ReferenceBonds(canonical);
            foreach (var bond in baseBonds)
                members[bond].Add(canonical);
            if (!canonical.IsUpgradable)
                continue;

            // Only the first upgrade can change membership. Dingy Dagger's later upgrades change damage and art.
            var upgraded = (CardModel)canonical.MutableClone();
            upgraded.UpgradeInternal();
            upgraded.FinalizeUpgradeInternal();
            foreach (var bond in CardMembership.ReferenceBonds(upgraded).Except(baseBonds))
                members[bond].Add(upgraded);
        }
        return members.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray());
    }

    private static bool IsCovenantGrid(NCardLibraryGrid grid)
        => grid.GetParent() is NCardLibrary library && Pages.TryGetValue(library, out _);

    private static bool SetupGrid(NCardLibraryGrid __instance)
    {
        if (!IsCovenantGrid(__instance)) return true;
        // This grid receives its own card source. The native full-card sort is unnecessary here.
        ConnectGrid.Invoke(__instance, null);
        // Initialize both discovery sets through the native method before its mod postfixes run.
        __instance.RefreshVisibility();
        return false;
    }

    private static (int Section, int Position) LibraryOrder(BondDefinition bond) => bond.Id switch
    {
        Bond.Fate => (4, 0),
        Bond.Oracle => (4, 1),
        _ => bond.Group switch
        {
            "关键词" => (1, bond.Id switch
            {
                Bond.Strike => 0, Bond.Guard => 1, Bond.Breach => 2,
                Bond.Chain => 3, Bond.Rotation => 4,
                Bond.Heavy => 5, Bond.Reflow => 6, Bond.Channel => 7,
                Bond.Retain => 8, Bond.Initiative => 9, Bond.Ritual => 10,
                _ => int.MaxValue
            }),
            "特殊" => (2, bond.Id switch
            {
                Bond.Mastery => 0, Bond.Curse => 1, Bond.Status => 2,
                Bond.Ancient => 3, Bond.Encounter => 4,
                Bond.Profit => 5, Bond.Foresight => 6,
                Bond.Aid => 7, Bond.Harmony => 8,
                Bond.Solitude => 9, Bond.Spectrum => 10,
                _ => int.MaxValue
            }),
            "副盟约" => (3, (int)bond.Id),
            _ => (0, (int)bond.Id)
        }
    };

    private static void Setup(NCardLibrary __instance)
    {
        var poolRow = __instance.GetNode<Control>("%IroncladPool").GetParent<Control>();
        var sidebar = poolRow.GetParent<Control>();
        if (!Pages.TryGetValue(__instance, out var state))
        {
            var entry = new Button { Name = "OpenCovenantLibrary", Text = "盟约图鉴",
                CustomMinimumSize = new(0, 42), MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            entry.UseBundledChinese();
            sidebar.AddChild(entry);
            sidebar.MoveChild(entry, poolRow.GetIndex());
            entry.Pressed += () =>
            {
                if (__instance.GetParent() is NSubmenuStack stack)
                    Open(stack);
            };
            return;
        }

        poolRow.Hide();
        var filters = new ScrollContainer { Name = "CovenantFilters", CustomMinimumSize = new(0, 154),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        sidebar.AddChild(filters);
        sidebar.MoveChild(filters, poolRow.GetIndex());
        var buttons = new GridContainer { Columns = 5, Name = "Bonds", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        buttons.AddThemeConstantOverride("h_separation", 2);
        buttons.AddThemeConstantOverride("v_separation", 2);
        filters.AddChild(buttons);

        var group = new ButtonGroup();
        foreach (var bond in BondCatalog.All.OrderBy(LibraryOrder))
        {
            var button = MakeBondButton(bond, group);
            buttons.AddChild(button);
            state.Buttons.Add(bond.Id, button);
            button.ButtonPressed = state.Selected == bond.Id;
            button.Pressed += () =>
            {
                if (state.Selected == bond.Id)
                    return;
                state.Selected = bond.Id;
                if (!state.Loading)
                    QueueRefresh(__instance, state, opening: false);
            };
            button.MouseEntered += () => ShowEffect(state, button, bond);
            button.FocusEntered += () => ShowEffect(state, button, bond);
            button.MouseExited += () => HideEffect(state, button);
            button.FocusExited += () => HideEffect(state, button);
        }
        var search = __instance.GetNode<NSearchBar>("%SearchBar");
        search.TextArea.FocusNeighborBottom = state.Buttons[Bond.Fury].GetPath();
        __instance.VisibilityChanged += () =>
        {
            if (!__instance.IsVisibleInTree() && state.Hovered is {} hovered)
                HideEffect(state, hovered);
        };
    }

    private static Button MakeBondButton(BondDefinition bond, ButtonGroup group)
    {
        var button = new Button
        {
            Name = bond.Id.ToString(), ToggleMode = true, ButtonGroup = group,
            CustomMinimumSize = new(46, 48), MouseDefaultCursorShape = Control.CursorShape.PointingHand
        };
        var selected = new StyleBoxFlat
        {
            BgColor = new Color(bond.Id switch { Bond.Fate => "4a3a21", Bond.Oracle => "492d28", _ => "214347" }),
            BorderColor = BondBadge.HasSpecialPalette(bond.Id) ? BondBadge.Accent(bond.Id, true) : new Color("81c5bd"),
            BorderWidthBottom = 2, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3
        };
        button.AddThemeStyleboxOverride("pressed", selected);
        button.AddThemeStyleboxOverride("hover_pressed", selected);
        var icon = new TextureRect
        {
            Texture = Presentation.Icon(bond.Id), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Position = new(12, 3), Size = new(22, 22), MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = BondBadge.HasSpecialPalette(bond.Id) ? BondBadge.Accent(bond.Id, false) : Colors.White
        };
        button.AddChild(icon);
        var name = new Label
        {
            Text = bond.Name, Position = new(0, 25), Size = new(46, 20),
            HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        name.AddThemeFontSizeOverride("font_size", 14);
        if (BondBadge.HasSpecialPalette(bond.Id))
            name.AddThemeColorOverride("font_color", BondBadge.Accent(bond.Id, false));
        name.UseBundledChinese();
        button.AddChild(name);
        return button;
    }

    internal static void OpenFromHud(NRun run, Bond selected)
    {
        var container = run.GlobalUi.CapstoneContainer;
        if (container.InUse)
            return;
        var submenu = run.GlobalUi.SubmenuStack;
        // Use the native compendium context with the covenant library as the root page.
        SetCapstoneType.Invoke(submenu, [CapstoneSubmenuType.Compendium]);
        Open(submenu.Stack, selected);
        container.Open(submenu);
    }

    internal static void WarmRunPage(NRun run)
    {
        var stack = run.GlobalUi.SubmenuStack.Stack;
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(stack) && stack.IsInsideTree())
                GetOrCreatePage(stack);
        }).CallDeferred();
    }

    private static NCardLibrary GetOrCreatePage(NSubmenuStack stack)
    {
        if (StackPages.TryGetValue(stack, out var page)) return page;

        page = NCardLibrary.Create()!;
        page.Name = "CovenantLibrary";
        page.Hide();
        Pages.Add(page, new PageState());
        StackPages.Add(stack, page);
        stack.AddChild(page);
        return page;
    }

    private static void Open(NSubmenuStack stack, Bond? selected = null)
    {
        var page = GetOrCreatePage(stack);
        var state = Pages.GetOrCreateValue(page);
        if (selected is {} id)
        {
            state.Selected = id;
            state.Buttons[id].ButtonPressed = true;
        }
        stack.Push(page);
        if (selected is {} target)
        {
            var button = state.Buttons[target];
            var filters = (ScrollContainer)button.GetParent().GetParent();
            Callable.From(() => filters.EnsureControlVisible(button)).CallDeferred();
        }
    }

    private static void BeforeOpen(NCardLibrary __instance)
    {
        if (Pages.TryGetValue(__instance, out var state))
            state.Loading = true;
    }

    private static void AfterOpen(NCardLibrary __instance)
    {
        if (Pages.TryGetValue(__instance, out var state))
            QueueRefresh(__instance, state, opening: true);
    }

    private static void QueueRefresh(NCardLibrary page, PageState state, bool opening)
    {
        state.Loading = true;
        TaskHelper.RunSafely(UpdateMembers(page, state, ++state.Request, opening));
    }

    private static async Task UpdateMembers(NCardLibrary library, PageState state, int request, bool opening)
    {
        var tree = library.GetTree();
        if (opening)
        {
            // OnSubmenuOpened runs before the native stack shows this page. Present the shell first.
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        var indexed = state.Selected is Bond.Fate or Bond.Oracle ? null : await PrepareMembership(tree);
        if (request != state.Request) return;
        state.Loading = false;
        if (!GodotObject.IsInstanceValid(library) || !library.IsVisibleInTree()) return;

        var cards = (List<CardModel>)CardSource.GetValue(library.GetNode<NCardLibraryGrid>("%CardGrid"))!;
        cards.Clear();
        cards.AddRange(state.Selected switch
        {
            Bond.Fate => CovenantTerminal.For(LocalRunPlayer)?.FateCards ?? [],
            Bond.Oracle => CovenantTerminal.For(LocalRunPlayer)?.OracleCards ?? [],
            _ => indexed![state.Selected]
        });
        NativeUpdate.Invoke(library, [false]);
    }

    private static void ShowEffect(PageState state, Button button, BondDefinition bond)
    {
        if (state.Hovered == button)
            return;
        if (state.Hovered is {} prior)
            HideEffect(state, prior);
        var scale = new Scale(0, bond.LayerUnit);
        var intro = BondCatalog.Intro(bond.Id);
        var text = $"[gold]I (0/{bond.Basic})[/gold]\n" + BondDescription.Format(bond.BasicText(scale), ModelDb.CardPool<ColorlessCardPool>(), showFormulas: true);
        if (intro.Length > 0)
            text = BondDescription.Format(intro, ModelDb.CardPool<ColorlessCardPool>(), showFormulas: true) + "\n\n" + text;
        if (bond.Advanced > 0)
            text += $"\n\n[gold]II (0/{bond.Advanced})[/gold]\n" + BondDescription.Format(bond.AdvancedText(scale), ModelDb.CardPool<ColorlessCardPool>(), showFormulas: true);
        foreach (var (layers, _) in BondCatalog.Milestones(bond.Id, scale))
        {
            var milestone = new Scale(layers, bond.LayerUnit);
            var effect = BondCatalog.Milestones(bond.Id, milestone).Single(entry => entry.Layers == layers).Text;
            text += $"\n\n[gold]{layers}层[/gold]\n" + BondDescription.Format(effect, ModelDb.CardPool<ColorlessCardPool>(), showFormulas: true);
        }
        var tip = new HoverTip(Presentation.Literal(bond.Name), text);
        if (NHoverTipSet.CreateAndShow(button, tip, HoverTipAlignment.Right) is {} shown)
        {
            var filters = button.GetParent().GetParent<Control>().GetGlobalRect();
            var x = filters.End.X + 18;
            var target = new Rect2(new Vector2(x, button.GetGlobalRect().Position.Y), Vector2.Zero);
            shown.SetAlignment(target, HoverTipAlignment.Right);
            state.Hovered = button;
        }
    }

    private static void HideEffect(PageState state, Button button)
    {
        if (state.Hovered != button)
            return;
        NHoverTipSet.Remove(button);
        state.Hovered = null;
    }

    private static void InitialFocus(NCardLibrary __instance, ref Control __result)
    {
        if (Pages.TryGetValue(__instance, out var state))
            __result = state.Buttons[state.Selected];
    }

    private static bool ShowEntries(NCardLibraryGrid __instance, ref ModelVisibility __result)
    {
        if (!IsCovenantGrid(__instance)) return true;
        __result = ModelVisibility.Visible;
        return false;
    }

    private static bool Inspect(NCardLibrary __instance, NCardHolder holder, out bool __state)
    {
        __state = OpeningInspection;
        OpeningInspection = true;
        if (!Pages.TryGetValue(__instance, out var state))
            return true;
        if (state.Loading) return false;
        if (holder.CardModel is not {} selected)
            return false;
        var grid = __instance.GetNode<NCardLibraryGrid>("%CardGrid");
        var cards = grid.VisibleCards.ToList();
        var index = cards.FindIndex(card => card.Id == selected.Id);
        if (index >= 0)
            NGame.Instance?.GetInspectCardScreen().Open(cards, index, grid.IsShowingUpgrades);
        return false;
    }

    private static void EndInspect(bool __state) => OpeningInspection = __state;

    private static bool UpdateFilter(NCardLibrary __instance, bool isTextInput)
    {
        if (!Pages.TryGetValue(__instance, out var state))
            return true;
        if (state.Loading) return false;
        var types = Selected<NCardTypeTickbox>(TypeFilters, box => box.IsTicked);
        var rarities = Selected<NCardRarityTickbox>(RarityFilters, box => box.IsTicked);
        var costs = Selected<NCardCostTickbox>(CostFilters, box => box.IsTicked);
        var query = NSearchBar.Normalize(__instance.GetNode<NSearchBar>("%SearchBar").Text);
        var showMultiplayer = __instance.GetNode<NLibraryStatTickbox>("%MultiplayerCards").IsTicked;
        var showUpgrades = __instance.GetNode<NCardLibraryGrid>("%CardGrid").IsShowingUpgrades;
        Filter.SetValue(__instance, (Func<CardModel, bool>)(card =>
            Matches(types, card) && Matches(rarities, card) && Matches(costs, card) &&
            (showMultiplayer || card.MultiplayerConstraint != CardMultiplayerConstraint.MultiplayerOnly) &&
            MatchesText(card, query, showUpgrades)));
        TaskHelper.RunSafely((Task)(isTextInput ? DisplayDelayed : Display).Invoke(__instance, null)!);
        return false;

        Func<CardModel, bool>[] Selected<T>(System.Reflection.FieldInfo field, Func<T, bool> ticked) where T : notnull =>
            ((Dictionary<T, Func<CardModel, bool>>)field.GetValue(__instance)!).Where(entry => ticked(entry.Key))
                .Select(entry => entry.Value).ToArray();
    }

    private static bool Matches(Func<CardModel, bool>[] filters, CardModel card) =>
        filters.Length == 0 || filters.Any(filter => filter(card));

    private static bool MatchesText(CardModel card, string query, bool upgrades)
    {
        if (query.Length == 0)
            return true;
        if (upgrades && card.IsUpgradable)
        {
            card = (CardModel)card.MutableClone();
            card.UpgradeInternal();
        }
        var text = NSearchBar.RemoveHtmlTags(card.GetDescriptionForPile(PileType.None).StripBbCode());
        return NSearchBar.Normalize(card.Title + " " + text).Contains(query, StringComparison.Ordinal) ||
               card.Rarity.ToString().Equals(query, StringComparison.OrdinalIgnoreCase);
    }
}
