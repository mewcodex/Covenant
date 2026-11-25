using Covenant.Core;
using Covenant.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

internal static partial class MechanismChecks
{
    private static async Task<(Battle Battle,CardModel Song)> PrepareInherited(CardTag tag)
    {
        var f=new Battle();f.Copies<WahChoKi>();f.Terminal.Ledger.Add(Bond.Melody,20);
        CombatChecks.Choose=cards=>cards.OfType<CovenantOption>().Where(c=>c.OptionValue==(int)tag).Cast<CardModel>();
        var song=f.Player.PlayerCombatState!.ExhaustPile.Cards.First();await f.ResolveCallbacks(song);
        return (f,song);
    }
    private static async Task FoolAndEtherealSemantics(Action<bool,string> check)
    {
        foreach(var native in new CardModel[]{ModelDb.Card<Yah>(),ModelDb.Card<CoveringFire>(),ModelDb.Card<Quietus>(),ModelDb.Card<WhistlingArrows>()})
            check(CardMembership.Of(native).Contains(Bond.Arrows),"An actual arrow-shot or dislodge component counts Arrows: "+native.Id.Entry);
        check(!CardMembership.Of(ModelDb.Card<BundleOfJoy>()).Contains(Bond.Emotion)&&CardMembership.Of(ModelDb.Card<BundleOfJoy>()).Contains(Bond.Creation),"A colorless generator does not become Emotion because its English class name contains Joy");
        foreach(var native in new CardModel[]{ModelDb.Card<CallOfTheVoid>(),ModelDb.Card<DarkEmbrace>(),ModelDb.Card<Eidolon>(),ModelDb.Card<Pagestorm>(),ModelDb.Card<SculptingStrike>(),ModelDb.Card<SpiritOfAsh>(),ModelDb.Card<Veilpiercer>(),ModelDb.Card<BansheesCry>(),ModelDb.Card<PullFromBelow>()})
            check(CardMembership.Of(native).Contains(Bond.Ethereal),"An explicit Ethereal enabler or payoff counts without requiring self-Ethereal: "+native.Id.Entry);
        var f=new Battle();var apparition=f.Add<Apparition>(PileType.Hand);
        check(CardMembership.Of(apparition).Contains(Bond.Ethereal),"An unupgraded native Ethereal card counts its actual keyword");
        apparition.UpgradeInternal();check(!CardMembership.Of(apparition).Contains(Bond.Ethereal),"Native upgrade removal of Ethereal immediately removes its membership");
        var ordinary=f.Add<DefendIronclad>(PileType.Hand);CardCmd.ApplyKeyword(ordinary,CardKeyword.Ethereal);
        check(CardMembership.Of(ordinary).Contains(Bond.Ethereal),"Applying Ethereal to an ordinary card adds its dynamic membership");
        CardCmd.RemoveKeyword(ordinary,CardKeyword.Ethereal);check(!CardMembership.Of(ordinary).Contains(Bond.Ethereal),"Removing an added Ethereal keyword removes the membership again");

        var prepared=await PrepareInherited(CardTag.Wah);f=prepared.Battle;
        await PowerCmd.Apply<MotifPower>(f.Context,f.Player.Creature,1,f.Player.Creature,null);
        await PowerCmd.Apply<ViolentChordPower>(f.Context,f.Player.Creature,3,f.Player.Creature,null);
        var drawn=f.Add<DefendIronclad>(PileType.Draw);var history=CombatManager.Instance.History.CardPlaysFinished.Count();var hp=f.Enemy.CurrentHp;
        await f.StartTurn();
        check(drawn.Pile?.Type==PileType.Hand&&hp-f.Enemy.CurrentHp==3,"Inherited Wah triggers the actual Motif draw and Violent Chord damage at acquisition");
        check(CombatManager.Instance.History.CardPlaysFinished.Count()==history&&MelodyCmd.MelodyCardsPlayedThisTurn(f.Player)==0,"Gaining an inherited melody does not fabricate a native card play");
        for(int i=0;i<3;i++)f.Add<DefendIronclad>(PileType.Draw);
        hp=f.Enemy.CurrentHp;await f.ResolveCallbacks(prepared.Song);
        check(f.Player.PlayerCombatState!.DrawPile.Cards.Count==0&&hp==f.Enemy.CurrentHp,"The repeated inherited Wah grants no second native reward while new Cho and Ki still draw normally");
        await CardPileCmd.RemoveFromCombat(f.Player.PlayerCombatState.AllCards.Where(c=>CardMembership.Of(c).Contains(Bond.Melody)).ToArray(),skipVisuals:true);
        check(f.Terminal.Tier(Bond.Melody)==0&&MelodyCmd.PlayedWahThisTurn(f.Player),"Losing the covenant tier does not erase an already acquired melody resource");
        var untouched=f.Add<DefendIronclad>(PileType.Draw);hp=f.Enemy.CurrentHp;
        await f.ResolveCallbacks(f.Add<WahTouch>(PileType.Hand));
        check(untouched.Pile?.Type==PileType.Draw&&hp==f.Enemy.CurrentHp,"Tier loss cannot duplicate native acquisition rewards for an inherited melody");
        await f.StartTurn();check(!MelodyCmd.PlayedWahThisTurn(f.Player),"The inherited melody still expires normally at the next turn when no new inheritance is active");

        prepared=await PrepareInherited(CardTag.Cho);f=prepared.Battle;
        await PowerCmd.Apply<SongPathPower>(f.Context,f.Player.Creature,4,f.Player.Creature,null);
        var block=f.Player.Creature.Block;await f.StartTurn();
        check(f.Player.Creature.Block-block==12,"Inherited Cho grants five covenant block from fully earned layers plus four native Song Path block");
        block=f.Player.Creature.Block;await f.ResolveCallbacks(f.Add<ChoFirm>(PileType.Hand));
        check(f.Player.Creature.Block==block,"Playing inherited Cho later does not receive another Song Path or covenant acquisition reward");

        prepared=await PrepareInherited(CardTag.Ki);f=prepared.Battle;
        var tune=await PowerCmd.Apply<TunePower>(f.Context,f.Player.Creature,1,f.Player.Creature,null)??throw new Exception("Tune missing");
        await f.StartTurn();await tune.AfterSideTurnEnd(f.Context,CombatSide.Player,[f.Player.Creature]);
        check(f.Player.Creature.GetPowerAmount<FreeCardPower>()==1,"Native Tune recognizes inherited Ki at turn end without a fictional play");

        prepared=await PrepareInherited(CardTag.Wah);f=prepared.Battle;
        await PowerCmd.Apply<LostLutePower>(f.Context,f.Player.Creature,1,f.Player.Creature,null);
        var played=f.Add<DefendIronclad>(PileType.Draw);history=CombatManager.Instance.History.CardPlaysFinished.Count();
        var names=new Harmony("Covenant.Tests.InheritedNames");var title=AccessTools.PropertyGetter(typeof(CardModel),nameof(CardModel.Title));
        names.Patch(title,prefix:new HarmonyMethod(typeof(MechanismChecks),nameof(PlainCardTitle)));
        try{await f.StartTurn();}finally{names.Unpatch(title,HarmonyPatchType.Prefix,names.Id);}
        check(played.Pile?.Type==PileType.Discard&&CombatManager.Instance.History.CardPlaysFinished.Count()==history+1,"Inherited melody invokes native Lost Lute autoplay and records only the real resulting card play");
        var stillInDraw=f.Add<DefendIronclad>(PileType.Draw);await f.ResolveCallbacks(f.Add<WahTouch>(PileType.Hand));
        check(stillInDraw.Pile?.Type==PileType.Draw,"Replaying the inherited melody cannot invoke Lost Lute twice");
        CombatChecks.Choose=null;
        Console.WriteLine("COVENANT_FOOL_ETHEREAL_OK arrow-support=pass mood-name-exclusion=pass dynamic-ethereal=pass inherited-motif=pass inherited-chord=pass inherited-song-path=pass inherited-tune=pass inherited-lute=pass no-fake-history=pass");
    }
}

