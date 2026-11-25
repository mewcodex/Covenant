using Covenant.Core;
using MegaCrit.Sts2.Core.Combat;
using Covenant.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.ValueProps;

internal static partial class RevisionProbe
{
    private static Rng? _renewedRng;
    private static Queue<double>? _renewedRolls;
    private static int _renewedBound;
    private static bool OverrideRenewedRoll(Rng __instance,int maxExclusive,ref int __result)
    {
        if(__instance!=_renewedRng||maxExclusive!=_renewedBound||_renewedRolls?.TryDequeue(out var roll)!=true)return true;
        __result=(int)roll;
        return false;
    }
    private static bool OverrideRenewedPercent(Rng __instance,ref double __result)
    {
        if(__instance!=_renewedRng||_renewedBound!=100||_renewedRolls?.TryDequeue(out var roll)!=true)return true;
        __result=roll/100;
        return false;
    }

    private static async Task LayerCurves(Player player,CovenantTerminal terminal,Func<Task> clear,Action<bool,string> check)
    {
        var context=new ThrowingPlayerChoiceContext();
        var combat=player.Creature.CombatState!;
        var state=player.PlayerCombatState!;
        async Task AddDistinct(Bond bond,int tier=2)
        {
            var definition=BondCatalog.Get(bond);
            var cards=ModelDb.AllCards.Where(card=>CardMembership.Of(card).Contains(bond))
                .DistinctBy(card=>card.Id).Take(tier==1?definition.Basic:definition.Advanced);
            foreach(var card in cards)
                await CardPileCmd.Add(combat.CreateCard(card,player),PileType.Exhaust,skipVisuals:true);
            check(terminal.Tier(bond)==tier,$"distinct native cards activate {bond} tier {tier}");
        }
        void Rolls(params double[] rolls)
        {
            _renewedRng=terminal.RandomFor(CovenantRandom.CombatRoll);
            _renewedBound=100;
            _renewedRolls=new(rolls);
        }
        var harmony=new Harmony("Covenant.RuntimeCheck.LayerCurves");
        harmony.Patch(AccessTools.Method(typeof(Rng),nameof(Rng.NextDouble),[]),prefix:new HarmonyMethod(typeof(RevisionProbe),nameof(OverrideRenewedPercent)));
        try
        {
            await clear();
            await AddDistinct(Bond.Heavy);
            async Task<T> CostCard<T>(int cost) where T:CardModel
            {
                var card=combat.CreateCard<T>(player);
                await CardPileCmd.Add(card,PileType.Hand,skipVisuals:true);
                card.EnergyCost.SetThisTurnOrUntilPlayed(cost);
                return card;
            }
            async Task PlaySkill(CardModel card)
            {
                var (energy,stars)=await card.SpendResources();
                await card.OnPlayWrapper(context,null,false,new ResourceInfo{EnergySpent=energy,EnergyValue=energy,StarsSpent=stars,StarValue=stars},true);
            }
            var discountedAttack=await CostCard<Bludgeon>(0);
            var discountedSkill=await CostCard<FlameBarrier>(0);
            var markedUpSkill=await CostCard<DefendIronclad>(2);
            var enemy=combat.HittableEnemies.First();
            decimal DamagePreview()=>terminal.ModifyDamageAdditive(enemy,1,ValueProp.Move,player.Creature,discountedAttack,null);
            decimal BlockPreview(CardModel card)=>terminal.ModifyBlockAdditive(player.Creature,1,ValueProp.Move,card,null);
            var damage=DamagePreview();
            var discountBlock=BlockPreview(discountedSkill);
            var markupBlock=BlockPreview(markedUpSkill);
            discountedAttack.EnergyCost.SetCustomBaseCost(1);
            discountedSkill.EnergyCost.SetCustomBaseCost(1);
            markedUpSkill.EnergyCost.SetCustomBaseCost(2);
            check(discountedAttack.EnergyCost.GetAmountToSpend()==0&&markedUpSkill.EnergyCost.GetAmountToSpend()==2&&
                damage-DamagePreview()==3&&discountBlock-BlockPreview(discountedSkill)==3&&
                BlockPreview(markedUpSkill)-markupBlock==3,
                "Heavy damage and block follow printed prices in card previews, despite temporary discounts and markups");
            discountedAttack.EnergyCost.SetCustomBaseCost(3);
            discountedSkill.EnergyCost.SetCustomBaseCost(2);
            markedUpSkill.EnergyCost.SetCustomBaseCost(1);
            state.Energy=3;
            await PlaySkill(markedUpSkill);
            check(state.Energy==1,"a printed one-energy skill marked up to two does not count for Heavy II");
            await PlaySkill(discountedSkill);
            check(state.Energy==1,"the first printed two-energy skill counts even when played free");
            await PlaySkill(await CostCard<FlameBarrier>(0));
            check(state.Energy==2,"two printed two-energy skills count for Heavy II even when both were free");

            await clear();
            await AddDistinct(Bond.Arrows,tier:1);
            foreach(var (layers,expected) in new[]{(149,0),(150,1),(299,1),(300,2)})
            {
                terminal.Ledger.Add(Bond.Arrows,layers-terminal.Scale(Bond.Arrows).Layers);
                var aim=player.Creature.GetPowerAmount<AimPower>();
                await PlaySkill(await CostCard<DefendIronclad>(0));
                check(player.Creature.GetPowerAmount<AimPower>()-aim==expected,
                    $"playing a skill with Arrows I grants {expected} Aim at {layers} layers");
            }
            var arrowMilestone=BondCatalog.Milestones(Bond.Arrows,new(150,BondCatalog.Get(Bond.Arrows).LayerUnit)).Single();
            check(arrowMilestone.Layers==150&&ScalingText.Render(arrowMilestone.Text,true,"[color=red]")
                .Contains("[color=red]150[/color]/150",StringComparison.Ordinal),
                "the Arrows tooltip shows the 150-layer milestone and its layer formula");

            await clear();
            await AddDistinct(Bond.Poison);
            await terminal.BeforeCombatStart();
            check(combat.HittableEnemies.Sum(enemy=>enemy.GetPowerAmount<PoisonPower>())==1,
                "Poison II applies an opening dose at zero layers");

            foreach(var (layers,ordinary,rare) in new[]{(0,2.99,.99),(1,3.04,1.01)})
            {
                await clear();
                await AddDistinct(Bond.Ritual);
                terminal.Ledger.Add(Bond.Ritual,layers);
                Rolls(ordinary,rare);
                await terminal.AfterPlayerTurnStart(context,player);
                check(_renewedRolls?.Count==0&&state.Hand.Cards.Count==2&&state.Hand.Cards.All(card=>card.Type==CardType.Power&&
                    card.Keywords.Contains(CardKeyword.Ethereal))&&state.Hand.Cards.Any(card=>card.Rarity==CardRarity.Rare),
                    $"Ritual applies both its initial and fractional probabilities at {layers} layers");
            }

            await clear();
            await AddDistinct(Bond.Creation);
            await CreatureCmd.LoseBlock(context,player.Creature,player.Creature.Block,null);
            terminal.Ledger.Add(Bond.Creation,250);
            player.Creature.SetCurrentHpInternal(30);
            for(var i=0;i<7;i++)
            {
                Rolls(99,99);
                await CardPileCmd.AddGeneratedCardToCombat(combat.CreateCard<Inflame>(player),PileType.Discard,player);
            }
            check(player.Creature.Block==14&&player.Creature.CurrentHp==37,
                "Creation gains block immediately at its 250-layer milestone and still heals on the seventh card");
            terminal.Ledger.Add(Bond.Creation,5);
            Rolls(2.01,20.1);
            var result=await CardPileCmd.AddGeneratedCardToCombat(combat.CreateCard<Inflame>(player),PileType.Hand,player);
            check(_renewedRolls?.Count==0&&result.cardAdded.Rarity==CardRarity.Ancient&&terminal.CreationFreeThisTurn(result.cardAdded),
                "Creation separately applies the 2.02% replacement and 20.2% free-play chances at 255 layers");

            var text=ScalingText.Render(BondCatalog.Get(Bond.Ritual).AdvancedText(new(1)),true,"[color=red]");
            check(text.Contains("[color=red]3.05%[/color] =(3+[color=red]1[/color]/20)%",StringComparison.Ordinal)&&
                text.Contains("[color=red]1.02%[/color]",StringComparison.Ordinal),
                "fractional probability tooltips color the result with its percent sign and display raw layers");
            _renewedRng=null;
            var rng=terminal.RandomFor(CovenantRandom.CombatRoll);
            var counter=rng.ToSerializable().counter;
            var snapshot=terminal.CovenantCombatRandomState;
            var fractionalRoll=rng.NextDouble();
            check(rng.ToSerializable().counter==counter+1,"a fractional roll consumes exactly one saved random value");
            terminal.CovenantCombatRandomState=snapshot;
            check(terminal.RandomFor(CovenantRandom.CombatRoll).NextDouble()==fractionalRoll,
                "the fractional roll replays identically after reloading its existing saved stream");
        }
        finally
        {
            _renewedRng=null;_renewedRolls=null;
            harmony.UnpatchAll(harmony.Id);
        }
    }

