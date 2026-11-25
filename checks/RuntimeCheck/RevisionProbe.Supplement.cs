using Covenant.Core;
using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

internal static partial class RevisionProbe
{
    private static async Task Supplement(SceneTree tree,Player player,CovenantTerminal terminal,Func<Task> clear,Func<CardModel,Task> play,Action<bool,string> check)
    {
        var context=new ThrowingPlayerChoiceContext();
        async Task<T> Add<T>(PileType pile=PileType.Hand,bool upgraded=false) where T:CardModel
        {
            var card=player.Creature.CombatState!.CreateCard<T>(player);if(upgraded)CardCmd.Upgrade(card);
            await CardPileCmd.Add(card,pile);return card;
        }
        await clear();terminal.Ledger.Add(Bond.Poison,150);var accelerant=await Add<Accelerant>();await play(accelerant);
        var enemy=player.Creature.CombatState!.HittableEnemies.First();await PowerCmd.Apply<PoisonPower>(context,enemy,5,player.Creature,null);
        var poison=enemy.GetPower<PoisonPower>()!;
        check(poison.CalculateTotalDamageNextTurn()==12,"Accelerant's native Poison forecast includes its extra-tick bonus");
        var historyStart=CombatManager.Instance.History.Entries.Count();var hp=enemy.CurrentHp;await poison.Trigger();
        var hits=CombatManager.Instance.History.Entries.Skip(historyStart).OfType<DamageReceivedEntry>().Where(e=>e.Receiver==enemy).Select(e=>e.Result.UnblockedDamage).ToArray();
        check(hp-enemy.CurrentHp==12&&hits.SequenceEqual(new[]{5,7}),"Accelerant preserves the ordinary Poison tick and adds its coefficient only to the actual extra tick");
        await clear();terminal.Ledger.Add(Bond.Arrows,150);var honing=await Add<Honing>();await play(honing);enemy=player.Creature.CombatState!.HittableEnemies.First();hp=enemy.CurrentHp;
        await CreatureCmd.Damage(context,enemy,10,ValueProp.Move|ValueProp.FromArrow,player.Creature);
        check(hp-enemy.CurrentHp==18,"Honing adds three fixed damage after its native fifty-percent arrow multiplier");
        await PowerCmd.Apply<IntangiblePower>(context,enemy,1,enemy,null);hp=enemy.CurrentHp;
        await CreatureCmd.Damage(context,enemy,10,ValueProp.Move|ValueProp.FromArrow,player.Creature);
        check(hp-enemy.CurrentHp==1,"Honing's fixed damage preserves native Intangible damage caps");
        await clear();if(player.PlayerCombatState!.OrbQueue.Capacity<3)await OrbCmd.AddSlots(player,3-player.PlayerCombatState.OrbQueue.Capacity);
        var spinner=await Add<Spinner>();await Add<BallLightning>(PileType.Exhaust);await Add<BallLightning>(PileType.Exhaust);await play(spinner);
        await player.Creature.GetPower<SpinnerPower>()!.AfterEnergyReset(player);
        await OrbCmd.Channel<GlassOrb>(context,player);
        await OrbCmd.EvokeNext(context,player,dequeue:false);
        check(terminal.Ledger.Permanent(Bond.Resonance)==2,"Spinner rewards an actual evoke of its own Glass orb");
        await OrbCmd.EvokeNext(context,player);
        check(terminal.Ledger.Permanent(Bond.Resonance)==4,"evoking the same Spinner orb twice pays twice without a shared cap");
        await OrbCmd.EvokeNext(context,player);
        check(terminal.Ledger.Permanent(Bond.Resonance)==4,"an unrelated Glass orb cannot trigger Spinner's origin trait");
        await clear();await OrbCmd.AddSlots(player,3);var hibernate=await Add<Hibernate>();await Add<BallLightning>(PileType.Exhaust);await Add<BallLightning>(PileType.Exhaust);await play(hibernate);
        var frost=player.PlayerCombatState!.OrbQueue.Orbs.First();await OrbCmd.Passive(context,frost,null);
        check(terminal.Ledger.Permanent(Bond.Resonance)==2,"Hibernate follows actual Block provided by one of its own Frost orbs");
        await OrbCmd.Passive(context,frost,null);
        check(terminal.Ledger.Permanent(Bond.Resonance)==4,"Hibernate's own Frost orb can provide repeatable permanent growth in its creation turn");
        await OrbCmd.Channel<FrostOrb>(context,player);var foreignFrost=player.PlayerCombatState.OrbQueue.Orbs.Last();await OrbCmd.Passive(context,foreignFrost,null);
        check(terminal.Ledger.Permanent(Bond.Resonance)==4,"an unrelated Frost orb cannot pay Hibernate's creation lineage");
        await clear();await OrbCmd.AddSlots(player,2);terminal.Ledger.Add(Bond.Resonance,150);await OrbCmd.Channel<LightningOrb>(context,player);await OrbCmd.Channel<LightningOrb>(context,player);
        var tesla=await Add<TeslaCoil>();enemy=player.Creature.CombatState!.HittableEnemies.First();historyStart=CombatManager.Instance.History.Entries.Count();await play(tesla);
        hits=CombatManager.Instance.History.Entries.Skip(historyStart).OfType<DamageReceivedEntry>().Where(e=>e.Receiver==enemy&&e.CardSource==null).Select(e=>e.Result.UnblockedDamage).ToArray();
        check(hits.SequenceEqual(new[]{4,4}),"Tesla Coil adds its coefficient to each native Lightning passive it directly triggers");
        hp=enemy.CurrentHp;await OrbCmd.Passive(context,player.PlayerCombatState.OrbQueue.Orbs.First(),enemy);
        check(hp-enemy.CurrentHp==3,"Tesla Coil's passive context is restored before an unrelated Lightning activation");
        await clear();terminal.Ledger.Add(Bond.Overload,150);await OrbCmd.Channel<FrostOrb>(context,player);var shatter=await Add<Shatter>();enemy=player.Creature.CombatState!.HittableEnemies.First();hp=enemy.CurrentHp;await play(shatter);
        check(hp-enemy.CurrentHp==11&&player.PlayerCombatState!.OrbQueue.Orbs.Count==0,"Shatter adds its flat two damage and both actual Frost-evoke damage events");
        await clear();var hatchet=await Add<ThrummingHatchet>();enemy=player.Creature.CombatState!.HittableEnemies.First();hp=enemy.CurrentHp;await play(hatchet);
        check(hp-enemy.CurrentHp==13,"Thrumming Hatchet's initial entry into hand does not count as a return");
        await CardPileCmd.Add(hatchet,PileType.Hand);hp=enemy.CurrentHp;await play(hatchet);
        check(hp-enemy.CurrentHp==14,"Thrumming Hatchet's first native return increases its battle damage by one");
        await CardPileCmd.Add(hatchet,PileType.Hand);hp=enemy.CurrentHp;await play(hatchet);
        check(hp-enemy.CurrentHp==15,"Thrumming Hatchet's return counter continues across later real plays");
        await clear();var crash=await Add<CrashLanding>();enemy=player.Creature.CombatState!.HittableEnemies.First();hp=enemy.CurrentHp;await play(crash);
        check(hp-enemy.CurrentHp==23,"Crash Landing applies its initial independent damage bonus");
        var debris=player.PlayerCombatState!.Hand.Cards.OfType<Debris>().ToArray();check(debris.Length==10,"Crash Landing records all ten native Debris cards it generated");
        await CardPileCmd.RemoveFromCombat(debris);await CardPileCmd.Add(crash,PileType.Hand);hp=enemy.CurrentHp;await play(crash);
        check(hp-enemy.CurrentHp==33,"Crash Landing's later damage includes the ten Debris created by its prior native play");
        await clear();terminal.Ledger.Add(Bond.Strike,200);var perfected=await Add<PerfectedStrike>();await Add<StrikeIronclad>(PileType.Exhaust);await Add<StrikeIronclad>(PileType.Exhaust);
        enemy=player.Creature.CombatState!.HittableEnemies.First();
        check(perfected.DynamicVars.CalculatedDamage.Calculate(enemy)==12,"Perfected Strike's native calculated-damage preview keeps its printed coefficient");
        for(var i=0;i<5;i++)_ =perfected.DynamicVars.CalculatedDamage.Calculate(enemy);
        check(perfected.DynamicVars.ExtraDamage.BaseValue==2,"repeated coefficient previews leave the native ExtraDamage variable unchanged");
        hp=enemy.CurrentHp;await play(perfected);
        check(hp-enemy.CurrentHp==17,"Perfected Strike's real play applies the printed coefficient plus active covenant bonuses");
        check(perfected.DynamicVars.ExtraDamage.BaseValue==2,"Perfected Strike's slay trait leaves its native ExtraDamage variable intact");
    }
}
