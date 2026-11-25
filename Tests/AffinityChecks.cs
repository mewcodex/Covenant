using Covenant.Core;
using Covenant.Game;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

internal static class AffinityChecks
{
    internal static void Run(Action<bool,string> check)
    {
        CombatChecks.CombatActive=false;
        var player=Player.CreateForNewRun(ModelDb.Character<Ironclad>(),UnlockState.all,1);
        var run=RunState.CreateForTest([player],[],[],seed:"covenant-affinity");
        player.Deck.Clear();for(int i=0;i<3;i++)player.Deck.AddInternal(run.CreateCard<NoxiousFumes>(player),-1);
        var terminal=CovenantTerminal.For(player)!;
        CardCreationOptions Options(ulong seed,CardCreationFlags flags=0,CardCreationSource source=CardCreationSource.Encounter)=>
            new CardCreationOptions(CovenantTerminal.SixPools,source,CardRarityOddsType.Uniform)
                .WithFlags(CardCreationFlags.IsCardReward|flags).WithRngOverride(new Rng(seed));
        List<CardCreationResult> Choices()=>new CardModel[]{run.CreateCard<Anger>(player),run.CreateCard<DefendIronclad>(player),run.CreateCard<StrikeIronclad>(player)}
            .Select(c=>new CardCreationResult(c)).ToList();
        var total=0;var hitSeed=0UL;
        for(ulong seed=1;seed<=256;seed++)
        {
            var choices=Choices();choices[0].Card.UpgradeInternal();
            var old=choices.Select(c=>c.Card).ToArray();
            if(!terminal.TryModifyCardRewardOptions(player,choices,Options(seed)))continue;
            total++;hitSeed=seed;
            var changed=Enumerable.Range(0,3).Where(i=>choices[i].Card!=old[i]).ToArray();
            check(changed.Length==1,"Affinity changes only one native candidate");
            var index=changed.Single();var card=choices[index].Card;
            check(index==0&&card.Rarity==old[index].Rarity&&card.IsUpgraded,"Affinity preserves rarity and native upgrade roll and leaves basic slots alone");
            check(!card.EnergyCost.WasJustUpgraded&&card.DynamicVars.Values.All(v=>!v.WasJustUpgraded),"Affinity finishes native upgrade display state instead of leaving a preview highlight");
            check(CardMembership.Of(card).Contains(Bond.Poison)&&!old.Any(c=>c.Id==card.Id),"Affinity follows a held cross-character flow and excludes duplicate choices");
        }
        check(total>60&&total<115,"Seeded affinity sample has the expected occasional frequency: "+total+"/256");
        foreach(var flag in new[]{CardCreationFlags.NoModifyHooks,CardCreationFlags.NoCardPoolModifications,CardCreationFlags.NoCardModelModifications})
            check(!terminal.TryModifyCardRewardOptions(player,Choices(),Options(hitSeed,flag)),"Affinity respects native restriction "+flag);
        check(!terminal.TryModifyCardRewardOptions(player,Choices(),Options(hitSeed,source:CardCreationSource.Other)),"Event rewards do not receive combat affinity");
        check(!terminal.TryModifyCardRewardOptions(player,Choices(),Options(hitSeed).WithFilter(c=>!CardMembership.Of(c).Contains(Bond.Poison))),"Affinity honors an excluding native filter without fallback injection");
        terminal.AllCardPools=false;
        check(!terminal.TryModifyCardRewardOptions(player,Choices(),Options(hitSeed)),"Disabled six-pool mode disables affinity");terminal.AllCardPools=true;
        var modified=Choices();modified[0].ModifyCard(run.CreateCard<Anger>(player));
        check(!terminal.TryModifyCardRewardOptions(player,modified,Options(hitSeed)),"Affinity preserves candidates already modified by another relic");
        var first=Choices();var second=Choices();
        check(terminal.TryModifyCardRewardOptions(player,first,Options(hitSeed))&&terminal.TryModifyCardRewardOptions(player,second,Options(hitSeed))&&first[0].Card.Id==second[0].Card.Id,"Native RNG override produces identical affinity choices after replay");
        // Exercise the actual factory/hook order: affinity replaces before egg upgrades.
        player.AddRelicInternal(ModelDb.Relic<MoltenEgg>().ToMutable(),silent:true);
        var seenAffinity=false;
        for(ulong seed=1;seed<=32;seed++)
        {
            var options=Options(seed).WithRarityOdds(CardRarityOddsType.RegularEncounter).WithFlags(CardCreationFlags.NoUpgradeRoll);
            var cards=CardFactory.CreateForReward(player,3,options).ToArray();
            check(cards.Select(c=>c.Card.Id).Distinct().Count()==3,"Native factory keeps affinity rewards distinct");
            check(cards.Where(c=>c.Card.Type==CardType.Attack).All(c=>c.Card.IsUpgraded),"Late native Molten Egg upgrades final reward cards");
            seenAffinity|=cards.Any(c=>c.ModifyingRelics.Contains(terminal));
        }
        check(seenAffinity,"Actual native factory invokes affinity before late relic hooks");
        foreach(var (name,bond) in new[]{("DemonForm",Bond.Fury),("NoxiousFumes",Bond.Poison),("Accuracy",Bond.Blades),("Defragment",Bond.Resonance)})
        {
            var trait=CardMembership.Definitions[name].Trait;
            check(trait.Bond==bond&&trait.Trigger=="play","Role power trait supports its own flow on play: "+name);
        }
        DensityChecks(check);
        Console.WriteLine($"COVENANT_AFFINITY_OK replacement-sample={total}/256 native-factory=pass late-egg=pass restrictions=pass replay=pass role-powers=pass");
    }

