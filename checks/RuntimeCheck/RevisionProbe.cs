using System.Text.Json;
using System.Text.Json.Serialization;
using Covenant.Core;
using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.ValueProps;
using HarmonyLib;
using System.Text.RegularExpressions;

internal static partial class RevisionProbe
{
    private sealed class Choices:ICardSelector
    {
        internal CardModel? Target {get;set;}
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> cards,int min,int max)=>Task.FromResult(
            Target is {} target&&cards.Contains(target)?[target]:cards.Take(min));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> cards,IReadOnlyList<CardRewardAlternative> alternatives)=>new();
    }
    internal static async Task Run(SceneTree tree,string output)
    {
        var options=new JsonSerializerOptions{PropertyNameCaseInsensitive=true,Converters={new JsonStringEnumConverter()}};
        var stagedText=File.ReadAllText(output+"/0.5/compiled-revisions.json");
        var stagedSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stagedText)));
        File.WriteAllText(output+"/0.5/native-programs-"+stagedSha256+".json",stagedText);
        var programs=JsonSerializer.Deserialize<Dictionary<string,RevisionTrait>>(stagedText,options)!;
        var definitions=(Dictionary<string,CardDefinition>)CardMembership.Definitions;
        var originals=definitions.ToDictionary(p=>p.Key,p=>p.Value);
        var focus=System.Environment.GetEnvironmentVariable("COVENANT_REVISION_FOCUS");
        var stagedDescriptions=focus is "staged-descriptions" or "strike-growth" or "divine-shot" or "decay" or "bad-luck" or "ascenders-bane" or "entropy-growth" or "guilty";
        var productionApplied=!stagedDescriptions&&originals.Values.All(d=>d.Trait.Revision!=null);
        // Keep all old generic/authored effects inert in this isolated test process.
        // This does not alter the production catalog or count unsupported cards as migrated.
        foreach(var (name,definition) in originals)
            if(stagedDescriptions) definitions[name]=definition with{Trait=definition.Trait with{Revision=programs[name]}};
            else if(productionApplied)
            {
                if(definition.Trait.Trigger!="revision"||definition.Trait.Amount!=0||definition.Trait.UpgradeAmount!=0||definition.Trait.Effects is not {Length:0}||JsonSerializer.Serialize(definition.Trait.Revision,options)!=JsonSerializer.Serialize(programs[name],options))
                    throw new Exception("Production revision differs from the reviewed staged program: "+name);
            }
            else definitions[name]=definition with{Trait=definition.Trait with{Effects=[new TraitEffect("own_remove","grow",0,UpgradeAmount:0)],Revision=programs.GetValueOrDefault(name)}};
        var player=RunManager.Instance.DebugOnlyGetState()!.Players.Single();var terminal=CovenantTerminal.For(player)!;
        var selector=new Choices();using var choices=RunManager.Instance.CardSelectorStack.UseSelector(player,selector);
        var context=new ThrowingPlayerChoiceContext();var checks=new List<string>();
        void Check(bool passed,string reason){if(!passed)throw new Exception(reason);checks.Add(reason);Console.WriteLine("COVENANT_REVISION_PASS "+reason);}
        async Task Clear()
        {
            await CardPileCmd.RemoveFromCombat(player.PlayerCombatState!.AllCards.ToArray());
            foreach(var power in player.Creature.Powers.Where(p=>p is not CovenantAttritionPower).ToArray())await PowerCmd.Remove(power);
            if(player.PlayerCombatState!.OrbQueue.Capacity>0)OrbCmd.RemoveSlots(player,player.PlayerCombatState.OrbQueue.Capacity);
            player.PlayerCombatState.OrbQueue.Clear();
            terminal.ResetTransient();terminal.Ledger.Layers.Clear();terminal.Ledger.ClearCombatUses();
            foreach(var enemy in player.Creature.CombatState!.HittableEnemies)
            {
                enemy.SetMaxHpInternal(10000);enemy.SetCurrentHpInternal(10000);
                foreach(var power in enemy.Powers.ToArray())await PowerCmd.Remove(power);
            }
        }
        async Task<T> Add<T>(PileType pile=PileType.Hand,bool upgraded=false) where T:CardModel
        {
            var card=player.Creature.CombatState!.CreateCard<T>(player);if(upgraded)CardCmd.Upgrade(card);
            await CardPileCmd.Add(card,pile);return card;
        }
        async Task ActivateWith<T>(Bond bond,int tier=1) where T:CardModel
        {
            var bondDefinition=BondCatalog.Get(bond);
            var missing=(tier==1?bondDefinition.Basic:bondDefinition.Advanced)-CardMembership.Counts(player).GetValueOrDefault(bond);
            for(var i=0;i<missing;i++)await Add<T>(PileType.Exhaust);
            if(terminal.Tier(bond)<tier)throw new Exception($"{typeof(T).Name} did not activate {bond} tier {tier} in the test fixture.");
        }
        Task Play(CardModel card)=>CardCmd.AutoPlay(context,card,player.Creature.CombatState!.HittableEnemies.First()).WaitAsync(TimeSpan.FromSeconds(25));
        async Task EntropyGrowth()
        {
            await Clear();
            var permanent=player.RunState.CreateCard<Entropy>(player);
            await CardPileCmd.Add(permanent,PileType.Deck);
            await Add<Backflip>(PileType.Draw);await Add<FlameBarrier>(PileType.Draw);
            await Add<IronWave>(PileType.Draw);await Add<ShrugItOff>(PileType.Draw);
            await Add<DefendIronclad>(PileType.Draw);await Add<ChargeBattery>(PileType.Draw);
            await terminal.BeforeCombatStart();
            await Play(await Add<Entropy>());
            await Play(await Add<Entropy>(upgraded:true));
            var power=player.Creature.GetPower<EntropyPower>()!;
            Check(power.Amount==2&&terminal.Tier(Bond.Harmony)==1,
                "both Entropy copies use the stacked native effect while the deck's Harmony remains active");

            var unrelated=await Add<DefendIronclad>();
            var untouched=player.Creature.CombatState!.CreateCard<Bludgeon>(player);
            var previousCreation=terminal.Ledger.Permanent(Bond.Creation);
            var previousCost=untouched.EnergyCost.GetWithModifiers(CostModifiers.All);
            await CardCmd.Transform(unrelated,untouched);
            Check(terminal.Ledger.Permanent(Bond.Creation)==previousCreation&&untouched.EnergyCost.GetWithModifiers(CostModifiers.All)==previousCost,
                "Entropy ignores unrelated transformations and does not discount the result");
            await CardPileCmd.RemoveFromCombat([untouched]);

            for(var i=0;i<2;i++)
            {
                var original=await Add<DefendIronclad>();
                var before=BondCatalog.All.ToDictionary(def=>def.Id,def=>terminal.Ledger.Get(def.Id));
                await power.AfterPlayerTurnStart(context,player);
                var replacement=player.PlayerCombatState!.Hand.Cards.Single();
                var counts=terminal.Counts();
                var active=BondCatalog.All.Where(def=>terminal.TierAtCount(def.Id,counts.GetValueOrDefault(def.Id))>0)
                    .Select(def=>def.Id).ToHashSet();
                var targets=CardMembership.Of(replacement).Where(bond=>active.Contains(bond)&&BondCatalog.Get(bond).UsesLayers).ToHashSet();
                Check(replacement!=original&&active.Contains(Bond.Harmony)&&targets.Contains(Bond.Creation)&&
                    BondCatalog.All.All(def=>terminal.Ledger.Get(def.Id)-before[def.Id]==(targets.Contains(def.Id)?active.Count:0)),
                    "each native Entropy transformation grows exactly the result's active stackable covenants once");
                await CardPileCmd.RemoveFromCombat([replacement]);
            }
            await CardPileCmd.RemoveFromDeck(permanent,showPreview:false);
            Console.WriteLine("COVENANT_ENTROPY_GROWTH_COMPLETE");
        }
        try
        {
            if(focus=="entropy-growth")
            {
                for(var i=0;i<100&&player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++)
                    await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
                Check(player.PlayerCombatState?.Phase==PlayerTurnPhase.Play,"Entropy verification starts after the native opening turn is ready");
                await EntropyGrowth();
            }
            if(focus=="activation-audit")
            {
                for(var i=0;i<100&&player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++)
                    await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
                Check(player.PlayerCombatState?.Phase==PlayerTurnPhase.Play,"audit starts after the opening draw");
                await EntropyGrowth();

                await Clear();
                await Add<Injury>(PileType.Draw);await Add<Shame>(PileType.Draw);
                var bane=await Add<AscendersBane>();
                Check(terminal.Tier(Bond.Curse)==1,"Ascender's Bane is the final name for Curse I");
                await CardCmd.Exhaust(context,bane,causedByEthereal:true);
                Check(terminal.Tier(Bond.Curse)==1&&terminal.Ledger.Get(Bond.Curse)==2,
                    "Ascender's Bane grants layers as it leaves and continues counting as an exhausted Curse");

                await Clear();
                await Add<Wound>(PileType.Draw);await Add<Burn>(PileType.Draw);
                var dazed=await Add<Dazed>();
                Check(terminal.Tier(Bond.Status)==1,"Dazed is the final name for Residue I");
                await CardCmd.Exhaust(context,dazed,causedByEthereal:true);
                Check(terminal.Tier(Bond.Status)==0&&terminal.Ledger.Get(Bond.Status)==4,
                    "Dazed grants its Ethereal growth at the threshold even though it no longer counts afterward");

                await Clear();
                await Add<WhiteNoise>(PileType.Draw);await Add<Backflip>(PileType.Draw);await Add<Skim>(PileType.Draw);
                await Add<BallLightning>(PileType.Draw);
                var soul=await Add<Soul>();
                Check(terminal.Tier(Bond.Creation)==1,"Soul is the final name for Creation I");
                await CardCmd.Exhaust(context,soul);
                Check(terminal.Tier(Bond.Creation)==0&&terminal.Ledger.Get(Bond.Creation)==2,
                    "Soul grants its own exhaust growth before its name stops counting for Creation");

                await Clear();
                var duplicates=player.Deck.Cards.GroupBy(CardMembership.NameKey).SelectMany(group=>group.Skip(1)).ToArray();
                await CardPileCmd.RemoveFromDeck(duplicates,showPreview:false);
                var fixtures=BondCatalog.Get(Bond.Solitude).Advanced-1;
                var combat=player.Creature.CombatState!;
                var unique=ModelDb.AllCards.Where(card=>card.Pool is MegaCrit.Sts2.Core.Models.CardPools.IroncladCardPool&&
                    card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare).DistinctBy(CardMembership.NameKey).Take(fixtures);
                await CardPileCmd.Add(unique.Select(card=>combat.CreateCard(card,player)).ToArray(),PileType.Draw,skipVisuals:true);
                var leaving=await Add<Dazed>();
                var alreadyExhausted=await Add<Dazed>(PileType.Exhaust);
                Check(terminal.Tier(Bond.Solitude)==2&&terminal.Counts().GetValueOrDefault(Bond.Solitude)==25,
                    "Solitude II is exactly at its unique-name threshold before exhausting Dazed");
                await CardCmd.Exhaust(context,leaving,causedByEthereal:true);
                Check(leaving.Pile==null&&alreadyExhausted.Pile==null&&
                    CovenantTerminal.TryGetCombatReplacement(leaving,out var replacement)&&replacement.Pile?.Type==PileType.Exhaust,
                    "Solitude II still transforms the exhausted name that provided its last count");
                Console.WriteLine("COVENANT_ACTIVATION_AUDIT_COMPLETE");
            }
            if(focus=="reflow")
            {
                for(var i=0;i<100&&player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++)
                    await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
                await Clear();
                Check(CardMembership.Of(ModelDb.Card<Hegemony>()).Contains(Bond.Heavy)&&CardMembership.Of(ModelDb.Card<Hegemony>()).Contains(Bond.Reflow)&&
                    !CardMembership.Of(ModelDb.Card<Bash>()).Contains(Bond.Heavy)&&CardMembership.Of(ModelDb.Card<BorrowedTime>()).Contains(Bond.Reflow),
                    "the Heavy/Reflow split keeps designed overlaps and removes ordinary 2-cost membership");
                var state=player.PlayerCombatState!;
                await Add<Convergence>(PileType.Draw);await Add<DrumOfBattle>(PileType.Draw);await Add<Encore>(PileType.Draw);
                await Add<Delay>(PileType.Draw);await Add<Neurosurge>(PileType.Draw);await Add<RefineBlade>(PileType.Draw);
                var charge=await Add<ChargeBattery>();
                var first=await Add<DoubleEnergy>();
                var second=await Add<Alignment>();
                terminal.BeginTurn();
                terminal.Ledger.Add(Bond.Reflow,200);
                await PlayerCmd.GainEnergy(1,player);
                Check(!player.Creature.HasPower<StretchPower>(),"a non-card energy gain does not spend the Reflow I trigger");
                await Play(first);
                Check(player.Creature.GetPower<StretchPower>()?.Amount==4&&state.Hand.Cards.Any(card=>card is Convergence),
                    "a native card energy gain draws one card and grants four Stretch at 200 layers");
                await Play(second);
                Check(player.Creature.GetPower<StretchPower>()?.Amount==4,"another card energy gain in the same turn does not trigger Reflow I");
                await Play(charge);
                terminal.BeginTurn();
                var stretchBefore=player.Creature.GetPowerAmount<StretchPower>();
                await player.Creature.GetPower<EnergyNextTurnPower>()!.AfterEnergyReset(player);
                Check(player.Creature.GetPowerAmount<StretchPower>()==stretchBefore+4,"a card's delayed energy power also triggers Reflow I in the new turn");

                var extraEnemy=await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Axebot>(player.Creature.CombatState!);
                extraEnemy.SetMaxHpInternal(10000);extraEnemy.SetCurrentHpInternal(10000);
                foreach(var power in extraEnemy.Powers.ToArray())await PowerCmd.Remove(power);
                var enemies=player.Creature.CombatState!.HittableEnemies.ToArray();
                Check(enemies.Length==2,"the Reflow damage fixture has two hittable enemies");
                async Task PlayPaidSkill()
                {
                    state.Energy=3;
                    var skill=await Add<DefendIronclad>();
                    if(!skill.TryManualPlay(null))throw new Exception("The real paid skill was rejected");
                    for(var i=0;i<200&&!CombatManager.Instance.History.CardPlaysFinished.Any(entry=>entry.CardPlay.Card==skill);i++)
                        await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
                    for(var i=0;i<100&&skill.Pile?.Type!=PileType.Discard;i++)
                        await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
                    if(skill.Pile?.Type!=PileType.Discard)throw new Exception("The paid skill did not finish");
                }
                terminal.BeginTurn();
                var a=terminal.Scale(Bond.Reflow).A;
                var baseDamage=1+a;
                var increment=1+a/4;
                var hp=enemies.Sum(enemy=>enemy.CurrentHp);
                await PlayPaidSkill();await PlayPaidSkill();
                Check(enemies.Sum(enemy=>enemy.CurrentHp)==hp-(2*baseDamage+increment),"two paid cards ramp Reflow II from its base to its second hit");
                await Play(await Add<DefendIronclad>());
                Check(enemies.Sum(enemy=>enemy.CurrentHp)==hp-(2*baseDamage+increment),"a free autoplay does not proc the paid-card effect");
                terminal.BeginTurn();
                hp=enemies.Sum(enemy=>enemy.CurrentHp);
                await PlayPaidSkill();
                Check(enemies.Sum(enemy=>enemy.CurrentHp)==hp-baseDamage,"on a fresh turn the damage returns to its base");

                terminal.Ledger.Add(Bond.Reflow,250-terminal.Scale(Bond.Reflow).Layers);
                terminal.BeginTurn();
                var before=enemies.Select(enemy=>enemy.CurrentHp).ToArray();
                await PlayPaidSkill();
                var losses=enemies.Select((enemy,index)=>before[index]-enemy.CurrentHp).ToArray();
                Check(losses.All(loss=>loss==6),$"at 250 layers Reflow II hits all enemies ({string.Join(", ",losses)})");
                await CreatureCmd.Kill(enemies.Skip(1).ToArray());
                terminal.BeginTurn();
                hp=enemies[0].CurrentHp;
                await PlayPaidSkill();await PlayPaidSkill();
                Check(hp-enemies[0].CurrentHp==6+3+8+4,"a single enemy takes one full and one half hit per paid card, ramping once per card");

                await CardPileCmd.RemoveFromCombat(state.DiscardPile.Cards.ToArray());
                await Add<Bludgeon>(PileType.Discard);
                var starCard=await Add<AstralPulse>(PileType.Discard);
                var affordable=await Add<DefendIronclad>(PileType.Discard);
                state.Energy=1;state.Stars=0;
                await terminal.AfterSideTurnEndLate(context,CombatSide.Player,[player.Creature]);
                Check(CombatManager.Instance.History.CardPlaysFinished.Any(entry=>entry.CardPlay.Card==affordable)&&
                    !CombatManager.Instance.History.CardPlaysFinished.Any(entry=>entry.CardPlay.Card==starCard)&&state.Energy==1,
                    "the end-turn autoplay checks both energy and stars and plays the chosen card for free");
                await LayerCurves(player,terminal,Clear,Check);
                Console.WriteLine("COVENANT_REFLOW_COMPLETE");
            }
            if(focus=="count-rebalance")
            {
                for(var i=0;i<100&&player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++)
                    await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
                Check(player.PlayerCombatState?.Phase==PlayerTurnPhase.Play,"the counting probe starts after the opening hand has been dealt");
                Check(BondCatalog.Get(Bond.Status) is {Basic:3,Advanced:6}&&BondCatalog.Get(Bond.Harmony) is {Basic:1,Advanced:0},
                    "Residue uses 3/6 and Harmony has a single tier");
                var mastery=BondCatalog.Get(Bond.Mastery);
                Check(mastery.Tier(9)==0&&mastery.Tier(10)==1&&mastery.Tier(24)==1&&mastery.Tier(25)==2,
                    "Mastery activates at exactly 10 and 25 names");

                var offer=player.RunState.CreateCard<StrikeSilent>(player);
                CardCmd.Upgrade(offer);
                var deck=terminal.Counts(deck:true);
                var projected=CardMembership.CountsAfterAcquisition(player,offer);
                Check(deck[Bond.Strike]==1&&deck[Bond.Guard]==1&&projected[Bond.Strike]==1&&projected.GetValueOrDefault(Bond.Mastery)==1,
                    "deck counts and reward previews deduplicate basic names while picking up the upgraded copy's new covenants");

                await Clear();
                await terminal.BeforeCombatStart();
                var entropy=await Add<Entropy>(PileType.Draw);
                Check(terminal.Tier(Bond.Harmony)==0,"a generated Entropy cannot activate Harmony in the middle of battle");
                await Add<StrikeIronclad>(PileType.Hand);
                var upgradedStrike=await Add<StrikeSilent>(PileType.Discard,upgraded:true);
                await Add<StrikeIronclad>(PileType.Draw,upgraded:true);
                await Add<Claw>(PileType.Exhaust,upgraded:true);
                await Add<FiendFire>(PileType.Exhaust);
                await Add<Apparition>(PileType.Exhaust);
                var withoutTokens=terminal.Counts();
                await Add<Shiv>(PileType.Exhaust);
                await Add<Dazed>(PileType.Exhaust);
                var combat=terminal.Counts();
                Check(combat[Bond.Strike]==1&&combat[Bond.Mastery]==2&&combat.GetValueOrDefault(Bond.Zero)==1,
                    "names deduplicate across piles and an exhausted generated Claw still contributes Zero and Mastery");
                Check(combat.Count==withoutTokens.Count&&combat.All(entry=>withoutTokens.GetValueOrDefault(entry.Key)==entry.Value),
                    "an exhausted Shiv and Dazed do not participate in any covenant count");
                Check(terminal.Counts(deck:true)[Bond.Strike]==1&&terminal.Tier(Bond.Mastery)==0,
                    "battle-specific cards do not change deck counts");
                await CardPileCmd.Add(upgradedStrike,PileType.Play);
                Check(terminal.Counts()[Bond.Strike]==1&&terminal.Counts()[Bond.Mastery]==2,
                    "a resolving copy participates in the same unique-name count");

                await Clear();
                await Add<Bury>(PileType.Exhaust);await Add<Bludgeon>(PileType.Draw);
                await Add<Knockdown>(PileType.Draw);await Add<Hegemony>(PileType.Draw);
                await Add<BodySlam>(PileType.Draw);await Add<DefendIronclad>(PileType.Draw);
                await Add<Injury>(PileType.Exhaust);await Add<Shame>(PileType.Draw);await Add<Clumsy>(PileType.Draw);
                var raw=terminal.Counts();
                var deckEntropy=player.RunState.CreateCard<Entropy>(player);
                await CardPileCmd.Add(deckEntropy,PileType.Deck);
                entropy=await Add<Entropy>(PileType.Draw);
                await terminal.BeforeCombatStart();
                await CardPileCmd.RemoveFromCombat([entropy]);
                var harmonized=terminal.Counts();
                Check(terminal.Tier(Bond.Harmony)==1&&harmonized[Bond.Heavy]==raw[Bond.Heavy]+1&&
                    harmonized[Bond.Curse]==raw[Bond.Curse]+1&&harmonized[Bond.Guard]==raw[Bond.Guard],
                    $"Harmony stays active after Entropy leaves combat and boosts all qualifying covenants (harmony={terminal.Tier(Bond.Harmony)}, Heavy={raw[Bond.Heavy]}/{harmonized[Bond.Heavy]}, Curse={raw[Bond.Curse]}/{harmonized[Bond.Curse]}, Guard={raw[Bond.Guard]}/{harmonized[Bond.Guard]})");
                Check(CardMembership.CountForCost(player,Bond.Heavy)==harmonized[Bond.Heavy]&&CardMembership.CurseTier(player)==terminal.Tier(Bond.Curse),
                    "cost quotes and curse playability use the same counting rules as the live covenant list");

                await Clear();
                var upgradedCards=new CardModel[]{await Add<DefendIronclad>(PileType.Draw,true),await Add<DefendSilent>(PileType.Draw,true),
                    await Add<Backflip>(PileType.Draw,true),await Add<Blur>(PileType.Draw,true),await Add<Skim>(PileType.Draw,true),
                    await Add<Armaments>(PileType.Draw,true),await Add<TrueGrit>(PileType.Draw,true),await Add<ShrugItOff>(PileType.Draw,true),
                    await Add<Claw>(PileType.Draw,true),await Add<Bash>(PileType.Draw,true),await Add<Bludgeon>(PileType.Draw,true),
                    await Add<Uppercut>(PileType.Draw,true),await Add<Inflame>(PileType.Draw,true),
                    await Add<IronWave>(PileType.Draw,true),await Add<Headbutt>(PileType.Draw,true),await Add<Havoc>(PileType.Draw,true),
                    await Add<Anger>(PileType.Draw,true),await Add<Rage>(PileType.Draw,true),await Add<BurningPact>(PileType.Draw,true),
                    await Add<BodySlam>(PileType.Draw,true),await Add<Bloodletting>(PileType.Draw,true),await Add<SecondWind>(PileType.Draw,true),
                    await Add<FlameBarrier>(PileType.Draw,true),await Add<Thunderclap>(PileType.Draw,true),await Add<Bully>(PileType.Draw,true)};
                Check(terminal.Counts()[Bond.Mastery]==24&&terminal.Tier(Bond.Mastery)==1,"24 distinct upgraded cards stay at Mastery I");
                await Add<TwinStrike>(PileType.Draw,true);
                Check(terminal.Counts()[Bond.Mastery]==25&&terminal.Tier(Bond.Mastery)==2,"the 25th distinct upgraded card activates Mastery II");
                for(var i=0;i<2;i++)await Play(upgradedCards[i]);
                var handTarget=await Add<Bludgeon>();
                var costBefore=CardMembership.CurrentPlayCost(handTarget);
                var energyBefore=player.PlayerCombatState!.Energy;
                await Play(upgradedCards[3]);
                terminal.BeginTurn();
                Check(CardMembership.CurrentPlayCost(handTarget)==costBefore-1&&player.PlayerCombatState.Energy==energyBefore,
                    "the third upgraded card discounts a hand card through the next turn without refunding energy");
                Console.WriteLine("COVENANT_COUNT_REBALANCE_COMPLETE");
            }
            if(focus=="aid-mastery")
            {
                var failures=new List<string>();
                void AidCheck(bool passed,string reason){if(passed)Check(passed,reason);else failures.Add(reason);}
                for(var i=0;i<100&&player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++)
                    await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
                Check(player.PlayerCombatState?.Phase==PlayerTurnPhase.Play,"Aid fixtures start after the opening hand finishes drawing");
                await Clear();
                AidCheck(new CardModel[]{ModelDb.Card<Armaments>(),ModelDb.Card<Aggression>(),ModelDb.Card<Apotheosis>(),ModelDb.Card<DrainPower>()}
                    .All(card=>!card.IsUpgraded&&CardMembership.Of(card).Contains(Bond.Mastery))&&
                    new CardModel[]{ModelDb.Card<Begone>(),ModelDb.Card<Abundance>(),ModelDb.Card<KnifeTrap>()}
                        .All(card=>!CardMembership.Of(card).Contains(Bond.Mastery)),
                    "unupgraded Mastery counts effects that upgrade existing cards in their current form");
                var science=player.RunState.CreateCard<MadScience>(player);
                science.TinkerTimeType=CardType.Power;
                science.TinkerTimeRider=MegaCrit.Sts2.Core.Models.Events.TinkerTime.RiderEffect.Improvement;
                AidCheck(CardMembership.Of(science).Contains(Bond.Mastery),"Mad Science's upgrade rider counts before upgrading the source card");

                var backflip=await Add<Backflip>(PileType.Discard);
                await ActivateWith<Backflip>(Bond.Guard);
                await ActivateWith<Skim>(Bond.Rotation);
                terminal.Ledger.Add(Bond.Guard,80);terminal.Ledger.Add(Bond.Rotation,150);
                await Play(await Add<DrainPower>());
                AidCheck(backflip.IsUpgraded&&terminal.Ledger.Get(Bond.Guard)==80&&terminal.Ledger.Get(Bond.Rotation)==153,
                    $"Drain Power picks the upgraded card's highest active covenant (upgraded={backflip.IsUpgraded}, layers={terminal.Ledger.Get(Bond.Guard)}/{terminal.Ledger.Get(Bond.Rotation)})");

                await Clear();
                var original=await Add<DeadlyPoison>();
                await ActivateWith<DeadlyPoison>(Bond.Poison);
                selector.Target=original;
                await Play(await Add<Begone>());
                selector.Target=null;
                AidCheck(player.PlayerCombatState!.Hand.Cards.Any(card=>card is MinionStrike)&&terminal.Tier(Bond.Poison)==0&&terminal.Ledger.Get(Bond.Poison)==2,
                    "Begone grows the covenant of the original card when the replacement has a different covenant");

                await Clear();
                await ActivateWith<PoisonedStab>(Bond.Poison);
                await Play(await Add<Terraforming>(upgraded:true));
                await Play(await Add<DefendIronclad>());
                AidCheck(player.Creature.GetPower<CovenantTerraformingPower>()?.Amount==3&&terminal.Ledger.Get(Bond.Poison)==0,
                    "Terraforming+ offers a visible three-layer status that survives a skill play");
                await Play(await Add<PoisonedStab>());
                AidCheck(!player.Creature.HasPower<CovenantTerraformingPower>()&&terminal.Ledger.Get(Bond.Poison)==7,
                    "the next attack receives three additional layers for its covenant and consumes Terraforming");

                await Clear();
                var returned=await Add<PoisonedStab>(PileType.Discard);
                returned.DeckVersion=player.RunState.CreateCard<PoisonedStab>(player);
                await ActivateWith<DeadlyPoison>(Bond.Poison);
                var headbutt=await Add<Headbutt>();
                selector.Target=returned;
                await Play(headbutt);
                selector.Target=null;
                AidCheck(returned.Pile?.Type==PileType.Draw&&terminal.RevisionHasAttachments(headbutt)&&terminal.Ledger.Get(Bond.Poison)==0,
                    $"Headbutt marks the returned card without giving it early layers (pile={returned.Pile?.Type}, layers={terminal.Ledger.Get(Bond.Poison)})");
                await CardPileCmd.Add(returned,PileType.Hand);
                await Play(returned);
                AidCheck(terminal.Ledger.Get(Bond.Poison)==6,"Headbutt adds two layers to the returned card's first play");
                await CardPileCmd.Add(returned,PileType.Hand);
                await Play(returned);
                AidCheck(terminal.Ledger.Get(Bond.Poison)==10,"Headbutt's attachment is used just once");

                await Clear();
                await ActivateWith<DeadlyPoison>(Bond.Poison);
                var enemy=player.Creature.CombatState!.HittableEnemies.First();
                await Play(await Add<BubbleBubble>());
                AidCheck(!enemy.HasPower<PoisonPower>()&&terminal.Ledger.Get(Bond.Poison)==6,
                    "Bubble Bubble grants its layers when no existing poison can be increased");
                if(failures.Count>0)throw new Exception(string.Join("\n",failures));
                Console.WriteLine("COVENANT_AID_MASTERY_COMPLETE");
            }
            if(focus=="ember-cleanup")
            {
                await Clear();
                await ActivateWith<FiendFire>(Bond.Ember,2);
                await ActivateWith<Skim>(Bond.Rotation);
                await ActivateWith<Slimed>(Bond.Status);
                var slimed=await Add<Slimed>();
                Check(!CardMembership.Of(slimed).Contains(Bond.Ember),"Slimed cannot count as Ember while it has native Exhaust");
                await Play(slimed);
                Check(terminal.Ledger.Get(Bond.Status)==4&&terminal.Ledger.Get(Bond.Rotation)==2&&terminal.Ledger.Get(Bond.Ember)==0,
                    "Slimed gives Residue and Rotation layers when all three covenants are active");

                await Clear();
                await ActivateWith<FiendFire>(Bond.Ember);
                await ActivateWith<WhiteNoise>(Bond.Creation);
                var soul=await Add<Soul>(upgraded:true);
                await CardCmd.Exhaust(context,soul);
                Check(terminal.Ledger.Get(Bond.Creation)==4&&terminal.Ledger.Get(Bond.Ember)==0,
                    "exhausting Soul+ gives four Creation layers");

                await Clear();
                await ActivateWith<FiendFire>(Bond.Ember);
                await ActivateWith<Bludgeon>(Bond.Heavy);
                await Play(await Add<TheEmperor>(upgraded:true));
                Check(terminal.Ledger.Get(Bond.Heavy)==5&&terminal.Ledger.Get(Bond.Ember)==0,
                    "The Emperor+ puts both former growth amounts into Heavy");
                Console.WriteLine("COVENANT_EMBER_CLEANUP_COMPLETE");
            }
            if(focus=="guilty")
            {
                for(var i=0;i<100&&player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++)
                    await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
                await GuiltyCounter(player,terminal,Clear,Check);
            }
            if(focus=="ascenders-bane")await AscendersBaneGrowth(player,terminal,Clear,Check);
            if(focus=="bad-luck")await BadLuckPercentage(player,terminal,Clear,Check);
            if(focus=="decay")await DecayTargets(player,terminal,Clear,Check);
            if(focus=="trait-trim")
            {
                for(var i=0;i<100&&player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++)
                    await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
                await DecayTargets(player,terminal,Clear,Check);
                await TrimmedTraits(player,terminal,Clear,Check);
            }
            if(focus=="divine-shot")await ReworkProbe.RunDivineShotChance(player);
            if(focus=="spectrum")
            {
                for(var i=0;i<100&&player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++)
                    await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
                await Clear();
                var colors=new CardModel[]{ModelDb.Card<DefendIronclad>(),ModelDb.Card<DefendSilent>(),ModelDb.Card<DefendDefect>(),
                    ModelDb.Card<DefendRegent>(),ModelDb.Card<DefendNecrobinder>(),ModelDb.Card<DefendFool>(),ModelDb.Card<PanicButton>()};
                async Task Acquire(CardModel model)
                    =>await CardPileCmd.Add(player.RunState.CreateCard(model,player),PileType.Deck,skipVisuals:true);

                await Acquire(colors[1]);
                await Acquire(colors[1]);
                await Acquire(colors[2]);
                await Acquire(colors[3]);
                Check(terminal.Counts().GetValueOrDefault(Bond.Spectrum)==4&&terminal.Tier(Bond.Spectrum)==0&&
                    CardMembership.CountsAfterAcquisition(player,colors[6]).GetValueOrDefault(Bond.Spectrum)==5,
                    "four distinct permanent colors are inactive and a colorless reward previews the fifth");
                await Acquire(colors[6]);
                Check(terminal.Tier(Bond.Spectrum)==1&&terminal.Ledger.Add(Bond.Spectrum,100)==0,
                    "five colors activate the non-stacking first tier");

                var bomb=await Add<TheBomb>();
                await Play(await Add<DefendIronclad>());
                Check(bomb.EnergyCost.GetWithModifiers(CostModifiers.Local)==1,"a colored play discounts a colorless card");
                terminal.BeginTurn();
                await Play(await Add<DefendIronclad>());
                Check(bomb.EnergyCost.GetWithModifiers(CostModifiers.Local)==1,
                    "the first color only triggers once in combat at tier I");
                await Play(bomb);
                Check(bomb.EnergyCost.GetWithModifiers(CostModifiers.Local)==2,"the native discount expires when the card is played");

                await Acquire(colors[4]);
                Check(terminal.Tier(Bond.Spectrum)==1,"six colors still use the first tier");
                await Acquire(colors[5]);
                Check(terminal.Counts().GetValueOrDefault(Bond.Spectrum)==7&&terminal.Tier(Bond.Spectrum)==2,
                    "all seven permanent colors activate tier II before the new deck cards enter the combat piles");
                var combat=player.Creature.CombatState!;
                foreach(var color in colors.Skip(1).Take(5).Append(colors[0]).Append(colors[6]))
                {
                    CardModel target=color is PanicButton?await Add<DefendIronclad>():await Add<TheBomb>();
                    var previous=target.EnergyCost.GetWithModifiers(CostModifiers.Local);
                    var card=combat.CreateCard(color,player);
                    await CardPileCmd.Add(card,PileType.Hand);
                    await Play(card);
                    Check(target.EnergyCost.GetWithModifiers(CostModifiers.Local)==previous-1,
                        "Spectrum discounts another color after "+color.Id.Entry);
                    await CardPileCmd.RemoveFromCombat([target]);
                }
                var status=player.Creature.Powers.OfType<CovenantEffectPower>().Single(power=>power.IconBond==Bond.Spectrum);
                Check(status.EffectText.Contains("猎手")&&!status.EffectText.Contains("战士")&&!status.EffectText.Contains("无色"),
                    "the new cycle marks Ironclad and colorless as used and still offers Silent");
                Console.WriteLine("COVENANT_SPECTRUM_COMPLETE");
            }
            if(focus=="fate-local")
            {
                for(var i=0;i<100&&player.PlayerCombatState?.Phase!=PlayerTurnPhase.Play;i++)
                    await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
                Check(player.PlayerCombatState?.Phase==PlayerTurnPhase.Play,"Fate audit starts after the opening draw");
                await Clear();
                var cards=terminal.FateCards;
                var roster=cards.Select(card=>card.Id).ToArray();
                Check(roster.Length is >=20 and <=30&&roster.Distinct().Count()==roster.Length,"Fate draws 20 to 30 distinct cards");
                var colorless=ModelDb.CardPool<ColorlessCardPool>();
                var pools=CovenantTerminal.SixPools.Append(colorless).ToHashSet();
                Check(cards.All(card=>pools.Contains(card.Pool)&&card.Rarity!=CardRarity.Basic&&card.Pool.AllCardIds.Contains(card.Id)),
                    "Fate is only assigned to non-Basic character and colorless pool cards");
                Check(cards.All(card=>
                {
                    if(CardMembership.CardBadges(card,null).Count>=5)return false;
                    if(!card.IsUpgradable)return true;
                    var upgraded=(CardModel)card.MutableClone();
                    upgraded.UpgradeInternal();
                    upgraded.FinalizeUpgradeInternal();
                    return CardMembership.CardBadges(upgraded,null).Count<5;
                }),"both printed card faces have fewer than five badges before Fate is assigned");
                var savedRoster=AccessTools.Field(typeof(CovenantTerminal),"_runRosters");
                savedRoster.SetValue(terminal,null);
                Check(terminal.FateCards.Select(card=>card.Id).SequenceEqual(roster),"Fate restores the same roster from the local run seed");
                terminal.AllCardPools=false;
                savedRoster.SetValue(terminal,null);
                Check(terminal.FateCards.Count is >=20 and <=30&&terminal.FateCards.All(card=>card.Pool==player.Character.CardPool||card.Pool==colorless),
                    "single-pool runs assign Fate only to available colors");
                terminal.AllCardPools=true;
                savedRoster.SetValue(terminal,null);

                var ui=NRun.Instance!.GlobalUi;
                var hud=ui.GetNode<Control>("CovenantRoot");
                var items=hud.GetNode<Control>("BondBar/Scroller/Items");
                async Task WaitUi()=>await tree.ToSignal(tree.CreateTimer(.45),SceneTreeTimer.SignalName.Timeout);
                await WaitUi();
                var fate=items.GetNode<Button>("Fate");
                Check(fate.GetNode<Control>("Badge").GetNode<Label>("BondName").GetThemeColor("font_color")==new Color("e5cf94"),
                    "Fate starts pale gold and is accessible before any Fate card has been taken");
                fate.EmitSignal(Control.SignalName.MouseEntered);
                Check(hud.GetNode<PanelContainer>("BondDetails").Visible,"hover still displays Fate effects");
                await OpenPage(fate,Bond.Fate);

                var tarotRng=terminal.CovenantCombatRandomState;
                var nextTarotRoll=terminal.RandomFor(CovenantRandom.FateTarot).NextInt(100000);
                terminal.CovenantCombatRandomState=tarotRng;
                Check(terminal.RandomFor(CovenantRandom.FateTarot).NextInt(100000)==nextTarotRoll,"Fate tarot rolls survive the RNG save round trip");
                var state=player.Creature.CombatState!;
                var source=terminal.FateCards.First(card=>card.Type==CardType.Attack&&card.TargetType==TargetType.AnyEnemy);
                Check(CardMembership.CountsAfterAcquisition(player,source).GetValueOrDefault(Bond.Fate)==1,"canonical reward previews count Fate");
                var support=terminal.FateCards.Where(card=>card.Id!=source.Id).Take(5).Prepend(source);
                foreach(var reference in support)await CardPileCmd.Add(state.CreateCard(reference,player),PileType.Exhaust);
                terminal.Ledger.Add(Bond.Guard,250);
                await WaitUi();
                var first=items.GetChild<Button>(0);
                Check(first.Name=="Fate"&&first.GetNode<Label>("Badge/BondName").GetThemeColor("font_color")==new Color("f5c75b"),
                    "activated Fate is gold and precedes higher-layer covenants on the HUD");
                await OpenPage(items.GetNode<Button>("Guard"),Bond.Guard);

                var revealed=AccessTools.Field(typeof(CovenantTerminal),"_revealedTarot");
                revealed.SetValue(terminal,ModelDb.Card<TheEmpress>());
                for(var i=0;i<3;i++)
                {
                    var member=state.CreateCard(source,player);
                    if(i==1)CardCmd.Upgrade(member);
                    await CardPileCmd.Add(member,PileType.Hand);
                    Check(CardMembership.Of(member).Contains(Bond.Fate),"a drawn instance and its upgrade keep the run's Fate membership");
                    Check(NCard.FindOnTable(member)!.Body.GetNode<Control>("CovenantIcons").GetChild<Control>(0).Name=="Fate",
                        "the displayed card always puts Fate first");
                    await Play(member);
                    if(i<2)Check(!player.PlayerCombatState!.Hand.Cards.OfType<TheEmpress>().Any(),"Fate waits for three plays");
                }
                var awarded=player.PlayerCombatState!.Hand.Cards.OfType<TheEmpress>().Single();
                Check(terminal.Scale(Bond.Fate).Layers==6&&awarded.Keywords.Contains(CardKeyword.Retain),
                    "three Fate plays grant six layers and award the previewed tarot with Retain");
                var drawn=await Add<DefendIronclad>(PileType.Draw);
                await Play(awarded);
                Check(drawn.Pile?.Type==PileType.Hand&&!CardMembership.Of(awarded).Contains(Bond.Fate),"the awarded tarot draws at II and does not fuel Fate itself");
                Console.WriteLine("COVENANT_FATE_LOCAL_COMPLETE");

                async Task OpenPage(Button badge,Bond selected)
                {
                    badge.EmitSignal(Button.SignalName.Pressed);
                    await WaitUi();
                    var page=ui.SubmenuStack.Stack.Peek() as NCardLibrary;
                    Check(ui.CapstoneContainer.InUse&&!hud.Visible&&page?.Name=="CovenantLibrary",
                        $"clicking {selected} opens the native covenant library above the HUD");
                    var filters=(GridContainer)page!.FindChild("Bonds",true,false)!;
                    Check(filters.GetNode<Button>(selected.ToString()).ButtonPressed&&
                        filters.GetChildren().OfType<Button>().Count(button=>button.ButtonPressed)==1,
                        $"{selected} is the only selected filter when the library opens");
                    var visible=page.GetNode<NCardLibraryGrid>("%CardGrid").VisibleCards.ToArray();
                    Check(selected==Bond.Fate
                        ? visible.Select(card=>card.Id).ToHashSet().SetEquals(roster)
                        : visible.Length>0&&visible.All(card=>CardMembership.ReferenceBonds(card).Contains(selected)),
                        $"the library shows the correct cards for {selected}");
                    var back=page.GetNode<NBackButton>("BackButton");
                    back.EmitSignal(NClickableControl.SignalName.Released,back);
                    await WaitUi();
                    Check(!ui.CapstoneContainer.InUse&&hud.Visible,"one native Back action returns to combat and restores the HUD");
                }
            }
            if(focus=="balance-cleanup")
            {
                await Clear();
                await ActivateWith<Bash>(Bond.Breach);
                foreach(var enemy in player.Creature.CombatState!.HittableEnemies)
                    await PowerCmd.Apply<ArtifactPower>(context,enemy,3,player.Creature,null);
                var enemyCount=player.Creature.CombatState.HittableEnemies.Count();
                await Play(await Add<Shockwave>(upgraded:true));
                Check(terminal.Ledger.Get(Bond.Breach)==4*enemyCount,"Shockwave+ counts enemies even when they block both debuffs");

                await Clear();
                await ActivateWith<Inflame>(Bond.Ritual);
                await Play(await Add<Synthesis>());
                Check(player.Creature.GetPower<CovenantSynthesisPower>()!=null,"Synthesis makes its pending contribution visible");
                await Play(await Add<DefendIronclad>());
                Check(player.Creature.GetPower<CovenantSynthesisPower>()!=null,"a skill does not consume Synthesis");
                await Play(await Add<Panache>());
                Check(terminal.Ledger.Get(Bond.Ritual)==2&&player.Creature.GetPower<CovenantSynthesisPower>()==null,
                    "Synthesis pays for the next zero-cost ability even when it was auto-played");

                await Clear();
                await ActivateWith<Bash>(Bond.Breach);
                await Play(await Add<Vicious>(upgraded:true));
                await Add<DefendIronclad>(PileType.Draw);
                await Play(await Add<Bash>());
                Check(terminal.Ledger.Get(Bond.Breach)==9&&terminal.Ledger.Get(Bond.Rotation)==0,
                    "Vicious+ grows Breach once when its draw resolves, without waiting for the drawn card to play");
                Console.WriteLine("COVENANT_BALANCE_CLEANUP_COMPLETE");
            }
            if(focus=="strike-growth")
            {
                await Clear();
                await Play(await Add<Shiv>());
                Check(terminal.Ledger.Get(Bond.Blades)==0,"Shiv still requires Blades to be active");
                await ActivateWith<Shiv>(Bond.Blades);
                await Play(await Add<Shiv>());
                await Play(await Add<Shiv>(upgraded:true));
                Check(terminal.Ledger.Get(Bond.Blades)==2,"ordinary and upgraded Shivs grant one each, even without a kill");

                await Clear();
                await Add<Hellraiser>(PileType.Exhaust);
                var leading=await Add<LeadingStrike>();
                await ActivateWith<TwinStrike>(Bond.Strike);
                await Play(leading);
                var shivs=player.PlayerCombatState!.Hand.Cards.OfType<Shiv>().ToArray();
                await CardPileCmd.RemoveFromCombat(shivs[1]);
                await ActivateWith<Shiv>(Bond.Blades);
                Check(terminal.Counts().GetValueOrDefault(Bond.Blades)==BondCatalog.Get(Bond.Blades).Basic&&terminal.Tier(Bond.Strike)>0,
                    "Blades is exactly at the activation threshold and Leading Strike still counts as Strike");
                await Play(shivs[0]);
                Check(terminal.Ledger.Get(Bond.Blades)==5&&terminal.Ledger.Get(Bond.Strike)==0,
                    "Leading Strike's next Shiv grants four Blades layers plus the Shiv's own layer");
                await Play(await Add<Shiv>());
                Check(terminal.Ledger.Get(Bond.Blades)==6,"Leading Strike pays once, including across multiple Shivs");

                await Clear();
                await Add<Hellraiser>(PileType.Exhaust);
                var solar=await Add<SolarStrike>(upgraded:true);
                await ActivateWith<SolarStrike>(Bond.Stars);
                await ActivateWith<TwinStrike>(Bond.Strike);
                await PlayerCmd.SetStars(0,player);
                await PlayerCmd.SetEnergy(5,player);
                await Play(solar);
                Check(terminal.Ledger.Get(Bond.Stars)==0&&CardMembership.Of(solar).Contains(Bond.Strike),
                    "Solar Strike still counts as Strike and waits for payment");
                for(var i=0;i<2;i++)
                {
                    var payment=await Add<Anger>();payment.SetStarCostThisTurn(1);
                    await payment.SpendResources();
                    Check(terminal.Ledger.Get(Bond.Stars)==5&&terminal.Ledger.Get(Bond.Strike)==0,
                        "Solar Strike+ grants five Stars layers only on the first actual spending");
                }

                await Clear();
                await Add<Hellraiser>(PileType.Exhaust);
                var pommel=await Add<PommelStrike>(upgraded:true);
                await ActivateWith<Skim>(Bond.Rotation);
                await ActivateWith<TwinStrike>(Bond.Strike);
                await Add<DefendIronclad>(PileType.Draw);await Add<DefendIronclad>(PileType.Draw);
                await Play(pommel);
                Check(terminal.Ledger.Get(Bond.Rotation)==0,"no attack drawn means no Pommel Strike growth");
                await CardPileCmd.Add(pommel,PileType.Hand);
                await Add<Anger>(PileType.Draw);await Add<Anger>(PileType.Draw);
                await Play(pommel);
                Check(terminal.Ledger.Get(Bond.Rotation)==5&&terminal.Ledger.Get(Bond.Strike)==0,
                    "Pommel Strike+ grants Rotation once for its draw, even when it finds two attacks");

                await Clear();
                await Add<Hellraiser>(PileType.Exhaust);
                await ActivateWith<Claw>(Bond.Zero);
                var momentum=await Add<MomentumStrike>();
                await ActivateWith<TwinStrike>(Bond.Strike);
                await Play(momentum);
                Check(terminal.Ledger.Get(Bond.Zero)==0&&terminal.Ledger.Get(Bond.Strike)==0&&momentum.EnergyCost.GetAmountToSpend()==0,
                    "Momentum Strike's first paid-cost play makes it free without awarding growth");
                await CardPileCmd.Add(momentum,PileType.Hand);await Play(momentum);
                Check(terminal.Ledger.Get(Bond.Zero)==3&&terminal.Ledger.Get(Bond.Strike)==3,
                    "zero-cost Momentum Strike grants three layers to each active covenant");
                var upgradedMomentum=await Add<MomentumStrike>(upgraded:true);
                upgradedMomentum.EnergyCost.SetThisCombat(0);
                await Play(upgradedMomentum);
                Check(terminal.Ledger.Get(Bond.Zero)==7&&terminal.Ledger.Get(Bond.Strike)==7,
                    "zero-cost Momentum Strike+ grants four layers to each active covenant");

                await Clear();
                await Add<Hellraiser>(PileType.Exhaust);
                await ActivateWith<TwinStrike>(Bond.Strike);
                await ActivateWith<WhiteNoise>(Bond.Creation);
                await Add<DefendIronclad>(PileType.Draw);await Add<DefendIronclad>(PileType.Draw);
                await Play(await Add<MinionStrike>());
                Check(terminal.Ledger.Get(Bond.Creation)==1&&terminal.Ledger.Get(Bond.Strike)==1,
                    "Minion Strike grants one layer to both active covenants");
                await Play(await Add<MinionStrike>(upgraded:true));
                Check(terminal.Ledger.Get(Bond.Creation)==3&&terminal.Ledger.Get(Bond.Strike)==3,
                    "Minion Strike+ grants two layers to both active covenants");
                await Clear();
                Console.WriteLine("COVENANT_STRIKE_GROWTH_COMPLETE");
            }
            if(focus=="trait-activation")await ReworkProbe.RunActivation(player);
            var traitsOnly=System.Environment.GetEnvironmentVariable("COVENANT_REVISION_FOCUS")=="traits";
            if(traitsOnly)await TraitPowers(tree,player,terminal,Clear,Play,Check);
            if(System.Environment.GetEnvironmentVariable("COVENANT_REVISION_FOCUS")=="closing")
            {
                var closingFailures=new List<string>();
                await Closing(tree,player,terminal,Clear,Play,(passed,reason)=>{if(passed)Check(true,reason);else{closingFailures.Add(reason);Console.WriteLine("COVENANT_CLOSING_FAIL "+reason);}});
                if(closingFailures.Count>0)throw new Exception("Closing checks failed:\n"+string.Join('\n',closingFailures));
                File.WriteAllText(output+"/0.5/revision-closing-native.json",JsonSerializer.Serialize(new{status="pass",checks,stagedSha256,productionCatalogApplied=productionApplied,modSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CovenantTerminal).Assembly.Location)))},options));
                Console.WriteLine("COVENANT_CLOSING_COMPLETE checks="+checks.Count);return;
            }
            if(System.Environment.GetEnvironmentVariable("COVENANT_REVISION_FOCUS")=="resources")
            {
                await Resources(tree,player,terminal,Clear,Play,Check);
                File.WriteAllText(output+"/0.5/revision-resource-native.json",JsonSerializer.Serialize(new{status="pass",checks,stagedSha256,modSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CovenantTerminal).Assembly.Location)))},options));
                Console.WriteLine("COVENANT_RESOURCE_COMPLETE checks="+checks.Count);return;
            }
            if(System.Environment.GetEnvironmentVariable("COVENANT_REVISION_FOCUS")=="supplement")
            {
                await Supplement(tree,player,terminal,Clear,Play,Check);
                File.WriteAllText(output+"/0.5/revision-supplement-native.json",JsonSerializer.Serialize(new{status="pass",checks,stagedSha256,modSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CovenantTerminal).Assembly.Location)))},options));
                Console.WriteLine("COVENANT_SUPPLEMENT_COMPLETE checks="+checks.Count);return;
            }
            if(System.Environment.GetEnvironmentVariable("COVENANT_REVISION_FOCUS")=="mourning")
            {
                new Harmony("Covenant.RevisionDiagnostic").Patch(AccessTools.Method(typeof(CovenantTerminal),nameof(CovenantTerminal.FireRevision)),prefix:new HarmonyMethod(typeof(RevisionProbe),nameof(TraceTransformation)));
                await Clear();var focusedMourning=await Add<MourningBlow>();await Add<Joy>(PileType.Exhaust);
                Console.WriteLine("MOURNING_BEFORE tier="+terminal.Tier(Bond.Emotion));await Play(focusedMourning);
                Console.WriteLine("MOURNING_AFTER layers="+terminal.Ledger.Get(Bond.Emotion)+" tier="+terminal.Tier(Bond.Emotion)+" piles="+string.Join(',',player.PlayerCombatState!.AllCards.Select(c=>c.GetType().Name+":"+c.Pile?.Type)));
                return;
            }
            if(focus=="balance")
            {
                CombatProbe.VerifyStartingRelics();
                var encounterBefore=terminal.Counts(deck:true).GetValueOrDefault(Bond.Encounter);
                CardModel[] questResults=[player.RunState.CreateCard<Abundance>(player),player.RunState.CreateCard<ByrdSwoop>(player),player.RunState.CreateCard<GodByrd>(player)];
                var eventCard=player.RunState.CreateCard<LanternKey>(player);
                foreach(var card in questResults.Append(eventCard))await CardPileCmd.Add(card,PileType.Deck,skipVisuals:true);
                Check(questResults.All(card=>CardMembership.Of(card).Contains(Bond.Encounter))&&
                    !CardMembership.Of(ModelDb.Card<SovereignBlade>()).Contains(Bond.Encounter)&&
                    CardMembership.Of(ModelDb.Card<TheMoon>()).Contains(Bond.Encounter)&&
                    terminal.Counts(deck:true).GetValueOrDefault(Bond.Encounter)==encounterBefore+questResults.Length+1,
                    "Encounter counts permanent quest rewards and event cards, and excludes fixed combat results");
                foreach(var card in questResults.Append(eventCard))await CardPileCmd.RemoveFromDeck(card,showPreview:false);
                await Clear();
                var shot=await Add<FoolishShot>();var secondShot=await Add<QuickShot>();
                Check(ArrowCmd.GetAvailableArrowCount(player)==1,"two foreign Shoot cards share one native arrow on Ironclad");
                await Play(shot);
                Check(ArrowCmd.GetAvailableArrowCount(player)==0,"the shot occupies the arrow slot until retrieval");
                for(var i=0;i<3;i++)await Play(await Add<StrikeIronclad>());
                Check(ArrowCmd.GetAvailableArrowCount(player)==1,"three native follow-up hits retrieve the same arrow");
                await Play(secondShot);
                Check(ArrowCmd.GetAvailableArrowCount(player)==0,"the retrieved arrow can be fired again");
                await Play(await Add<Quiver>());await Play(await Add<Quiver>(upgraded:true));
                Check(player.PlayerCombatState!.Arrows.Count==4&&ArrowCmd.GetAvailableArrowCount(player)==3,
                    "Quiver and Quiver+ add one and two reusable slots respectively, independently of the stuck arrow");

                await Clear();await ActivateWith<WhiteNoise>(Bond.Creation);
                await Play(await Add<Anger>());
                Check(terminal.Ledger.Get(Bond.Creation)==1,"Anger grows immediately when it creates its own copy");
                await Play(await Add<Anger>());
                Check(terminal.Ledger.Get(Bond.Creation)==2,"base Anger adds one layer per successful play");
                for(var i=0;i<5;i++)await Add<Anger>(PileType.Discard);
                await Play(await Add<Anger>(upgraded:true));
                Check(terminal.Ledger.Get(Bond.Creation)==4,"Anger+ grants exactly two layers regardless of the copies in the discard pile");

                await Clear();
                var upgradedShiv=await Add<Shiv>(PileType.Exhaust,upgraded:true);upgradedShiv.AddKeyword(CardKeyword.Retain);
                var shivCounts=terminal.Counts().Where(entry=>entry.Key!=Bond.Solitude).ToArray();
                Check(CardMembership.IsGenerated(upgradedShiv)&&CardMembership.Of(upgradedShiv).SequenceEqual([Bond.Blades])&&
                    CardMembership.Of(ModelDb.Card<Shiv>()).SequenceEqual([Bond.Blades])&&
                    shivCounts.SequenceEqual([new KeyValuePair<Bond,int>(Bond.Blades,1)]),
                    "a generated upgraded Shiv with Retain and a catalog Shiv only count toward Blades");
                await ActivateWith<Shiv>(Bond.Blades);
                await Play(await Add<Shiv>());
                Check(terminal.Ledger.Get(Bond.Blades)==1,"nonlethal Shivs grant one layer");
                for(var i=0;i<2;i++)
                {
                    var enemy=player.Creature.CombatState!.HittableEnemies.First();
                    var spare=await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Axebot>(player.Creature.CombatState);
                    spare.SetMaxHpInternal(10000);spare.SetCurrentHpInternal(10000);enemy.SetCurrentHpInternal(1);
                    await CardCmd.AutoPlay(context,await Add<Shiv>(upgraded:i==1),enemy);
                    Check(terminal.Ledger.Get(Bond.Blades)==i+2,"a lethal Shiv also grants one layer at either upgrade stage");
                }

                Console.WriteLine("COVENANT_BALANCE_COMPLETE checks="+checks.Count);
            }
            if(focus is "renewed-bonds" or "adjusted-bonds" or "shipping-bonds" or "solitude" or "new-bonds" or "stars" or "curses")
            {
                for(var i=0;i<100&&player.PlayerCombatState!.Phase!=PlayerTurnPhase.Play;i++)
                    await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
                Check(player.PlayerCombatState!.Phase==PlayerTurnPhase.Play,"native opening turn has finished setting up");
                if(focus=="curses")await Curses(player,terminal,Clear,Check);
                else if(focus=="stars")await Stars(player,terminal,Clear,Play,Check);
                else if(focus=="new-bonds")await NewBonds(player,terminal,selector,Clear,Check);
                else if(focus=="solitude")await Solitude(player,terminal,Clear,Check);
                else if(focus=="shipping-bonds")await ShippingBonds(player,terminal,selector,Clear,Check);
                else if(focus=="adjusted-bonds")
                {
                    var failures=new List<string>();
                    await AdjustedBonds(player,terminal,Clear,(passed,reason)=>
                    {
                        if(passed)Check(true,reason);
                        else {failures.Add(reason);Console.WriteLine("COVENANT_ADJUSTED_BONDS_FAIL "+reason);}
                    });
                    if(failures.Count>0)throw new Exception(string.Join('\n',failures));
                    Console.WriteLine("COVENANT_ADJUSTED_BONDS_COMPLETE");
                }
                else await RenewedBonds(player,terminal,Clear,Check);
            }
            var descriptionsOnly=traitsOnly||stagedDescriptions||focus is "descriptions" or "balance-cleanup" or "fate-local" or "spectrum" or "ember-cleanup" or "aid-mastery" or "count-rebalance" or "reflow" or "activation-audit" or "trait-activation" or "trait-trim" or "balance" or "renewed-bonds" or "adjusted-bonds" or "shipping-bonds" or "solitude" or "new-bonds" or "stars" or "curses";
            if(!descriptionsOnly)
            {
            await Clear();var poison=await Add<DeadlyPoison>();await Play(poison);
            Check(terminal.Ledger.Get(Bond.Poison)==0,"inactive target covenant receives no growth");
            await Clear();poison=await Add<DeadlyPoison>();
            await ActivateWith<DeadlyPoison>(Bond.Poison);
            await Play(poison);Check(terminal.Ledger.Get(Bond.Poison)==4,"Deadly Poison can grow Poison on a fresh target");
            await CardPileCmd.Add(poison,PileType.Hand);await Play(poison);
            Check(terminal.Ledger.Permanent(Bond.Poison)==8,"the second native Deadly Poison adds the same fixed growth");
            await Clear();var inflame=await Add<Inflame>();
            await ActivateWith<Inflame>(Bond.Fury);
            await Play(inflame);
            Check(terminal.Tier(Bond.Fury)==0&&terminal.Ledger.Permanent(Bond.Fury)==6,"a power's own growth uses the play activation snapshot before leaving the piles");
            await Clear();var trance=await Add<BattleTrance>();
            for(var i=0;i<6;i++)await Add<Anger>(PileType.Exhaust);
            await Add<DefendIronclad>(PileType.Draw);await Add<Anger>(PileType.Draw);await Add<Anger>(PileType.Draw);
            await Play(trance);
            Check(terminal.Ledger.Permanent(Bond.Zero)==4,"Battle Trance counts only zero-cost cards drawn by its native effect");
            await Clear();var backflip=await Add<Backflip>();
            await Add<DefendIronclad>(PileType.Draw);await Add<DefendIronclad>(PileType.Draw);
            await ActivateWith<Backflip>(Bond.Guard);await Play(backflip);
            Check(terminal.Ledger.Permanent(Bond.Guard)==4,"Backflip attributes its two native skill draws without counting covenant draws");
            await Clear();var bash=await Add<Bash>();await ActivateWith<Bash>(Bond.Breach);
            await Play(bash);Check(terminal.Ledger.Permanent(Bond.Breach)==4,"Bash adds its Breach growth without requiring pre-existing Vulnerable");
            await CardPileCmd.Add(bash,PileType.Hand);await Play(bash);
            Check(terminal.Ledger.Permanent(Bond.Breach)==8,"Bash does not receive extra growth from pre-existing Vulnerable");
            await Clear();var form=await Add<DemonForm>();var originalCost=form.EnergyCost.GetAmountToSpend();
            await ActivateWith<DemonForm>(Bond.Fury);
            await Play(await Add<Bloodletting>());
            Check(form.EnergyCost.GetAmountToSpend()==originalCost-1,"Demon Form discounts itself only as a result of actual self-inflicted HP loss");
            Check(RevisionTrace.Current==null,"source attribution is restored after awaited native plays and callbacks");
            await Clear();var impervious=await Add<Impervious>();var blockBefore=player.Creature.Block;
            await Play(impervious);
            Check(player.Creature.Block==blockBefore+impervious.DynamicVars.Block.BaseValue+3,"an independent passive Block trait works while its covenant is inactive");
            terminal.Ledger.Add(Bond.Guard,100);
            Check(Presentation.TraitText(impervious)=="此牌额外获得5点格挡。"&&Presentation.TraitText(impervious,true)=="此牌额外获得6点格挡。","passive descriptions resolve current and upgraded values without exposing scaling formulas");
            Check(Presentation.TraitText(ModelDb.Card<Impervious>())=="此牌额外获得3点格挡。","canonical card previews do not require an owner");
            await Clear();var bludgeon=await Add<Bludgeon>();terminal.Ledger.Add(Bond.Heavy,200);
            var damageBefore=player.Creature.CombatState!.HittableEnemies.First().CurrentHp;
            await Play(bludgeon);
            Check(damageBefore-player.Creature.CombatState.HittableEnemies.First().CurrentHp==bludgeon.DynamicVars.Damage.BaseValue+12,"native damage includes the revised per-card coefficient and current layer scaling");
            await Clear();var noise=await Add<WhiteNoise>();for(var i=0;i<6;i++)await Add<Soul>(PileType.Exhaust);
            var beforeGeneration=player.PlayerCombatState!.AllCards.ToHashSet();await Play(noise);
            var created=player.PlayerCombatState.AllCards.Single(c=>!beforeGeneration.Contains(c)&&c.Type==CardType.Power);
            Check(terminal.Ledger.Permanent(Bond.Creation)==0,"White Noise does not pay its lineage trait before the generated card is played");
            Check(terminal.EffectiveTraits().Any(e=>e.Source==noise.Title),"a live generated-card attachment appears as effective support");
            if(created.Pile?.Type!=PileType.Hand)await CardPileCmd.Add(created,PileType.Hand);
            await PowerCmd.Apply<SignalBoostPower>(context,player.Creature,1,player.Creature,null);
            await Play(created);
            Check(terminal.Ledger.Permanent(Bond.Creation)==12,"each native Signal Boost replay of the White Noise result pays its origin's permanent lineage growth");
            var unrelated=await Add<Inflame>();await Play(unrelated);
            Check(terminal.Ledger.Permanent(Bond.Creation)==12,"an unrelated generated-looking card cannot trigger another card's lineage trait");
            Check(CombatManager.Instance.History.CardPlaysFinished.Count(e=>e.CardPlay.Card==created)==2,"lineage growth follows two actual native plays without an invented per-card cap");
            await Clear();var fusion=await Add<Fusion>();await Play(fusion);
            Check(terminal.Ledger.Permanent(Bond.Resonance)==0,"an inactive first-play covenant does not award Fusion growth");
            await CardPileCmd.Add(fusion,PileType.Hand);await ActivateWith<BallLightning>(Bond.Resonance);await Play(fusion);
            Check(terminal.Ledger.Permanent(Bond.Resonance)==0,"activating later does not turn Fusion's second play into its first play");
            var anotherFusion=await Add<Fusion>();await Play(anotherFusion);
            Check(terminal.Ledger.Permanent(Bond.Resonance)==4,"a different physical Fusion retains its own first-play allowance");
            await Clear();var afterimage=await Add<Afterimage>();
            await Play(afterimage);
            await ActivateWith<Backflip>(Bond.Guard);
            for(var i=0;i<5;i++)await Play(await Add<Anger>());
            Check(terminal.Ledger.Permanent(Bond.Guard)==1,"Afterimage's native Block accumulates across five actual callbacks");
            for(var i=0;i<5;i++)await Play(await Add<Anger>());
            Check(terminal.Ledger.Permanent(Bond.Guard)==2,"Afterimage repeats its threshold reward without a total turn cap");
            await Clear();var flick=await Add<FlickFlack>();await ActivateWith<FlickFlack>(Bond.Sleight);
            await Play(flick);Check(terminal.Ledger.Permanent(Bond.Sleight)==0,"ordinary autoplay of a Sly card does not count as Sly discard autoplay");
            await CardPileCmd.Add(flick,PileType.Hand);
            await CardCmd.AutoPlay(context,flick,player.Creature.CombatState!.HittableEnemies.First(),AutoPlayType.SlyDiscard);
            Check(terminal.Ledger.Permanent(Bond.Sleight)==6,"actual native Sly discard autoplay triggers the declared permanent growth");
            await Clear();var begone=await Add<Begone>();for(var i=0;i<6;i++)await Add<Soul>(PileType.Exhaust);await Add<DefendIronclad>();
            await Play(begone);var minion=player.PlayerCombatState!.Hand.Cards.Single(c=>c is MinionStrike);
            Check(terminal.Ledger.Permanent(Bond.Creation)==2,"Begone immediately grows the transformed card's original covenants");
            await Play(minion);
            Check(terminal.Ledger.Permanent(Bond.Creation)==3,"Minion Strike then contributes only its own Creation growth");
            await Clear();var mourning=await Add<MourningBlow>();await ActivateWith<Joy>(Bond.Emotion);
            await Play(mourning);
            Check(terminal.Ledger.Permanent(Bond.Emotion)==4,"Mourning Blow awards its own growth after the native transformation to Sadness");
            await Clear();var cho=await Add<WahCho>();await Add<WahTouch>(PileType.Exhaust);await Add<WahTouch>(PileType.Exhaust);
            await Play(cho);Check(terminal.Ledger.Permanent(Bond.Melody)==0,"Wah Cho cannot satisfy its pre-existing melody condition with itself");
            await CardPileCmd.Add(cho,PileType.Hand);await Play(cho);
            Check(terminal.Ledger.Permanent(Bond.Melody)==4,"a later Wah Cho recognizes the melody completed by its prior native play");
            await Clear();var equilibrium=await Add<Equilibrium>();await Add<DefendIronclad>();await Add<DefendIronclad>();
            for(var i=0;i<4;i++)await Add<Equilibrium>(PileType.Exhaust);
            await Play(equilibrium);
            Check(terminal.Ledger.Permanent(Bond.Retain)==4,"native Retain Hand support records the two hand cards it retains");
            await Clear();terminal.Ledger.Add(Bond.Zero,200);var claw=await Add<Claw>();var otherClaw=await Add<Claw>(PileType.Exhaust);
            await Play(claw);
            Check(claw.DynamicVars.Damage.BaseValue==5&&otherClaw.DynamicVars.Damage.BaseValue==5,"Claw buffs all copies using its printed coefficient");
            Check(claw.DynamicVars["Increase"].BaseValue==2,"the return trait preserves Claw's native increase variable");
            await Clear();terminal.Ledger.Add(Bond.Zero,200);claw=await Add<Claw>(upgraded:true);otherClaw=await Add<Claw>(PileType.Exhaust);
            await Play(claw);
            Check(claw.DynamicVars.Damage.BaseValue==7&&otherClaw.DynamicVars.Damage.BaseValue==6,"upgraded Claw preserves its native upgrade value");
            await Clear();terminal.Ledger.Add(Bond.Blades,150);var accuracy=await Add<Accuracy>();await Play(accuracy);var shiv=await Add<Shiv>();
            var accuracyPower=player.Creature.GetPower<AccuracyPower>()!;var target=player.Creature.CombatState!.HittableEnemies.First();
            Check(accuracyPower.ModifyDamageAdditive(target,4,ValueProp.Move,player.Creature,shiv,null)==accuracyPower.Amount+1,"native Accuracy modifier adds the source card's revised coefficient");
            var nonShiv=await Add<DefendIronclad>(PileType.Exhaust);
            Check(accuracyPower.ModifyDamageAdditive(target,4,ValueProp.Move,player.Creature,nonShiv,null)==0,"Accuracy bonus preserves the native Shiv filter");
            await Clear();terminal.Ledger.Add(Bond.Ember,150);var pain=await Add<FeelNoPain>();await Play(pain);var waste=await Add<DefendIronclad>();
            blockBefore=player.Creature.Block;await CardCmd.Exhaust(context,waste);
            Check(player.Creature.Block-blockBefore==4,"native Feel No Pain callback includes its revised Block bonus while the covenant is inactive");
            await Clear();var signal=await Add<SignalBoost>();var upgradedSignal=await Add<SignalBoost>(upgraded:true);
            for(var i=0;i<6;i++)await Add<Inflame>(PileType.Exhaust);
            await Play(signal);await Play(upgradedSignal);
            await Play(await Add<Inflame>());
            Check(terminal.Ledger.Permanent(Bond.Ritual)==6,"the first real replay consumes only the first Signal Boost source's trait");
            Check(!RevisionTrace.HasLivePower(signal,programs[nameof(SignalBoost)].Base)&&RevisionTrace.HasLivePower(upgradedSignal,programs[nameof(SignalBoost)].Upgraded),"remaining replay charges retain the correct physical source for the support sidebar");
            await Play(await Add<Footwork>());
            Check(terminal.Ledger.Permanent(Bond.Ritual)==14,"the next real replay consumes the upgraded Signal Boost source's eight-layer trait");
            await Clear();var fan=await Add<FanOfKnives>();await Play(fan);var fanShiv=await Add<Shiv>();
            await ActivateWith<Shiv>(Bond.Blades);await Play(fanShiv);
            Check(terminal.Ledger.Permanent(Bond.Blades)==1,"a nonlethal Shiv hitting one enemy grants only its own growth");
            var secondEnemy=await CreatureCmd.Add<MegaCrit.Sts2.Core.Models.Monsters.Axebot>(player.Creature.CombatState!);
            secondEnemy.SetMaxHpInternal(10000);secondEnemy.SetCurrentHpInternal(10000);
            await Play(await Add<Shiv>());
            Check(terminal.Ledger.Permanent(Bond.Blades)==3,"Fan of Knives adds a layer when the Shiv hits both native enemies");
            await Play(await Add<Shiv>());
            Check(terminal.Ledger.Permanent(Bond.Blades)==5,"Fan of Knives repeats its support without a shared cap");
            await Clear();var dance=await Add<BladeDance>();await Add<Shiv>(PileType.Exhaust);
            await Play(dance);var dancer=player.PlayerCombatState!.Hand.Cards.OfType<Shiv>().First();
            var bladeBefore=terminal.Ledger.Permanent(Bond.Blades);await Play(dancer);
            Check(terminal.Ledger.Permanent(Bond.Blades)-bladeBefore==3,"Blade Dance origin adds two layers on top of its Shiv's own layer");
            await CardPileCmd.Add(dancer,PileType.Hand);bladeBefore=terminal.Ledger.Permanent(Bond.Blades);await Play(dancer);
            Check(terminal.Ledger.Permanent(Bond.Blades)-bladeBefore==1,"a generated Shiv consumes its next-play origin bonus on the first play");
            await Clear();var headbutt=await Add<Headbutt>();var backed=await Add<DefendIronclad>(PileType.Discard);
            await ActivateWith<Backflip>(Bond.Guard);
            await Play(headbutt);Check(backed.Pile?.Type==PileType.Draw,"Headbutt returns the selected physical card to the native draw pile");
            await CardPileCmd.Add(backed,PileType.Hand);await Play(backed);
            Check(terminal.Ledger.Get(Bond.Guard)==2,"Headbutt grows the returned card's Guard covenant");
            await CardPileCmd.Add(backed,PileType.Hand);await Play(backed);
            Check(terminal.Ledger.Get(Bond.Guard)==2,"Headbutt support expires after the selected card's next real play");
            await Clear();var sneaky=await Add<Sneaky>();await Play(sneaky);var slyDiscard=await Add<FlickFlack>();
            blockBefore=player.Creature.Block;await CardCmd.Discard(context,slyDiscard);
            Check(player.Creature.Block-blockBefore==2,"Sneaky's independent Block triggers from a real Sly discard after its native autoplay");
            await Clear();var cruelty=await Add<Cruelty>();await Play(cruelty);var randomAttack=await Add<Bludgeon>();
            await Play(await Add<Bash>());
            Check(randomAttack.EnergyCost.GetWithModifiers(CostModifiers.All)==2,"Cruelty discounts the only remaining attack when native Bash applies Vulnerable");
            await Play(await Add<Bash>());
            Check(randomAttack.EnergyCost.GetWithModifiers(CostModifiers.All)==2,"Cruelty's authored first-per-turn condition prevents a second discount");
            await Clear();
            await Add<Bulwark>(PileType.Exhaust);await Add<Reflect>(PileType.Exhaust);await Add<ShadowShield>(PileType.Exhaust);
            await Add<UltimateDefend>(PileType.Exhaust);await Add<ExpectAFight>(PileType.Exhaust);
            Check(terminal.Tier(Bond.Tony)==1,"five cards activate Tony, including unupgraded cards with qualifying upgrades");
            var attrition=player.Creature.GetPower<CovenantAttritionPower>()!;var enemyTurns=attrition.EnemyTurns;
            async Task WaitFor(Func<bool> condition,string reason)
            {
                for(var i=0;i<600;i++)
                {if(condition())return;await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);}
                throw new Exception(reason);
            }
            async Task EndNativeTurn(int block)
            {
                await CreatureCmd.LoseBlock(context,player.Creature,player.Creature.Block,null);
                await CreatureCmd.GainBlock(player.Creature,block,ValueProp.Unpowered,null);
                await WaitFor(()=>player.PlayerCombatState!.Phase==PlayerTurnPhase.Play,"Tony probe did not reach play phase");
                var turn=player.PlayerCombatState!.TurnNumber;NCombatRoom.Instance!.Ui.EndTurnButton.CallReleaseLogic();
                await WaitFor(()=>player.PlayerCombatState.TurnNumber>turn&&player.PlayerCombatState.Phase==PlayerTurnPhase.Play,"Tony did not finish native turn transition");
            }
            for(var streak=1;streak<=4;streak++)
            {
                await EndNativeTurn(16);
                Check(terminal.Ledger.Get(Bond.Tony)==streak,"Tony records native consecutive extra turn "+streak);
                Check(player.PlayerCombatState!.Energy==Math.Max(0,player.PlayerCombatState.MaxEnergy-streak),"Tony only reduces opening energy on extra turn "+streak);
                Check(attrition.EnemyTurns==enemyTurns,"Tony extra turn does not advance enemy-turn attrition "+streak);
            }
            await PlayerCmd.GainEnergy(2,player);
            Check(player.PlayerCombatState!.Energy==2,"Tony does not reduce energy gained later in the turn");
            await EndNativeTurn(15);
            Check(terminal.Ledger.Get(Bond.Tony)==0&&attrition.EnemyTurns==enemyTurns+1,"a non-16 block ending breaks the streak and permits the enemy turn");
            Check(player.PlayerCombatState.Energy==player.PlayerCombatState.MaxEnergy,"opening energy returns to normal after Tony streak ends");
            await Clear();var unmovable=await Add<Unmovable>();await ActivateWith<Backflip>(Bond.Guard);
            await Play(unmovable);var doubled=await Add<DefendIronclad>();
            var previewPower=player.Creature.GetPower<UnmovablePower>()!;
            for(var i=0;i<5;i++)previewPower.ModifyBlockMultiplicative(player.Creature,5,ValueProp.Move,doubled,null);
            Check(terminal.Ledger.Permanent(Bond.Guard)==0,"repeated native Block previews cannot trigger Unmovable's trait");
            await Play(doubled);
            Check(terminal.Ledger.Permanent(Bond.Guard)==1,"Unmovable counts only five Block actually added by its native multiplier");
            await EntropyGrowth();
            await Clear();var gaze=await Add<MonarchsGaze>();for(var i=0;i<3;i++)await Add<Bludgeon>(PileType.Exhaust);
            await Play(gaze);await Play(await Add<Anger>());
            Check(terminal.Ledger.Permanent(Bond.Heavy)==2,"Monarch's Gaze follows its native child power that actually removes Strength");
            await Play(await Add<Anger>());
            Check(terminal.Ledger.Permanent(Bond.Heavy)==2,"Monarch's Gaze rewards only its first Strength reduction in the turn");
            await Clear();var abundance=player.RunState.CreateCard<Abundance>(player);
            await CardPileCmd.Add(abundance,PileType.Deck,skipVisuals:true);
            Check(terminal.Ledger.Permanent(Bond.Creation)==12,"Abundance grows Creation when acquired");
            await CardPileCmd.RemoveFromDeck(abundance,showPreview:false);
            await Clear();var gem=await Add<HiddenGem>();var gemTarget=await Add<Anger>(PileType.Draw);
            await ActivateWith<HiddenGem>(Bond.Profit);
            await Play(gem);Check(gemTarget.BaseReplayCount==2,"Hidden Gem adds its native two replays to the actual draw-pile target");
            await CardPileCmd.Add(gemTarget,PileType.Hand);await Play(gemTarget);
            Check(terminal.Ledger.Permanent(Bond.Profit)==6,"Hidden Gem grants its trait only for the target's first real replay");
            await CardPileCmd.Add(gemTarget,PileType.Hand);await Play(gemTarget);
            Check(terminal.Ledger.Permanent(Bond.Profit)==6,"Hidden Gem's consumed first-replay bonus cannot trigger on a later series");
            await Clear();var transfigure=await Add<Transfigure>();var transfigured=await Add<DefendIronclad>();
            await ActivateWith<Backstab>(Bond.Ember);
            await Play(transfigure);await Play(transfigured);
            Check(terminal.Ledger.Permanent(Bond.Ember)==2,"Transfigure's target gains permanent layers only on its real extra play");
            await CardPileCmd.Add(transfigured,PileType.Hand);await Play(transfigured);
            Check(terminal.Ledger.Permanent(Bond.Ember)==4,"Transfigure continues to reward later replay series without a battle cap");
            await Resources(tree,player,terminal,Clear,Play,Check);
            await Supplement(tree,player,terminal,Clear,Play,Check);
            }
            var descriptionRows=new List<object>();
            var referenceRows=new List<object>();
            var referencePool=CovenantTerminal.SixPools.Append(ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>())
                .SelectMany(pool=>pool.AllCards).Select(card=>card.Id).ToHashSet();
            var expressionFailures=new List<string>();
            var nativeTypes=typeof(CardModel).Assembly.GetTypes().Where(t=>programs.ContainsKey(t.Name)).ToDictionary(t=>t.Name);
            foreach(var definition in BondCatalog.All.Where(d=>d.UsesLayers))terminal.Ledger.Add(definition.Id,500);
            static void CheckStarTerminology(string text,string source)
            {
                if(text.Contains("蓝星",StringComparison.Ordinal)||text.Contains("藍星",StringComparison.Ordinal)||
                    text.Contains("[S]",StringComparison.Ordinal)||text.Contains("{starIcon}",StringComparison.Ordinal))
                    throw new Exception("Outdated or unrendered star terminology in "+source+": "+text);
            }
            foreach(var definition in BondCatalog.All)
            {
                var scale=terminal.Scale(definition.Id);
                var descriptions=new[]{BondCatalog.Intro(definition.Id),definition.BasicText(scale),definition.AdvancedText(scale)}
                    .Concat(BondCatalog.Milestones(definition.Id,scale).Select(milestone=>milestone.Text));
                foreach(var description in descriptions.Where(text=>text.Length>0))
                {
                    CheckStarTerminology(BondDescription.Format(description,terminal),definition.Name);
                    CheckStarTerminology(BondDescription.Format(description,terminal,showFormulas:true),definition.Name+" 图鉴");
                }
            }
            foreach(var (name,revision) in programs)
            {
                var canonical=ModelDb.GetById<CardModel>(ModelDb.GetId(nativeTypes[name]));
                var card=canonical.ToMutable();card.Owner=player;
                string Render(bool preview)
                {
                    var traitText=Presentation.TraitText(card,preview,annotateScaling:true);
                    var text=BondDescription.Format(traitText,card,splitSentences:false);
                    CheckStarTerminology(text,name);
                    CheckStarTerminology(BondDescription.Format(traitText,card,showFormulas:true,splitSentences:false),name+" 图鉴");
                    var plain=Regex.Replace(Regex.Replace(text,@"\[img[^\]]*\].*?\[/img\]",""),@"\[[^\]]+\]","");
                    if(plain.EndsWith('\n')||plain.EndsWith('\r'))throw new Exception("Trailing blank line in "+name);
                    if(CardMembership.IsMarker(card))
                    {
                        if(plain.Length!=0||CardMembership.Of(card).Count!=0)throw new Exception("Marker shows covenant data: "+name);
                        return plain;
                    }
                    if(string.IsNullOrWhiteSpace(plain)||Regex.IsMatch(plain,@"\bA\b|\{\d+\}")||text.Contains("energyPrefix")||text.Contains("临时增加"))throw new Exception("Unresolved revised description: "+name+" "+text);
                    return plain;
                }
                descriptionRows.Add(new{card=name,baseText=Render(false),upgradeText=Render(true)});
                if(productionApplied)
                {
                    var baseBonds=CardMembership.ReferenceBonds(canonical);
                    var upgradedBonds=baseBonds;
                    if(canonical.IsUpgradable)
                    {
                        var upgraded=(CardModel)canonical.MutableClone();
                        upgraded.UpgradeInternal();upgraded.FinalizeUpgradeInternal();
                        upgradedBonds=CardMembership.ReferenceBonds(upgraded);
                    }
                    if(CardMembership.Definition(canonical)?.HeavyExcluded==true&&(baseBonds.Contains(Bond.Heavy)||upgradedBonds.Contains(Bond.Heavy)))
                        throw new Exception("Excluded card counts toward Heavy: "+name);
                    if(canonical is Mayhem or Simpleton or Stampede&&(!baseBonds.Contains(Bond.Heavy)||!upgradedBonds.Contains(Bond.Heavy)))
                        throw new Exception("Heavy should count both levels of "+name);
                    if(canonical is Armaments or TrueGrit&&(!baseBonds.Contains(Bond.Foresight)||!upgradedBonds.Contains(Bond.Foresight)))
                        throw new Exception("Foresight should count both levels of "+name);
                    if(!CardMembership.IsMarker(canonical)&&
                        (revision.Base.Rules.Any(rule=>rule.Bond!=Bond.Mastery&&!baseBonds.Contains(rule.Bond))||
                        canonical.IsUpgradable&&revision.Upgraded.Rules.Any(rule=>rule.Bond!=Bond.Mastery&&!upgradedBonds.Contains(rule.Bond))))
                        throw new Exception("Card is missing its own covenant: "+name);
                    referenceRows.Add(new{card=name,baseBonds=baseBonds.Select(bond=>BondCatalog.Get(bond).Name).ToArray(),
                        upgradedBonds=upgradedBonds.Select(bond=>BondCatalog.Get(bond).Name).ToArray(),
                        inPool=referencePool.Contains(canonical.Id),multiplayerOnly=canonical.MultiplayerConstraint==CardMultiplayerConstraint.MultiplayerOnly,
                        countsForSolitude=!CardMembership.IsMarker(canonical)});
                }
                foreach(var rule in revision.Base.Rules.Concat(revision.Upgraded.Rules).Where(r=>r.Event=="variable"))
                    if(rule.Parameter==null||!card.DynamicVars.ContainsKey(rule.Parameter))throw new Exception("Missing native coefficient variable "+name+"/"+rule.Parameter);
                foreach(var rule in revision.Base.Rules.Concat(revision.Upgraded.Rules))
                {
                    var evaluation=new RevisionFrame(card,rule.Event);evaluation.Values["sum_before"]=0;evaluation.Values["sum_after"]=0;evaluation.Values["x_value"]=0;evaluation.Values["attachment_turn"]=0;
                    foreach(Match metric in Regex.Matches(rule.Condition+" "+rule.Amount+" "+rule.Accumulate,@"\bbefore_power_var_\w+"))evaluation.Before[metric.Value[7..]]=0;
                    decimal Resolve(string key)=>key switch{"A"=>10,"L"=>500,"source_bonus"=>0,_=>evaluation.Resolve(key)};
                    try{_ =RuleExpression.Evaluate(rule.Condition,Resolve);_ =RuleExpression.Evaluate(rule.Amount,Resolve);if(rule.Accumulate!=null)_ =RuleExpression.Evaluate(rule.Accumulate,Resolve);}
                    catch(Exception exception){expressionFailures.Add(name+" "+rule.Event+" "+rule.Condition+" / "+rule.Amount+": "+exception.Message);}
                }
            }
            if(expressionFailures.Count>0)throw new Exception("Invalid revised expressions:\n"+string.Join('\n',expressionFailures));
            Check(descriptionRows.Count==programs.Count,"all staged card descriptions resolve at 500 layers without exposing formulas, including upgrade previews");
            Check(ModelDb.AllCards.Where(card=>card.GetType().Assembly==typeof(CardModel).Assembly)
                .Select(card=>card.GetType().Name).ToHashSet().SetEquals(programs.Keys),
                $"all {descriptionRows.Count} native cards have valid base and upgrade revision expressions");
            Check(true,"all rendered covenant and card trait descriptions use the native star icon or the current terminology");
            File.WriteAllText(output+"/0.5/revision-description-check.json",JsonSerializer.Serialize(new{status="pass",stagedSha256,cards=descriptionRows},new JsonSerializerOptions{WriteIndented=true}));
            if(productionApplied)File.WriteAllText(output+"/0.5/reference-memberships.json",JsonSerializer.Serialize(referenceRows));
            if(descriptionsOnly)
            {
                Console.WriteLine("COVENANT_DESCRIPTIONS_COMPLETE cards="+descriptionRows.Count);
                if(traitsOnly)Console.WriteLine("COVENANT_TRAITS_COMPLETE checks="+checks.Count);
                return;
            }
            File.WriteAllText(output+"/0.5/revision-native.json",JsonSerializer.Serialize(new{status="pass",completedUtc=DateTimeOffset.UtcNow,checks,
                stagedPrograms=programs.Count,stagedSha256,productionCatalogApplied=productionApplied,modSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CovenantTerminal).Assembly.Location)))},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("COVENANT_REVISION_COMPLETE checks="+checks.Count);
        }
        finally{foreach(var (name,definition) in originals)definitions[name]=definition;}
    }
    private static void TraceTransformation(CovenantTerminal __instance,RevisionFrame frame)
    {
        if(frame.Event!="transform")return;
        var snapshots=(Dictionary<CardModel,Dictionary<Bond,int>>)AccessTools.Field(typeof(CovenantTerminal),"_playCounts").GetValue(__instance)!;
        Console.WriteLine("TRANSFORM_DIAGNOSTIC source="+frame.Source.GetType().Name+" subject="+frame.Subject?.GetType().Name+" liveTier="+__instance.Tier(Bond.Emotion)+" snapshot="+snapshots.GetValueOrDefault(frame.Source)?.GetValueOrDefault(Bond.Emotion)+" condition="+frame.Resolve("transformed_into_Sadness"));
    }
}
