using Covenant.Core;
using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Random;

internal static class GenerationPoolsProbe
{
    internal static async Task Run(SceneTree tree,Player player)
    {
        var terminal=CovenantTerminal.For(player)!;
        var combat=player.Creature.CombatState!;
        var state=player.PlayerCombatState!;
        var enemy=combat.HittableEnemies.Single();
        var context=new ThrowingPlayerChoiceContext();
        for(var index=0;index<100&&state.Phase!=PlayerTurnPhase.Play;index++)
            await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
        void Check(bool passed,string message)
        {
            if(!passed)throw new Exception(message);
            Console.WriteLine("COVENANT_GENERATION_POOLS_PASS "+message);
        }
        async Task Clear()
        {
            await CardPileCmd.RemoveFromCombat(state.AllCards.ToArray());
            foreach(var power in player.Creature.Powers.Where(power=>power is not CovenantAttritionPower).ToArray())
                await PowerCmd.Remove(power);
            terminal.ResetTransient();terminal.Ledger.Layers.Clear();terminal.BeginTurn();
        }
        async Task<T> Add<T>(PileType pile=PileType.Hand) where T:CardModel
        {
            var card=combat.CreateCard<T>(player);
            await CardPileCmd.Add(card,pile,skipVisuals:true);
            return card;
        }
        Task Play(CardModel card)=>CardCmd.AutoPlay(context,card,card.TargetType==TargetType.AnyEnemy?enemy:null)
            .WaitAsync(TimeSpan.FromSeconds(16));

        var rng=player.RunState.Rng.CombatCardGeneration;
        var rngBefore=rng.ToSerializable();
        var sixPools=CovenantTerminal.SixPools.Prepend(player.Character.CardPool).Distinct().ToArray();
        async Task CheckGeneration<T>(string name,Func<CardModel,bool> predicate,CardPoolModel[] pools,
            Func<T,Task>? generate=null,int count=1,bool allowRepeats=false) where T:CardModel
        {
            await Clear();
            var source=await Add<T>();
            var before=state.AllCards.ToHashSet();
            var candidates=CardFactory.FilterForCombat(pools
                .SelectMany(pool=>pool.GetUnlockedCards(player.UnlockState,player.RunState.CardMultiplayerConstraint))
                .DistinctBy(card=>card.Id).Where(predicate).Where(card=>card.IsAllowedInRun(player.RunState))).ToArray();
            if(pools.Length>1)Check(candidates.Select(card=>card.Pool).Distinct().Count()==6,$"{typeof(T).Name} has eligible candidates in all six pools");
            var sample=Enumerable.Range(0,128).Select(seed=>
            {
                var seeded=new Rng((ulong)seed);
                var initial=seeded.ToSerializable();
                var picked=allowRepeats?Enumerable.Range(0,count).Select(_=>seeded.NextItem(candidates)!).ToArray()
                    :candidates.TakeRandom(count,seeded).ToArray();
                return (Initial:initial,Picked:picked,Final:seeded.ToSerializable());
            }).First(result=>pools.Length==1||result.Picked.Any(card=>card.Pool!=player.Character.CardPool));
            rng.LoadFromSerializable(sample.Initial);
            await (generate==null?Play(source):generate(source));
            var generated=state.AllCards.Where(card=>!before.Contains(card)).Select(card=>card.Id).Order().ToArray();
            Check(generated.SequenceEqual(sample.Picked.Select(card=>card.Id).Order())&&
                rng.ToSerializable().counter==sample.Final.counter,
                $"{name} (expected {string.Join(',',sample.Picked.Select(card=>card.Id.Entry))}, got {string.Join(',',generated.Select(id=>id.Entry))})");
        }
        try
        {
            terminal.AllCardPools=false;
            await CheckGeneration<WhiteNoise>("White Noise draws only from the current character when mixing is disabled",
                card=>card.Type==CardType.Power,[player.Character.CardPool]);
            terminal.AllCardPools=true;
            await CheckGeneration<WhiteNoise>("White Noise draws Powers from all six character pools",
                card=>card.Type==CardType.Power,sixPools);
            var generated=state.Hand.Cards.Single();
            Check(CardMembership.IsGenerated(generated)&&generated.EnergyCost.GetAmountToSpend()==0,
                "White Noise still creates a temporary card that can be played for free");
            await CheckGeneration<InfernalBlade>("Infernal Blade keeps its Attack filter across the six pools",
                card=>card.Type==CardType.Attack,sixPools);
            await CheckGeneration<Metamorphosis>("Metamorphosis uses the six pools on the factory path that allows repeats",
                card=>card.Type==CardType.Attack,sixPools,count:3,allowRepeats:true);
            await CheckGeneration<HelloWorld>("Hello World still generates only Common cards across the six pools",
                card=>card.Rarity==CardRarity.Common,sixPools,async source=>
                {
                    await Play(source);
                    player.Creature.BeforeTurnStart(CombatSide.Player);
                    await player.Creature.GetPower<HelloWorldPower>()!.BeforeHandDraw(player,context,combat);
                });
            await CheckGeneration<JackOfAllTrades>("Jack of All Trades keeps its dedicated colorless pool",
                card=>card is not JackOfAllTrades,[ModelDb.CardPool<ColorlessCardPool>()]);

            await Clear();
            terminal.Ledger.Add(Bond.Ethereal,500);
            var rareRollSeed=Enumerable.Range(0,32).First(seed=>new Rng((ulong)seed).NextInt(100)<75);
            terminal.RandomFor(CovenantRandom.CombatRoll).LoadFromSerializable(new Rng((ulong)rareRollSeed).ToSerializable());
            await Play(await Add<CallOfTheVoid>());
            var nativeCounter=rng.ToSerializable().counter;
            await player.Creature.GetPower<CallOfTheVoidPower>()!.BeforeHandDraw(player,context,combat);
            var rare=state.Hand.Cards.Single();
            Check(rare.Rarity==CardRarity.Rare&&rare.Keywords.Contains(CardKeyword.Ethereal)&&rng.ToSerializable().counter==nativeCounter,
                "Call of the Void keeps its rare-only trait and independent RNG on a successful roll");
        }
        finally
        {
            rng.LoadFromSerializable(rngBefore);
            terminal.AllCardPools=true;
        }

        await Clear();
        enemy.SetMaxHpInternal(10000);enemy.SetCurrentHpInternal(10000);
        for(var index=0;index<BondCatalog.Get(Bond.Strike).Advanced;index++)await Add<Hellraiser>(PileType.Exhaust);
        for(var index=0;index<4;index++)await Add<DefendIronclad>(PileType.Draw);
        async Task<decimal> PlayStrike()
        {
            var card=await Add<TwinStrike>();
            card.DeckVersion=player.RunState.CreateCard<TwinStrike>(player);
            var hp=enemy.CurrentHp;
            await Play(card);
            await CardPileCmd.Add(card,PileType.Exhaust,skipVisuals:true);
            return hp-enemy.CurrentHp;
        }
        await PlayerCmd.SetEnergy(0,player);
        var ordinaryDamage=await PlayStrike();
        await PlayStrike();await PlayStrike();
        Check(state.Energy==1&&state.Hand.Cards.Count==1,"the first Strike II trigger grants one Energy and draws");
        var boosted=await PlayStrike();
        Check(boosted==ordinaryDamage+6,$"the next Strike still gains the damage bonus on each hit ({ordinaryDamage} to {boosted})");
        await PlayStrike();await PlayStrike();
        Check(state.Energy==1&&state.Hand.Cards.Count==2,"later Strike II triggers in the same turn still draw without granting Energy");
        Check(await PlayStrike()==ordinaryDamage+6,"later triggers also prepare the original damage bonus");
        terminal.BeginTurn();
        terminal.Ledger.Add(Bond.Strike,250);
        await PlayStrike();await PlayStrike();
        Check(state.Energy==2&&state.Hand.Cards.Count==3,"the 250-layer cadence refunds again on its first trigger next turn");
        await PlayStrike();await PlayStrike();
        Check(state.Energy==2&&state.Hand.Cards.Count==4,"the 250-layer cadence still draws again without refunding Energy");
        Console.WriteLine("COVENANT_GENERATION_POOLS_COMPLETE");
    }
}
