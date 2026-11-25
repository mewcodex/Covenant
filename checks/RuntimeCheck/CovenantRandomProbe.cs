using System.Text.Json;
using Covenant.Core;
using Covenant.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

internal static class CovenantRandomProbe
{
    private static void Check(bool passed,string detail)
    {
        if(!passed)throw new InvalidOperationException(detail);
        Console.WriteLine("COVENANT_RANDOM_PASS "+detail);
    }

    internal static async Task Run(string output)
    {
        var run=RunManager.Instance.DebugOnlyGetState()!;
        var player=run.Players.Single();
        var terminal=CovenantTerminal.For(player)!;
        var combat=player.Creature.CombatState!;
        var enemy=combat.HittableEnemies.First();
        var context=new ThrowingPlayerChoiceContext();
        await CardPileCmd.RemoveFromCombat(player.PlayerCombatState!.AllCards.ToArray());
        terminal.ResetTransient();
        terminal.Ledger.Layers.Clear();
        enemy.SetMaxHpInternal(10_000);
        enemy.SetCurrentHpInternal(10_000);

        var nativeSelection=run.Rng.CombatCardSelection.ToSerializable();
        var nativeTargets=run.Rng.CombatTargets.ToSerializable();
        var clawStream=terminal.RandomFor(CovenantRandom.Claw);
        var claw=combat.CreateCard<Claw>(player);
        await CardPileCmd.Add(claw,PileType.Hand,skipVisuals:true);
        await CardCmd.AutoPlay(context,claw,enemy,skipCardPileVisuals:true);
        Check(claw.Pile?.Type==PileType.Discard&&clawStream.ToSerializable().counter==0,"Claw at zero layers does not roll or return");
        terminal.Ledger.Add(Bond.Zero,100);
        var plus=(Claw)claw.MutableClone();
        CardCmd.Upgrade(plus);
        Check(terminal.ReworkAmount(claw)==25&&terminal.ReworkAmount(plus)==33,"Claw and Claw+ scale at one point per four and three layers");
        terminal.Ledger.Add(Bond.Zero,999);
        Check(terminal.ReworkAmount(claw)==50&&terminal.ReworkAmount(plus)==50,"both Claw versions cap their return chance at 50 percent");

        var knownSeed=Enumerable.Range(1,256).Select(seed=>new Rng((ulong)seed)).First(rng=>
        {
            var probe=new Rng(rng.ToSerializable());
            return probe.NextInt(100)>=50&&probe.NextInt(100)<50;
        });
        clawStream.LoadFromSerializable(knownSeed.ToSerializable());
        claw.BaseReplayCount=2;
        await CardPileCmd.Add(claw,PileType.Hand,skipVisuals:true);
        await CardCmd.AutoPlay(context,claw,enemy,skipCardPileVisuals:true);
        Check(claw.Pile?.Type==PileType.Discard&&clawStream.ToSerializable().counter==1,
            "a failed Claw roll is not retried by either of its two replays");
        await CardPileCmd.Add(claw,PileType.Hand,skipVisuals:true);
        await terminal.PlayForCovenant(context,claw);
        Check(claw.Pile?.Type==PileType.Hand&&clawStream.ToSerializable().counter==2&&enemy.CurrentHp<10_000,
            "the next real play rolls exactly once and the successful Claw returns");
        Check(nativeSelection==run.Rng.CombatCardSelection.ToSerializable()&&nativeTargets==run.Rng.CombatTargets.ToSerializable()&&
            terminal.RandomFor(CovenantRandom.CombatTarget).ToSerializable().counter>0,"Claw and Covenant autoplay targeting do not advance native RNGs");

        var calling=combat.CreateCard<CallOfTheVoid>(player);
        await CardPileCmd.Add(calling,PileType.Hand,skipVisuals:true);
        await CardCmd.AutoPlay(context,calling,null,skipCardPileVisuals:true);
        var power=player.Creature.GetPower<CallOfTheVoidPower>()!;
        await PowerCmd.ModifyAmount(context,power,1,player.Creature,calling);
        terminal.Ledger.Add(Bond.Ethereal,300);
        var chance=terminal.ReworkAmount(calling);
        var mixedRollSeed=Enumerable.Range(1,256).Select(seed=>new Rng((ulong)seed)).First(rng=>
        {
            var probe=new Rng(rng.ToSerializable());
            return probe.NextInt(100)<chance&&probe.NextInt(100)>=chance;
        }).ToSerializable();
        var effectRolls=terminal.RandomFor(CovenantRandom.CombatRoll);
        effectRolls.LoadFromSerializable(mixedRollSeed);
        var generation=run.Rng.CombatCardGeneration;
        var expectedGeneration=new Rng(generation.ToSerializable());
        var nativeCard=CardFactory.FilterForCombat(player.Character.CardPool.GetUnlockedCards(player.UnlockState,player.RunState.CardMultiplayerConstraint))
            .Where(card=>card.IsAllowedInRun(run)).TakeRandom(1,expectedGeneration).Single();
        var modGeneration=terminal.RandomFor(CovenantRandom.CombatCard);
        var initialCardRolls=modGeneration.ToSerializable().counter;
        var beforeHand=player.PlayerCombatState!.Hand.Cards.ToHashSet();
        await power.BeforeHandDraw(player,context,combat);
        var generated=player.PlayerCombatState.Hand.Cards.Where(card=>!beforeHand.Contains(card)).ToArray();
        Check(generated.Length==2&&generated[0].Rarity==CardRarity.Rare&&generated[1].Id==nativeCard.Id&&
            effectRolls.ToSerializable().counter==mixedRollSeed.counter+2&&
            modGeneration.ToSerializable().counter>initialCardRolls&&generation.ToSerializable()==expectedGeneration.ToSerializable(),
            "Call of the Void rolls for each card and only its normal result uses native card-generation randomness");

        await PowerCmd.Apply<LostLutePower>(context,player.Creature,1,player.Creature,null);
        var top=combat.CreateCard<StrikeIronclad>(player);
        await CardPileCmd.Add(top,PileType.Draw,CardPilePosition.Top,skipVisuals:true);
        var targetRolls=terminal.RandomFor(CovenantRandom.CombatTarget);
        var initialTargetRolls=targetRolls.ToSerializable().counter;
        await CovenantMelody.Inherit(context,player,CardTag.Cho);
        Check(top.Pile?.Type==PileType.Discard&&targetRolls.ToSerializable().counter==initialTargetRolls+1&&
            nativeTargets==run.Rng.CombatTargets.ToSerializable(),
            "inherited Lost Lute targets its autoplay without advancing the native target stream");

        var reward=new CardReward(CardCreationOptions.ForRoom(player,RoomType.Monster),3,player);
        reward.Populate();
        var nativeRewards=player.PlayerRng.Rewards.ToSerializable();
        var previousOdds=player.PlayerOdds.CardRarity.CurrentValue;
        var reroll=(Task)AccessTools.Method(typeof(CovenantRewards),"Reroll").Invoke(null,[reward,terminal,2])!;
        await reroll;
        Check(reward.Cards.Count()==4&&player.PlayerRng.Rewards.ToSerializable()==nativeRewards&&
            player.PlayerOdds.CardRarity.CurrentValue==previousOdds&&terminal.RandomFor(CovenantRandom.RewardReroll).ToSerializable().counter>0,
            "the native reward generation gets its Covenant reroll RNG and preserves native reward RNG and rarity offset");

        var clawPosition=clawStream.ToSerializable();
        foreach(var stream in Enum.GetValues<CovenantRandom>().Where(stream=>stream!=CovenantRandom.Claw))
            terminal.RandomFor(stream).NextInt(100);
        Check(clawStream.ToSerializable()==clawPosition,"advancing every other Covenant stream leaves the Claw stream alone");
        var properties=NetFullCombatState.FromRun(run,null).Players.Single().relics.Select(entry=>entry.relic)
            .Single(relic=>relic.Id==ModelDb.GetId<CovenantTerminal>()).Props!.strings!;
        Check(properties.Any(property=>property.name==nameof(CovenantTerminal.CovenantCombatRandomState))&&
            !properties.Any(property=>property.name==nameof(CovenantTerminal.CovenantEconomyRandomState)),
            "combat checksums exclude local reward and shop RNGs just like the base game");

        await SaveManager.Instance.SaveRun(null,saveProgress:false);
        var expected=Enum.GetValues<CovenantRandom>().ToDictionary(stream=>stream,stream=>Sample(new Rng(terminal.RandomFor(stream).ToSerializable())));
        File.WriteAllText(output+"/0.5/random-sl-expected.json",JsonSerializer.Serialize(expected));
        Resume(output);
        Console.WriteLine("COVENANT_RANDOM_SAVED");
    }

    internal static void Resume(string output)
    {
        var saved=SaveManager.Instance.LoadRunSave();
        if(!saved.Success||saved.SaveData==null)throw new InvalidOperationException("The native run save could not be read.");
        var run=RunState.FromSerializable(saved.SaveData);
        var terminal=CovenantTerminal.For(run.Players.Single())!;
        var expected=JsonSerializer.Deserialize<Dictionary<CovenantRandom,int[]>>(File.ReadAllText(output+"/0.5/random-sl-expected.json"))!;
        Check(Enum.GetValues<CovenantRandom>().All(stream=>Sample(terminal.RandomFor(stream)).SequenceEqual(expected[stream])),
            "all Covenant streams reproduce the same subsequent rolls after loading the native run save");
        Console.WriteLine("COVENANT_RANDOM_RESUME_COMPLETE");
    }

    private static int[] Sample(Rng rng)=>[rng.NextInt(100),rng.NextInt(7),rng.NextInt(100_003),rng.NextInt(100)];
}
