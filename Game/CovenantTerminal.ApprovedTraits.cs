using System.Globalization;
using System.Runtime.CompilerServices;
using Covenant.Core;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private sealed class Experience { internal int Points; }
    private sealed record SnakeCharge(int Plays=0,int Retains=0,int Bonus=0);
    private static readonly ConditionalWeakTable<CardModel,Experience> BirdExperience=new();
    private Dictionary<TheBombPower,int> _bombSpent=new();
    private Dictionary<Snakebite,SnakeCharge> _snakeCharges=new();
    private BadLuck? _savingBadLuck;
    private int[]? _birdExperienceToRestore;

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int PendingBadLuckTransforms { get; set; }

    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)]
    public int PendingBadLuckCards
    {
        get=>0;
        set{if(value>0)PendingBadLuckTransforms=Math.Max(1,PendingBadLuckTransforms);}
    }

    // Deck order supplies the identity on save; live copies refer to their own permanent card.
    [SavedProperty]
    public int[] BirdExperienceByDeckOrder
    {
        get=>_birdExperienceToRestore??(IsMutable&&Owner!=null?Owner.Deck.Cards.Where(CardMembership.IsBird).Select(GetBirdExperience).ToArray():[]);
        set=>_birdExperienceToRestore=value;
    }

    internal static int GetBirdExperience(CardModel card)=>BirdExperience.TryGetValue(Original(card),out var experience)?experience.Points:0;
    private static void SetBirdExperience(CardModel card,int points)=>BirdExperience.GetOrCreateValue(Original(card)).Points=Math.Clamp(points,0,100);

    internal void RestoreBirdExperience()
    {
        if(_birdExperienceToRestore is not {} points)return;
        foreach(var (card,experience) in Owner.Deck.Cards.Where(CardMembership.IsBird).Zip(points))SetBirdExperience(card,experience);
        _birdExperienceToRestore=null;
    }

    internal static void TransferBirdExperience(CardModel original,CardModel replacement)
    {
        if((original,replacement) is (ByrdonisEgg,ByrdSwoop) or (ByrdSwoop,GodByrd))
            SetBirdExperience(replacement,GetBirdExperience(original));
    }

    internal static void CopyBirdExperience(CardModel original,CardModel clone)
    {
        var points=GetBirdExperience(original);
        if(points>0)SetBirdExperience(clone,points);
    }

    private void ResetApprovedTraits()
    {
        _bombSpent=new();_snakeCharges=new();_savingBadLuck=null;
    }

    internal int TotalCovenantLayers()=>BondCatalog.All.Where(definition=>definition.UsesLayers).Sum(definition=>_ledger.Get(definition.Id));

    private Bond HighestGrowthBond(IReadOnlyDictionary<Bond,int> counts)=>BondCatalog.All
        .Where(definition=>definition.UsesLayers)
        .OrderByDescending(definition=>_ledger.Get(definition.Id))
        .ThenByDescending(definition=>TierAtCount(definition.Id,counts.GetValueOrDefault(definition.Id))>0)
        .ThenByDescending(definition=>counts.GetValueOrDefault(definition.Id)>0)
        .ThenBy(definition=>definition.Id).First().Id;

    private void ApplyAidAndSnakebite(CardModel card,IReadOnlyList<Bond> bonds)
    {
        if(bonds.Contains(Bond.Poison))
            foreach(var snake in Owner.PlayerCombatState!.Hand.Cards.OfType<Snakebite>().Where(HasRework))
            {
                var charge=_snakeCharges.GetValueOrDefault(snake)??new();
                var a=Scale(Bond.Poison).A;
                _snakeCharges[snake]=charge with{Plays=charge.Plays+1,Bonus=charge.Bonus+(snake.IsUpgraded?2+a/4:1+a/5)};
            }
        if(!bonds.Contains(Bond.Aid)||!Advanced(Bond.Aid,card))return;
        var scale=Scale(Bond.Aid);
        if(!Once("aid_play",1+scale.A/2))return;
        foreach(var definition in BondCatalog.All.Where(definition=>definition.UsesLayers&&Active(definition.Id,card)))
            GrantLayers(definition.Id,1+scale.A/3);
    }

    private void RewardBirdPlay(CardModel card)
    {
        var xp=Math.Min(100,GetBirdExperience(card)+(card is ByrdonisEgg?1:card is ByrdSwoop?2:3));
        SetBirdExperience(card,xp);
        if(card is ByrdonisEgg)return;
        var amount=card is ByrdSwoop?(card.IsUpgraded?2+xp/14:1+xp/15):(card.IsUpgraded?4+xp/9:2+xp/10);
        var candidates=BondCatalog.All.Where(definition=>definition.UsesLayers&&Active(definition.Id,card)).Select(definition=>definition.Id).ToList();
        var limit=card is ByrdSwoop?1:2;
        for(var i=0;i<limit&&candidates.Count>0;i++)
        {
            var index=RandomFor(CovenantRandom.CombatCard).NextInt(candidates.Count);
            GrantLayers(candidates[index],amount);
            candidates.RemoveAt(index);
        }
    }

    internal async Task PlayApprovedSpecial(PlayerChoiceContext context,CardModel card)
    {
        if(card is ByrdonisEgg){RewardBirdPlay(card);return;}
        if(card is not Guilty guilt)return;
        var original=(Guilty)Original(guilt);
        var shownCount=Math.Max(0,original.DynamicVars["Combats"].IntValue);
        await Draw(context,2);
        GrantLayers(Bond.Curse,shownCount);
        original.CombatsSeen-=3;
        guilt.CombatsSeen=original.CombatsSeen;
    }

    private async Task PrepareGrandFinale(PlayerChoiceContext context,CardModel card)
    {
        var divisor=card.IsUpgraded?15:20;
        var threshold=Math.Max(0,(card.IsUpgraded?95:100)*divisor-TotalCovenantLayers());
        var rng=RandomFor(CovenantRandom.CombatRoll);
        foreach(var original in Owner.PlayerCombatState!.DrawPile.Cards.ToArray())
        {
            if(Owner.Creature.IsDead||CombatManager.Instance.IsOverOrEnding)break;
            TryGetCombatReplacement(original,out var next);
            if(next.Pile?.Type!=PileType.Draw)continue;
            if(rng.NextInt(2)==0)await CardCmd.Discard(context,next);
            else if((await CardCmd.Exhaust(context,next,false))?.success!=true)continue;
            if(threshold>0&&!CombatManager.Instance.IsOverOrEnding&&rng.NextInt(100*divisor)<threshold)
                await CreatureCmd.Damage(context,Owner.Creature,1,ValueProp.Unpowered,Owner.Creature,card,null);
        }
    }

    internal int TakeSnakebiteBonus(Snakebite card)=>_snakeCharges.Remove(card,out var charge)?charge.Bonus:0;

    private async Task ResolveSnakebiteRetention(PlayerChoiceContext context,IReadOnlyCollection<CardModel> retained)
    {
        foreach(var card in retained.OfType<Snakebite>().Where(HasRework).ToArray())
        {
            if(Owner.Creature.IsDead||CombatManager.Instance.IsOverOrEnding)break;
            if(card.Pile?.Type!=PileType.Hand)continue;
            var charge=_snakeCharges.GetValueOrDefault(card)??new();
            charge=charge with{Retains=charge.Retains+1};
            _snakeCharges[card]=charge;
            var combat=Owner.Creature.CombatState!;
            var target=combat.HittableEnemies.OrderByDescending(enemy=>enemy.CurrentHp).FirstOrDefault();
            if(target!=null&&HookAllowsSnakebite(combat,card)&&RandomFor(CovenantRandom.CombatRoll).NextInt(100)<charge.Plays*5+charge.Retains*20)
                await PlayForCovenant(context,card,target);
        }
    }

    private static bool HookAllowsSnakebite(ICombatState combat,CardModel card)=>
        MegaCrit.Sts2.Core.Hooks.Hook.ShouldPlay(combat,card,out _,AutoPlayType.Default);

    public override async Task AfterEnergySpent(CardModel card,int amount)
    {
        if(card.Owner!=Owner||amount<=0||CombatManager.Instance.IsOverOrEnding)return;
        foreach(var bomb in Owner.Creature.GetPowerInstances<TheBombPower>().ToArray())
        {
            if(CombatManager.Instance.IsOverOrEnding)break;
            if(ReworkSource<TheBomb>(bomb) is not {} source)continue;
            var spent=_bombSpent.GetValueOrDefault(bomb)+amount;
            if(spent/3<bomb.Amount)
            {
                _bombSpent[bomb]=spent%3;
                if(spent>=3)await PowerCmd.ModifyAmount(Context,bomb,-(spent/3),Owner.Creature,source);
                continue;
            }
            _bombSpent.Remove(bomb);
            await PowerCmd.Remove(bomb);
            GrantLayers(HighestGrowthBond(Counts()),source.IsUpgraded?16:12);
            await CreatureCmd.Damage(Context,Owner.Creature.CombatState!.HittableEnemies.ToArray(),bomb.DynamicVars.Damage.BaseValue,
                ValueProp.Unpowered|ValueProp.Unblockable,Owner.Creature,source,null);
        }
        RefreshBattleStatuses();
    }

    public override bool ShouldDieLate(Creature creature)
    {
        if(creature!=Owner.Creature)return true;
        _savingBadLuck=Owner.Deck.Cards.OfType<BadLuck>().FirstOrDefault();
        return _savingBadLuck==null;
    }

    public override async Task AfterPreventingDeath(Creature creature)
    {
        if(creature!=Owner.Creature||_savingBadLuck is not {} original)return;
        _savingBadLuck=null;
        var copies=Owner.PlayerCombatState?.AllCards.OfType<BadLuck>().Where(card=>Original(card)==original).ToArray()??[];
        if(copies.Length>0)await CardPileCmd.RemoveFromCombat(copies);
        if(original.Pile?.Type==PileType.Deck)await CardPileCmd.RemoveFromDeck(original,showPreview:false);
        await CreatureCmd.SetCurrentHp(creature,creature.MaxHp);
        PendingBadLuckTransforms++;
        if(!TraitDisplay.InCombat(Owner))await ResolveBadLuckTransforms();
        RefreshBattleStatuses();
    }

    private async Task ResolveApprovedCombatEnd()
    {
        if(Owner.Creature.IsDead)return;
        var counts=Counts(deck:true);
        if(BondCatalog.Get(Bond.Aid).Tier(counts.GetValueOrDefault(Bond.Aid))>0&&_ledger.Claim("aid_combat_end"))
            GrantLayers(HighestGrowthBond(counts),6+Scale(Bond.Aid).A);
        await ResolveBadLuckTransforms();
    }

    private decimal BadLuckPercent()=>Math.Min(100m,1m+Scale(Bond.Curse).Layers/10m);
    private async Task ResolveBadLuckTransforms()
    {
        if(PendingBadLuckTransforms<=0)return;
        var curses=CurseCovenant.DiceCurses(Owner).Where(card=>card.IsAllowedInRun(Owner.RunState)).ToArray();
        var rng=RandomFor(CovenantRandom.BadLuck);
        while(PendingBadLuckTransforms>0)
        {
            PendingBadLuckTransforms--;
            if(curses.Length==0)continue;
            var eligible=Owner.Deck.Cards.Where(card=>card.Type!=CardType.Curse&&!card.Keywords.Contains(CardKeyword.Eternal)).ToList();
            var count=(int)Math.Ceiling(eligible.Count*BadLuckPercent()/100m);
            var changes=new List<CardTransformation>(count);
            for(var i=0;i<count;i++)
            {
                var source=rng.NextItem(eligible)!;
                eligible.Remove(source);
                var replacement=Owner.RunState.CreateCard(rng.NextItem(curses)!,Owner);
                changes.Add(new CardTransformation(source,replacement));
            }
            if(changes.Count>0)await CardCmd.Transform(changes,null,CardPreviewStyle.HorizontalLayout);
        }
    }

    private IEnumerable<TraitEntry> ApprovedStatus(IReadOnlyDictionary<Bond,int> counts)
    {
        if(PendingBadLuckTransforms>0)
        {
            var percent=BadLuckPercent().ToString("0.#",CultureInfo.InvariantCulture);
            yield return new(Bond.Curse,$"本场战斗结束后，将牌组中随机{percent}%的非诅咒牌变化为随机诅咒牌。","霉运","",Counter:PendingBadLuckTransforms);
        }
        foreach(var bomb in Owner.Creature.GetPowerInstances<TheBombPower>())
            if(ReworkSource<TheBomb>(bomb) is {} source)
                yield return new(Bond.Aid,$"再花费{{energyPrefix:energyIcons({3-_bombSpent.GetValueOrDefault(bomb)})}}，这枚炸弹的倒数缩短1回合。若由此引爆，伤害无视格挡并使层数最高的可叠层盟约增加{(source.IsUpgraded?16:12)}层。","炸弹","",$"剩余{bomb.Amount}回合");
        foreach(var snake in Owner.PlayerCombatState!.Hand.Cards.OfType<Snakebite>().Where(HasRework))
            yield return new(Bond.Poison,"回合结束时，此牌有概率对生命值最高的敌人打出。","蛇咬","",$"下次打出时额外施加{(_snakeCharges.GetValueOrDefault(snake)?.Bonus??0)}层中毒。");
        if(TierAtCount(Bond.Aid,counts.GetValueOrDefault(Bond.Aid))>1)
            yield return new(Bond.Aid,$"本回合还可触发{Math.Max(0,1+Scale(Bond.Aid).A/2-_turn.GetValueOrDefault("aid_play"))}次助力进阶效果。","助力","");
    }
}
