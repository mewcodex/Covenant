using Covenant.Core;
using Covenant.Game;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

internal static partial class MechanismChecks
{
    private static async Task MembershipSemantics(Action<bool,string> check)
    {
        var f=new Battle();
        foreach(var native in new CardModel[]{ModelDb.Card<CrushUnder>(),ModelDb.Card<DarkShackles>(),ModelDb.Card<DyingStar>(),ModelDb.Card<EnfeeblingTouch>(),ModelDb.Card<Friendship>(),ModelDb.Card<Malaise>(),ModelDb.Card<Mangle>(),ModelDb.Card<MonarchsGaze>(),ModelDb.Card<PiercingWail>(),ModelDb.Card<SharedFate>()})
            check(!CardMembership.Of(native).Contains(Bond.Fury),"Enemy Strength loss and self Strength penalties are not Fury: "+native.Id.Entry);
        check(CardMembership.Of(ModelDb.Card<Monologue>()).Contains(Bond.Fury),"Monologue remains Fury because its native power grants positive temporary Strength");
        foreach(var native in new CardModel[]{ModelDb.Card<Brand>(),ModelDb.Card<Breakthrough>(),ModelDb.Card<DemonicShield>(),ModelDb.Card<TheHangedMan>()})
            check(CardMembership.Of(native).Contains(Bond.Blood),"A real playable self-HP-loss card counts Blood: "+native.Id.Entry);
        foreach(var native in new CardModel[]{ModelDb.Card<Haunt>(),ModelDb.Card<BadLuck>(),ModelDb.Card<Beckon>()})
            check(!CardMembership.Of(native).Contains(Bond.Blood),"Enemy damage or forced curse/status damage is not voluntary Blood: "+native.Id.Entry);
        check(CardMembership.Of(ModelDb.Card<ConsumingShadow>()).Contains(Bond.Overload)&&CardMembership.Of(ModelDb.Card<Thunder>()).Contains(Bond.Overload),"Indirect native evoke abilities count Overload");

        var upgradedOnly=f.Add<Anointed>(PileType.Hand);
        check(!upgradedOnly.ShouldRetainThisTurn&&!CardMembership.Of(upgradedOnly).Contains(Bond.Retain),"A card that gains Retain only on upgrade does not count early");
        upgradedOnly.UpgradeInternal();
        check(CardMembership.Of(upgradedOnly).Contains(Bond.Retain),"Native upgrade immediately adds the actual Retain membership");
        CardCmd.RemoveKeyword(upgradedOnly,CardKeyword.Retain);
        check(!CardMembership.Of(upgradedOnly).Contains(Bond.Retain),"Removing the actual Retain keyword also removes its membership");
        var support=f.Add<Expertise>();
        check(!support.ShouldRetainThisTurn&&CardMembership.Of(support).Contains(Bond.Retain),"A native whole-hand retention enabler counts even without retaining itself");
        check(!CardMembership.Of(f.Add<SummonForth>()).Contains(Bond.Retain),"Retrieving a sword does not infer Retain from a hover-tip reference alone");

        var shiv=f.Add<Shiv>(PileType.Hand);
        check(!CardMembership.Of(shiv).Contains(Bond.Retain),"An ordinary Shiv has no Retain membership");
        var phantom=await PowerCmd.Apply<PhantomBladesPower>(f.Context,f.Player.Creature,1,f.Player.Creature,null);
        check(shiv.ShouldRetainThisTurn&&CardMembership.Of(shiv).Contains(Bond.Retain),"A native ongoing ability dynamically gives the Shiv its Retain membership");
        await PowerCmd.Remove(phantom);
        check(!shiv.ShouldRetainThisTurn&&!CardMembership.Of(shiv).Contains(Bond.Retain),"Removing the native Retain-granting ability removes the Shiv's membership");
        var temporary=f.Add<DefendIronclad>(PileType.Hand);CardCmd.ApplySingleTurnRetain(temporary);
        check(CardMembership.Of(temporary).Contains(Bond.Retain),"Native single-turn Retain also contributes to membership");
        temporary.EndOfTurnCleanup();
        check(!CardMembership.Of(temporary).Contains(Bond.Retain),"Native end-of-turn cleanup also expires a temporary Retain membership");
        Console.WriteLine("COVENANT_MEMBERSHIP_SEMANTICS_OK self-strength=pass voluntary-hp-loss=pass indirect-evoke=pass native-dynamic-retain=pass");
    }
}

