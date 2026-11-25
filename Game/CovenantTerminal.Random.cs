using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Covenant.Game;

internal enum CovenantRandom
{
    Claw, CombatRoll, CombatCard, CombatTarget, Rewards, RewardReroll, Shop, PerfectedStrike, CurseReward, CurseShop, BadLuck, FateTarot,
    ReflowCard, ReflowTarget, SpectrumDiscount
}

public sealed partial class CovenantTerminal
{
    private static readonly JsonSerializerOptions RandomSaveOptions=new(){IncludeFields=true};
    private Dictionary<CovenantRandom,Rng> _randomStreams=new();

    [SavedProperty]
    public string CovenantCombatRandomState
    {
        get=>SaveRandomStreams(economy:false);
        set=>LoadRandomStreams(value,economy:false);
    }

    [SavedProperty]
    public string CovenantEconomyRandomState
    {
        get=>SaveRandomStreams(economy:true);
        set=>LoadRandomStreams(value,economy:true);
    }

    internal Rng RandomFor(CovenantRandom stream)
    {
        if(_randomStreams.TryGetValue(stream,out var rng))return rng;
        var run=Owner.RunState;
        rng=new Rng(run.Rng.Seed,$"Covenant:{run.GetPlayerSlotIndex(Owner)}:{stream}");
        _randomStreams.Add(stream,rng);
        return rng;
    }

    private static bool RollPercent(Rng rng,decimal chance)=>rng.NextDouble()*100d<(double)chance;

    private static bool IsEconomyStream(CovenantRandom stream)
        =>stream is CovenantRandom.Rewards or CovenantRandom.RewardReroll or CovenantRandom.Shop or CovenantRandom.CurseReward or CovenantRandom.CurseShop;

    private string SaveRandomStreams(bool economy)=>JsonSerializer.Serialize(_randomStreams
        .Where(pair=>IsEconomyStream(pair.Key)==economy).OrderBy(pair=>pair.Key)
        .Select(pair=>(pair.Key,State:pair.Value.ToSerializable())).Where(pair=>pair.State.counter!=0)
        .ToDictionary(pair=>pair.Key,pair=>pair.State),RandomSaveOptions);

    private void LoadRandomStreams(string? saved,bool economy)
    {
        foreach(var key in _randomStreams.Keys.Where(key=>IsEconomyStream(key)==economy).ToArray())_randomStreams.Remove(key);
        if(string.IsNullOrEmpty(saved))return;
        var states=JsonSerializer.Deserialize<Dictionary<CovenantRandom,SerializableRng>>(saved,RandomSaveOptions);
        if(states==null)return;
        foreach(var (key,state) in states)
            if(IsEconomyStream(key)==economy)_randomStreams.Add(key,new Rng(state));
    }

    internal static void InstallRandomState(Harmony harmony)
        =>harmony.Patch(AccessTools.Method(typeof(NetFullCombatState),nameof(NetFullCombatState.FromRun)),
            postfix:new HarmonyMethod(typeof(CovenantTerminal),nameof(ExcludeEconomyRandomFromCombatChecksum)));

    private static void ExcludeEconomyRandomFromCombatChecksum(NetFullCombatState __result)
    {
        // Match the native checksum, which omits reward and shop RNGs that can advance locally.
        var terminalId=ModelDb.GetId<CovenantTerminal>();
        foreach(var relic in __result.Players.SelectMany(player=>player.relics))
            if(relic.relic.Id==terminalId)
                relic.relic.Props?.strings?.RemoveAll(property=>property.name==nameof(CovenantEconomyRandomState));
    }

    internal Task PlayForCovenant(PlayerChoiceContext context,CardModel card,Creature? target=null,bool skipXCapture=false)
    {
        if(CombatManager.Instance.IsOverOrEnding||Owner.Creature.IsDead)return Task.CompletedTask;
        if(target==null&&Owner.Creature.CombatState is {} combat)
        {
            target=card.TargetType switch
            {
                TargetType.AnyEnemy=>RandomFor(CovenantRandom.CombatTarget).NextItem(combat.HittableEnemies),
                TargetType.AnyAlly=>RandomFor(CovenantRandom.CombatTarget).NextItem(combat.Allies.Where(ally=>ally.IsAlive&&ally.IsPlayer&&ally!=Owner.Creature)),
                TargetType.AnyPlayer=>RandomFor(CovenantRandom.CombatTarget).NextItem(combat.Players.Select(player=>player.Creature).Where(creature=>creature.IsAlive)),
                _=>null
            };
        }
        return CardCmd.AutoPlay(context,card,target,skipXCapture:skipXCapture);
    }
}
