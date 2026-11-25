using System.Text.Json;
using Covenant.Core;
using Covenant.Game;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

internal static partial class MechanismChecks
{
    private static async Task BalanceRegression(Action<bool,string> check)
    {
        foreach(var layers in new[]{0,9,10,29,30,59,60,99,100,149,150,210,450,550,2100,int.MaxValue})
            check(new Scale(layers).A==layers/10,"Linear growth remains exact at "+layers);
        var migrated=Ledger.Load(JsonSerializer.Serialize(new{Schema=1,Layers=new Dictionary<Bond,int>{{Bond.Bones,292}}}));
        check(migrated.Get(Bond.Bones)==214&&migrated.Scale(Bond.Bones).A==7&&migrated.Remainders[Bond.Bones]==new LayerRemainder(1,2),"Legacy nonlinear save preserves its tier and exact within-tier progress");
        migrated.Add(Bond.Bones,1);var loaded=Ledger.Load(migrated.Save());
        check(loaded.Get(Bond.Bones)==215&&loaded.Remainders[Bond.Bones]==new LayerRemainder(1,2)&&Ledger.Load(loaded.Save()).Save()==loaded.Save(),"Saving and loading never migrate a ledger twice or erase residue");
        var denomination=Ledger.Load(JsonSerializer.Serialize(new{Schema=2,Layers=new Dictionary<Bond,int>{{Bond.Stars,7}},Units=new Dictionary<Bond,int>{{Bond.Stars,5}},Remainders=new Dictionary<Bond,LayerRemainder>{{Bond.Stars,new(1,3)}}}));
        check(denomination.Get(Bond.Stars)==29&&denomination.Remainders[Bond.Stars]==new LayerRemainder(1,3),"Changing only the denomination rescales whole layers and exact residue together");
        var large=new Ledger();large.Add(Bond.Strike,int.MaxValue);large.Add(Bond.Strike,1);
        check(large.Get(Bond.Strike)==int.MaxValue,"Integer storage exhaustion cannot throw during combat");

        var f=new Battle();f.Copies<BladeDance>(2);var shivs=new List<Shiv>();
        for(int i=0;i<9;i++)
        {
            var shiv=f.State.CreateCard<Shiv>(f.Player);shivs.Add(shiv);
            await CardPileCmd.AddGeneratedCardToCombat(shiv,PileType.Hand,f.Player);await f.ResolveCallbacks(shiv);
        }
        check(f.Terminal.Ledger.Get(Bond.Blades)==36,"Nine Shivs award nine own-action layers and six continuous support layers without old-token fanout");
        check(!Presentation.TraitText(shivs[0]).Contains("临时增加")&&!Presentation.TraitText(shivs[0]).Contains("至多"),"Generated-card text declares permanent uncapped growth");
        var restored=(CovenantTerminal)RelicModel.FromSerializable(f.Terminal.ToSerializable());
        check(restored.Ledger.Get(Bond.Blades)==36,"Native relic serialization retains generated-card permanent growth");
        await f.Terminal.AfterCombatVictory(new CombatRoom(f.State));
        check(f.Terminal.Scale(Bond.Blades).Layers==36,"Generated growth persists into victory settlement");

        f=new();f.Copies<Doubt>(2);f.Copies<Wound>(3);f.Copies<NoxiousFumes>(2);
        var curse=f.State.CreateCard<Doubt>(f.Player);await CardPileCmd.AddGeneratedCardToCombat(curse,PileType.Draw,f.Player);await CardPileCmd.Draw(f.Context,1,f.Player);
        var status=f.State.CreateCard<Wound>(f.Player);await CardPileCmd.AddGeneratedCardToCombat(status,PileType.Draw,f.Player);await CardPileCmd.Draw(f.Context,1,f.Player);
        check(f.Terminal.Ledger.Get(Bond.Curse)==1&&f.Terminal.Ledger.Get(Bond.Status)==1,"Generated curse and status draw traits earn permanent growth");
        var power=f.State.CreateCard<NoxiousFumes>(f.Player);await CardPileCmd.AddGeneratedCardToCombat(power,PileType.Hand,f.Player);
        var powerPlay=f.Play(power);await f.Terminal.BeforeCardPlayed(powerPlay);
        await CardPileCmd.RemoveFromCombat([power],skipVisuals:true);await f.Terminal.AfterCardPlayed(f.Context,powerPlay);
        var poisonBefore=f.Terminal.Ledger.Get(Bond.Poison);
        for(int i=0;i<3;i++)await PowerCmd.Apply<PoisonPower>(f.Context,f.Enemy,1,f.Player.Creature,power);
        check(poisonBefore==4&&f.Terminal.Ledger.Get(Bond.Poison)>poisonBefore,"A generated power grants on play and its attached support still grows after leaving the piles");

        f=new();f.Copies<StrikeIronclad>(6);f.Copies<Claw>(8);f.Copies<TrueGrit>(8);
        for(int i=0;i<15;i++)f.Add<DefendIronclad>(PileType.Draw);
        var strike=f.Add<StrikeIronclad>(PileType.Hand);var claw=f.Add<Claw>(PileType.Hand);var energy=f.Player.PlayerCombatState!.Energy;
        for(int i=0;i<9;i++)await f.ResolveCallbacks(strike);
        check(f.Player.PlayerCombatState.Energy==energy+3,"Nine Strikes trigger three complete energy payouts");
        for(int i=0;i<3;i++)await f.ResolveCallbacks(claw);
        for(int i=0;i<3;i++)await CardCmd.Exhaust(f.Context,f.Add<DefendIronclad>(PileType.Hand));
        check(f.Player.PlayerCombatState.Energy==energy+5,"Strike, Zero and Ember each pay without a global energy cap");
        await PlayerCmd.GainEnergy(1,f.Player);
        check(f.Player.PlayerCombatState.Energy==energy+6,"Native energy remains independent");

        f=new();f.Copies<Dualcast>();
        await OrbCmd.Channel<DarkOrb>(f.Context,f.Player);
        await OrbCmd.Channel<FrostOrb>(f.Context,f.Player);
        check(f.Player.PlayerCombatState!.OrbQueue.Orbs.Count==1,"A full native Channel completes before Overload appends its extra orb");
        await OrbCmd.EvokeNext(f.Context,f.Player);var block=f.Player.Creature.Block;
        await OrbCmd.EvokeNext(f.Context,f.Player);
        check(f.Player.Creature.Block>block&&f.Player.PlayerCombatState!.OrbQueue.Orbs.Count==1,"Overload terminates its own generated chain and rewards a later independent evoke in the same turn");

        f=new();for(int i=0;i<10;i++)f.Add<Bludgeon>().UpgradeInternal();f.Terminal.Ledger.Add(Bond.Mastery,30);
        var first=f.Add<Bludgeon>(PileType.Hand);first.UpgradeInternal();var nested=f.Add<Bludgeon>(PileType.Hand);nested.UpgradeInternal();
        check(f.Terminal.ModifyDamageAdditive(f.Enemy,10,ValueProp.Move,f.Player.Creature,first,null)==0,"Mastery's first-card bonus is not advertised on every hand preview");
        await f.Terminal.BeforeCardPlayed(f.Play(first));await f.Terminal.BeforeCardPlayed(f.Play(nested));
        check(f.Terminal.ModifyDamageAdditive(f.Enemy,10,ValueProp.Move,f.Player.Creature,first,null)==6&&f.Terminal.ModifyDamageAdditive(f.Enemy,10,ValueProp.Move,f.Player.Creature,nested,null)==0,"Mastery's first-card bonus locks before nested plays can claim it");

        f=new();f.Copies<SovereignBlade>(6);var retained=f.Add<DefendIronclad>(PileType.Hand);
        for(int turn=0;turn<3;turn++)
        {
            CardCmd.ApplySingleTurnRetain(retained);await f.Terminal.AfterFlush(f.Context,f.Player,[],[retained]);await f.StartTurn();await f.ResolveCallbacks(retained);
        }
        check(f.Terminal.Ledger.Get(Bond.Retain)==6,"Retain continues growing across turns instead of stopping after the first award");

        f=new();f.Copies<Glow>(6);var aegis=f.Add<NeutronAegis>(PileType.Hand);var stars=f.Player.PlayerCombatState!.Stars;
        await f.ResolveCallbacks(aegis);
        check(f.Terminal.Ledger.Get(Bond.Stars)==12&&f.Player.Creature.Block>=6,"Neutron Aegis receives substantial own-play growth and immediate protection");
        await f.ResolveCallbacks(f.Add<KnockoutBlow>(PileType.Hand));
        check(f.Player.PlayerCombatState.Stars==stars+2&&f.Terminal.Ledger.Get(Bond.Stars)==18,"Knockout Blow grants its own growth and two stars without a kill");
        Console.WriteLine("COVENANT_BALANCE_REGRESSION_OK continuous-growth=pass generated-permanent=pass migration=pass repeated-energy=pass overload-local-reentry=pass weak-card-compensation=pass");
    }
}
