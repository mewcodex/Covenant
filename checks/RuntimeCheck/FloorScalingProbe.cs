using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.addons.mega_text;

internal static class FloorScalingProbe
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("COVENANT_SCALING_PASS " + message);
    }

    private static Player CreatePlayer(ulong id) => Player.CreateForNewRun(ModelDb.Character<Ironclad>(), UnlockState.all, id);

    private static RunState CreateRun(params Player[] players) => RunState.CreateForNewRun(players,
        ActModel.GetDefaultList().Select(act => act.ToMutable()).ToArray(), [], GameMode.Standard, false, 0, "COVENANT_SCALING");

    private static void AddFloors(RunState run, int count)
    {
        for (var i = 0; i < count; i++) run.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, null);
    }

    internal static async Task Run(SceneTree tree)
    {
        Check(CovenantFloorScaling.SettingTitle == "敌人每层强化 1%", "the settings label shows the 1 percent rate");
        CovenantSettings.SetEnemyFloorScaling(false);
        var unscaled = CreatePlayer(401);
        var unscaledRun = CreateRun(unscaled);
        AddFloors(unscaledRun, 50);
        var unscaledCombat = new CombatState(runState: unscaledRun);
        unscaledCombat.AddPlayer(unscaled);
        var unscaledEnemy = unscaledCombat.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, null);
        Check(CovenantTerminal.For(unscaled)?.EnemyFloorScaling == false && unscaledEnemy.MaxHp == unscaledEnemy.MonsterMaxHpBeforeModification,
            "the disabled option leaves a floor-50 enemy at native health");
        Check(CovenantTerminal.For(unscaled) is { ShowCounter: false, DisplayAmount: 0 } inactiveTerminal&&
            inactiveTerminal.DynamicDescription.GetFormattedText()=="同一盟约内，同名牌只计一次。消耗牌堆也计入，状态牌和衍生牌除外。",
            "the disabled terminal shows the counting rule and hides the scaling line and floor counter");

        CovenantSettings.SetEnemyFloorScaling(true);
        await CombatProbe.Prepare(tree, run =>
        {
            AddFloors(run, 25);
            run.CurrentActIndex = 1;
            AddFloors(run, 50 - run.TotalFloor - 1);
        });
        var run = RunManager.Instance.DebugOnlyGetState()!;
        var player = run.Players.Single();
        var terminal = CovenantTerminal.For(player)!;
        var terminalHolder = NRun.Instance!.GlobalUi.RelicInventory.RelicNodes.Single(node => node.Relic.Model == terminal);
        var counter = terminalHolder.GetNode<MegaLabel>("%AmountLabel");
        var combat = player.Creature.CombatState!;
        var enemy = combat.HittableEnemies.Single();
        var multiplier = (decimal)Math.Pow(1.01, 50);
        Check(run.TotalFloor == 50 && terminal.EnemyFloorScaling && run.Modifiers.Count == 0 && !run.IsCustomGame,
            "new runs keep the native modifier list and count visited floors across acts");
        Check(counter.IsVisibleInTree() && counter.Text == "50" && terminal.DisplayAmount == 50,
            "the native relic label shows the exact exponent used by floor scaling");
        var bonus=(multiplier-1m).ToString("0.##%",System.Globalization.CultureInfo.InvariantCulture);
        Check(terminal.DynamicDescription.GetFormattedText().EndsWith($"敌人当前强化幅度为 +{bonus}。",StringComparison.Ordinal),
            "the relic tooltip shows the compounded bonus used at floor 50");
        Check(enemy.MaxHp == (int)(enemy.MonsterMaxHpBeforeModification!.Value * multiplier) && enemy.CurrentHp == enemy.MaxHp,
            "a real enemy enters combat with its maximum and current health scaled once");
        Check(Creature.ScaleHpForMultiplayer(100, combat.Encounter, 1, 1) == 100 * multiplier,
            "the native health calculation used by hatching and revival also scales in solo runs");

        var preview = new SingleAttackIntent(20).GetTotalDamage([player.Creature], enemy);
        var hp = player.Creature.CurrentHp;
        var context = new ThrowingPlayerChoiceContext();
        await CreatureCmd.Damage(context, player.Creature, 20, ValueProp.Move, enemy);
        Check(preview == 20 && hp - player.Creature.CurrentHp == 20,
            "the attack intent and actual damage stay at their native values with floor scaling enabled");
        await PowerCmd.Apply<IntangiblePower>(context, player.Creature, 1, player.Creature, null);
        Check(new SingleAttackIntent(20).GetTotalDamage([player.Creature], enemy) == 1,
            "native damage caps still apply with floor scaling enabled");
        hp = enemy.CurrentHp;
        await CreatureCmd.Damage(context, enemy, 20, ValueProp.Move, player.Creature);
        Check(hp - enemy.CurrentHp == 20, "player damage is unchanged");

        var startingBlock = enemy.Block;
        await CreatureCmd.GainBlock(enemy, 20, ValueProp.Move, null, fast: true);
        Check(enemy.Block - startingBlock == (int)(20 * multiplier), "enemy move Block scales");
        startingBlock = enemy.Block;
        await CreatureCmd.GainBlock(enemy, 20, ValueProp.Unpowered, null, fast: true);
        Check(enemy.Block - startingBlock == (int)(20 * multiplier), "direct unpowered enemy Block, such as Guardbot's shield, also scales");

        await PowerCmd.Apply<RegenPower>(context, enemy, 10, enemy, null);
        await PowerCmd.Apply<StrengthPower>(context, enemy, 10, enemy, null);
        var regen = enemy.GetPower<RegenPower>()!;
        Check(regen.Amount == (int)(10 * multiplier) && enemy.GetPower<StrengthPower>()!.Amount == 10,
            "enemy Regen scales when first applied and an unmarked native status keeps its original amount");
        await PowerCmd.Apply<RegenPower>(context, enemy, 5, enemy, null);
        await PowerCmd.Decrement(regen);
        Check(regen.Amount == (int)(10 * multiplier) + 4, "later status stacks and ticks retain native increments");
        await PowerCmd.Apply<PlatingPower>(context, enemy, 10, enemy, null);
        var plating = enemy.GetPower<PlatingPower>()!;
        startingBlock = enemy.Block;
        await plating.BeforeSideTurnEndEarly(context, CombatSide.Enemy, [enemy]);
        Check(plating.Amount == (int)(10 * multiplier) && enemy.Block - startingBlock == plating.Amount,
            "Plating's Block uses the already scaled status amount exactly once");

        CovenantSettings.SetEnemyFloorScaling(false);
        var restored = RunState.FromSerializable(RunManager.Instance.ToSave(null));
        Check(CovenantTerminal.For(restored.Players.Single()) is { EnemyFloorScaling: true, ShowCounter: true, DisplayAmount: 50 } &&
            restored.TotalFloor == 50,
            "resuming retains the saved choice and floor even after changing the menu preference");

        var marker = ModelDb.Modifier<CovenantFloorScaling>().ToMutable();
        var writer = new PacketWriter();
        writer.Write(marker.ToSerializable());
        var reader = new PacketReader();
        reader.Reset(writer.Buffer);
        Check(ModifierModel.FromSerializable(reader.Read<SerializableModifier>()) is CovenantFloorScaling,
            "the native lobby packet round-trips the host choice");

        var first = CreatePlayer(501);
        var second = CreatePlayer(502);
        CovenantTerminal.For(first)!.EnemyFloorScaling = true;
        CovenantTerminal.For(second)!.EnemyFloorScaling = true;
        var coopRun = CreateRun(first, second);
        AddFloors(coopRun, 50);
        var coop = new CombatState(runState: coopRun);
        coop.AddPlayer(first);
        coop.AddPlayer(second);
        var coopEnemy = coop.CreateCreature(ModelDb.Monster<Axebot>().ToMutable(), CombatSide.Enemy, null);
        var coopExpected = (int)(coopEnemy.MonsterMaxHpBeforeModification!.Value * 2.2m * multiplier);
        Check(coopEnemy.MaxHp == coopExpected,
            $"a multiplayer spawn applies both health multipliers once ({coopEnemy.MaxHp}/{coopExpected})");
        Check(ModelDb.Power<ArtifactPower>().GetScaledAmountForMultiplayer(coop, coopEnemy, 10, coopEnemy, null) == 11 * multiplier,
            "Artifact scales after its native additive multiplayer bonus");
        Check(CovenantTerminal.For(first)!.ModifyBlockMultiplicative(coopEnemy, 20, ValueProp.Move, null, null) == multiplier &&
            CovenantTerminal.For(second)!.ModifyBlockMultiplicative(coopEnemy, 20, ValueProp.Move, null, null) == 1m,
            "two players' terminals apply the enemy Block multiplier only once");

        var egg = (ToughEgg)ModelDb.Monster<ToughEgg>().ToMutable();
        egg.IsHatched = true;
        var hatchling = await CreatureCmd.Add(egg, combat);
        Check(hatchling.MaxHp >= (int)(egg.HatchlingMinHp * multiplier) && hatchling.MaxHp <= (int)(egg.HatchlingMaxHp * multiplier) &&
            hatchling.CurrentHp == hatchling.MaxHp, "the actual Tough Egg hatch keeps floor scaling when replacing its health");
        await RunManager.Instance.EnterRoomDebug(RoomType.RestSite, showTransition: false).WaitAsync(TimeSpan.FromSeconds(25));
        Check(run.TotalFloor == 51 && terminal.DisplayAmount == 51 && counter.IsVisibleInTree() && counter.Text == "51",
            "the same native relic label advances to floor 51 upon entering a noncombat room");
        Console.WriteLine("COVENANT_SCALING_COMPLETE");
    }
}