    private static async Task RenewedBonds(Player player,CovenantTerminal terminal,Func<Task> clear,Action<bool,string> check)
    {
        var context=new ThrowingPlayerChoiceContext();
        var combat=player.Creature.CombatState!;
        var state=player.PlayerCombatState!;
        async Task<T> Add<T>(PileType pile=PileType.Exhaust) where T:CardModel
        {
            var card=combat.CreateCard<T>(player);
            await CardPileCmd.Add(card,pile,skipVisuals:true);
            return card;
        }
        async Task Sources<T>(Bond bond,int tier=1) where T:CardModel
        {
            var definition=BondCatalog.Get(bond);
            for(var i=0;i<(tier==1?definition.Basic:definition.Advanced);i++)await Add<T>();
            check(terminal.Tier(bond)>=tier,$"native setup activates {bond} {tier}");
        }
        async Task PaidPlay(CardModel card)
        {
            var (energy,stars)=await card.SpendResources();
            var target=card.TargetType==TargetType.AnyEnemy?combat.HittableEnemies.First():null;
            await card.OnPlayWrapper(context,target,true,new ResourceInfo{EnergySpent=energy,EnergyValue=energy,StarsSpent=stars,StarValue=stars},true);
        }
        async Task<Shiv> AddPaidShiv(PileType pile)
        {
            var card=await Add<Shiv>(pile);
            card.EnergyCost.SetThisCombat(1);
            return card;
        }
        void Rolls(CovenantRandom stream,int bound,params int[] rolls)
        {
            _renewedRng=terminal.RandomFor(stream);_renewedBound=bound;_renewedRolls=new(rolls.Select(roll=>(double)roll));
        }
        var harmony=new Harmony("Covenant.RuntimeCheck.RenewedBonds");
        harmony.Patch(AccessTools.Method(typeof(Rng),nameof(Rng.NextInt),[typeof(int)]),prefix:new HarmonyMethod(typeof(RevisionProbe),nameof(OverrideRenewedRoll)));
        harmony.Patch(AccessTools.Method(typeof(Rng),nameof(Rng.NextDouble),[]),prefix:new HarmonyMethod(typeof(RevisionProbe),nameof(OverrideRenewedPercent)));
        try
        {
            await clear();await Sources<Bloodletting>(Bond.Blood,2);
            await CreatureCmd.LoseBlock(context,player.Creature,player.Creature.Block,null);
            terminal.Ledger.Add(Bond.Blood,250);
            player.Creature.SetCurrentHpInternal(30);await PlayerCmd.SetEnergy(2,player);
            var draw=await Add<DefendIronclad>(PileType.Draw);
            await terminal.BeforeCombatStart();
            terminal.BeginTurn();await terminal.AfterPlayerTurnStart(context,player);
            check(player.Creature.CurrentHp==29&&player.Creature.Block==9&&draw.Pile?.Type==PileType.Hand&&state.Energy==3,
                "Blood II loses one life and pays the I benefits and the 250-layer energy");
            await CreatureCmd.Damage(context,player.Creature,1m,ValueProp.Unpowered|ValueProp.Unblockable,player.Creature,null,null);
            check(player.Creature.Block==9&&state.Energy==3,$"Blood I does not repeat on a second effect loss in the same turn (Block {player.Creature.Block}, Energy {state.Energy})");

            await clear();await Sources<DeadlyPoison>(Bond.Poison,2);
            terminal.Ledger.Add(Bond.Poison,250);await terminal.BeforeCombatStart();
            var poisonedEnemies=combat.HittableEnemies.ToArray();
            var enemy=poisonedEnemies.First(target=>target.GetPowerAmount<PoisonPower>()>0);
            check(poisonedEnemies.Sum(target=>target.GetPowerAmount<PoisonPower>())==5,"Poison II applies five individual opening doses without re-amplifying them");
            await PowerCmd.Apply<PoisonPower>(context,enemy,1,player.Creature,null);
            check(enemy.GetPowerAmount<WeakPower>()==1,"Poison's milestone weakens the first already poisoned target");

            await clear();await Sources<Backstab>(Bond.Ember);
            terminal.Ledger.Add(Bond.Ember,500);await PlayerCmd.SetEnergy(6,player);
            for(var i=0;i<2;i++)
            {
                var card=await Add<Apotheosis>(PileType.Hand);
                await PaidPlay(card);
                check(state.Hand.Cards.Any(),"Ember returns an eligible exhausted card on each paid exhaust at 500 layers");
                foreach(var held in state.Hand.Cards.ToArray())await CardPileCmd.Add(held,PileType.Exhaust,skipVisuals:true);
            }

            await clear();await Sources<BladeDance>(Bond.Blades,2);
            await PlayerCmd.SetEnergy(12,player);
            for(var i=0;i<3;i++)await Add<DefendIronclad>(PileType.Draw);
            var discardedAttack=await AddPaidShiv(PileType.Discard);
            var exhaustedAttack=await AddPaidShiv(PileType.Exhaust);
            for(var i=0;i<4;i++)await PaidPlay(await AddPaidShiv(PileType.Hand));
            check(discardedAttack.Pile?.Type==PileType.Hand&&exhaustedAttack.Pile?.Type==PileType.Exhaust,
                "Blades II takes a generated attack from discard before exhaust");
            var secondAttack=await AddPaidShiv(PileType.Discard);
            for(var i=0;i<4;i++)await PaidPlay(await AddPaidShiv(PileType.Hand));
            check(secondAttack.Pile?.Type==PileType.Discard,"Blades II only retrieves on the first trigger each turn");

            await clear();await Sources<Acrobatics>(Bond.Sleight);
            await CreatureCmd.LoseBlock(context,player.Creature,player.Creature.Block,null);
            var toDiscard=await Add<StrikeIronclad>(PileType.Hand);
            var drawnThenDiscarded=await Add<DefendIronclad>(PileType.Draw);
            await CardCmd.Discard(context,toDiscard);
            check(drawnThenDiscarded.Pile?.Type==PileType.Discard&&player.Creature.Block==3,
                "Sleight I grants Block, draws and mandates a hand discard");

            foreach(var (layers,interval) in new[]{(0,3),(250,2)})
            {
                await clear();await Sources<Bloodletting>(Bond.Zero,2);
                terminal.Ledger.Add(Bond.Zero,layers);
                await PlayerCmd.SetEnergy(0,player);
                for(var i=0;i<2;i++)await Add<DefendIronclad>(PileType.Draw);
                var discountedDiscard=await Add<StrikeIronclad>(PileType.Discard);
                discountedDiscard.EnergyCost.SetThisTurnOrUntilPlayed(0);
                async Task PlayDiscounted(int count)
                {
                    for(var i=0;i<count;i++)
                    {
                        var discounted=await Add<DefendIronclad>(PileType.Hand);
                        discounted.EnergyCost.SetThisTurnOrUntilPlayed(0);
                        await PaidPlay(discounted);
                    }
                }
                await PlayDiscounted(1);
                check(discountedDiscard.Pile?.Type==PileType.Hand,"Zero I retrieves using the candidate's current energy cost");
                check(state.Energy==0&&state.DrawPile.Cards.Count==2,$"Zero II waits to trigger at {layers} layers");
                await PlayDiscounted(interval-1);
                check(state.Energy==1&&state.DrawPile.Cards.Count==1,$"the first Zero II trigger draws and grants energy at {layers} layers");
                await PlayDiscounted(interval);
                var damageBonus=(decimal)AccessTools.Field(typeof(CovenantTerminal),"_nextZero").GetValue(terminal)!;
                check(state.Energy==1&&state.DrawPile.Cards.Count==1&&damageBonus==2*(3+terminal.Scale(Bond.Zero).A),
                    $"a repeated Zero II trigger only adds the damage bonus at {layers} layers");
                terminal.BeginTurn();
                await PlayDiscounted(interval);
                check(state.Energy==2&&state.DrawPile.Cards.Count==0,$"Zero II draws and grants energy again on the next turn at {layers} layers");
            }

            await clear();await Sources<Constellation>(Bond.Stars,2);
            terminal.Ledger.Add(Bond.Stars,500);
            await PlayerCmd.SetEnergy(0,player);await PlayerCmd.SetStars(2,player);
            var paid=await Add<DefendIronclad>(PileType.Hand);
            check(paid.CanPlay(),"native affordability accepts two stars for one missing energy");
            var cost=await paid.SpendResources();
            check(cost==(0,2)&&state.Stars==0,"native spent resources record two stars for one missing energy");
            await terminal.AfterSideTurnEndLate(context,CombatSide.Player,[player.Creature]);
            terminal.BeginTurn();
            await terminal.AfterPlayerTurnStart(context,player);
            check(state.Stars==1,"Stars I refunds one on the next turn after two stars were spent");

            await clear();await Sources<Furnace>(Bond.Forge,2);
            var sword=await Add<SovereignBlade>(PileType.Hand);
            Rolls(CovenantRandom.CombatCard,7,0,2,6,5);
            await ForgeCmd.Forge(1,player,null);
            check(sword.Enchantment is Spiral&&sword.GetEnchantedReplayCount()==1,"Forge can apply the native Spiral replay to a sword");
            await ForgeCmd.Forge(1,player,null);
            check(sword.Enchantment is SlumberingEssence,"Forge replaces Spiral with a valid Slumbering Essence");
            await sword.Enchantment!.BeforeFlush(context,player);
            check(sword.EnergyCost.GetWithModifiers(CostModifiers.All)==1,"native Slumbering Essence discounts a sword before flush");
            await ForgeCmd.Forge(1,player,null);
            var momentum=sword.Enchantment!;
            await momentum.OnPlay(context,null);
            check(momentum is Momentum&&momentum.Amount==15&&momentum.EnchantDamageAdditive(10,ValueProp.Move)==15,"Momentum accumulates only in its own enchantment instance");
            await ForgeCmd.Forge(1,player,null);
            check(sword.Enchantment is Adroit {Amount:10}&&sword.Enchantment.EnchantDamageAdditive(10,ValueProp.Move)==0,"replacing Momentum removes its accumulated damage");

            await clear();await Sources<WhiteNoise>(Bond.Creation,2);
            terminal.BeginTurn();
            await CreatureCmd.LoseBlock(context,player.Creature,player.Creature.Block,null);
            terminal.Ledger.Add(Bond.Creation,350);player.Creature.SetCurrentHpInternal(30);
            await PlayerCmd.SetEnergy(0,player);
            Rolls(CovenantRandom.CombatRoll,100,0,0,99,0);
            var own=combat.CreateCard<Inflame>(player);
            var result=await CardPileCmd.AddGeneratedCardToCombat(own,PileType.Hand,player);
            check(result.cardAdded.Rarity==CardRarity.Ancient&&result.cardAdded.Keywords.Contains(CardKeyword.Ethereal)&&
                result.cardAdded.Keywords.Contains(CardKeyword.Exhaust)&&terminal.CreationFreeThisTurn(result.cardAdded),
                "Creation's independent rolls can both replace a card and make the replacement free");
            var free=await CardPileCmd.AddGeneratedCardToCombat(combat.CreateCard<Inflame>(player),PileType.Hand,player);
            var newCard=free.cardAdded;
            check(newCard is Inflame&&newCard.EnergyCost.GetAmountToSpend()==0&&state.Energy==1&&player.Creature.Block==12,
                "Creation pays first and second generation bonuses and the extra early Block");
            await PaidPlay(newCard);
            check(!terminal.CreationFreeThisTurn(newCard)&&newCard.EnergyCost.GetWithModifiers(CostModifiers.Local)==1,
                "the generated discount is consumed by the first actual play");
            for(var i=0;i<5;i++)
            {
                Rolls(CovenantRandom.CombatRoll,100,99,99);
                await CardPileCmd.AddGeneratedCardToCombat(combat.CreateCard<Inflame>(player),PileType.Discard,player);
            }
            check(player.Creature.CurrentHp==37&&player.Creature.Block==16,"Creation heals exactly on the seventh non-Status generation");
            terminal.BeginTurn();
            for(var i=0;i<7;i++)
            {
                Rolls(CovenantRandom.CombatRoll,100,99,99);
                await CardPileCmd.AddGeneratedCardToCombat(combat.CreateCard<Inflame>(player),PileType.Discard,player);
            }
            check(player.Creature.CurrentHp==37,"the Creation seventh-generation heal cannot repeat in the same battle");
            Rolls(CovenantRandom.CombatRoll,100,0,99);
            var forged=await ForgeCmd.Forge(2,player,null);
            check(!forged.Any()&&!state.AllCards.OfType<SovereignBlade>().Any()&&state.Hand.Cards.Count(card=>card.Rarity==CardRarity.Ancient)==2,
                "Forge completes when Creation replaces the new Sword with an Ancient card");
            Console.WriteLine("COVENANT_RENEWED_BONDS_COMPLETE");
        }
        finally
        {
            _renewedRng=null;_renewedRolls=null;
            harmony.UnpatchAll(harmony.Id);
        }
    }
}
