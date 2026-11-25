using Covenant.Core;
using Covenant.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.ValueProps;

internal static partial class RevisionProbe
{
    private static async Task AdjustedBonds(Player player,CovenantTerminal terminal,Func<Task> clear,Action<bool,string> check)
    {
        var context=new ThrowingPlayerChoiceContext();
        var combat=player.Creature.CombatState!;
        var state=player.PlayerCombatState!;
        var enemy=combat.HittableEnemies.First();
        async Task<T> Add<T>(PileType pile=PileType.Hand) where T:CardModel
        {
            var card=combat.CreateCard<T>(player);
            await CardPileCmd.Add(card,pile,skipVisuals:pile!=PileType.Hand);
            return card;
        }
        async Task Activate<T>(Bond bond) where T:CardModel
        {
            for(var i=0;i<BondCatalog.Get(bond).Advanced;i++)await Add<T>(PileType.Exhaust);
            check(terminal.Tier(bond)==2,$"native setup activates {bond} II");
        }
        Task Play(CardModel card)=>CardCmd.AutoPlay(context,card,card.TargetType==TargetType.AnyEnemy?enemy:null,skipCardPileVisuals:true);
        async Task WithRoll(CovenantRandom stream,int bound,int result,Func<Task> action)
        {
            var harmony=new Harmony("Covenant.RuntimeCheck.AdjustedBonds");
            harmony.Patch(AccessTools.Method(typeof(Rng),nameof(Rng.NextInt),[typeof(int)]),prefix:new HarmonyMethod(typeof(RevisionProbe),nameof(OverrideRenewedRoll)));
            _renewedRng=terminal.RandomFor(stream);_renewedBound=bound;_renewedRolls=new([result]);
            try{await action();}
            finally{_renewedRng=null;_renewedRolls=null;harmony.UnpatchAll(harmony.Id);}
        }

        await clear();await Add<DeathsDoor>(PileType.Exhaust);
        check(terminal.Tier(Bond.Doom)==1&&BondCatalog.Get(Bond.Doom).Advanced==6,"Doom uses the 1/6 thresholds");
        var top=await Add<StrikeIronclad>();
        var drawn=await Add<DefendIronclad>(PileType.Draw);
        await PowerCmd.Apply<DoomPower>(context,enemy,1,player.Creature,null);
        check(state.DrawPile.Cards.FirstOrDefault()==top&&drawn.Pile?.Type==PileType.Hand&&enemy.GetPowerAmount<DoomPower>()==2,
            "Doom draws first and then puts the selected hand card on top");

        await clear();await Activate<CallOfTheVoid>(Bond.Ethereal);
        var ethereal=await Add<IronWave>();
        ethereal.AddKeyword(CardKeyword.Ethereal);
        await CardCmd.Exhaust(context,ethereal,causedByEthereal:true);
        check(ethereal.Pile?.Type==PileType.Exhaust,"Ethereal still exhausts normally with zero conversion chance");
        terminal.BeginTurn();
        await terminal.AfterPlayerTurnStart(context,player);
        check(ethereal.Pile?.Type==PileType.Hand,"Ethereal II returns an Ethereal card from exhaust");
        terminal.Ledger.Add(Bond.Ethereal,500);
        var hp=enemy.CurrentHp;
        var block=player.Creature.Block;
        await PlayerCmd.SetEnergy(0,player);
        await WithRoll(CovenantRandom.CombatRoll,100,29,()=>CardCmd.Exhaust(context,ethereal,causedByEthereal:true));
        var target=CombatManager.Instance.History.CardPlaysFinished.LastOrDefault()?.CardPlay.Target;
        check(ethereal.Pile?.Type==PileType.Discard&&state.Energy==0&&target!=null&&
            target.CurrentHp==hp-ethereal.DynamicVars.Damage.BaseValue-12&&player.Creature.Block==block+ethereal.DynamicVars.Block.BaseValue+12,
            $"Ethereal II autoplays for free with both I bonuses (pile {ethereal.Pile?.Type}, energy {state.Energy}, damage {hp-target?.CurrentHp}, block {player.Creature.Block-block})");

        await clear();await Activate<FoolishShot>(Bond.Arrows);
        while(ArrowCmd.GetAvailableArrowCount(player)<2)ArrowCmd.GainArrowSlot(player);
        await ArrowCmd.ShootArrowAtTarget(enemy,player,context,card:null);
        var impaled=enemy.GetPowerInstances<ImpaledPower>().Single(power=>power.Applier==player.Creature);
        impaled.HitCounter=2;
        var recalled=await Add<FoolishShot>(PileType.Discard);
        var shot=await Add<FoolishShot>();
        await Play(shot);
        check(recalled.Pile?.Type==PileType.Hand&&player.Creature.GetPowerAmount<AimPower>()==2,
            "another Shoot hits an impaled enemy, advances the extra count and randomly recalls a Shoot card");
        var nextRecall=await Add<FoolishShot>(PileType.Discard);
        await ArrowCmd.DislodgeArrow(enemy,player,context,shot);
        check(state.DiscardPile.Cards.Count(card=>card.Tags.Contains(CardTag.Shoot))==1&&
            player.Creature.GetPowerAmount<AimPower>()==2&&nextRecall.Owner==player,
            "another dislodge in the same turn recalls a card without granting the former Aim bonus");
        terminal.Ledger.Add(Bond.Arrows,250);
        await Play(await Add<DefendIronclad>());
        check(player.Creature.GetPowerAmount<AimPower>()==3,"Arrows milestone grants Aim even when playing a skill");

        await clear();await Activate<WahTouch>(Bond.Melody);
        terminal.Ledger.Add(Bond.Melody,250);
        var melody=await Add<WahChoKi>();
        var ordinary=await Add<DefendIronclad>(PileType.Draw);
        var extraDraw=await Add<DefendIronclad>(PileType.Draw);
        var plainAttack=await Add<StrikeIronclad>(PileType.Exhaust);
        check(terminal.ModifyDamageAdditive(enemy,5,ValueProp.Move,player.Creature,melody,null)-
            terminal.ModifyDamageAdditive(enemy,5,ValueProp.Move,player.Creature,plainAttack,null)==2,
            "Melody milestone adds damage to melody cards");
        var energy=state.Energy;
        await Play(melody);
        check(ordinary.Pile?.Type==PileType.Hand&&extraDraw.Pile?.Type==PileType.Hand&&state.Energy==energy+1,
            "completing three melodies draws on the second melody and gains energy on the third");
        await WithRoll(CovenantRandom.CombatCard,3,1,()=>
        {
            terminal.BeginTurn();
            check(terminal.HasInheritedMelody(CardTag.Cho),"the next turn rolls for its inherited melody");
            return Task.CompletedTask;
        });

        await clear();await Activate<Anguish>(Bond.Emotion);
        await Add<Sadness>();await Add<Sadness>();
        var history=CombatManager.Instance.History;
        var historyStart=history.Entries.Count();
        await terminal.BeforeSideTurnEndVeryEarly(context,CombatSide.Player,[player.Creature]);
        var joy=state.Hand.Cards.OfType<Joy>().SingleOrDefault();
        var sadnessCount=state.Hand.Cards.OfType<Sadness>().Count();
        var blockGains=history.Entries.Skip(historyStart).OfType<BlockGainedEntry>().Where(entry=>entry.Receiver==player.Creature).Select(entry=>entry.Amount).ToArray();
        check(joy?.ShouldRetainThisTurn==true&&sadnessCount==1&&blockGains.LastOrDefault()==3,
            $"Emotion transforms one Sadness into retained Joy then grants Block (Retain {joy?.ShouldRetainThisTurn}, Sadness {sadnessCount}, Block entries {string.Join(",",blockGains)})");

        await clear();await Activate<Backflip>(Bond.Chain);
        await Add<DefendIronclad>(PileType.Draw);
        await Play(await Add<StrikeIronclad>());
        await Play(await Add<Bloodletting>());
        block=player.Creature.Block;
        await Play(await Add<StrikeIronclad>());
        check(player.Creature.Block==block+3&&player.Creature.GetPowerAmount<VigorPower>()==2&&
            player.Creature.GetPowerAmount<StretchPower>()==2,
            "Chain II grants the base reward and both native powers on the third alternating card");
        await Play(await Add<Bloodletting>());
        check(player.Creature.GetPowerAmount<VigorPower>()==2&&player.Creature.GetPowerAmount<StretchPower>()==2,
            "a card with no attack or Block consumes neither Chain II reward");
        await Play(await Add<StrikeIronclad>());
        check(player.Creature.GetPowerAmount<VigorPower>()==0&&player.Creature.GetPowerAmount<StretchPower>()==2,
            "an attack consumes Vigor and leaves Stretch for the next Block card");
    }
}
