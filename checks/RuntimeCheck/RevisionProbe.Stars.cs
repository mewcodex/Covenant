using Covenant.Core;
using Covenant.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;

internal static partial class RevisionProbe
{
    private static async Task Stars(Player player,CovenantTerminal terminal,Func<Task> clear,Func<CardModel,Task> play,Action<bool,string> check)
    {
        var state=player.PlayerCombatState!;
        var combat=player.Creature.CombatState!;
        var context=new BlockingPlayerChoiceContext();
        async Task<T> Add<T>(PileType pile=PileType.Hand,bool upgraded=false) where T:CardModel
        {
            var card=combat.CreateCard<T>(player);
            if(upgraded)CardCmd.Upgrade(card);
            await CardPileCmd.Add(card,pile,CardPilePosition.Bottom);
            return card;
        }
        async Task ActivateStars(int count)
        {
            while(terminal.Counts().GetValueOrDefault(Bond.Stars)<count)await Add<Constellation>(PileType.Exhaust);
        }
        async Task Pay(CardModel card,bool auto=true)
        {
            var (energy,stars)=await card.SpendResources();
            await card.OnPlayWrapper(context,card.TargetType==TargetType.AnyEnemy?combat.HittableEnemies.First():null,auto,
                new ResourceInfo{EnergySpent=energy,EnergyValue=energy,StarsSpent=stars,StarValue=stars},true);
        }

        await clear();terminal.BeginTurn();
        await ActivateStars(BondCatalog.Get(Bond.Stars).Basic);
        await play(await Add<Venerate>());
        await play(await Add<GatherLight>());
        check(terminal.Ledger.Get(Bond.Stars)==0,"Venerate and Gather Light defer both gains");
        state.Stars=9;state.Energy=10;
        var falling=await Add<FallingStar>();var enemy=combat.HittableEnemies.First();
        await Pay(falling);
        check(state.Stars==7&&terminal.Ledger.Get(Bond.Stars)==10&&enemy.GetPowerAmount<WeakPower>()==2&&enemy.GetPowerAmount<VulnerablePower>()==2,
            "a real non-depleting payment claims both queued traits and Falling Star applies two of each debuff");
        await play(await Add<Venerate>());
        await terminal.AfterSideTurnEndLate(context,CombatSide.Player,[player.Creature]);
        var starsBefore=state.Stars;
        terminal.BeginTurn();await terminal.AfterPlayerTurnStart(context,player);
        check(state.Stars==starsBefore+1,"Stars I refunds one after spending at least two without depleting the pool");
        await Pay(await Add<CloakOfStars>());
        check(terminal.Ledger.Get(Bond.Stars)==16,"Venerate expiry prevents next-turn growth and Cloak of Stars grows unconditionally");
        while(terminal.Counts().GetValueOrDefault(Bond.Guard)<BondCatalog.Get(Bond.Guard).Basic)
            await Add<DefendIronclad>(PileType.Exhaust);
        await play(await Add<ParticleWall>());
        check(terminal.Ledger.Get(Bond.Stars)==20&&terminal.Ledger.Get(Bond.Guard)==2,
            $"Particle Wall grows both active covenants even without a star payment: {terminal.Ledger.Get(Bond.Stars)}, {terminal.Ledger.Get(Bond.Guard)}");

        await clear();terminal.BeginTurn();
        await ActivateStars(BondCatalog.Get(Bond.Stars).Advanced);
        state.Stars=30;state.Energy=11;
        var aegis=await Add<NeutronAegis>(upgraded:true);
        var cost=aegis.GetStarCostWithModifiers();
        await Pay(aegis);
        check(state.Energy==11&&state.Stars==30-cost&&player.Creature.GetPowerAmount<PlatingPower>()==11,
            "Stars II removes the energy cost of a star card while preserving its star cost and native Plating");
        var baseline=terminal.Ledger.Get(Bond.Stars);
        var spender=await Add<DefendIronclad>();spender.SetStarCostThisTurn(7);await Pay(spender);
        check(player.Creature.GetPowerAmount<AfterimagePower>()==0,"Neutron Aegis+ does not count the stars spent to cast itself");
        spender=await Add<DefendIronclad>();spender.SetStarCostThisTurn(1);await Pay(spender);
        check(player.Creature.GetPowerAmount<AfterimagePower>()==1&&terminal.Ledger.Get(Bond.Stars)==baseline+5+2*((cost%3+8)/3)&&
            player.Creature.GetPowerAmount<VigorPower>()>0,"Neutron Aegis+ and Stars II independently carry cumulative payments");

        await clear();
        await ActivateStars(BondCatalog.Get(Bond.Stars).Basic);
        terminal.Ledger.Add(Bond.Stars,250);
        state.Stars=0;
        await terminal.BeforeCombatStart();
        check(state.Stars==3,"Stars I adds one opening star at 250 layers");
        var top=await Add<Venerate>(PileType.Draw);var bottom=await Add<SolarStrike>(PileType.Draw);
        terminal.BeginTurn();var opening=state.Stars;await terminal.AfterPlayerTurnStart(context,player);
        check(top.Pile?.Type==PileType.Draw&&bottom.Pile?.Type==PileType.Discard&&state.Stars>opening,
            "the 250-layer effect autoplays the bottommost star source from the draw pile");
        for(var i=1;i<terminal.Scale(Bond.Stars).A/5;i++) terminal.BeginTurn();
        terminal.BeginTurn();await terminal.AfterPlayerTurnStart(context,player);
        check(top.Pile?.Type==PileType.Draw,"the star-source autoplay stops after the stated opening turns");

        foreach(var upgraded in new[]{false,true})
        {
            await clear();terminal.BeginTurn();
            await ActivateStars(BondCatalog.Get(Bond.Stars).Basic);
            for(var i=0;i<3;i++)await Add<GammaBlast>(PileType.Exhaust);
            await PowerCmd.Apply<WeakPower>(context,enemy,3,enemy,null);
            await PowerCmd.Apply<VulnerablePower>(context,enemy,6,enemy,null);
            await play(await Add<MeteorShower>(upgraded:upgraded));
            var weak=enemy.GetPowerAmount<WeakPower>();var vulnerable=enemy.GetPowerAmount<VulnerablePower>();
            var expected=(upgraded?(weak+vulnerable)/2:Math.Min(weak,vulnerable))+4;
            check(terminal.Ledger.Get(Bond.Stars)==expected&&terminal.Ledger.Get(Bond.Breach)==expected,
                $"Meteor Shower {(upgraded?"upgrade":"base")} uses the entire {(upgraded?"average":"minimum")}");
        }

        await clear();terminal.BeginTurn();
        await Add<TwinStrike>(PileType.Exhaust);await Add<Hellraiser>(PileType.Exhaust);
        var leading=await Add<LeadingStrike>();await play(leading);
        check(terminal.Tier(Bond.Strike)>0&&!CardMembership.IsStrikeCard(ModelDb.Card<Hellraiser>())&&
            CardMembership.Of(ModelDb.Card<Hellraiser>()).Contains(Bond.Strike),"Hellraiser counts for Strike without becoming a named Strike card");
        await Add<Shiv>(PileType.Hand);
        await play(state.Hand.Cards.OfType<Shiv>().Last());
        check(terminal.Ledger.Get(Bond.Blades)==5&&terminal.Ledger.Get(Bond.Strike)==0,"Leading Strike grants Blades on the next independent Shiv");

        await clear();terminal.BeginTurn();
        await CreatureCmd.Add<Axebot>(combat);
        var first=combat.HittableEnemies.First();var second=combat.HittableEnemies.Last();
        first.SetCurrentHpInternal(100);second.SetCurrentHpInternal(100);
        terminal.Ledger.Add(Bond.Stars,100);
        state.Stars=0;
        var knockout=await Add<KnockoutBlow>();await CardCmd.AutoPlay(context,knockout,first);
        check(first.CurrentHp==70-terminal.Scale(Bond.Stars).A&&state.Stars==0,"Knockout Blow adds scaled damage without granting stars on a nonlethal hit");
        await CreatureCmd.Kill(first);
        check(state.Stars==5,"same-turn death from another cause grants Knockout Blow's native reward");
        second.SetCurrentHpInternal(100);
        knockout=await Add<KnockoutBlow>();await CardCmd.AutoPlay(context,knockout,second);
        await terminal.AfterSideTurnEndLate(context,CombatSide.Player,[player.Creature]);
        terminal.BeginTurn();
        var spare=await CreatureCmd.Add<Axebot>(combat);
        await CreatureCmd.Kill(second);
        check(state.Stars==5,"Knockout Blow's mark expires at turn end");
        spare.SetCurrentHpInternal(1);
        await CreatureCmd.Add<Axebot>(combat);
        await CardCmd.AutoPlay(context,await Add<KnockoutBlow>(),spare);
        check(state.Stars==10,"a lethal Knockout Blow pays the native reward once");

        await clear();terminal.BeginTurn();
        for(var i=0;i<5;i++)await Add<DemonForm>(PileType.Exhaust);
        var auto=await Add<DefendIronclad>();
        var manual=await Add<StrikeIronclad>();
        await play(auto);
        var capture=new Harmony("Covenant.Stars.VakuuEnd");
        var end=AccessTools.Method(typeof(PlayerCmd),nameof(PlayerCmd.EndTurn));
        capture.Patch(end,prefix:new HarmonyMethod(typeof(RevisionProbe),nameof(RecordVakuuEnd)));
        try
        {
            VakuuEndRequests=0;state.Energy=3;
            await Pay(manual,auto:false);
            check(VakuuEndRequests==1,"autoplay from hand does not consume Vakuu's first manual-play check");
        }
        finally{capture.Unpatch(end,HarmonyPatchType.Prefix,capture.Id);}
        Console.WriteLine("COVENANT_STARS_REVISION_COMPLETE");
    }
}
