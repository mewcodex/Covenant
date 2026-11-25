using Covenant.Core;
using Covenant.Game;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

internal static partial class MechanismChecks
{
    private static async Task IndirectPoison(Action<bool,string> check)
    {
        var f=new Battle();f.Copies<DeadlyPoison>();f.Terminal.Ledger.Add(Bond.Poison,60);
        var fumes=await PowerCmd.Apply<NoxiousFumesPower>(f.Context,f.Player.Creature,2,f.Player.Creature,null)??throw new Exception("Native Noxious Fumes missing");
        await fumes.AfterSideTurnStart(CombatSide.Player,[f.Player.Creature],f.State);
        check(f.Enemy.GetPowerAmount<PoisonPower>()==6&&f.Other.GetPowerAmount<PoisonPower>()==6,"Native source-null Noxious Fumes applies layer-scaled poison to both targets");
        check(f.Terminal.Ledger.Get(Bond.Poison)==84,"Two native indirect applications trigger exactly two sets of poison traits, without recursion");
        check(f.Enemy.GetPowerAmount<WeakPower>()==1&&f.Other.GetPowerAmount<WeakPower>()==0,"Indirect poison applies new-target Weak only once per turn");
        await CreatureCmd.Kill(f.Enemy,true);
        check(f.Other.GetPowerAmount<PoisonPower>()==8&&f.Terminal.Ledger.Get(Bond.Poison)==90,"Covenant death transfer does not reuse the remaining poison bonus or create a trait event");
        await PowerCmd.Apply<AccelerantPower>(f.Context,f.Player.Creature,1,f.Player.Creature,null);
        var hp=f.Other.CurrentHp;await f.Other.GetPower<PoisonPower>()!.Trigger();
        check(hp-f.Other.CurrentHp==15&&f.Other.GetPowerAmount<PoisonPower>()==6,"Native Accelerant triggers eight then seven poison damage and decrements both applications normally");
        check(f.Terminal.Ledger.Get(Bond.Poison)==90,"Poison ticks and Accelerant's extra tick do not count as new poison applications");

        f=new();f.Copies<DeadlyPoison>();f.Terminal.Ledger.Add(Bond.Poison,60);
        await PowerCmd.Apply<EnvenomPower>(f.Context,f.Player.Creature,1,f.Player.Creature,null);
        var attack=f.Add<Anger>(PileType.Hand);
        await CreatureCmd.Damage(f.Context,f.Enemy,6,ValueProp.Move,attack,f.Play(attack));
        check(f.Enemy.GetPowerAmount<PoisonPower>()==5&&f.Enemy.GetPowerAmount<WeakPower>()==1,"Native Envenom poison from a real attack receives the covenant bonus and Weak");
        for(int i=0;i<3;i++)await CreatureCmd.Damage(f.Context,f.Enemy,6,ValueProp.Move,attack,f.Play(attack));
        check(f.Enemy.GetPowerAmount<PoisonPower>()==23,"Exactly the first three indirect poison applications receive the bonus");
        check(f.Terminal.Ledger.Get(Bond.Poison)==108,"Three real applications award every source three uses; later applications cannot exceed each card's limit");

        f=new();f.Copies<DeadlyPoison>();f.Terminal.Ledger.Add(Bond.Poison,60);
        await PowerCmd.Apply<PoisonPower>(f.Context,f.Other,2,f.Enemy,null);
        check(f.Other.GetPowerAmount<PoisonPower>()==2&&f.Terminal.Ledger.Get(Bond.Poison)==60,"Another creature's source-null poison does not count as the player's poison");
        await PowerCmd.Apply<PoisonPower>(f.Context,f.Player.Creature,1,f.Player.Creature,null);
        check(f.Player.Creature.GetPowerAmount<WeakPower>()==0,"Poison applied to the player never grants the enemy-only Weak effect");

        foreach(var native in new CardModel[]{ModelDb.Card<Barrage>(),ModelDb.Card<CompileDriver>(),ModelDb.Card<Coolant>(),ModelDb.Card<Hailstorm>(),ModelDb.Card<LightningRod>(),ModelDb.Card<Loop>(),ModelDb.Card<Storm>(),ModelDb.Card<Thunder>(),ModelDb.Card<TrashToTreasure>()})
            check(CardMembership.Of(native).Contains(Bond.Resonance),"A native orb enabler or orb-dependent payoff counts Resonance: "+native.Id.Entry);
        foreach(var native in new CardModel[]{ModelDb.Card<BulkUp>(),ModelDb.Card<Hyperbeam>(),ModelDb.Card<Orbit>()})
            check(!CardMembership.Of(native).Contains(Bond.Resonance),"An orb penalty or name-only match does not grant Resonance: "+native.Id.Entry);
        Console.WriteLine("COVENANT_INDIRECT_POISON_OK fumes=pass envenom-native-attack=pass first-three=pass no-recursion=pass no-transfer-retrigger=pass orb-support-memberships=pass");
    }
}

