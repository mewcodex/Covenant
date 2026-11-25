using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;

namespace Covenant.Game;

internal static class LibraryGridSpacing
{
    internal const int ExtraTop = 74;
    internal const float ExtraRow = 95;
    internal const float NativeRowPadding = 40;
    private const int DeckHeaderSpace = 32;
    private static readonly ConditionalWeakTable<NCardGrid,object> Initialized = new();
    private static readonly FieldInfo ScrollContainer = AccessTools.Field(typeof(NCardGrid), "_scrollContainer");
    private static readonly FieldInfo CardSize = AccessTools.Field(typeof(NCardGrid), "_cardSize");
    private static readonly FieldInfo SlidingIndex = AccessTools.Field(typeof(NCardGrid), "_slidingWindowCardIndex");
    private static readonly MethodInfo ContainedSize = AccessTools.Method(typeof(NCardGrid), "GetContainedCardsSize");
    private static readonly MethodInfo ScrollLimitTop = AccessTools.PropertyGetter(typeof(NCardGrid), "ScrollLimitTop");
    private static readonly MethodInfo ScrollLimitBottom = AccessTools.PropertyGetter(typeof(NCardGrid), "ScrollLimitBottom");
    private static readonly MethodInfo Padding = AccessTools.PropertyGetter(typeof(NCardGrid), "CardPadding");
    private static readonly MethodInfo VerticalPaddingMethod = AccessTools.Method(typeof(LibraryGridSpacing), nameof(VerticalPadding));
    private static readonly MethodInfo RowAllocationPaddingMethod = AccessTools.Method(typeof(LibraryGridSpacing), nameof(RowAllocationPadding));
    private static readonly MethodInfo YOffset = AccessTools.PropertyGetter(typeof(NCardGrid), nameof(NCardGrid.YOffset));
    private static readonly MethodInfo TopOffsetMethod = AccessTools.Method(typeof(LibraryGridSpacing), nameof(TopOffset));
    private static readonly MethodInfo Columns = AccessTools.PropertyGetter(typeof(NCardGrid), "Columns");
    private static readonly MethodInfo RowCount = AccessTools.Method(typeof(NCardGrid), "GetTotalRowCount");
    private static readonly MethodInfo AssignRow = AccessTools.Method(typeof(NCardGrid), "AssignCardsToRow");
    private static readonly MethodInfo PositionRows = AccessTools.Method(typeof(NCardGrid), "UpdateGridPositions");
    private static readonly MethodInfo NavigateRows = AccessTools.Method(typeof(NCardGrid), "UpdateGridNavigation");
    private static readonly MethodInfo RecycleAbove = AccessTools.Method(typeof(NCardGrid), "ReallocateAbove");
    private static readonly MethodInfo RecycleBelow = AccessTools.Method(typeof(NCardGrid), "ReallocateBelow");

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(NCardGrid), "InitGrid", Type.EmptyTypes),
            transpiler: new HarmonyMethod(typeof(LibraryGridSpacing), nameof(UseTopOffset)));
        harmony.Patch(AccessTools.Method(typeof(NCardGrid), "GetContainedCardsSize"),
            postfix: new HarmonyMethod(typeof(LibraryGridSpacing), nameof(ExtendScrollHeight)));
        // Retain enough native holders for compact spacing so changing the preference keeps the current selection.
        foreach (var name in new[] { "UpdateGridPositions", "ReallocateAbove", "ReallocateBelow" })
        {
            harmony.Patch(AccessTools.Method(typeof(NCardGrid), name),
                transpiler: new HarmonyMethod(typeof(LibraryGridSpacing), nameof(UseVerticalPadding)));
        }
        harmony.Patch(AccessTools.Method(typeof(NCardGrid), "AllocateCardHolders"),
            prefix: new HarmonyMethod(typeof(LibraryGridSpacing), nameof(UseNativeAllocation)));
        harmony.Patch(AccessTools.Method(typeof(NCardGrid), "CalculateRowsNeeded"),
            transpiler: new HarmonyMethod(typeof(LibraryGridSpacing), nameof(UseRowAllocationPadding)));
        harmony.Patch(AccessTools.Method(typeof(NCardGrid), "UpdateScrollPosition"),
            postfix: new HarmonyMethod(typeof(LibraryGridSpacing), nameof(SyncVisibleRows)));
    }

    private static void ExtendScrollHeight(NCardGrid __instance, ref Vector2 __result)
    {
        Initialized.GetOrCreateValue(__instance);
        if (!ReservesTraitSpace(__instance))
            return;
        if ((int)Columns.Invoke(__instance, null)! < 1)
            return;
        var rows = (int)RowCount.Invoke(__instance, null)!;
        __result.Y += Math.Max(0, rows - 1) * ExtraRow;
    }

    internal static bool ReservesTraitSpace(NCardGrid grid) => grid is NCardLibraryGrid || CovenantSettings.ShowCardTraits;
    internal static int TopOffset(NCardGrid grid) => grid.YOffset + (ReservesTraitSpace(grid)
        ? ExtraTop + (grid.GetParent() is NDeckViewScreen ? DeckHeaderSpace : 0)
        : 0);
    internal static float VerticalPadding(NCardGrid grid) => NativeRowPadding + (ReservesTraitSpace(grid) ? ExtraRow : 0);
    private static float RowAllocationPadding(NCardGrid grid) => grid is NCardLibraryGrid ? VerticalPadding(grid) : NativeRowPadding;

    private static bool UseNativeAllocation() => false;

    internal static void RefreshPreference()
    {
        foreach (var entry in Initialized.ToArray())
        {
            var grid = entry.Key;
            if (grid is NCardLibraryGrid || !GodotObject.IsInstanceValid(grid) || !grid.IsInsideTree())
                continue;
            var scroll = (Control)ScrollContainer.GetValue(grid)!;
            var cardSize = (Vector2)CardSize.GetValue(grid)!;
            var oldPitch = cardSize.Y + NativeRowPadding + (CovenantSettings.ShowCardTraits ? 0 : ExtraRow);
            var newPitch = cardSize.Y + VerticalPadding(grid);
            var newHeight = ((Vector2)ContainedSize.Invoke(grid, null)!).Y + 400 + TopOffset(grid);
            var oldTop = (float)ScrollLimitTop.Invoke(grid, null)!;
            var relativeScroll = Math.Min(0, scroll.Position.Y - oldTop) * newPitch / oldPitch;
            scroll.Size = new Vector2(scroll.Size.X, newHeight);
            var newTop = (float)ScrollLimitTop.Invoke(grid, null)!;
            var newBottom = (float)ScrollLimitBottom.Invoke(grid, null)!;
            grid.SetScrollPosition(Mathf.Clamp(newTop + relativeScroll, Math.Min(newBottom, newTop), newTop));
            PositionRows.Invoke(grid, [SlidingIndex.GetValue(grid)]);
        }
    }

    private static void SyncVisibleRows(NCardGrid __instance, Control ____scrollContainer,
        Vector2 ____cardSize, List<CardModel> ____cards, List<List<NGridCardHolder>> ____cardRows,
        ref int ____slidingWindowCardIndex)
    {
        if (____cardRows.Count == 0 || ____cards.Count == 0)
            return;
        var columns = ____cardRows[0].Count;
        if (columns == 0)
            return;
        var rowPitch = ____cardSize.Y + VerticalPadding(__instance);
        var contentTop = 80 + TopOffset(__instance);
        var topRow = Mathf.FloorToInt((-____scrollContainer.Position.Y - contentTop) / rowPitch) - 1;
        var lastWindow = Math.Max(0, (____cards.Count + columns - 1) / columns - ____cardRows.Count);
        var firstCard = Math.Clamp(topRow, 0, lastWindow) * columns;
        var shift = firstCard - ____slidingWindowCardIndex;
        if (shift == 0)
            return;
        if (shift == columns && ____cardRows.Count > 1)
        {
            RecycleBelow.Invoke(__instance, [____cardRows[0]]);
            return;
        }
        if (shift == -columns && ____cardRows.Count > 1)
        {
            RecycleAbove.Invoke(__instance, [____cardRows[^1]]);
            return;
        }
        // Large scrollbar jumps must land on a valid window even at the final partial row.
        ____slidingWindowCardIndex = firstCard;
        for (var i = 0; i < ____cardRows.Count; i++)
            AssignRow.Invoke(__instance, [____cardRows[i], firstCard + i * columns]);
        PositionRows.Invoke(__instance, [firstCard]);
        NavigateRows.Invoke(__instance, null);
    }

    private static IEnumerable<CodeInstruction> UseVerticalPadding(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var code = instructions.ToList();
        var calls = code.Where(instruction => instruction.Calls(Padding)).ToArray();
        // UpdateGridPositions computes X first, then Y. Both calculations need their original units
        // everywhere except the vertical occurrence.
        var horizontalFirst = __originalMethod.Name == "UpdateGridPositions";
        var expected = horizontalFirst ? 2 : 1;
        if (calls.Length != expected)
            throw new InvalidOperationException($"Unexpected card grid layout in {__originalMethod.Name}.");
        foreach (var call in calls.Skip(horizontalFirst ? 1 : 0))
        {
            call.opcode = OpCodes.Call;
            call.operand = VerticalPaddingMethod;
        }
        return horizontalFirst ? UseTopOffset(code) : code;
    }

    private static IEnumerable<CodeInstruction> UseTopOffset(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var call = code.Single(instruction => instruction.Calls(YOffset));
        call.opcode = OpCodes.Call;
        call.operand = TopOffsetMethod;
        return code;
    }

    private static IEnumerable<CodeInstruction> UseRowAllocationPadding(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(Padding))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = RowAllocationPaddingMethod;
            }
            yield return instruction;
        }
    }
}
