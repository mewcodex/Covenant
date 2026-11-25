using Covenant.Core;
using Covenant.Game;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

internal static partial class MechanismChecks
{
    private static async Task KeywordSpecialMemberships(Action<bool,string> check)
    {
        var f=new Battle();var feral=f.Add<Feral>();
        check(CardMembership.Of(feral).Contains(Bond.Heavy),"Native two-energy Feral initially supplies Heavy");
        feral.UpgradeInternal();
        check(!CardMembership.Of(feral).Contains(Bond.Heavy),"Native upgrade reducing base energy below two removes Heavy membership");
        feral.DowngradeInternal();
        check(CardMembership.Of(feral).Contains(Bond.Heavy),"Native downgrade restores Feral's expensive base and Heavy membership");
        var expensive=f.Add<Bludgeon>();expensive.EnergyCost.SetThisTurn(0);
        check(CardMembership.Of(expensive).Contains(Bond.Heavy),"A temporary discount does not erase the expensive base-cost component");
        var ordinary=f.Add<StrikeIronclad>();ordinary.EnergyCost.SetThisTurn(3);
        check(!CardMembership.Of(ordinary).Contains(Bond.Heavy),"A temporary surcharge does not manufacture a Heavy component");
        var x=f.Add<Whirlwind>();
        check(!CardMembership.Of(x).Contains(Bond.Heavy)&&!CardMembership.Of(x).Contains(Bond.Zero),"Native X energy does not supply fixed heavy or zero-cost components");
        f.Copies<Bludgeon>();f.Terminal.Ledger.Add(Bond.Heavy,20);
        check(f.Terminal.ModifyDamageAdditive(f.Enemy,5,ValueProp.Move,f.Player.Creature,x,f.Play(x,3))==5,"An actually paid three-energy X attack still receives the enabled Heavy effect");

        f=new();var grit=f.Add<TrueGrit>();var armaments=f.Add<Armaments>();
        check(!CardMembership.Of(grit).Contains(Bond.Foresight),"Unupgraded True Grit randomly exhausts and does not supply selection");
        grit.UpgradeInternal();check(CardMembership.Of(grit).Contains(Bond.Foresight),"Upgraded True Grit supplies its native manual selection");
        check(CardMembership.Of(armaments).Contains(Bond.Foresight),"Unupgraded Armaments supplies a native upgrade choice");
        armaments.UpgradeInternal();check(!CardMembership.Of(armaments).Contains(Bond.Foresight),"Upgraded Armaments upgrades the whole hand and no longer supplies selection");
        check(CardMembership.Counts(f.Player,deck:true).GetValueOrDefault(Bond.Foresight)==1,"Temporary combat upgrades do not change permanent-deck selection counts");
        armaments.DowngradeInternal();grit.DowngradeInternal();
        check(CardMembership.Of(armaments).Contains(Bond.Foresight)&&!CardMembership.Of(grit).Contains(Bond.Foresight),"Native downgrade restores both cards' original selection behavior");
        foreach(var card in new CardModel[]{ModelDb.Card<SeekingEdge>(),ModelDb.Card<SovereignBlade>()})
            check(!CardMembership.Of(card).Contains(Bond.Foresight),"A Seek reference without a native choice is excluded: "+card.Id.Entry);

        var blade=f.Add<SovereignBlade>(PileType.Hand);
        check(!blade.GainsBlock&&!CardMembership.Of(blade).Contains(Bond.Guard),"The native sword cannot supply Guard without its block-granting ability");
        var parry=await PowerCmd.Apply<ParryPower>(f.Context,f.Player.Creature,10,f.Player.Creature,null)??throw new Exception("Parry missing");
        check(blade.GainsBlock&&CardMembership.Of(blade).Contains(Bond.Guard),"Native Parry dynamically enables the sword's Guard membership");
        await PowerCmd.Remove(parry);
        check(!CardMembership.Of(blade).Contains(Bond.Guard),"Removing native Parry removes the conditional Guard membership");
        foreach(var card in new CardModel[]{ModelDb.Card<Fisticuffs>(),ModelDb.Card<Entrench>(),ModelDb.Card<BodySlam>(),ModelDb.Card<FeelNoPain>(),ModelDb.Card<Footwork>(),ModelDb.Card<Barricade>()})
            check(CardMembership.Of(card).Contains(Bond.Guard),"A direct block, block payoff or actual block support supplies Guard: "+card.Id.Entry);
        foreach(var card in new CardModel[]{ModelDb.Card<HandOfGreed>(),ModelDb.Card<MyShinies>(),ModelDb.Card<Royalties>(),ModelDb.Card<SpoilsMap>()})
            check(CardMembership.Of(card).Contains(Bond.Profit),"An actual gold source or gold payoff supplies Profit: "+card.Id.Entry);
        check(!CardMembership.Of(ModelDb.Card<Debt>()).Contains(Bond.Profit)&&CardMembership.Of(ModelDb.Card<Debt>()).Contains(Bond.Curse),"Debt remains a curse and does not count as an economic benefit");
        f=new();f.Copies<Entrench>();var punch=f.Add<Fisticuffs>(PileType.Hand);
        var play=f.Play(punch);await f.Terminal.BeforeCardPlayed(play);
        await CreatureCmd.GainBlock(f.Player.Creature,7,ValueProp.Move,play);
        check(f.Player.Creature.Block==10,"A native direct-block command attributed to the newly recognized Fisticuffs receives Guard's first-block reward once");
        Console.WriteLine("COVENANT_KEYWORD_SPECIAL_MEMBERSHIP_OK upgraded-heavy=pass actual-paid-x=pass selection-upgrade-boundaries=pass native-parry-guard=pass block-support=pass positive-economy=pass");
    }
}

