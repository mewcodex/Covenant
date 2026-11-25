using Covenant.Core;
using Covenant.Game;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.ValueProps;

internal static partial class RevisionProbe
{
    private static int VakuuEndRequests;
    private static bool RecordVakuuEnd(){VakuuEndRequests++;return false;}
    private static async Task Closing(SceneTree tree,Player player,CovenantTerminal terminal,Func<Task> clear,Func<CardModel,Task> play,Action<bool,string> check)
    {
        var context=new ThrowingPlayerChoiceContext();
        async Task<T> Add<T>(PileType pile=PileType.Hand,bool upgraded=false) where T:CardModel
        {var card=player.Creature.CombatState!.CreateCard<T>(player);if(upgraded)CardCmd.Upgrade(card);await CardPileCmd.Add(card,pile);return card;}
        async Task Manual(CardModel card)
        {
            var before=CombatManager.Instance.History.CardPlaysFinished.Count();
            if(!card.TryManualPlay(card.TargetType==TargetType.AnyEnemy?player.Creature.CombatState!.HittableEnemies.First():null))throw new Exception("Closing manual play rejected: "+card.Id);
            for(var i=0;i<1200;i++)
            {
                if(CombatManager.Instance.History.CardPlaysFinished.Skip(before).Any(e=>e.CardPlay.Card==card)&&!(bool)AccessTools.Field(typeof(CovenantTerminal),"_vakuuRunning").GetValue(terminal)!)return;
                await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
            }
            throw new Exception("Closing manual play did not finish: "+card.Id);
        }
        await clear();
        // Remove the covenant relic while checking intrinsic defense witnesses: no covenant or trait can inflate them.
        await RelicCmd.Remove(terminal);
        try
        {
            var entrench=await Add<Entrench>();await CreatureCmd.GainBlock(player.Creature,15,ValueProp.Unpowered,null);
            await play(entrench);check(player.Creature.Block==30,"Tony's Entrench witness gains exactly fifteen native Block without covenant bonuses");
            await CreatureCmd.LoseBlock(context,player.Creature,player.Creature.Block,null);
            var genetic=await Add<GeneticAlgorithm>();
            for(var i=0;i<4;i++){if(i==2)CardCmd.Upgrade(genetic);await CardPileCmd.Add(genetic,PileType.Hand);await play(genetic);}
            check(genetic.CurrentBlock==15,"Genetic Algorithm reaches fifteen through its own real mixed-upgrade play history");
            var before=player.Creature.Block;await CardPileCmd.Add(genetic,PileType.Hand);await play(genetic);
            check(player.Creature.Block-before==15,"the grown Genetic Algorithm grants exactly fifteen in a single native play");
            await OrbCmd.AddSlots(player,3);for(var i=0;i<3;i++)await OrbCmd.Channel<FrostOrb>(context,player);
            before=player.Creature.Block;await play(await Add<Rainbow>());
            check(player.Creature.Block-before==15,"Rainbow's native overflow of three unfocused Frost orbs supplies exactly fifteen Block");
            OrbCmd.RemoveSlots(player,player.PlayerCombatState!.OrbQueue.Capacity);player.PlayerCombatState.OrbQueue.Clear();await OrbCmd.AddSlots(player,1);
            await OrbCmd.Channel<FrostOrb>(context,player);player.PlayerCombatState.Energy=3;
            before=player.Creature.Block;await Manual(await Add<MultiCast>());
            check(player.Creature.Block-before==15&&player.PlayerCombatState.Energy==0,"Multi-Cast's real three-energy payment evokes fifteen Block without external bonuses");
        }
        finally{AccessTools.Property(typeof(RelicModel),nameof(RelicModel.HasBeenRemovedFromState)).SetValue(terminal,false);await RelicCmd.Obtain(terminal,player);}
        await clear();var severance=await Add<Severance>();await play(severance);
        var soul=player.PlayerCombatState!.AllCards.OfType<Soul>().First();await CardPileCmd.Add(soul,PileType.Hand);await play(soul);
        check(terminal.RevisionSourceBonus(severance)==1,"Severance's actual generated Soul increments only its physical source's damage counter; actual="+terminal.RevisionSourceBonus(severance));
        await play(await Add<Soul>());check(terminal.RevisionSourceBonus(severance)==1,"an unrelated Soul does not increase Severance's counter");
        await clear();var quietus=await Add<Quietus>();await Add<HeavyShot>(PileType.Exhaust);await Add<QuickShot>(PileType.Exhaust);
        ArrowCmd.GainArrowSlot(player);await play(quietus);await play(await Add<TwinShot>());
        check(terminal.Ledger.Permanent(Bond.Arrows)==8,"Quietus awards its eight permanent layers when the enhanced native shot finishes");
        await clear();
        // Keep the command boundary observable without allowing this fixture to start an autonomous sequence of turns.
        var capture=new Harmony("Covenant.Closing.VakuuEnd");capture.Patch(AccessTools.Method(typeof(PlayerCmd),nameof(PlayerCmd.EndTurn)),prefix:new HarmonyMethod(typeof(RevisionProbe),nameof(RecordVakuuEnd)));
        try
        {
            for(var i=0;i<5;i++)await Add<DemonForm>(PileType.Exhaust);
            var first=await Add<StrikeIronclad>();var wall=await Add<ParticleWall>();player.PlayerCombatState.Energy=40;player.PlayerCombatState.Stars=40;
            var history=CombatManager.Instance.History.CardPlaysFinished.Count();await Manual(first);
            for(var i=0;i<1200&&VakuuEndRequests==0;i++)await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
            var wallPlays=CombatManager.Instance.History.CardPlaysFinished.Skip(history).Where(e=>e.CardPlay.Card==wall).ToArray();
            check(wallPlays.Length==13,"Vakuu's first-turn leftmost opt-in performs thirteen real native autoplays and stops; actual="+wallPlays.Length+" played="+AccessTools.Field(typeof(CovenantTerminal),"_vakuuPlayed").GetValue(terminal));
            check(wallPlays.Length>0&&wallPlays.All(e=>e.CardPlay.Resources.EnergySpent==wall.EnergyCost.GetWithModifiers(CostModifiers.None)&&e.CardPlay.Resources.StarsSpent==2),"Vakuu preserves actual paid energy and stars on each autoplay's native CardPlay");
            check(player.PlayerCombatState.Energy==40-first.EnergyCost.GetWithModifiers(CostModifiers.None)-13*wall.EnergyCost.GetWithModifiers(CostModifiers.None),"Vakuu pays the printed costs rather than granting free autoplays");
            check(player.PlayerCombatState.Stars==14,"Vakuu spends all twenty-six native stars for its thirteen returning-wall plays");
            check(VakuuEndRequests==1,"Vakuu requests native end-turn after reaching its declared autoplay limit");
        }
        finally{capture.Unpatch(AccessTools.Method(typeof(PlayerCmd),nameof(PlayerCmd.EndTurn)),HarmonyPatchType.Prefix,capture.Id);await clear();}
    }
}
