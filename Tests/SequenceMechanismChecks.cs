using Covenant.Core;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.ValueProps;

internal static partial class MechanismChecks
{
    private static async Task CardSequences(Action<bool,string> check)
    {
        var f=new Battle();f.Copies<Bludgeon>(10);f.Terminal.Ledger.Add(Bond.Heavy,20);
        CardModel heavy=f.Add<Bludgeon>(PileType.Hand);var retained=f.Add<Bludgeon>(PileType.Hand);
        CardModel draw=f.Add<DefendIronclad>(PileType.Draw);var energy=f.Player.PlayerCombatState!.Energy;
        CombatChecks.Choose=cards=>cards.Where(c=>c==retained);
        check(f.Terminal.ModifyDamageAdditive(f.Enemy,6,ValueProp.Move,f.Player.Creature,heavy,f.Play(heavy,2))==5,"Heavy adds scaled damage to a card actually paid with two energy");
        check(f.Terminal.ModifyBlockAdditive(f.Player.Creature,6,ValueProp.Move,heavy,f.Play(heavy,2))==5,"Heavy adds the same scaled block to an expensive play");
        check(f.Terminal.ModifyDamageAdditive(f.Enemy,6,ValueProp.Move,f.Player.Creature,heavy,f.Play(heavy,0))==0,"A free expensive card receives no Heavy bonus");
        await f.ResolveCallbacks(heavy,0);
        check(!retained.ShouldRetainThisTurn&&draw.Pile?.Type==PileType.Draw,"A free expensive play does not advance Heavy's paid-play sequence");
        await f.ResolveCallbacks(heavy,2);
        check(retained.ShouldRetainThisTurn&&draw.Pile?.Type==PileType.Draw,"First actual expensive play retains a different expensive hand card");
        await f.ResolveCallbacks(heavy,2);
        check(draw.Pile?.Type==PileType.Hand&&f.Player.PlayerCombatState.Energy==energy+1&&f.Player.Creature.Block==6,"Second actual expensive play draws, refunds one energy and grants scaled block");
        await f.ResolveCallbacks(heavy,2);
        check(f.Player.PlayerCombatState.Energy==energy+1&&f.Player.Creature.Block==6,"Further expensive plays do not repeat Heavy's second-play rewards");

        f=new();f.Copies<Backflip>();f.Terminal.Ledger.Add(Bond.Chain,20);
        var attack=f.Add<Bludgeon>(PileType.Hand);var skill=f.Add<DefendIronclad>(PileType.Hand);
        var returned=f.Add<Bludgeon>(PileType.Discard);draw=f.Add<BattleTrance>(PileType.Draw);
        CombatChecks.Choose=cards=>cards.Where(c=>c==returned);
        await f.ResolveCallbacks(attack);await f.ResolveCallbacks(skill);
        check(draw.Pile?.Type==PileType.Draw&&returned.Pile?.Type==PileType.Discard,"Two alternating plays do not prematurely activate Chain");
        await f.ResolveCallbacks(attack);
        // The just-played card is an attack, so the advanced selection must exclude this discarded attack.
        check(returned.Pile?.Type==PileType.Discard&&draw.Pile?.Type==PileType.Hand&&f.Player.Creature.Block==5,"Third alternating play grants the base draw and block while excluding same-type retrieval");
        await f.StartTurn();var recovered=f.Add<DefendIronclad>(PileType.Discard);f.Add<BattleTrance>(PileType.Draw);
        CombatChecks.Choose=cards=>cards.Where(c=>c==recovered);
        await f.ResolveCallbacks(attack);await f.ResolveCallbacks(skill);await f.ResolveCallbacks(attack);
        check(recovered.Pile?.Type==PileType.Hand&&recovered.EnergyCost.GetWithModifiers(CostModifiers.All)==0,"Advanced Chain retrieves an opposite-type card with a native turn discount");
        await f.Terminal.BeforeCardPlayed(f.Play(recovered));
        check(f.Terminal.ModifyBlockAdditive(f.Player.Creature,5,ValueProp.Move,recovered,null)==4,"Chain's recovered card keeps its layer-scaled next-play bonus");

        f=new();f.Copies<Inflame>();f.Terminal.Ledger.Add(Bond.Ritual,20);
        var power=f.Add<Inflame>(PileType.Hand);skill=f.Add<DefendIronclad>(PileType.Draw);
        CombatChecks.Choose=cards=>cards.Where(c=>c==skill);
        await f.ResolveCallbacks(power);
        check(f.Player.Creature.Block==6&&skill.Pile?.Type==PileType.Hand,"Ritual's first power grants scaled block and draws the selected top-deck card");
        check(!f.Terminal.TryModifyEnergyCostInCombat(skill,1,out _),"Advanced Ritual no longer discounts the selected card");
        await f.Terminal.BeforeCardPlayed(f.Play(skill));
        check(f.Terminal.ModifyBlockAdditive(f.Player.Creature,5,ValueProp.Move,skill,null)==5,"Ritual's next non-power card receives the scaled block bonus");
        check(!f.Terminal.TryModifyEnergyCostInCombat(skill,1,out _),"Starting the non-power play does not introduce a Ritual discount");

        f=new();for(int i=0;i<10;i++)f.Add<Bludgeon>().UpgradeInternal();f.Terminal.Ledger.Add(Bond.Mastery,20);
        var upgraded=f.Add<Bludgeon>(PileType.Hand);upgraded.UpgradeInternal();var upgradeTarget=f.Add<DefendIronclad>(PileType.Hand);
        energy=f.Player.PlayerCombatState!.Energy;CombatChecks.Choose=cards=>cards.Where(c=>c==upgradeTarget);
        check(f.Terminal.ModifyDamageAdditive(f.Enemy,6,ValueProp.Move,f.Player.Creature,upgraded,null)==0,"Mastery does not paint its first-play bonus on every unplayed upgraded card");
        await f.ResolveCallbacks(upgraded);
        check(upgradeTarget.IsUpgraded&&!upgradeTarget.DeckVersion!.IsUpgraded,"Mastery upgrades only the selected combat copy");
        check(f.Player.Creature.Block==5&&f.Player.PlayerCombatState.Energy==energy,"First upgraded play grants Mastery's block without an early energy refund");
        await f.ResolveCallbacks(upgraded);
        check(f.Player.Creature.Block==5&&f.Player.PlayerCombatState.Energy==energy,"Mastery's second upgraded play does not refund energy");
        await f.ResolveCallbacks(upgraded);
        check(f.Player.PlayerCombatState.Energy==energy+1&&f.Terminal.Ledger.Get(Bond.Mastery)==22,"Mastery refunds on the third upgraded play and earns its two battle-growth layers once");

        f=new();f.Copies<Doubt>(4);f.Terminal.Ledger.Add(Bond.Curse,20);
        draw=f.Add<DefendIronclad>(PileType.Draw);var curse=f.Add<Doubt>(PileType.Draw);
        var hp=f.Enemy.CurrentHp;CombatChecks.Choose=cards=>cards.Where(c=>c==curse);
        await CardPileCmd.Draw(f.Context,1,f.Player);
        check(curse.Pile?.Type==PileType.Exhaust&&draw.Pile?.Type==PileType.Hand,"Drawing a curse can exhaust it and perform the advanced extra draw");
        check(f.Player.Creature.Block==6&&hp-f.Enemy.CurrentHp==9,"Curse exhaust grants the described scaled block and damage to enemies");
        var secondCurse=f.Add<Doubt>(PileType.Draw);await CardPileCmd.Draw(f.Context,1,f.Player);
        check(secondCurse.Pile?.Type==PileType.Hand&&f.Player.Creature.Block==6,"Curse's optional protection is limited to the first drawn curse in a turn");

        f=new();f.Copies<Wound>(7);f.Terminal.Ledger.Add(Bond.Status,20);
        var status=f.Add<Wound>(PileType.Draw);CombatChecks.Choose=cards=>cards.Where(c=>c==status);
        await CardPileCmd.Draw(f.Context,1,f.Player);
        check(status.Pile?.Type==PileType.Discard&&f.Player.Creature.Block==5,"First drawn status can be discarded for layer-scaled block");
        var retrieved=f.Add<DefendIronclad>(PileType.Discard);CombatChecks.Choose=cards=>cards.Where(c=>c==retrieved);
        await CardCmd.Exhaust(f.Context,f.Add<Wound>(PileType.Hand));
        await f.Terminal.BeforeCardPlayed(f.Play(retrieved));
        check(retrieved.Pile?.Type==PileType.Hand&&f.Terminal.ModifyBlockAdditive(f.Player.Creature,5,ValueProp.Move,retrieved,null)==5,"First status exhaust retrieves an eligible card with a layer-scaled next-play bonus");

        f=new();f.Copies<Dazed>();f.Terminal.Ledger.Add(Bond.Ethereal,40);
        var protectedCard=f.Add<Apparition>(PileType.Hand);var exhausted=f.Add<Dazed>(PileType.Hand);
        CombatChecks.Choose=cards=>cards.Where(c=>c==protectedCard);
        await f.Terminal.BeforeSideTurnEndVeryEarly(f.Context,CombatSide.Player,[f.Player.Creature]);
        check(protectedCard.ShouldRetainThisTurn&&!f.Terminal.ShouldEtherealTrigger(protectedCard)&&f.Terminal.ShouldEtherealTrigger(exhausted),"Ethereal protection retains exactly the selected hand card and leaves other ethereal cards unchanged");
        await CardCmd.Exhaust(f.Context,exhausted,causedByEthereal:true);
        check(f.Player.Creature.Block==5,"The first real ethereal exhaust grants layer-scaled block");
        await f.Terminal.AfterFlush(f.Context,f.Player,[],[protectedCard]);
        CombatChecks.Choose=cards=>cards.Where(c=>c==exhausted);await f.StartTurn();
        check(exhausted.Pile?.Type==PileType.Hand,"Advanced Ethereal retrieves a card exhausted by ethereal on the previous turn");
        check(f.Terminal.ModifyBlockAdditive(f.Player.Creature,5,ValueProp.Move,exhausted,null)==4,"The ethereal retrieval carries its layer-scaled next-play bonus");

        f=new();f.Terminal.Ledger.Add(Bond.Solitude,20);
        var uniqueAttack=f.Add<Conflagration>();f.Add<SwordBoomerang>();var uniqueSkill=f.Add<BorrowedTime>();
        f.Add<RipAndTear>();f.Add<DaggerSpray>();f.Add<Burst>();
        draw=f.Add<DefendIronclad>(PileType.Draw);power=f.Add<Inflame>(PileType.Hand);
        check(f.Terminal.Tier(Bond.Solitude)==2,"Six distinct Solitude members activate the advanced unique-deck condition");
        await f.ResolveCallbacks(uniqueAttack);await f.ResolveCallbacks(uniqueSkill);
        check(draw.Pile?.Type==PileType.Hand,"The first pair of different card types triggers Solitude's draw");
        await f.Terminal.BeforeCardPlayed(f.Play(draw));
        check(f.Terminal.ModifyBlockAdditive(f.Player.Creature,5,ValueProp.Move,draw,null)==4,"Solitude's next-card bonus scales with its layers");
        await f.Terminal.AfterCardPlayedLate(f.Context,f.Play(draw));
        CardModel? solitudeRecovered=null;
        CombatChecks.Choose=cards=>{solitudeRecovered=cards.FirstOrDefault(c=>c==uniqueAttack||c==uniqueSkill);return solitudeRecovered==null?[]:[solitudeRecovered];};await f.ResolveCallbacks(power);
        check(solitudeRecovered?.Pile?.Type==PileType.Hand&&f.Terminal.ModifyDamageAdditive(f.Enemy,6,ValueProp.Move,f.Player.Creature,solitudeRecovered,null)==5,$"Playing all three card types retrieves a card with Solitude bonus: selected={solitudeRecovered?.GetType().Name}, pile={solitudeRecovered?.Pile?.Type}, bonus={(solitudeRecovered==null?-1:f.Terminal.ModifyDamageAdditive(f.Enemy,6,ValueProp.Move,f.Player.Creature,solitudeRecovered,null))}, tier={f.Terminal.Tier(Bond.Solitude)}");
        f.Add<Conflagration>();
        check(f.Terminal.Tier(Bond.Solitude)==0,"A duplicate non-basic card immediately deactivates Solitude");
        CombatChecks.Choose=null;
        Console.WriteLine("COVENANT_SEQUENCE_MECHANISMS_OK paid-heavy=pass alternating-chain=pass first-power-ritual=pass upgraded-mastery=pass curse=pass status=pass ethereal=pass solitude=pass");
    }
}

