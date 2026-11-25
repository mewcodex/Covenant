using Covenant.Core;
using Covenant.Game;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

internal static partial class MechanismChecks
{
    private static async Task ResourceSemantics(Action<bool,string> check)
    {
        foreach(var native in new CardModel[]{ModelDb.Card<BlackHole>(),ModelDb.Card<Genesis>(),ModelDb.Card<VoidForm>()})
            check(CardMembership.Of(native).Contains(Bond.Stars),"An attached star generator, payoff or cost reduction counts Stars: "+native.Id.Entry);
        check(CardMembership.Of(ModelDb.Card<HammerTime>()).Contains(Bond.Forge),"Native allied forging counts Forge");
        check(CardMembership.Of(ModelDb.Card<Calcify>()).Contains(Bond.Bones)&&CardMembership.Of(ModelDb.Card<DevourLife>()).Contains(Bond.Bones),"Osty damage and Soul-triggered summon abilities count Bones");
        check(!CardMembership.Of(ModelDb.Card<Thrash>()).Contains(Bond.Bones),"Reading a consumed attack's OstyDamage variable does not make Thrash a Bones component");
        check(!CardMembership.Of(ModelDb.Card<Neurosurge>()).Contains(Bond.Doom),"Self-applied Doom is an ability cost rather than an enemy Doom component");

        var f=new Battle();f.Copies<Genesis>();f.Terminal.Ledger.Add(Bond.Stars,40);
        var genesis=await PowerCmd.Apply<GenesisPower>(f.Context,f.Player.Creature,3,f.Player.Creature,null)??throw new Exception("Genesis missing");
        await PowerCmd.Apply<BlackHolePower>(f.Context,f.Player.Creature,1,f.Player.Creature,null);
        var hp=f.Enemy.CurrentHp;await genesis.AfterEnergyReset(f.Player);
        check(f.Player.PlayerCombatState!.Stars==3&&hp-f.Enemy.CurrentHp==1,"Native Genesis grants three stars and the native Black Hole reacts to the gain");
        var returned=f.Add<Comet>(PileType.Discard);var spender=f.Add<AstralPulse>(PileType.Hand);
        CombatChecks.Choose=cards=>cards.Where(c=>c==returned);
        var spent=await spender.SpendResources();
        check(spent.Item2==3&&f.Player.PlayerCombatState.Stars==0&&f.Player.Creature.Block==6,"Native star resource payment dispatches the scaled covenant block once");
        check(returned.Pile?.Type==PileType.Hand&&returned.ShouldRetainThisTurn&&returned.CurrentStarCost==returned.BaseStarCost-1,"Emptying stars retrieves the chosen native star card with Retain and an until-played star discount");
        await f.StartTurn();
        check(f.Player.PlayerCombatState.Stars==1&&hp-f.Enemy.CurrentHp==2,"The next-turn covenant star also triggers native Black Hole normally");

        f=new();f.Copies<Furnace>();f.Terminal.Ledger.Add(Bond.Forge,20);
        var furnace=await PowerCmd.Apply<FurnacePower>(f.Context,f.Player.Creature,5,f.Player.Creature,null)??throw new Exception("Furnace missing");
        await furnace.AfterSideTurnStart(CombatSide.Player,[f.Player.Creature],f.State);
        var sword=f.Player.PlayerCombatState!.Hand.Cards.OfType<SovereignBlade>().Single();
        check(sword.DynamicVars.Damage.BaseValue==18,"Native Furnace forges a real blade with exactly one layer-scaled augmentation");
        await CardPileCmd.Add(sword,PileType.Discard);CombatChecks.Choose=cards=>cards.Where(c=>c==sword);
        await furnace.AfterSideTurnStart(CombatSide.Player,[f.Player.Creature],f.State);
        check(sword.Pile?.Type==PileType.Hand&&sword.DynamicVars.Damage.BaseValue==26,"A second actual forging effect returns the blade without repeating the first-forge bonus");

        f=new();f.Copies<Calcify>();f.Terminal.Ledger.Add(Bond.Bones,60);
        var poke=f.Add<Poke>(PileType.Hand);var draw=f.Add<DefendIronclad>(PileType.Draw);
        await OstyCmd.Summon(f.Context,f.Player,5,poke);
        await PowerCmd.Apply<CalcifyPower>(f.Context,f.Player.Creature,3,f.Player.Creature,null);
        hp=f.Enemy.CurrentHp;
        await DamageCmd.Attack(6).FromOsty(f.Player.Osty!,poke,null).Targeting(f.Enemy).WithNoAttackerAnim().Execute(f.Context);
        check(hp-f.Enemy.CurrentHp==9&&draw.Pile?.Type==PileType.Hand,"Native Calcify strengthens Ironclad's real Osty attack while the first Bones draw still resolves");
        await PowerCmd.Apply<DevourLifePower>(f.Context,f.Player.Creature,2,f.Player.Creature,null);
        var soul=f.Add<Soul>(PileType.Hand);var ostyHp=f.Player.Osty!.MaxHp;
        await f.ResolveCallbacks(soul);
        check(f.Player.Osty.MaxHp-ostyHp==6,"Native Soul-triggered Devour Life consumes Bones' pending summon bonus exactly once");
        ostyHp=f.Player.Osty.MaxHp;await f.ResolveCallbacks(soul);
        check(f.Player.Osty.MaxHp-ostyHp==2,"The next native Soul-triggered summon receives its base amount without repeating the consumed bonus");

        f=new();f.Copies<DeathsDoor>();f.Terminal.Ledger.Add(Bond.Doom,40);draw=f.Add<DefendIronclad>(PileType.Draw);
        await PowerCmd.Apply<ShroudPower>(f.Context,f.Player.Creature,2,f.Player.Creature,null);
        var selfDoom=await PowerCmd.Apply<NeurosurgePower>(f.Context,f.Player.Creature,3,f.Player.Creature,null)??throw new Exception("Neurosurge missing");
        await selfDoom.AfterSideTurnStart(CombatSide.Player,[f.Player.Creature],f.State);
        check(f.Player.Creature.GetPowerAmount<DoomPower>()==3&&f.Terminal.Ledger.Get(Bond.Doom)==40&&draw.Pile?.Type==PileType.Draw,"Self Doom is neither amplified nor used for covenant growth or draw");
        check(f.Player.Creature.Block==2,"Excluding self Doom from the covenant preserves native Shroud's own block effect");
        await PowerCmd.Apply<DoomPower>(f.Context,f.Enemy,10,f.Player.Creature,null);
        check(f.Enemy.GetPowerAmount<DoomPower>()==13&&f.Terminal.Ledger.Get(Bond.Doom)==52&&draw.Pile?.Type==PileType.Hand,"Self Doom does not consume the first hostile Doom bonus or its layer growth");
        CombatChecks.Choose=null;
        Console.WriteLine("COVENANT_RESOURCE_SEMANTICS_OK native-star-payment=pass genesis-black-hole=pass furnace=pass calcify-devour=pass hostile-doom-only=pass native-self-doom-effects-preserved=pass");
    }
}

