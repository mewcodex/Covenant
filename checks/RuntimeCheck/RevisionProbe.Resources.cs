using Covenant.Core;
using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

internal static partial class RevisionProbe
{
    private static async Task Resources(SceneTree tree,Player player,CovenantTerminal terminal,Func<Task> clear,Func<CardModel,Task> play,Action<bool,string> check)
    {
        async Task<T> Add<T>(PileType pile=PileType.Hand,bool upgraded=false) where T:CardModel
        {
            var card=player.Creature.CombatState!.CreateCard<T>(player);if(upgraded)CardCmd.Upgrade(card);
            await CardPileCmd.Add(card,pile);return card;
        }
        async Task Manual(CardModel card)
        {
            var state=player.PlayerCombatState!;var before=CombatManager.Instance.History.CardPlaysFinished.Count(e=>e.CardPlay.Card==card);
            var target=card.TargetType==TargetType.AnyEnemy?player.Creature.CombatState!.HittableEnemies.First():null;
            if(!card.TryManualPlay(target))throw new Exception("Resource probe manual play rejected "+card.Id+" energy="+state.Energy+" stars="+state.Stars+" phase="+state.Phase);
            for(var i=0;i<700;i++)
            {
                if(CombatManager.Instance.History.CardPlaysFinished.Count(e=>e.CardPlay.Card==card)>before)return;
                await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
            }
            throw new Exception("Resource probe manual play did not finish "+card.Id);
        }
        await clear();terminal.Ledger.Add(Bond.Blades,150);var accuracy=await Add<Accuracy>();
        await PowerCmd.Apply<SignalBoostPower>(new ThrowingPlayerChoiceContext(),player.Creature,1,player.Creature,null);await play(accuracy);
        var shiv=await Add<Shiv>();var accuracyPower=player.Creature.GetPower<AccuracyPower>()!;var enemy=player.Creature.CombatState!.HittableEnemies.First();
        Console.WriteLine("ACCURACY_RESOURCE_DIAGNOSTIC amount="+accuracyPower.Amount+" applications="+RevisionTrace.PowerApplications(accuracyPower,accuracy)+" sources="+RevisionTrace.Sources(accuracyPower).Count()+" A="+terminal.Scale(Bond.Blades).A+" result="+accuracyPower.ModifyDamageAdditive(enemy,4,ValueProp.Move,player.Creature,shiv,null));
        check(accuracyPower.ModifyDamageAdditive(enemy,4,ValueProp.Move,player.Creature,shiv,null)==accuracyPower.Amount+2,"replaying the same physical Accuracy preserves both applications of its dynamic trait coefficient");
        await clear();var terraform=await Add<Terraforming>();for(var i=0;i<3;i++)await Add<Claw>(PileType.Exhaust);await play(terraform);await play(await Add<Claw>());
        check(!player.Creature.HasPower<CovenantTerraformingPower>()&&terminal.Ledger.Get(Bond.Zero)==2,"Terraforming grows the next attack's active covenant");
        await clear();terraform=await Add<Terraforming>();for(var i=0;i<3;i++)await Add<Claw>(PileType.Exhaust);await play(terraform);
        await PowerCmd.Remove(player.Creature.GetPower<VigorPower>()!);
        await PowerCmd.Apply<VigorPower>(new ThrowingPlayerChoiceContext(),player.Creature,3,player.Creature,null);
        await play(await Add<Claw>());
        check(terminal.Ledger.Get(Bond.Zero)==2,"changing Vigor does not affect Terraforming's separate covenant growth");
        await clear();var prep=await Add<PrepTime>();for(var i=0;i<3;i++)await Add<Inflame>(PileType.Exhaust);await play(prep);
        var prepPower=player.Creature.GetPower<PrepTimePower>()!;
        await prepPower.AfterSideTurnStart(CombatSide.Player,[player.Creature],player.Creature.CombatState!);await play(await Add<Anger>());
        check(terminal.Ledger.Permanent(Bond.Fury)==2,"Prep Time grows when its native Vigor is granted");
        await prepPower.AfterSideTurnStart(CombatSide.Player,[player.Creature],player.Creature.CombatState!);await play(await Add<Anger>());
        check(terminal.Ledger.Permanent(Bond.Fury)==4,"new Prep Time grants continue to pay without an invented per-turn cap");
        await clear();await PlayerCmd.SetStars(0,player);var solar=await Add<SolarStrike>(upgraded:true);
        for(var i=1;i<BondCatalog.Get(Bond.Stars).Basic;i++)await Add<SolarStrike>(PileType.Exhaust);
        await play(solar);
        check(player.PlayerCombatState!.Stars==2,"upgraded Solar Strike provides two native stars for the payment test");
        var paying=await Add<Anger>();paying.SetStarCostThisTurn(1);await Manual(paying);
        check(terminal.Ledger.Permanent(Bond.Stars)==5,"Solar Strike's queued Stars grant pays on the next actual expenditure");
        paying=await Add<Anger>();paying.SetStarCostThisTurn(1);await Manual(paying);
        check(terminal.Ledger.Permanent(Bond.Stars)==5,"a later payment does not duplicate Solar Strike's one-shot reward");
        await clear();await PlayerCmd.SetStars(0,player);var genesis=await Add<Genesis>();for(var i=0;i<3;i++)await Add<SolarStrike>(PileType.Exhaust);await play(genesis);
        check(terminal.Ledger.Permanent(Bond.Stars)==0,"Genesis waits for its native power to grant stars");
        await player.Creature.GetPower<GenesisPower>()!.AfterEnergyReset(player);
        check(terminal.Ledger.Permanent(Bond.Stars)==2,"Genesis grants layers when it provides the stars");
        paying=await Add<Anger>();paying.SetStarCostThisTurn(1);await Manual(paying);
        check(terminal.Ledger.Permanent(Bond.Stars)==2,"spending stars does not repeat Genesis's growth");
        paying=await Add<Anger>();paying.SetStarCostThisTurn(1);await Manual(paying);
        check(terminal.Ledger.Permanent(Bond.Stars)==2,"a second payment still does not repeat Genesis's growth");
        await clear();var friendship=await Add<Friendship>();for(var i=0;i<4;i++)await Add<Bludgeon>(PileType.Exhaust);await play(friendship);
        check(terminal.Ledger.Permanent(Bond.Heavy)==2,"Friendship grows when it raises maximum energy");
        player.PlayerCombatState!.ResetEnergy();await Hook.AfterEnergyReset(player.Creature.CombatState!,player);
        check(player.PlayerCombatState.Energy==4,"Friendship contributes one native opening energy above the three base energy");
        await Manual(await Add<Bludgeon>());
        check(terminal.Ledger.Permanent(Bond.Heavy)==2,"the energy reset and a later payment do not repeat Friendship's growth");
        var discountedHeavy=await Add<Bludgeon>();discountedHeavy.EnergyCost.SetThisTurnOrUntilPlayed(1);await Manual(discountedHeavy);
        check(terminal.Ledger.Permanent(Bond.Heavy)==2,"a later payment does not repeat Friendship's growth");
        await clear();await PlayerCmd.SetEnergy(3,player);var pounce=await Add<Pounce>();for(var i=0;i<3;i++)await Add<Bludgeon>(PileType.Exhaust);await play(pounce);
        var freeSkill=await Add<Impervious>();for(var i=0;i<8;i++)_ =freeSkill.EnergyCost.GetWithModifiers(CostModifiers.All);
        check(terminal.Ledger.Permanent(Bond.Heavy)==0,"free-skill cost previews never grant Pounce's lineage growth");
        check(terminal.EffectiveTraits().Any(e=>e.Source==pounce.Title),"a live unspent free-skill charge exposes Pounce's support trait");
        await Manual(freeSkill);
        check(terminal.Ledger.Permanent(Bond.Heavy)==4,"Pounce attributes the real free play of a printed-two-cost skill");
        check(player.PlayerCombatState!.Energy==3,"the attributed Pounce play actually spends zero native energy");
        await CardPileCmd.Add(freeSkill,PileType.Hand);await Manual(freeSkill);
        check(terminal.Ledger.Permanent(Bond.Heavy)==4,"an exhausted Pounce charge cannot reward a later paid play of the same skill");
        await clear();await PlayerCmd.SetEnergy(3,player);var synthesis=await Add<Synthesis>();for(var i=0;i<3;i++)await Add<Inflame>(PileType.Exhaust);await play(synthesis);
        await Manual(await Add<Inflame>());
        check(terminal.Ledger.Permanent(Bond.Ritual)==4,"Synthesis gives Ritual layers based on the next power's printed cost plus one");
        await clear();await PlayerCmd.SetEnergy(3,player);var unrelenting=await Add<Unrelenting>();await Add<Bludgeon>(PileType.Exhaust);await play(unrelenting);
        await Manual(await Add<Bludgeon>());
        check(terminal.Ledger.Permanent(Bond.Heavy)==6,"Unrelenting attributes a printed-three-cost attack's actual free play");
    }
}
