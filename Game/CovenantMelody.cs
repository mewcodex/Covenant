using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace Covenant.Game;

internal static class CovenantMelody
{
    private static readonly CardTag[] Tags=[CardTag.Wah,CardTag.Cho,CardTag.Ki];
    internal static void Install(Harmony harmony)
    {
        // These two native powers count history directly. During an inherited-melody turn, use the
        // same first-acquisition predicate as the native song-specific powers instead.
        foreach(var type in new[]{typeof(MotifPower),typeof(LostLutePower)})
            harmony.Patch(AccessTools.Method(type,nameof(PowerModel.AfterCardPlayed)),prefix:new HarmonyMethod(typeof(CovenantMelody),nameof(AfterCardPlayed)));
    }
    private static bool AfterCardPlayed(PowerModel __instance,PlayerChoiceContext choiceContext,CardPlay cardPlay,ref Task __result)
    {
        if(!__instance.Owner.IsPlayer||CovenantTerminal.For(__instance.Owner.Player)?.HasInheritedMelodyThisTurn!=true)return true;
        var count=cardPlay.Player==__instance.Owner.Player?Tags.Count(tag=>cardPlay.Card.Tags.Contains(tag)&&MelodyCmd.IsFirstPlayThisTurn(cardPlay,tag)):0;
        __result=count==0?Task.CompletedTask:Apply(__instance,choiceContext,count);return false;
    }
    internal static async Task Inherit(PlayerChoiceContext context,Player player,CardTag tag)
    {
        var powers=player.Creature.CombatState!.IterateHookListeners().OfType<PowerModel>().Where(p=>p.Owner==player.Creature&&
            (p is MotifPower or LostLutePower||p is SongPathPower&&tag==CardTag.Cho||p is ViolentChordPower&&tag==CardTag.Wah||p is TunePower&&tag==CardTag.Ki)).ToArray();
        foreach(var power in powers)await Apply(power,context,1);
    }
    private static async Task Apply(PowerModel power,PlayerChoiceContext context,int count)
    {
        context.PushModel(power);
        try
        {
            AccessTools.Method(typeof(PowerModel),"Flash").Invoke(power,null);
            var player=power.Owner.Player!;
            switch(power)
            {
                case MotifPower:await CardPileCmd.Draw(context,count*power.Amount,player);break;
                case LostLutePower:await PlayFromDeck(context,player,count*power.Amount);break;
                case SongPathPower:await CreatureCmd.GainBlock(power.Owner,power.Amount,ValueProp.Unpowered,null);break;
                case ViolentChordPower:await CreatureCmd.Damage(context,power.Owner.CombatState!.HittableEnemies,power.Amount,ValueProp.Unpowered,power.Owner);break;
                case TunePower tune:await UpgradeForInheritedKi(tune,player);break;
            }
            power.InvokeExecutionFinished();
        }
        finally{context.PopModel(power);}
    }

    private static async Task UpgradeForInheritedKi(TunePower power,Player player)
    {
        if(CovenantTerminal.For(player) is not {} terminal)return;
        var upgraded=player.PlayerCombatState!.Hand.Cards.Where(card=>card.IsUpgradable)
            .TakeRandom(power.Amount,terminal.RandomFor(CovenantRandom.CombatCard)).ToArray();
        foreach(var card in upgraded)CardCmd.Upgrade(card);
        if(upgraded.Length==0)return;
        foreach(var source in RevisionTrace.Sources(power).Where(card=>card.Owner==player))
        {
            var frame=new RevisionFrame(source,"native") {NativePower=power};
            foreach(var card in upgraded)frame.AddCard("upgraded",card);
            await terminal.FireRevision(frame);
        }
    }

    private static async Task PlayFromDeck(PlayerChoiceContext context,Player player,int count)
    {
        if(CombatManager.Instance.IsOverOrEnding||CovenantTerminal.For(player) is not {} terminal)return;
        var cards=new List<CardModel>(count);
        for(var i=0;i<count;i++)
        {
            await CardPileCmd.ShuffleIfNecessary(context,player);
            if(player.PlayerCombatState!.DrawPile.Cards.FirstOrDefault() is not {} card)break;
            cards.Add(card);
            await CardPileCmd.Add(card,PileType.Play);
        }
        foreach(var card in cards)
        {
            if(player.Creature.IsDead)break;
            card.ExhaustOnNextPlay=false;
            await terminal.PlayForCovenant(context,card);
        }
    }
}
