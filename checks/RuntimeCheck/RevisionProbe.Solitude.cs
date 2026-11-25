using Covenant.Core;
using Covenant.Game;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

internal static partial class RevisionProbe
{
    private static async Task Solitude(Player player,CovenantTerminal terminal,Func<Task> clear,Action<bool,string> check)
    {
        var context=new ThrowingPlayerChoiceContext();
        var combat=player.Creature.CombatState!;
        var state=player.PlayerCombatState!;
        async Task<T> Add<T>(PileType pile) where T:CardModel
        {
            var card=pile==PileType.Deck?player.RunState.CreateCard<T>(player):combat.CreateCard<T>(player);
            await CardPileCmd.Add(card,pile,skipVisuals:pile!=PileType.Hand);
            return card;
        }
        var pools=CovenantTerminal.SixPools.Append(ModelDb.CardPool<ColorlessCardPool>()).ToHashSet();
        bool Ordinary(CardModel card)=>pools.Contains(card.Pool);

        await clear();
        check(CardMembership.SolitudeSuppressed(player),"duplicate basic cards in the permanent starter deck suppress Solitude");
        var repeats=player.Deck.Cards.GroupBy(CardMembership.NameKey).SelectMany(group=>group.Skip(1)).ToArray();
        await CardPileCmd.RemoveFromDeck(repeats,showPreview:false);
        var crossColor=await Add<StrikeSilent>(PileType.Deck);
        check(CardMembership.SolitudeSuppressed(player),"the Strike from a second color has the same name");
        await CardPileCmd.RemoveFromDeck(crossColor,showPreview:false);
        await Add<Doubt>(PileType.Deck);await Add<Doubt>(PileType.Deck);
        check(!CardMembership.SolitudeSuppressed(player),"duplicate curses retain the previous deck-check exception");
        var permanent=player.Deck.Cards.ToArray();

        var zero=await Add<Finesse>(PileType.Draw);
        await Add<Bash>(PileType.Draw);
        await Add<Whirlwind>(PileType.Draw);
        await Add<Wound>(PileType.Draw);
        for(var i=0;i<5;i++)await Add<DefendIronclad>(PileType.Draw);
        check(terminal.Tier(Bond.Solitude)==0,"nine physical cards fall below Solitude I");
        await Add<DefendIronclad>(PileType.Draw);
        check(terminal.Tier(Bond.Solitude)==1&&terminal.Counts().GetValueOrDefault(Bond.Solitude)==10&&
            !CardMembership.Of(zero).Contains(Bond.Solitude),"all ten cards count without a Solitude card badge");
        await terminal.BeforeCombatStart();
        check(player.Creature.GetPowerAmount<VigorPower>()==8&&player.Creature.GetPowerAmount<StretchPower>()==8&&
            player.Creature.GetPowerAmount<PlatingPower>()==8,"opening 0, 1, 2, and X grant eight stacks each and an unplayable card adds none");

        await clear();
        var played=await Add<IronWave>(PileType.Hand);
        played.DeckVersion=player.RunState.CreateCard<IronWave>(player);
        var handTwin=await Add<IronWave>(PileType.Hand);
        var drawTwin1=await Add<IronWave>(PileType.Draw);
        var between=await Add<Bash>(PileType.Draw);
        var drawTwin2=await Add<IronWave>(PileType.Draw);
        var discardTwin=await Add<IronWave>(PileType.Discard);
        var exhaustTwin=await Add<IronWave>(PileType.Exhaust);
        for(var i=0;i<18;i++)await Add<Bash>(PileType.Exhaust);
        check(terminal.Tier(Bond.Solitude)==2,"25 cards across the four combat piles qualify for Solitude II");
        await CardCmd.AutoPlay(context,played,combat.HittableEnemies.First());
        check(played.Pile?.Type==PileType.Discard&&new[]{handTwin,drawTwin1,drawTwin2,discardTwin,exhaustTwin}.All(card=>card.Pile==null)&&
            state.Hand.Cards.Count==1&&Ordinary(state.Hand.Cards.Single())&&state.DrawPile.Cards.Count==3&&
            state.DrawPile.Cards[1]==between&&state.DrawPile.Cards.Where(card=>card!=between).All(Ordinary),
            $"playing at exactly 25 changes same-name cards and preserves positions (played {played.Pile?.Type}, hand {state.Hand.Cards.Count}, draw {state.DrawPile.Cards.Count})");
        await CardCmd.AutoPlay(context,exhaustTwin,combat.HittableEnemies.First());
        check(exhaustTwin.Pile==null&&state.ExhaustPile.Cards.Count==19,"a queued autoplay skips a card already changed by Solitude");

        await clear();
        var burned=await Add<Dazed>(PileType.Hand);
        var queued=await Add<Dazed>(PileType.Hand);
        var inDraw=await Add<Dazed>(PileType.Draw);
        var inDiscard=await Add<Dazed>(PileType.Discard);
        var exhausted=await Add<Dazed>(PileType.Exhaust);
        for(var i=0;i<20;i++)await Add<Bash>(PileType.Exhaust);
        var added=await CardCmd.Exhaust(context,burned,causedByEthereal:true);
        check(added is {success:true} result&&result.cardAdded.Pile?.Type==PileType.Exhaust&&Ordinary(result.cardAdded)&&
            new[]{burned,queued,inDraw,inDiscard,exhausted}.All(card=>card.Pile==null)&&
            CardMembership.Cards(player).Count()==25,"exhausting a Status changes every copy including the just-exhausted card");
        check(await CardCmd.Exhaust(context,queued,causedByEthereal:true)==null,
            "the native Ethereal queue skips an original already changed by Solitude");
        var endTurnBurn=await Add<Dazed>(PileType.Hand);
        await CardPileCmd.Add(endTurnBurn,PileType.Play);
        check(await CardCmd.Exhaust(context,endTurnBurn,causedByEthereal:true,skipVisuals:true)==null&&
            CovenantTerminal.TryGetCombatReplacement(endTurnBurn,out var endTurnReplacement)&&endTurnReplacement.Pile?.Type==PileType.Exhaust,
            "native end-of-turn exhaustion animates the original before changing its model");
        check(permanent.SequenceEqual(player.Deck.Cards),"combat transformations do not change the permanent deck");
        Console.WriteLine("COVENANT_SOLITUDE_COMPLETE");
    }
}
