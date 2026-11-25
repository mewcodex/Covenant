using Covenant.Core;
using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Rooms;

internal static class AttritionProbe
{
    internal static async Task Run(SceneTree tree,Player player)
    {
        var terminal=CovenantTerminal.For(player)!;
        var state=player.PlayerCombatState!;
        var combat=player.Creature.CombatState!;
        var attrition=player.Creature.GetPower<CovenantAttritionPower>()!;
        var context=new ThrowingPlayerChoiceContext();
        for(var i=0;i<100&&state.Phase!=PlayerTurnPhase.Play;i++)await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);

        void Check(bool ok,string description)
        {
            if(!ok)throw new Exception(description);
            Console.WriteLine("COVENANT_ATTRITION_PASS "+description);
        }
        Task StartSide(CombatSide side)=>attrition.BeforeSideTurnStart(context,side,[],combat);
        async Task PlayClash()
        {
            var clash=combat.CreateCard<Clash>(player);
            await CardPileCmd.Add(clash,PileType.Hand);
            await CardPileCmd.Add(combat.CreateCard<DefendIronclad>(player),PileType.Hand);
            await CardCmd.AutoPlay(context,clash,combat.HittableEnemies.First());
        }

        Check(new[]{RoomType.Monster,RoomType.Elite,RoomType.Boss}.Select(room=>CovenantAttritionPower.LimitFor(room)).SequenceEqual([20,30,50]),
            "normal, elite and boss use the 20 / 30 / 50 enemy turn limits");
        foreach(var limit in new[]{20,30,50})
        {
            var threshold=(CovenantAttritionPower)ModelDb.Power<CovenantAttritionPower>().ToMutable();
            threshold.EnemyTurnLimit=limit;threshold.EnemyTurns=limit/2;
            Check(!threshold.GrowthLocked,"layers can still grow on enemy turn "+limit/2);
            threshold.EnemyTurns++;
            Check(threshold.GrowthLocked,"layers stop growing from enemy turn "+(limit/2+1));
        }

        attrition.EnemyTurns=9;
        await StartSide(CombatSide.Enemy);
        await StartSide(CombatSide.Player);
        var before=terminal.Ledger.Get(Bond.Encounter);
        await PlayClash();
        var grown=terminal.Ledger.Get(Bond.Encounter);
        Check(attrition.EnemyTurns==10&&grown>before,"a real Clash gains layers at the halfway point; a player-side turn does not advance the count");
        await StartSide(CombatSide.Enemy);
        await PlayClash();
        Check(attrition.GrowthLocked&&terminal.Ledger.Get(Bond.Encounter)==grown,"a real Clash cannot grant layers after the halfway point");

        attrition.EnemyTurns=19;
        var hp=player.Creature.CurrentHp;
        await StartSide(CombatSide.Enemy);
        Check(player.Creature.CurrentHp==hp,"enemy turn 20 still has no attrition damage");
        await StartSide(CombatSide.Enemy);
        await StartSide(CombatSide.Enemy);
        Check(player.Creature.CurrentHp==hp-15,"enemy turns 21 and 22 deal 5 and 10 damage");

        for(var i=0;i<2;i++)await CardPileCmd.Add(player.RunState.CreateCard<Deflect>(player),PileType.Deck,skipVisuals:true);
        Check(terminal.Counts(deck:true).GetValueOrDefault(Bond.Aid)>=BondCatalog.Get(Bond.Aid).Basic,"Aid is active at victory");
        var layers=terminal.Ledger.Layers.ToDictionary();
        for(var wave=0;wave<8&&CombatManager.Instance.IsInProgress;wave++)
        {
            await CreatureCmd.Kill(combat.Enemies.Where(enemy=>enemy.IsAlive).ToArray(),true);
            if(await CombatManager.Instance.CheckWinCondition())break;
        }
        Check(!CombatManager.Instance.IsInProgress&&layers.Count==terminal.Ledger.Layers.Count&&
            layers.All(pair=>terminal.Ledger.Get(pair.Key)==pair.Value),"a late victory does not grant Aid's combat-end layers");
        terminal.GrantLayers(Bond.Foresight,8);
        Check(terminal.Ledger.Get(Bond.Foresight)==layers.GetValueOrDefault(Bond.Foresight)+8,"outside-combat growth resumes after settlement");
        Console.WriteLine("COVENANT_ATTRITION_COMPLETE");
    }
}
