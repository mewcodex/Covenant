using System.Diagnostics;
using System.Text.Json;
using Covenant.Core;
using Covenant.Game;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using File=System.IO.File;

internal static class PerformanceProbe
{
    private const int DeckSeed=20261120, DeckSize=50, DummyHp=999999999, Turns=8;
    private static long _countCalls,_countTicks,_maxCountTicks;
    private static bool _sampleCounts;

    private sealed class AutomaticChoices:ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options,int minSelect,int maxSelect)
            =>Task.FromResult(options.Take(Math.Min(maxSelect,Math.Max(minSelect,1))));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options,IReadOnlyList<CardRewardAlternative> alternatives)=>new();
    }

    private static bool GenerateDummies(ExoskeletonsNormal __instance,ref IReadOnlyList<(MonsterModel,string?)> __result)
    {
        __result=__instance.Slots.Select(slot=>((MonsterModel)ModelDb.Monster<BigDummy>().ToMutable(),(string?)slot)).ToArray();
        return false;
    }

    private static void CountStarted(out long __state)=>__state=_sampleCounts?Stopwatch.GetTimestamp():0;
    private static void CountFinished(long __state)
    {
        if(__state==0)return;
        var elapsed=Stopwatch.GetTimestamp()-__state;
        _countCalls++;
        _countTicks+=elapsed;
        _maxCountTicks=Math.Max(_maxCountTicks,elapsed);
    }

    private sealed record FrameSample(string Phase,double Ms,double ProcessMs);
    private sealed record Step(string Phase,int Turn,string? Card,double Ms,int NativePlays,int Active,int Advanced,int Cards);

    private static object Summary(IEnumerable<double> samples)
    {
        var sorted=samples.Order().ToArray();
        if(sorted.Length==0)return new{count=0};
        double P(double p)=>Math.Round(sorted[(int)Math.Ceiling(sorted.Length*p)-1],2);
        return new{count=sorted.Length,mean=Math.Round(sorted.Average(),2),p50=P(.5),p95=P(.95),p99=P(.99),max=P(1),
            over33=sorted.Count(ms=>ms>33.34),over50=sorted.Count(ms=>ms>50)};
    }

    internal static async Task Run(SceneTree tree,string output)
    {
        var layers=System.Environment.GetEnvironmentVariable("COVENANT_PERF_BASELINE")=="1"?0:Ledger.MaxLayers;
        var harmony=new Harmony("Covenant.RuntimeCheck.Performance");
        HarmonyMethod Patch(string name)=>new(typeof(PerformanceProbe),name);
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ExoskeletonsNormal),"GenerateMonsters"),prefix:Patch(nameof(GenerateDummies)));
        harmony.Patch(AccessTools.Method(typeof(CardMembership),nameof(CardMembership.Counts)),prefix:Patch(nameof(CountStarted)),postfix:Patch(nameof(CountFinished)));
        SaveManager.Instance.SettingsSave.LimitFpsInBackground=false;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps=60;
        CovenantSettings.SetShowCardTraits(true);
        CovenantSettings.SetEnemyFloorScaling(false);
        var sources=BondCatalog.All.Where(bond=>bond.UsesLayers).ToArray();
        string[] deck=[];
        IDisposable? choices=null;
        var phase="opening";
        var frames=new List<FrameSample>();
        var steps=new List<Step>();
        var lastFrame=Stopwatch.GetTimestamp();
        void Frame()
        {
            var now=Stopwatch.GetTimestamp();
            frames.Add(new(phase,Stopwatch.GetElapsedTime(lastFrame,now).TotalMilliseconds,
                Performance.GetMonitor(Performance.Monitor.TimeProcess)*1000));
            lastFrame=now;
        }
        async Task Wait(Func<bool> done,string name)
        {
            var started=Stopwatch.GetTimestamp();
            while(!done())
            {
                if(Stopwatch.GetElapsedTime(started)>TimeSpan.FromSeconds(20))throw new TimeoutException(name);
                await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);
            }
        }
        try
        {
            long opening=0;
            await CombatProbe.Prepare(tree,run=>
            {
                var player=run.Players.Single();
                var terminal=CovenantTerminal.For(player)!;
                var pool=CovenantTerminal.SixPools.Append(ModelDb.CardPool<ColorlessCardPool>())
                    .SelectMany(pool=>pool.GetUnlockedCards(player.UnlockState,player.RunState.CardMultiplayerConstraint))
                    .Where(card=>card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
                    .Where(card=>card.IsAllowedInRun(run)).DistinctBy(card=>card.Id).OrderBy(card=>card.Id).ToList();
                var rng=new Rng(DeckSeed);
                var cards=new List<CardModel>(DeckSize);
                for(var i=0;i<DeckSize;i++)
                {
                    var index=rng.NextInt(pool.Count);
                    cards.Add(pool[index]);
                    pool.RemoveAt(index);
                }
                foreach(var card in player.Deck.Cards.ToArray())
                {
                    player.Deck.RemoveInternal(card,silent:true);
                    run.RemoveCard(card);
                }
                foreach(var card in cards)player.Deck.AddInternal(run.CreateCard(card,player),silent:true);
                deck=player.Deck.Cards.Select(card=>card.Id.Entry).ToArray();
                if(deck.Length!=DeckSize)throw new Exception($"Expected {DeckSize} deck cards, got {deck.Length}.");
                foreach(var bond in sources)terminal.Ledger.Add(bond.Id,layers);
                choices=RunManager.Instance.CardSelectorStack.UseSelector(player,new AutomaticChoices());
                _sampleCounts=true;
                lastFrame=opening=Stopwatch.GetTimestamp();
                tree.ProcessFrame+=Frame;
            },ModelDb.Encounter<ExoskeletonsNormal>().ToMutable());

            var player=RunManager.Instance.DebugOnlyGetState()!.Players.Single();
            var terminal=CovenantTerminal.For(player)!;
            var state=player.PlayerCombatState!;
            var enemies=player.Creature.CombatState!.HittableEnemies.ToArray();
            if(enemies.Length!=4||enemies.Any(enemy=>enemy.Monster is not BigDummy))throw new Exception($"Expected four dummies, got {enemies.Length}.");
            foreach(var enemy in enemies)
            {
                enemy.SetMaxHpInternal(DummyHp);
                enemy.SetCurrentHpInternal(DummyHp);
            }
            bool Ready()=>state.Phase==PlayerTurnPhase.Play&&!RunManager.Instance.ActionExecutor.IsRunning;
            await Wait(Ready,"initial turn");
            int Record(string kind,long start,CardModel? card=null,int previousPlays=0)
            {
                var elapsed=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var plays=CombatManager.Instance.History.CardPlaysFinished.Count()-previousPlays;
                var counts=terminal.Counts();
                var tiers=BondCatalog.All.Select(bond=>terminal.TierAtCount(bond.Id,counts.GetValueOrDefault(bond.Id))).ToArray();
                steps.Add(new(kind,state.TurnNumber,card?.Id.Entry,Math.Round(elapsed,2),plays,
                    tiers.Count(tier=>tier>0),tiers.Count(tier=>tier==2),state.AllCards.Count()));
                return plays;
            }
            Record("opening",opening);
            var initialCounts=terminal.Counts();
            var initialActive=BondCatalog.All.Where(bond=>terminal.TierAtCount(bond.Id,initialCounts.GetValueOrDefault(bond.Id))>0)
                .Select(bond=>new{bond.Name,Count=initialCounts.GetValueOrDefault(bond.Id),Tier=terminal.TierAtCount(bond.Id,initialCounts.GetValueOrDefault(bond.Id))}).ToArray();
            if(layers==Ledger.MaxLayers&&sources.Any(bond=>terminal.Ledger.Get(bond.Id)!=layers))throw new Exception("Layer preset changed before play.");
            Console.WriteLine($"COVENANT_PERF_READY deck={deck.Length} enemies={enemies.Length} layers={layers} active={initialActive.Length}");

            phase="idle";
            for(var i=0;i<90;i++)await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);
            var cpuStart=Process.GetCurrentProcess().TotalProcessorTime;
            var allocStart=GC.GetTotalAllocatedBytes();
            var gcStart=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
            var playStart=Stopwatch.GetTimestamp();
            var countStart=(_countCalls,_countTicks);

            for(var round=0;round<Turns&&CombatManager.Instance.IsInProgress;round++)
            {
                await Wait(Ready,"turn ready");
                phase="card";
                var played=0;
                for(var attempt=0;attempt<10&&Ready()&&CombatManager.Instance.IsInProgress;attempt++)
                {
                    CardModel? accepted=null;
                    var start=Stopwatch.GetTimestamp();
                    var previous=CombatManager.Instance.History.CardPlaysFinished.Count();
                    foreach(var card in state.Hand.Cards.ToArray())
                    {
                        var target=card.TargetType switch
                        {
                            TargetType.AnyEnemy=>enemies[(round+played)%enemies.Length],
                            TargetType.AnyPlayer=>player.Creature,
                            _=>null
                        };
                        if(card.TargetType==TargetType.AnyAlly||!card.CanPlay()||!card.TryManualPlay(target))continue;
                        accepted=card;
                        break;
                    }
                    if(accepted==null)break;
                    await Wait(()=>!CombatManager.Instance.IsInProgress||Ready()&&CombatManager.Instance.History.CardPlaysFinished.Count()>previous,
                        $"play {accepted.Id.Entry}");
                    Record("card",start,accepted,previous);
                    played++;
                }
                if(!CombatManager.Instance.IsInProgress||round==Turns-1)break;
                phase="turn";
                var end=Stopwatch.GetTimestamp();
                var prior=CombatManager.Instance.History.CardPlaysFinished.Count();
                await CombatProbe.EndTurn(tree);
                await Wait(Ready,"turn ready after ending");
                Record("turn",end,previousPlays:prior);
                Console.WriteLine($"COVENANT_PERF_TURN turn={state.TurnNumber} manual={played} cards={state.AllCards.Count()}");
            }

            phase="done";
            _sampleCounts=false;
            var wallMs=Stopwatch.GetElapsedTime(playStart).TotalMilliseconds;
            var cpuMs=(Process.GetCurrentProcess().TotalProcessorTime-cpuStart).TotalMilliseconds;
            var allocated=GC.GetTotalAllocatedBytes()-allocStart;
            var gc=Enumerable.Range(0,3).Select(index=>GC.CollectionCount(index)-gcStart[index]).ToArray();
            var suffix=System.Environment.GetEnvironmentVariable("COVENANT_PERF_OPTIMIZED")=="1"?"-optimized":"";
            var path=$"{output}/performance-dummies-{layers}{suffix}.json";
            var report=new
            {
                deckSeed=DeckSeed,deck,layers,layeredCovenants=sources.Length,initialActive,
                targets=enemies.Select(enemy=>new{enemy.MaxHp,enemy.CurrentHp,alive=enemy.IsAlive}).ToArray(),
                screen="1280x720",fpsCap=Engine.MaxFps,traits=CovenantSettings.ShowCardTraits,
                turn=state.TurnNumber,playerHp=player.Creature.CurrentHp,
                wallMs,cpuMs,allocated,gc,opening=steps.First(),steps,
                countCalls=_countCalls-countStart.Item1,countMs=1000d*(_countTicks-countStart.Item2)/Stopwatch.Frequency,
                maxCountMs=1000d*_maxCountTicks/Stopwatch.Frequency,
                frames=frames.GroupBy(sample=>sample.Phase).ToDictionary(group=>group.Key,group=>new
                    {frame=Summary(group.Select(sample=>sample.Ms)),process=Summary(group.Select(sample=>sample.ProcessMs))}),
                actionLatency=steps.Skip(1).GroupBy(step=>step.Phase).ToDictionary(group=>group.Key,group=>Summary(group.Select(step=>step.Ms)))
            };
            File.WriteAllText(path,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"COVENANT_PERF_COMPLETE {path} turn={state.TurnNumber} manual={steps.Count(step=>step.Phase=="card")}");
        }
        finally
        {
            _sampleCounts=false;
            tree.ProcessFrame-=Frame;
            choices?.Dispose();
        }
    }
}