    private static void DensityChecks(Action<bool,string> check)
    {
        var player=Player.CreateForNewRun(ModelDb.Character<Ironclad>(),UnlockState.all,1);
        var run=RunState.CreateForTest([player],[],[],seed:"covenant-density");var terminal=CovenantTerminal.For(player)!;
        player.Deck.Clear();
        for(int i=0;i<2;i++)player.Deck.AddInternal(run.CreateCard<DeadlyPoison>(player),-1);
        for(int i=0;i<5;i++){player.Deck.AddInternal(run.CreateCard<TrueGrit>(player),-1);player.Deck.AddInternal(run.CreateCard<Anger>(player),-1);}
        var possible=CovenantTerminal.SixPools.SelectMany(p=>p.GetUnlockedCards(player.UnlockState,player.RunState.CardMultiplayerConstraint)).Where(c=>c.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare).ToArray();
        var samples=512;
        (int Changed,int First,int Second) Sample(Player owner,CovenantTerminal model,Bond first,Bond second)
        {
            var changed=0;var firstCount=0;var secondCount=0;
            for(ulong seed=1;seed<=(ulong)samples;seed++)
            {
                var choices=new[]{CardRarity.Common,CardRarity.Uncommon,CardRarity.Rare}.Select(r=>new CardCreationResult(owner.RunState.CreateCard(possible.First(c=>c.Rarity==r),owner))).ToList();
                var options=new CardCreationOptions(CovenantTerminal.SixPools,CardCreationSource.Encounter,CardRarityOddsType.Uniform).WithFlags(CardCreationFlags.IsCardReward).WithRngOverride(new Rng(seed));
                if(!model.TryModifyCardRewardOptions(owner,choices,options))continue;
                changed++;var bonds=CardMembership.Of(choices.Single(c=>c.ModifyingRelics.Contains(model)).Card);
                if(bonds.Contains(first))firstCount++;if(bonds.Contains(second))secondCount++;
            }
            return (changed,firstCount,secondCount);
        }
        var mixed=Sample(player,terminal,Bond.Poison,Bond.Zero);
        check(mixed.Changed>0&&mixed.First*4>=mixed.Changed,"A held sparse Poison flow receives at least a quarter of affinity replacements despite common Zero/Ember/Creation components");
        var robot=Player.CreateForNewRun(ModelDb.Character<Defect>(),UnlockState.all,1);var robotRun=RunState.CreateForTest([robot],[],[],seed:"covenant-singleton");
        for(int i=0;i<3;i++)robot.Deck.AddInternal(robotRun.CreateCard<Coolheaded>(robot),-1);
        var focused=Sample(robot,CovenantTerminal.For(robot)!,Bond.Resonance,Bond.Overload);
        check(focused.First>focused.Second,"An incidental starter Dualcast does not outweigh three chosen Resonance components");
        var output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../audits"));Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"affinity-density.json"),System.Text.Json.JsonSerializer.Serialize(new{status="pass",method="Seeded native reward modification hooks; replacement memberships may overlap. Not a win-rate simulation.",samplesPerDeck=samples,mixedDeck=new{replacements=mixed.Changed,poison=mixed.First,zero=mixed.Second},defectStarterPlusThreeCoolheaded=new{replacements=focused.Changed,resonance=focused.First,overload=focused.Second}},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"COVENANT_AFFINITY_DENSITY_OK mixed-poison={mixed.First}/{mixed.Changed} focused-resonance={focused.First}/{focused.Changed} incidental-overload={focused.Second}/{focused.Changed}");
    }
}
