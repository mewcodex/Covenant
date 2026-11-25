using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;

namespace Covenant.Game;

public sealed class CovenantFloorScaling : ModifierModel
{
    private const double FloorGrowth = 0.01;
    internal static string SettingTitle => "敌人每层强化 " + FloorGrowth.ToString("0.#%", CultureInfo.InvariantCulture);
    private const decimal MaxHealth = 999_999_999m;
    private static readonly Func<RunManager, RunState?> ActiveRun = AccessTools.MethodDelegate<Func<RunManager, RunState?>>(
        AccessTools.PropertyGetter(typeof(RunManager), "State"));
    private static readonly ConditionalWeakTable<StartRunLobby, CovenantFloorScaling> EnabledLobbies = new();
    private static readonly Type[] MultiplayerPowerSignature =
        [typeof(ICombatState), typeof(Creature), typeof(decimal), typeof(Creature), typeof(CardModel)];

    [ThreadStatic]
    private static IRunState? _spawningRun;

    private static decimal FloorMultiplier(IRunState run) => (decimal)Math.Pow(1 + FloorGrowth, run.TotalFloor);

    internal static string CurrentBonusText(IRunState run) => "+" +
        (FloorMultiplier(run) - 1m).ToString("0.##%", CultureInfo.InvariantCulture);

    internal static bool IsEnabled(IRunState run) =>
        CovenantTerminal.For(run.Players.FirstOrDefault())?.EnemyFloorScaling == true;

    private static decimal ActiveMultiplier(IRunState? run) =>
        run != null && IsEnabled(run) ? FloorMultiplier(run) : 1m;

    internal static decimal CombatMultiplier(CovenantTerminal terminal) =>
        terminal.EnemyFloorScaling && ReferenceEquals(terminal, CovenantTerminal.For(terminal.Owner.RunState.Players.FirstOrDefault()))
            ? FloorMultiplier(terminal.Owner.RunState) : 1m;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(RunState), nameof(RunState.CreateForNewRun)), prefix: Patch(nameof(ConfigureSoloRun)));
        harmony.Patch(AccessTools.Method(typeof(StartRunLobby), "BeginRunForAllPlayers"), prefix: Patch(nameof(ConfigureLobbyRun)));
        harmony.Patch(AccessTools.Method(typeof(StartRunLobby), "BeginRunLocally"), prefix: Patch(nameof(ReadLobbyPreference)));
        harmony.Patch(AccessTools.Method(typeof(StartRunLobby), nameof(StartRunLobby.GeneratePlayers)), postfix: Patch(nameof(ConfigureLobbyPlayers)));
        harmony.Patch(AccessTools.Method(typeof(CombatState), nameof(CombatState.CreateCreature)), prefix: Patch(nameof(BeginSpawn)), finalizer: Patch(nameof(EndSpawn)));
        harmony.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.ScaleMonsterHpForMultiplayer)), postfix: Patch(nameof(ScaleSpawn)));
        harmony.Patch(AccessTools.Method(typeof(Creature), nameof(Creature.ScaleHpForMultiplayer)), postfix: Patch(nameof(ScaleHealth)));
        harmony.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.ModifyPowerAmountReceived)), postfix: Patch(nameof(ScaleSoloPower)));
        foreach (var type in typeof(PowerModel).Assembly.GetTypes().Where(type => typeof(PowerModel).IsAssignableFrom(type)))
        {
            var method = AccessTools.DeclaredMethod(type, nameof(PowerModel.GetScaledAmountForMultiplayer), MultiplayerPowerSignature);
            if (method != null) harmony.Patch(method, postfix: Patch(nameof(ScaleMultiplayerPower)));
        }
    }

    private static HarmonyMethod Patch(string name) => new(typeof(CovenantFloorScaling), name);

    private static void ConfigureSoloRun(IReadOnlyList<Player> players)
    {
        if (players.Count == 1 && CovenantTerminal.For(players[0]) is { } terminal)
            terminal.EnemyFloorScaling = CovenantSettings.EnemyFloorScaling;
    }

    private static void ConfigureLobbyRun(StartRunLobby __instance, ref List<ModifierModel> modifiers)
    {
        if (__instance.NetService.Type == NetGameType.Host && CovenantSettings.EnemyFloorScaling)
            modifiers = [.. modifiers, ModelDb.Modifier<CovenantFloorScaling>().ToMutable()];
    }

    // Transport the host's choice in the start message without changing the run's actual modifier list.
    private static void ReadLobbyPreference(StartRunLobby __instance, ref List<ModifierModel> modifiers)
    {
        if (modifiers.OfType<CovenantFloorScaling>().FirstOrDefault() is not { } preference) return;
        EnabledLobbies.AddOrUpdate(__instance, preference);
        modifiers = modifiers.Where(modifier => modifier is not CovenantFloorScaling).ToList();
    }

    private static void ConfigureLobbyPlayers(StartRunLobby __instance, List<Player> __result)
    {
        if (!EnabledLobbies.TryGetValue(__instance, out _)) return;
        foreach (var player in __result)
            if (CovenantTerminal.For(player) is { } terminal) terminal.EnemyFloorScaling = true;
    }

    private static void BeginSpawn(CombatState __instance, out IRunState? __state)
    {
        __state = _spawningRun;
        _spawningRun = __instance.RunState;
    }

    private static void EndSpawn(IRunState? __state) => _spawningRun = __state;

    private static void ScaleSpawn(Creature __instance, EncounterModel? encounter, int playerCount, int actIndex)
    {
        if (!__instance.IsEnemy || __instance.MonsterMaxHpBeforeModification is not { } baseHp) return;
        var multiplier = ActiveMultiplier(_spawningRun);
        if (multiplier == 1m) return;
        var nativeHp = Creature.ScaleHpForMultiplayer(baseHp, encounter, playerCount, actIndex);
        __instance.SetMaxHpInternal(Math.Min(MaxHealth, nativeHp * multiplier));
        __instance.SetCurrentHpInternal(__instance.MaxHp);
    }

    // Phase changes and previews use the same native helper outside the initial spawn.
    private static void ScaleHealth(ref decimal __result)
    {
        if (_spawningRun != null) return;
        var multiplier = ActiveMultiplier(ActiveRun(RunManager.Instance));
        if (multiplier != 1m) __result = Math.Min(MaxHealth, __result * multiplier);
    }

    private static void ScaleSoloPower(ICombatState combatState, PowerModel canonicalPower, Creature target, Creature? giver, ref decimal __result)
    {
        if (__result <= 0m || combatState.Players.Count > 1 || !target.IsEnemy || !canonicalPower.ShouldScaleInMultiplayer) return;
        var multiplier = ActiveMultiplier(combatState.RunState);
        if (multiplier != 1m && PowerCmd.FindExistingInstanceForStacking(canonicalPower, target, giver) == null)
            __result *= multiplier;
    }

    private static void ScaleMultiplayerPower(PowerModel __instance, MethodBase __originalMethod, ICombatState combatState, Creature target, ref decimal __result)
    {
        if (__result <= 0m || !target.IsEnemy || !__instance.ShouldScaleInMultiplayer) return;
        var multiplier = ActiveMultiplier(combatState.RunState);
        // Overrides can call the base method. Scale only the outer result, after any native adjustment.
        if (multiplier != 1m && AccessTools.Method(__instance.GetType(), nameof(PowerModel.GetScaledAmountForMultiplayer), MultiplayerPowerSignature) == __originalMethod)
            __result *= multiplier;
    }
}
