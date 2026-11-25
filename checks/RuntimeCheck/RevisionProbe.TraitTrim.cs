using Covenant.Core;
using Covenant.Game;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using VoidCard=MegaCrit.Sts2.Core.Models.Cards.Void;

internal static partial class RevisionProbe
{
    private static async Task TrimmedTraits(Player player,CovenantTerminal terminal,Func<Task> clear,Action<bool,string> check)
    {
        var context=new ThrowingPlayerChoiceContext();
        var combat=player.Creature.CombatState!;
        var state=player.PlayerCombatState!;
        async Task<CardModel> Add(CardModel model,PileType pile=PileType.Hand,bool upgraded=false)
        {
            var card=combat.CreateCard(model,player);
            if(upgraded)CardCmd.Upgrade(card);
            await CardPileCmd.Add(card,pile,skipVisuals:true);
            return card;
        }
        Task Play(CardModel card)=>CardCmd.AutoPlay(context,card,
            card.TargetType==TargetType.AnyEnemy?combat.HittableEnemies.First():null,skipCardPileVisuals:true);
        foreach(var upgraded in new[]{false,true})
        {
            await clear();
            await Add(ModelDb.Card<WahTouch>(),PileType.Discard);
            await Add(ModelDb.Card<ChoReady>(),PileType.Discard);
            await Play(await Add(ModelDb.Card<WahChoKi>(),upgraded:upgraded));
            await Play(await Add(ModelDb.Card<WahChoKi>(),upgraded:upgraded));
            var songs=state.Hand.Cards.Where(card=>card.Tags.Count(tag=>tag is CardTag.Wah or CardTag.Cho or CardTag.Ki)==2).ToArray();
            check(songs is [var song]&&song.EnergyCost.GetAmountToSpend()==0&&song.Keywords.Contains(CardKeyword.Exhaust)&&
                song.Keywords.Contains(CardKeyword.Retain)==upgraded,
                $"Wah Cho Ki upgraded={upgraded} makes one free, exhausting card with the expected Retain keyword");

            await clear();
            await Add(ModelDb.Card<Dazed>(),PileType.Discard);
            await Add(ModelDb.Card<Slimed>(),PileType.Discard);
            await Add(ModelDb.Card<Burn>(),PileType.Discard);
            var unrelatedVoid=await Add(ModelDb.Card<VoidCard>());
            await Play(await Add(ModelDb.Card<Turbo>(),upgraded:upgraded));
            check(player.Creature.GetPowerInstances<CovenantEffectPower>().Any(power=>power.EffectName=="内核加速")&&
                player.Creature.GetPowerAmount<EnergyNextTurnPower>()==0,"Turbo exposes its pending effect before exhausting any Void");
            await CardCmd.Exhaust(context,unrelatedVoid);
            check(player.Creature.GetPowerAmount<EnergyNextTurnPower>()==1&&
                player.Creature.GetPowerInstances<CovenantEffectPower>().All(power=>power.EffectName!="内核加速"),
                $"Turbo upgraded={upgraded} awards one delayed energy from an unrelated Void that never drained energy");
            await CardCmd.Exhaust(context,await Add(ModelDb.Card<VoidCard>()));
            check(player.Creature.GetPowerAmount<EnergyNextTurnPower>()==1,"Turbo triggers on the next Void exhaust once");
        }

        foreach(var model in new CardModel[]{ModelDb.Card<Bludgeon>(),ModelDb.Card<AstralPulse>()})
        {
            await clear();
            await Add(ModelDb.Card<Injury>(),PileType.Exhaust);
            await Add(ModelDb.Card<Doubt>(),PileType.Exhaust);
            for(var i=0;i<3;i++)await Play(await Add(ModelDb.Card<DefendIronclad>()));
            state.Energy=4;state.Stars=4;
            var energyX=await Add(ModelDb.Card<Whirlwind>());
            var starX=await Add(ModelDb.Card<Stardust>());
            var priority=await Add(model);
            var normality=await Add(ModelDb.Card<Normality>());
            check(normality.CanPlay(),"Normality is playable after its native limit");
            await Play(normality);
            var another=await Add(ModelDb.Card<DefendIronclad>());
            check(priority.CanPlay()&&!another.CanPlay()&&!energyX.CanPlay()&&!starX.CanPlay(),
                $"Normality prioritizes the fixed-cost {model.Id} over both kinds of X cards");
            var (energy,stars)=await priority.SpendResources();
            check(energy==0&&stars==0&&state.Energy==4&&state.Stars==4,$"Normality pays no energy or stars for {model.Id}");
            var target=priority.TargetType==TargetType.AnyEnemy?combat.HittableEnemies.First():null;
            await priority.OnPlayWrapper(context,target,false,new ResourceInfo{EnergySpent=energy,EnergyValue=energy,StarsSpent=stars,StarValue=stars},true);
            check(another.CanPlay()&&energyX.CanPlay()&&starX.CanPlay()&&
                (priority.EnergyCost.GetAmountToSpend()>0||priority.GetStarCostWithModifiers()>0),
                $"Normality consumes the free play and releases the priority for {model.Id}");
            check(await energyX.SpendResources()==(4,0)&&await starX.SpendResources()==(0,4),
                "X-cost cards still spend their full available energy or stars after Normality");
        }

        var greed=ModelDb.Card<HandOfGreed>();
        var basic=RevisionDescriptions.Text(greed);
        var upgradedText=RevisionDescriptions.Text(greed,preview:true);
        check(basic.Contains("每满15金币")&&upgradedText.Contains("每满20金币")&&
            !basic.Contains("判定")&&!upgradedText.Contains("任一次"),
            "Hand of Greed retains both gold intervals with the shorter wording");
        Console.WriteLine("COVENANT_TRAIT_TRIM_COMPLETE");
    }
}
