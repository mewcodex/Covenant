using Covenant.Core;
using Covenant.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Random;

internal static partial class RevisionProbe
{
    private static async Task ShippingBonds(Player player,CovenantTerminal terminal,Choices selector,Func<Task> clear,Action<bool,string> check)
    {
        var context=new ThrowingPlayerChoiceContext();
        var combat=player.Creature.CombatState!;
        var state=player.PlayerCombatState!;
        var enemy=combat.HittableEnemies.First();
        async Task<T> Add<T>(PileType pile=PileType.Hand,bool permanent=false) where T:CardModel
        {
            var card=combat.CreateCard<T>(player);
            if(permanent)card.DeckVersion=player.RunState.CreateCard<T>(player);
            await CardPileCmd.Add(card,pile,skipVisuals:pile!=PileType.Hand);
            return card;
        }
        async Task Activate<T>(Bond bond,bool permanent=false) where T:CardModel
        {
            for(var i=0;i<BondCatalog.Get(bond).Advanced;i++)await Add<T>(PileType.Exhaust,permanent);
            check(terminal.Tier(bond)==2,$"native setup activates {bond} II");
        }
        Task Play(CardModel card)=>CardCmd.AutoPlay(context,card,card.TargetType==TargetType.AnyEnemy?enemy:null,skipCardPileVisuals:true);
        void Rolls(params double[] results){_renewedBound=100;_renewedRolls=new(results);}

        var harmony=new Harmony("Covenant.RuntimeCheck.ShippingBonds");
        harmony.Patch(AccessTools.Method(typeof(Rng),nameof(Rng.NextInt),[typeof(int)]),prefix:new HarmonyMethod(typeof(RevisionProbe),nameof(OverrideRenewedRoll)));
        harmony.Patch(AccessTools.Method(typeof(Rng),nameof(Rng.NextDouble),[]),prefix:new HarmonyMethod(typeof(RevisionProbe),nameof(OverrideRenewedPercent)));
        _renewedRng=terminal.RandomFor(CovenantRandom.CombatRoll);
        try
        {
            await clear();await Activate<Inflame>(Bond.Ritual);
            terminal.Ledger.Add(Bond.Ritual,100);terminal.BeginTurn();
            var first=await Add<Inflame>();
            var second=await Add<Inflame>();
            var block=player.Creature.Block;
            await Play(first);
            check(player.Creature.Block==block+6&&second.EnergyCost.GetAmountToSpend()==1,
                "Ritual grants its Block now and saves the discount for next turn");
            terminal.BeginTurn();
            var third=await Add<Inflame>();
            check(second.EnergyCost.GetAmountToSpend()==0&&third.EnergyCost.GetAmountToSpend()==0,
                "Ritual discounts the first Power of the next turn");
            await Play(second);
            check(third.EnergyCost.GetAmountToSpend()==1,"playing the first Power consumes the Ritual discount");
            terminal.BeginTurn();
            check(third.EnergyCost.GetAmountToSpend()==0,"playing a Power renews the discount for the following turn");
            await terminal.AfterSideTurnEnd(context,CombatSide.Player,[player.Creature]);
            check(third.EnergyCost.GetAmountToSpend()==1,"an unused Ritual discount expires when that turn ends");

            await clear();await Activate<Inflame>(Bond.Ritual);
            terminal.Ledger.Add(Bond.Ritual,300);terminal.BeginTurn();
            var drawnPower=await Add<Inflame>(PileType.Draw);
            await terminal.BeforeHandDraw(player,context,combat);
            check(drawnPower.Pile?.Type==PileType.Hand,"Ritual at 300 puts a chosen opening Power into hand");

            await clear();await Activate<Inflame>(Bond.Ritual);
            terminal.Ledger.Add(Bond.Ritual,600);terminal.BeginTurn();await PlayerCmd.SetEnergy(0,player);
            var openingPower=await Add<Inflame>(PileType.Draw);
            await terminal.BeforeHandDraw(player,context,combat);
            check(openingPower.Pile==null&&player.Creature.GetPowerAmount<StrengthPower>()>0&&state.Energy==0,
                "Ritual at 600 plays the selected opening Power for free");

            await clear();await Activate<Inflame>(Bond.Ritual);
            terminal.Ledger.Add(Bond.Ritual,500);terminal.BeginTurn();
            Rolls(29,9);
            await terminal.AfterPlayerTurnStart(context,player);
            var powers=state.Hand.Cards.ToArray();
            check(powers.Length==2&&powers.All(card=>card.Type==CardType.Power&&card.Keywords.Contains(CardKeyword.Ethereal)&&
                card.EnergyCost.GetWithModifiers(CostModifiers.Local)==Math.Max(0,card.EnergyCost.GetWithModifiers(CostModifiers.None)-1))&&
                powers[1].Rarity==CardRarity.Rare,"both Ritual II rolls independently generate reduced-cost Ethereal Powers");
            terminal.BeginTurn();Rolls(99,0);
            await terminal.AfterPlayerTurnStart(context,player);
            var separateRare=state.Hand.Cards.Where(card=>!powers.Contains(card)).ToArray();
            check(separateRare is [{Rarity:CardRarity.Rare}],"Ritual can roll a rare even when the ordinary roll fails");

            await clear();await Activate<Peck>(Bond.Encounter,permanent:true);
            terminal.Ledger.Add(Bond.Encounter,100);
            var peck=await Add<Peck>(permanent:true);
            selector.Target=peck;
            var expectedBonus=terminal.ModifyDamageAdditive(enemy,5,MegaCrit.Sts2.Core.ValueProps.ValueProp.Move,player.Creature,peck,null);
            await Play(peck);
            check(state.DrawPile.Cards.FirstOrDefault()==peck&&peck.EnergyCost.GetAmountToSpend()==0&&
                terminal.ModifyDamageAdditive(enemy,5,MegaCrit.Sts2.Core.ValueProps.ValueProp.Move,player.Creature,peck,null)==expectedBonus,
                "Encounter II retains the topdeck and discount without adding damage");
            selector.Target=null;

            await clear();await Activate<Regret>(Bond.Curse);
            terminal.Ledger.Add(Bond.Curse,100);
            terminal.BeginTurn();block=player.Creature.Block;
            var hp=combat.HittableEnemies.Select(target=>(Target:target,Hp:target.CurrentHp)).ToArray();
            var firstCurse=await Add<Injury>(PileType.Draw);
            var secondCurse=await Add<Doubt>(PileType.Draw);
            await CardPileCmd.Draw(context,player);
            check(firstCurse.Pile?.Type==PileType.Hand&&secondCurse.Pile?.Type==PileType.Draw&&player.Creature.Block==block+3&&
                hp.All(entry=>entry.Target.CurrentHp==entry.Hp),"drawing a curse triggers only Curse I");
            await Play(firstCurse);
            check(secondCurse.Pile?.Type==PileType.Hand&&hp.All(entry=>entry.Target.CurrentHp==entry.Hp-4),
                "playing a curse draws and deals the scaled area damage");

            await clear();await Activate<Wound>(Bond.Status);
            terminal.BeginTurn();
            var drawnStatus=await Add<Wound>(PileType.Draw);
            terminal.Ledger.Add(Bond.Status,250);
            Rolls(9);
            var generated=await CardPileCmd.AddGeneratedCardToCombat(combat.CreateCard<Debris>(player),PileType.Hand,player);
            check(generated.cardAdded is Slimed&&player.Creature.GetPowerAmount<StrengthPower>()==1,
                "a generated playable Status becomes Slimed and grants one temporary Strength");
            Rolls(9);
            var inserted=await CardPileCmd.AddGeneratedCardToCombat(combat.CreateCard<Burn>(player),PileType.Discard,null);
            check(inserted.cardAdded is Dazed,"a monster insert replaces an unplayable Status with Dazed");
            Rolls(9);
            var directInsert=await CardPileCmd.Add(combat.CreateCard<Debris>(player),PileType.Discard);
            check(directInsert.cardAdded is Slimed,"direct combat-pile insertion also replaces a new Status");
            Rolls(9);
            var drawn=await CardPileCmd.Draw(context,player);
            check(drawn is Dazed&&state.Hand.Cards.Contains(drawn)&&drawnStatus.Pile==null&&player.Creature.GetPowerAmount<StrengthPower>()==2,
                "native draw returns the replacement card and updates temporary Strength");
            await CardCmd.Discard(context,generated.cardAdded);
            check(player.Creature.GetPowerAmount<StrengthPower>()==1,"leaving hand removes one temporary Strength");
            Rolls(0);
            var obsolete=await CardPileCmd.AddGeneratedCardToCombat(combat.CreateCard<DeprecatedCard>(player),PileType.Discard,null);
            check(obsolete.cardAdded is DeprecatedCard&&_renewedRolls!.Count==1,"deprecated cards bypass the Status milestone entirely");
            await player.Creature.GetPower<CovenantStatusStrengthPower>()!.AfterSideTurnEnd(context,CombatSide.Player,[player.Creature]);
            check(player.Creature.GetPowerAmount<StrengthPower>()==0,"temporary Status Strength expires at turn end");

            Console.WriteLine("COVENANT_SHIPPING_BONDS_COMPLETE");
        }
        finally
        {
            selector.Target=null;_renewedRng=null;_renewedRolls=null;harmony.UnpatchAll(harmony.Id);
        }
    }
}
