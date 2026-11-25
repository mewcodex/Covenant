using Covenant.Core;
using Covenant.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

internal static partial class MechanismChecks
{
    private static bool NoHittableEnemies(ref IReadOnlyList<Creature> __result){__result=[];return false;}
    private static async Task General(Action<bool,string> check)
    {
        var f=new Battle();f.Copies<Inflame>();f.Terminal.Ledger.Add(Bond.Fury,30);
        await f.Terminal.BeforeCombatStart();
        check(f.Player.Creature.GetPowerAmount<StrengthPower>()==2,"Fury grants scaled native starting Strength");
        CardModel attack=f.Add<Anger>(PileType.Hand);var drawn=f.Add<DefendIronclad>(PileType.Draw);
        check(f.Terminal.TryModifyEnergyCostInCombat(attack,1,out var discounted)&&discounted==0,"Fury's first Strength gain discounts the next attack");
        await f.ResolveCallbacks(attack);
        check(!f.Terminal.TryModifyEnergyCostInCombat(attack,1,out _),"The attack consumes Fury's discount");
        await f.ResolveCallbacks(attack);await f.ResolveCallbacks(attack);
        check(f.Player.Creature.GetPowerAmount<StrengthPower>()==4&&drawn.Pile?.Type==PileType.Hand,"Third attack grants scaled temporary Strength and a real draw");

        f=new();f.Copies<Acrobatics>();f.Terminal.Ledger.Add(Bond.Sleight,60);
        var first=f.Add<StrikeIronclad>(PileType.Hand);var kept=f.Add<DefendIronclad>(PileType.Hand);var fresh=f.Add<BattleTrance>(PileType.Draw);
        CombatChecks.Choose=cards=>cards[0] is CovenantOption?[cards[0]]:cards.Where(c=>c==fresh);
        await CardCmd.Discard(f.Context,first);
        check(fresh.Pile?.Type==PileType.Discard&&kept.Pile?.Type==PileType.Hand,"Sleight draws before discarding, allowing the newly drawn card to be discarded");
        check(f.Player.Creature.Block==5,"Sleight grants layer-scaled block on the first active discard");
        CombatChecks.Choose=cards=>cards.Where(c=>c==first);await CardCmd.Discard(f.Context,kept);
        check(first.Pile?.Type==PileType.Hand&&first.EnergyCost.GetWithModifiers(CostModifiers.All)==0,"Third active discard retrieves a discarded card with a real discount");
        await f.Terminal.BeforeCardPlayed(f.Play(first));
        check(f.Terminal.ModifyDamageAdditive(f.Enemy,6,ValueProp.Move,f.Player.Creature,first,null)==4,"Sleight's returned card carries its current scaled next-play bonus");

        f=new();f.Copies<TrueGrit>(8);f.Terminal.Ledger.Add(Bond.Ember,90);
        var top=f.Add<Anger>(PileType.Discard);var energy=f.Player.PlayerCombatState!.Energy;
        CombatChecks.Choose=cards=>cards.Where(c=>c==top);
        for(int i=0;i<3;i++)await CardCmd.Exhaust(f.Context,f.Add<StrikeIronclad>(PileType.Hand));
        check(f.Player.Creature.Block==21,"Ember's first two exhausts grant scaled block; the third grants energy");
        check(f.Player.PlayerCombatState.Energy==energy+1&&top.Pile?.Type==PileType.Draw,"Ember refunds energy once and places the chosen discard on the draw pile");
        await CardCmd.Exhaust(f.Context,f.Add<StrikeIronclad>(PileType.Hand));check(f.Player.Creature.Block==27,"The fourth exhaust continues granting Ember block");

        f=new();f.Copies<Claw>(8);f.Terminal.Ledger.Add(Bond.Zero,60);
        var returned=f.Add<BeamCell>(PileType.Discard);attack=f.Add<Claw>(PileType.Hand);energy=f.Player.PlayerCombatState!.Energy;
        CombatChecks.Choose=cards=>cards.Where(c=>c==returned);
        await f.ResolveCallbacks(attack);
        check(returned.Pile?.Type==PileType.Draw&&f.Player.Creature.Block==5,"Zero's first printed-zero play blocks and retrieves another zero-cost card to the draw pile");
        await f.ResolveCallbacks(attack);await f.ResolveCallbacks(attack);
        check(returned.Pile?.Type==PileType.Hand&&f.Player.PlayerCombatState.Energy==energy+1,"Third zero-cost play draws the retrieved card and refunds energy");
        var next=f.Add<Claw>(PileType.Hand);await f.Terminal.BeforeCardPlayed(f.Play(next));
        check(f.Terminal.ModifyDamageAdditive(f.Enemy,6,ValueProp.Move,f.Player.Creature,next,null)==6,"Ten zero-cost sources earn all thirty layers before the next attack's scaled bonus");
        foreach(var (encounter,limit) in new (EncounterModel,int)[]{(ModelDb.Encounter<CultistsNormal>(),20),(ModelDb.Encounter<BygoneEffigyElite>(),30),(ModelDb.Encounter<AeonglassBoss>(),100)})
        {
            f=new(encounter.ToMutable());f.Run.PushRoom(new CombatRoom(f.State));
            await PowerCmd.Apply<ArtifactPower>(f.Context,f.Player.Creature,2,null,null);
            await f.Terminal.BeforeCombatStart();var attrition=f.Player.Creature.GetPower<CovenantAttritionPower>()!;
            check(attrition.EnemyTurnLimit==limit&&attrition.EnemyTurns==0&&f.Player.Creature.GetPowerAmount<ArtifactPower>()==2,$"The {limit}-enemy-turn rule is attached at battle start without consuming Artifact");
            var hp=f.Player.Creature.CurrentHp;
            f.State.RoundNumber=1000;
            for(int i=0;i<limit;i++)
            {
                await attrition.BeforeSideTurnStart(f.Context,CombatSide.Player,[f.Player.Creature],f.State);
                await attrition.BeforeSideTurnStart(f.Context,CombatSide.Enemy,[f.Enemy,f.Other],f.State);
            }
            check(attrition.EnemyTurns==limit&&f.Player.Creature.CurrentHp==hp,$"The first {limit} enemy turns are safe, independent of round number and extra player turns");
            for(int i=0;i<3;i++)await attrition.BeforeSideTurnStart(f.Context,CombatSide.Player,[f.Player.Creature],f.State);
            check(attrition.EnemyTurns==limit&&f.Player.Creature.CurrentHp==hp,"Extra player turns do not advance or trigger attrition");
            await attrition.BeforeSideTurnStart(f.Context,CombatSide.Enemy,[f.Enemy,f.Other],f.State);
            check(f.Player.Creature.CurrentHp==hp-5&&!f.Player.Creature.IsDead,"The first overdue enemy turn deals five damage instead of forcing defeat");
            await attrition.BeforeSideTurnStart(f.Context,CombatSide.Enemy,[f.Enemy,f.Other],f.State);
            check(f.Player.Creature.CurrentHp==hp-15&&attrition.DisplayAmount==limit+2,"The next enemy turn deals ten damage and updates the visible counter");
            await CreatureCmd.GainBlock(f.Player.Creature,20,ValueProp.Unpowered,null);
            await attrition.BeforeSideTurnStart(f.Context,CombatSide.Enemy,[f.Enemy,f.Other],f.State);
            check(f.Player.Creature.CurrentHp==hp-15&&f.Player.Creature.Block==5,"Overdue damage uses normal damage rules and can be blocked");
            var hidden=new Harmony("Covenant.Tests.UntargetableEnemies");var hittable=AccessTools.PropertyGetter(typeof(CombatState),nameof(CombatState.HittableEnemies));
            hidden.Patch(hittable,prefix:new HarmonyMethod(typeof(MechanismChecks),nameof(NoHittableEnemies)));
            try
            {
                check(!f.State.HittableEnemies.Any()&&f.State.Enemies.Any(e=>!e.IsDead),"The fixture has living enemies but no currently targetable enemies");
                await attrition.BeforeSideTurnStart(f.Context,CombatSide.Enemy,[f.Enemy,f.Other],f.State);
                check(attrition.EnemyTurns==limit+4&&f.Player.Creature.CurrentHp==hp-30,"Untargetable enemies still advance the enemy-turn rule and cause overdue damage");
            }
            finally{hidden.Unpatch(hittable,HarmonyPatchType.Prefix,hidden.Id);}
        }
        CombatChecks.Choose=null;
        Console.WriteLine("COVENANT_GENERAL_MECHANISMS_OK fury=pass sleight-draw-before-discard=pass ember=pass zero=pass native-commands=pass enemy-turn-attrition=20/30/100 extra-player-turns=ignored damage=5,10,15");
    }
}

