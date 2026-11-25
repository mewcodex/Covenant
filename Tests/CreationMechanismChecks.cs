using Covenant.Core;
using Covenant.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

internal static partial class MechanismChecks
{
    private static bool PlainCardTitle(CardModel __instance,ref string __result){__result=__instance.Id.Entry;return false;}
    private static async Task CreationSemantics(Action<bool,string> check)
    {
        foreach(var native in new CardModel[]{ModelDb.Card<CreativeAi>(),ModelDb.Card<HelloWorld>(),ModelDb.Card<InfiniteBlades>(),ModelDb.Card<CallOfTheVoid>(),ModelDb.Card<Calamity>(),ModelDb.Card<Nightmare>(),ModelDb.Card<Juggling>(),ModelDb.Card<MythicForm>(),ModelDb.Card<SentryMode>(),ModelDb.Card<SpectrumShift>(),ModelDb.Card<Arsenal>(),ModelDb.Card<PillarOfCreation>(),ModelDb.Card<RefineBlade>(),ModelDb.Card<Furnace>(),ModelDb.Card<Begone>(),ModelDb.Card<Charge>(),ModelDb.Card<Compact>(),ModelDb.Card<Ecstasy>(),ModelDb.Card<Entropy>()})
            check(CardMembership.Of(native).Contains(Bond.Creation),"Native generation or generation support belongs to Creation: "+native.Id.Entry);
        foreach(var native in new CardModel[]{ModelDb.Card<BoostAway>(),ModelDb.Card<CollisionCourse>(),ModelDb.Card<CrashLanding>(),ModelDb.Card<FightThrough>(),ModelDb.Card<GunkUp>(),ModelDb.Card<Jest>(),ModelDb.Card<MightyShield>(),ModelDb.Card<MightyWeapon>(),ModelDb.Card<Overclock>(),ModelDb.Card<Rummage>(),ModelDb.Card<SorrowfulStrike>(),ModelDb.Card<Turbo>(),ModelDb.Card<Vitriol>(),ModelDb.Card<Ennui>()})
            check(!CardMembership.Of(native).Contains(Bond.Creation),"Generating only status cards is not a Creation enabler: "+native.Id.Entry);
        foreach(var native in new CardModel[]{ModelDb.Card<Shiv>(),ModelDb.Card<Soul>()})
            check(!CardMembership.Of(native).Contains(Bond.Creation),"A factory helper alone is not the permanent token's own Creation effect: "+native.Id.Entry);

        var f=new Battle();f.Copies<CreativeAi>(4);f.Terminal.Ledger.Add(Bond.Creation,60);
        var ai=await PowerCmd.Apply<CreativeAiPower>(f.Context,f.Player.Creature,1,f.Player.Creature,null)??throw new Exception("Creative AI native power missing");
        await ai.BeforeHandDraw(f.Player,f.Context,f.State);
        var generated=f.Player.PlayerCombatState!.Hand.Cards.Single();
        check(generated.Type==CardType.Power&&CardMembership.IsGenerated(generated),"Native Creative AI generates a real temporary power for Ironclad");
        check(f.Player.Creature.Block==5&&CardMembership.Of(generated).Contains(Bond.Creation),"A foreign ongoing generator activates Creation and its generated card contributes to the live count");

        f=new();f.Copies<SentryMode>(4);f.Terminal.Ledger.Add(Bond.Creation,60);
        var sentry=await PowerCmd.Apply<SentryModePower>(f.Context,f.Player.Creature,1,f.Player.Creature,null)??throw new Exception("Sentry native power missing");
        await CardPileCmd.RemoveFromCombat([f.Player.PlayerCombatState!.ExhaustPile.Cards.First()],skipVisuals:true);
        check(f.Terminal.Tier(Bond.Creation)==0,"Removing a played power source can deactivate Creation's live count");
        await sentry.BeforeHandDraw(f.Player,f.Context,f.State);
        check(f.Player.PlayerCombatState.Hand.Cards.Single() is SweepingGaze&&f.Terminal.Tier(Bond.Creation)==1&&f.Player.Creature.Block==5,"An ongoing native power survives source removal and its new card can reactivate Creation");

        f=new();f.Copies<Nightmare>(10);f.Terminal.Ledger.Add(Bond.Creation,60);
        var original=f.Add<DefendIronclad>(PileType.Hand);
        var nightmare=await PowerCmd.Apply<NightmarePower>(f.Context,f.Player.Creature,3,f.Player.Creature,null)??throw new Exception("Nightmare native power missing");
        // The CLI fixture has no localization database. Only the displayed selected-card name is substituted;
        // native clone creation, cleanup and delayed generation remain unchanged.
        var names=new Harmony("Covenant.Tests.CreationNames");var title=AccessTools.PropertyGetter(typeof(CardModel),nameof(CardModel.Title));
        names.Patch(title,prefix:new HarmonyMethod(typeof(MechanismChecks),nameof(PlainCardTitle)));
        try{nightmare.SetSelectedCard(original);}finally{names.Unpatch(title,HarmonyPatchType.Prefix,names.Id);}
        // Decline the optional first-generation cycle, then choose from the three generated copies.
        CombatChecks.Choose=cards=>cards.Count==3?cards.Where(CardMembership.IsGenerated).Take(1):[];
        await nightmare.BeforeHandDraw(f.Player,f.Context,f.State);
        var clones=f.Player.PlayerCombatState!.Hand.Cards.Where(CardMembership.IsGenerated).ToArray();
        check(clones.Length==3&&clones.All(c=>c is DefendIronclad),"Native delayed Nightmare generates three actual temporary copies");
        var rewarded=clones.Single(c=>c.ShouldRetainThisTurn);
        await f.Terminal.BeforeCardPlayed(f.Play(rewarded));
        var bonus=f.Terminal.ModifyBlockAdditive(f.Player.Creature,5,MegaCrit.Sts2.Core.ValueProps.ValueProp.Move,rewarded,null);
        check(rewarded.EnergyCost.GetWithModifiers(CostModifiers.All)==0&&f.Terminal.Ledger.Get(Bond.Creation)==120&&bonus==6,$"Creation's third generation uses all sixty awarded layers for its bonus, free play and Retain; layers={f.Terminal.Ledger.Get(Bond.Creation)}, bonus={bonus}");
        check(f.Player.Creature.GetPower<NightmarePower>()==null,"The native delayed generation power is removed after resolving");

        f=new();f.Copies<AdaptiveStrike>(4);f.Terminal.Ledger.Add(Bond.Creation,60);
        original=f.Add<DefendIronclad>(PileType.Hand);var before=f.Terminal.Counts()[Bond.Creation];
        var transformed=(await CardCmd.TransformTo<MinionStrike>(original))!.Value.cardAdded;
        check(CardMembership.IsGenerated(transformed)&&f.Terminal.Counts()[Bond.Creation]==before+1,"A real temporary transformation result contributes to live temporary-card membership");
        check(f.Player.Creature.Block==5&&f.Terminal.Ledger.Get(Bond.Creation)==68,$"Native transformation dispatches exactly one generation event and one trait event per source; block={f.Player.Creature.Block}, layers={f.Terminal.Ledger.Get(Bond.Creation)}");
        CombatChecks.Choose=null;
        Console.WriteLine("COVENANT_CREATION_SEMANTICS_OK direct=pass delayed=pass ongoing-power=pass generation-support=pass status-excluded=pass transformation-generation=once native-factories=pass");
    }
}

