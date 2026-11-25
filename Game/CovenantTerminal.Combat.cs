using Covenant.Core;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private Dictionary<string,int> _turn=new();
    private Dictionary<CardModel,int> _traitUses=new(ReferenceEqualityComparer.Instance);
    private Dictionary<CardModel,int> _traitProgress=new(ReferenceEqualityComparer.Instance);
    private Dictionary<CardModel,int> _ongoingProgress=new(ReferenceEqualityComparer.Instance);
    private HashSet<string> _executing=new();
    internal int TraitProgress(CardModel card,bool ongoing=false)
    {
        var progress=ongoing?_ongoingProgress:_traitProgress;
        return progress.GetValueOrDefault(card);
    }
    internal int TraitUses(CardModel card)
    {
        if(_traitUses.TryGetValue(card,out var used))return used;
        return card.CombatState==null?_traitUses.FirstOrDefault(pair=>pair.Key.DeckVersion==card).Value:0;
    }
    private Dictionary<CardModel,CardTrait> _powerTraits=new(ReferenceEqualityComparer.Instance);
    internal IEnumerable<TraitEntry> EffectiveTraits(bool withoutNativePowers=false)
    {
        var combat=TraitDisplay.InCombat(Owner);
        if(withoutNativePowers&&!combat)yield break;
        var attachmentOrigins=_revisionAttachments.Values.SelectMany(entries=>entries).Select(entry=>entry.Origin);
        var live=CardMembership.Cards(Owner).Concat(_powerTraits.Keys).Concat(attachmentOrigins).Distinct().ToArray();
        // Victory traits use the permanent deck, including quest cards that never enter combat piles.
        bool HasVictory(CardModel card)=>CardMembership.Definition(card)?.Trait is {} trait&&(trait.Revision is {} revision
            ?(card.IsUpgraded?revision.Upgraded:revision.Base).Rules.Any(r=>r.Event=="victory")
            :trait.Effects?.Any(e=>e.Trigger=="victory")==true);
        var sources=combat?live.Concat(Owner.Deck.Cards.Where(c=>HasVictory(c)&&!live.Any(x=>x==c||x.DeckVersion==c))):Owner.Deck.Cards;
        foreach(var card in sources)
        {
            var trait=CardMembership.Definition(card)?.Trait;if(trait==null)continue;
            if(trait.Revision is {} revision)
            {
                var program=card.IsUpgraded?revision.Upgraded:revision.Base;
                if(withoutNativePowers&&RevisionTrace.HasLivePower(card,program))continue;
                var rules=program.Rules.Where(r=>!RevisionDescriptions.Direct(r)&&(!withoutNativePowers||r.Event is not ("victory" or "shop_discount"))&&RevisionSupportEffective(card,program,r,combat)).ToArray();
                if(rules.Length>0)yield return new(rules[0].Bond,RevisionDescriptions.Text(card,annotateScaling:true),Presentation.CardTitle(card),"revision:"+rules[0].Event);
                continue;
            }
            if(combat&&!TraitDisplay.UsesAuthored(trait)&&!TraitDisplay.Own(trait.Trigger)&&Active(trait.Bond))yield return new(trait.Bond,Presentation.TraitText(card,primaryOnly:true),Presentation.CardTitle(card),"legacy:"+trait.Trigger);
            if(combat&&!TraitDisplay.UsesAuthored(trait)&&_powerTraits.ContainsKey(card)&&trait.OngoingTrigger is {} ongoing&&Active(trait.Bond))
            {
                var text=Presentation.TraitText(card,primaryOnly:true);var index=text.IndexOf("打出后，",StringComparison.Ordinal);
                if(index>=0)yield return new(trait.Bond,text[index..],Presentation.CardTitle(card),"legacy:"+ongoing);
            }
            var effects=trait.Effects??[];
            for(var i=0;i<effects.Length;i++)
            {
                var effect=effects[i];
                if(TraitDisplay.Direct(effect)||effect.Scope=="combat"&&!combat||effect.Scope=="outside"&&combat||!TraitCondition(card,effect)||effect.Condition=="first"&&_authoredTurnUses.Contains((card,i)))continue;
                yield return new(effect.Bond??trait.Bond,TraitDescriptions.Text(card,effect),Presentation.CardTitle(card),$"{effect.Trigger}:{effect.Kind}:{effect.Filter}:{effect.Condition}");
            }
        }
        if(!combat)
        {
            foreach(var id in new[]{Bond.Profit,Bond.Foresight})
            {
                var definition=BondCatalog.Get(id);var tier=Tier(id);if(tier==0)continue;
                yield return new(id,definition.BasicText(Scale(id)),definition.Name,"covenant_basic");
                if(tier>1)yield return new(id,definition.AdvancedText(Scale(id)),definition.Name,"covenant_advanced");
            }
        }
    }
    private Dictionary<CardModel,decimal> _nextBonus=new(ReferenceEqualityComparer.Instance);
    private Dictionary<CardModel,decimal> _nextDamageBonus=new(ReferenceEqualityComparer.Instance);
    private Dictionary<CardModel,Dictionary<Bond,int>> _playCounts=new(ReferenceEqualityComparer.Instance);
    private HashSet<CardModel> _retained=new(ReferenceEqualityComparer.Instance);
    private HashSet<CardModel> _discarded=new(ReferenceEqualityComparer.Instance);
    private Dictionary<CardModel,int> _playEnergy=new(ReferenceEqualityComparer.Instance);
    private Dictionary<CardModel,int> _paidCostQuotes=new(ReferenceEqualityComparer.Instance);
    private Dictionary<CardModel,int> _playCostQuotes=new(ReferenceEqualityComparer.Instance);
    private HashSet<CardModel> _resolvingZero=new(ReferenceEqualityComparer.Instance);
    private CardModel? _currentCard;
    private List<CardModel> _resolving=new();
    private CardType? _previousType;
    private int _alternating;
    private bool _insideReward;
    private bool _victoryTraitsDone;
    private bool _furyDiscount;
    private decimal _nextZero;
    private decimal _nextGenerated;
    private decimal _nextStrike;
    private bool _ritualDiscountPending,_ritualDiscountThisTurn;
    private Dictionary<Bond,decimal> _nextAnyCard=new();
    private bool _boneBonus;
    private bool _bloodOpeningLoss;
    private bool _augmentingDoom;
    private bool _augmentingPoison;
    private int _turnEpoch;
    internal void ResetTransient()
    {
        _normalityPriority=new();
        _effectPowers=new();ResetSecondary();ResetRevisions();ResetCardReworks();ResetApprovedTraits();_runningTraits=new();_growthBoosts=new(ReferenceEqualityComparer.Instance);_authoredTurnUses=new();_traitPaidEnergy=0;
        _turn=new();_traitUses=new(ReferenceEqualityComparer.Instance);_powerTraits=new(ReferenceEqualityComparer.Instance);
        _traitProgress=new(ReferenceEqualityComparer.Instance);_ongoingProgress=new(ReferenceEqualityComparer.Instance);_executing=new();
        _nextBonus=new(ReferenceEqualityComparer.Instance);_nextDamageBonus=new(ReferenceEqualityComparer.Instance);_playCounts=new(ReferenceEqualityComparer.Instance);
        _retained=new(ReferenceEqualityComparer.Instance);
        _discarded=new(ReferenceEqualityComparer.Instance);_resolving=new();_currentCard=null;_previousType=null;_alternating=0;
        _playEnergy=new(ReferenceEqualityComparer.Instance);_paidCostQuotes=new(ReferenceEqualityComparer.Instance);_playCostQuotes=new(ReferenceEqualityComparer.Instance);
        _resolvingZero=new(ReferenceEqualityComparer.Instance);_creationFree=new(ReferenceEqualityComparer.Instance);
        _insideReward=false;_victoryTraitsDone=false;_furyDiscount=false;_nextZero=0;_nextGenerated=0;_nextStrike=0;_nextAnyCard=new();_pendingCardBonuses=new();_boneBonus=false;_bloodOpeningLoss=false;_augmentingDoom=false;_augmentingPoison=false;_turnEpoch=0;ResetMechanisms();
        ResetStarTraits();
        _ritualDiscountPending=false;_ritualDiscountThisTurn=false;_ritualCombatTier=0;ResetStatusStrength();
        HarmonyAtCombatStart=null;
        _storedEnergy=0;_channelOpeningCard=null;_channelStarPayments=new(ReferenceEqualityComparer.Instance);
        _growthLockedAtCombatEnd=false;
        ResetNewBonds();
        ResetFateCombat();
        _oracleActiveAtStart=null;
        _oracleOpeningPending=false;
        _spectrumPlayedColors=0;
        _reflowTriggers=0;
    }
    internal void BeginTurn()
    {
        _starsSpentThisTurn=0;
        _revisionTurnUses.Clear();_revisionTurnCounts.Clear();_revisionCardTurnCounts.Clear();_revisionTurnAccumulations.Clear();
        _authoredTurnUses.Clear();
        var opening=_turnEpoch==0;
        var openingDiscount=opening&&_furyDiscount;
        _turn.Clear();_discarded.Clear();_turnEpoch++;_alternating=0;_previousType=null;
        _reflowTriggers=0;
        _furyDiscount=openingDiscount;_boneBonus=false;BeginFoolTurn();
        _boneSummonAtStart=_boneSummonNextTurn;_boneSummonNextTurn=false;
        _ritualDiscountThisTurn=_ritualDiscountPending;_ritualDiscountPending=false;
        BeginStatusStrengthTurn();
        _ancientFree.Clear();
        if(!opening)_creationFree.Clear();
        ResetReworkTurn();
    }
    private PlayerChoiceContext Context=>new BlockingPlayerChoiceContext();
    private bool Once(string key,int limit=1){if(_turn.GetValueOrDefault(key)>=limit)return false;_turn[key]=_turn.GetValueOrDefault(key)+1;return true;}
    private int Inc(string key){return _turn[key]=_turn.GetValueOrDefault(key)+1;}
    private int EffectiveTier(Bond bond,CardModel? source=null)
    {
        if(source!=null&&_playCounts.TryGetValue(source,out var counts))return TierAtCount(bond,counts.GetValueOrDefault(bond));
        return Tier(bond);
    }
    private int CostTier(Bond bond,CardModel source)
    {
        var count=_playCounts.TryGetValue(source,out var counts)?counts.GetValueOrDefault(bond):CardMembership.CountForCost(Owner,bond);
        return TierAtCount(bond,count);
    }
    private bool Active(Bond bond,CardModel? source=null)=>EffectiveTier(bond,source)>0;
    private bool Advanced(Bond bond,CardModel? source=null)=>EffectiveTier(bond,source)>1;
    internal int EtherealAutoPlayChance()=>Advanced(Bond.Ethereal)?(int)Math.Min(100L,3L*Scale(Bond.Ethereal).A):0;
    private async Task Block(decimal amount)
    {
        if(amount<=0||Owner.Creature.IsDead)return;
        await CreatureCmd.GainBlock(Owner.Creature,amount,ValueProp.Unpowered,null);
    }
    private Task Draw(PlayerChoiceContext c,int amount=1)=>CardPileCmd.Draw(c,amount,Owner);
    private Task CovenantEnergy()=>PlayerCmd.GainEnergy(1,Owner);
    private Task Power<T>(PlayerChoiceContext c,decimal amount) where T:PowerModel=>PowerCmd.Apply<T>(c,Owner.Creature,amount,Owner.Creature,null);
    private async Task Burst(PlayerChoiceContext c,decimal amount,IEnumerable<Creature>? targets=null)
    {
        foreach(var enemy in (targets??Owner.Creature.CombatState?.HittableEnemies??[]).Where(e=>!e.IsDead).ToArray())
            await CreatureCmd.Damage(c,enemy,amount,ValueProp.Unpowered,Owner.Creature);
    }
    private async Task<CardModel?> Select(PlayerChoiceContext c,IEnumerable<CardModel> source,string prompt,bool optional=true)
    {
        var options=source.Distinct().ToArray();if(options.Length==0)return null;
        if(!optional&&options.Length==1)return options[0];
        var text=Presentation.Literal(BondDescription.Highlight(prompt));text.Add("energyPrefix",Owner.Character.CardPool.EnergyColorName);
        return (await CardSelectCmd.FromSimpleGrid(c,options,Owner,CovenantSelection.Single(text,optional))).FirstOrDefault();
    }
    private async Task RetainOne(PlayerChoiceContext c,Func<CardModel,bool>? predicate=null)
    {
        var card=await Select(c,Owner.PlayerCombatState!.Hand.Cards.Where(x=>!x.ShouldRetainThisTurn&&(predicate?.Invoke(x)??true)),"你可以选择1张手牌，使其获得保留。");
        if(card!=null)CardCmd.ApplySingleTurnRetain(card);
    }
    private async Task<CardModel?> Retrieve(PlayerChoiceContext c,IEnumerable<CardModel> options,PileType pile=PileType.Hand,int discount=0,decimal bonus=0,bool optional=true,Bond? bonusBond=null)
    {
        if(pile==PileType.Hand&&Owner.PlayerCombatState!.Hand.Cards.Count>=CardPile.MaxCardsInHand)return null;
        var prompt=(optional?"你可以将1张牌":"选择1张牌，将其")+(pile==PileType.Draw?"放到抽牌堆顶部":"放入手牌");
        if(discount>0)prompt+=$"，使其在本回合的能量消耗减少{{energyPrefix:energyIcons({discount})}}";
        if(bonus>0)prompt+=$"，并使其下次打出时造成的伤害和获得的格挡增加{bonus}点";
        var card=await Select(c,options,prompt+"。",optional);if(card==null)return null;
        if(!(await CardPileCmd.Add(card,pile,CardPilePosition.Top)).success||card.Pile?.Type!=pile)return null;
        if(discount>0)card.EnergyCost.AddThisTurn(-discount);
        if(bonus!=0&&bonusBond is {} bond)QueueCardBonus(bond,card,bonus);
        return card;
    }
    private async Task DrawThenDiscard(PlayerChoiceContext c)
    {
        await Draw(c);
        var prefs=CovenantSelection.Single(Presentation.Literal("选择1张手牌丢弃。"),optional:false);
        var selected=await CardSelectCmd.FromHandForDiscard(c,Owner,prefs,null,(AbstractModel?)_currentCard??this);
        await CardCmd.Discard(c,selected);
    }
    private async Task FireTraits(string trigger,CardModel? specific=null)
    {
        if(_insideReward)return;
        var changed=false;
        var deckCounts=trigger=="victory"?CardMembership.Counts(Owner,deck:true):null;
        var cards=specific!=null?new[]{specific}:trigger=="victory"?Owner.Deck.Cards.ToArray():trigger=="power_turn"?_powerTraits.Keys.ToArray():CardMembership.Cards(Owner).Concat(_powerTraits.Keys).Append(_currentCard).OfType<CardModel>().Distinct().ToArray();
        foreach(var card in cards)
        {
            var definition=CardMembership.Definition(card);if(definition==null)continue;
            var trait=definition.Trait;
            if(trait.Revision!=null||TraitDisplay.UsesAuthored(trait))continue;
            var attached=_powerTraits.ContainsKey(card)&&trait.OngoingTrigger==trigger;
            if(!attached&&trait.Trigger!=trigger)continue;
            if(!attached&&trait.Limit>0&&_traitUses.GetValueOrDefault(card)>=trait.Limit)continue;
            if(deckCounts!=null?BondCatalog.Get(trait.Bond).Tier(deckCounts.GetValueOrDefault(trait.Bond))==0:!Active(trait.Bond,card))continue;
            var progress=attached?_ongoingProgress:_traitProgress;
            var period=Math.Max(1,attached?trait.OngoingEvery:trait.Every);
            var next=progress.GetValueOrDefault(card)+1;
            progress[card]=next%period;
            changed=true;
            if(next<period)continue;
            var amount=(attached?trait.OngoingAmount:trait.Amount)+(card.IsUpgraded?(attached?1:trait.UpgradeAmount):0);
            _traitUses[card]=(int)Math.Min(int.MaxValue,(long)_traitUses.GetValueOrDefault(card)+1);
            GrantTraitGrowth(card,trait.Bond,amount);
            if(!attached&&trait.Extra=="aegis")await Block(6+2*Scale(trait.Bond).A);
            if(!attached&&trait.Extra=="stars")await PlayerCmd.GainStars(2,Owner);
            changed=true;
        }
        if(changed)Presentation.RefreshCardTraits();
        await FireAuthoredTraits(trigger,specific);
    }
    public int TurnLimit=>CovenantAttritionPower.LimitFor(Owner.Creature.CombatState?.Encounter?.RoomType);
    public override async Task BeforeCombatStart()
    {
        ClearBattleStatuses();
        ResetTransient();_ledger.ClearBattle();_ledger.ClearCombatUses();
        HarmonyAtCombatStart=Owner.Deck.Cards.Any(card=>card is Entropy);
        PrepareOracleOpening();
        RememberRitualTier(Counts().GetValueOrDefault(Bond.Ritual));
        if(Active(Bond.Fate))RevealTarot();
        ApplyDivineMarks();
        if(!Owner.Creature.HasPower<CovenantAttritionPower>())
        {
            var attrition=(CovenantAttritionPower)ModelDb.Power<CovenantAttritionPower>().ToMutable();attrition.EnemyTurnLimit=TurnLimit;
            await PowerCmd.Apply(Context,attrition,Owner.Creature,1,null,null);
        }
        if(Active(Bond.Fury))await Power<StrengthPower>(Context,1+Scale(Bond.Fury).A/4);
        if(Active(Bond.Resonance))
        {
            await OrbCmd.AddSlots(Owner,1);
            var focus=Scale(Bond.Resonance).A/4;
            if(focus>0)await Power<FocusPower>(Context,focus);
        }
        if(Active(Bond.Stars))await PlayerCmd.GainStars(2+Scale(Bond.Stars).A/5,Owner);
        if(Active(Bond.Bones))await OstyCmd.Summon(Context,Owner,3,this);
        if(Active(Bond.Solitude))await ApplySolitudeOpening();
        _bloodOpeningLoss=Advanced(Bond.Blood);
        if(Advanced(Bond.Poison)&&Owner.Creature.CombatState is {} combat)
        {
            var doses=Math.Max(1,Scale(Bond.Poison).A);
            for(var i=0;i<doses;i++)
            {
                var target=RandomFor(CovenantRandom.CombatTarget).NextItem(combat.HittableEnemies);
                if(target==null)break;
                await ApplyCovenantPoison(Context,target,1);
            }
        }
        await FireTraits("battle_start");
        RefreshBattleStatuses();
    }
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext c,Player player)
    {
        if(player!=Owner)return;
        if(_turnEpoch==1&&Active(Bond.Initiative))await Block(2+Scale(Bond.Initiative).A);
        if(Advanced(Bond.Guard)&&LiveRework<Barricade>() is {} barricade)
            await Block((barricade.IsUpgraded?2:1)+Scale(Bond.Guard).A);
        if(_bloodOpeningLoss)
        {
            _bloodOpeningLoss=false;
            await CreatureCmd.Damage(c,Owner.Creature,1m,ValueProp.Unpowered|ValueProp.Unblockable,Owner.Creature,null,null);
        }
        if(_vakuuControlled)await PlayerCmd.GainEnergy(1,Owner);
        await StartFoolTurn(c);
        await StartSpecialTurn(c);
        await GenerateRitualPowers();
        await RefreshStatusStrength(c);
        if(_boneRevivePending){_boneRevivePending=false;if(Advanced(Bond.Bones))await OstyCmd.Summon(c,Owner,3+2*Scale(Bond.Bones).A,this);}
        if(_boneSummonAtStart){_boneSummonAtStart=false;await OstyCmd.Summon(c,Owner,1,this);}
        await StartStarTurn(c);
        if(Advanced(Bond.Ethereal))
            await ReturnRandom(Owner.PlayerCombatState!.ExhaustPile.Cards.Where(EtherealReturnEligible));
        await FireTraits("power_turn");
        await FireTraits("turn_start");
        await RevisionObserve("turn_start");
        RefreshBattleStatuses();
    }
    public override Task AfterPlayerTurnStartLate(PlayerChoiceContext c,Player player)
    {
        if(player==Owner)RefreshBattleStatuses();
        return Task.CompletedTask;
    }
    public override async Task BeforeCardPlayed(CardPlay play)
    {
        if(play.Player!=Owner||play.PlayIndex!=0)return;
        _normalityPriority.Remove(play.Card);
        if(play.IsAutoPlay&&CardMembership.IsXCost(play.Card))
        {
            var released=ReleaseChannelEnergy(play.Card);
            if(play.Card.EnergyCost.CostsX)play.Card.EnergyCost.CapturedXValue+=released;
        }
        if((AncientFreeThisTurn(play.Card)||CreationFreeThisTurn(play.Card))&&play.Card.EnergyCost.CostsX)
            play.Card.EnergyCost.CapturedXValue=Owner.PlayerCombatState!.Energy;
        if(CreationFreeThisTurn(play.Card)&&play.Card.HasStarCostX)
            play.Card.LastStarsSpent=Owner.PlayerCombatState!.Stars;
        _playEnergy[play.Card]=play.Resources.EnergySpent;
        if(play.Card.HasStarCostX)_channelStarPayments[play.Card]=play.Resources.StarsSpent;
        _playCounts[play.Card]=CardMembership.Counts(Owner,resolving:play.Card);
        var cost=_paidCostQuotes.Remove(play.Card,out var quoted)?quoted:CardMembership.CurrentPlayCost(play.Card);
        _playCostQuotes[play.Card]=cost;
        ConsumeInitiativeDiscount(play);
        if(!play.IsAutoPlay&&IsHeavyAttack(play.Card))_bludgeonDiscount=0;
        var zero=cost==0;
        if(zero)_resolvingZero.Add(play.Card);
        _currentCard=play.Card;_resolving.Add(play.Card);
        RememberRitualTier(_playCounts[play.Card].GetValueOrDefault(Bond.Ritual));
        if(play.Card.Rarity==CardRarity.Ancient)_ancientPlayBonds[play.Card]=CardMembership.Of(play.Card);
        if(play.Card.IsUpgraded&&Once("mastery_started")&&Advanced(Bond.Mastery,play.Card))
            _nextBonus[play.Card]=_nextBonus.GetValueOrDefault(play.Card)+3+Scale(Bond.Mastery).A;
        if(CardMembership.IsPrintedHeavy(play.Card)&&play.Card.EnergyCost.GetWithModifiers(CostModifiers.None)>=3&&Active(Bond.Heavy,play.Card)&&Scale(Bond.Heavy).A>=5)Once("heavy_discount");
        TrackMelodies(play);
        _encounterDiscount.Remove(play.Card);
        if(_nextAnyCard.Count!=0){_nextBonus[play.Card]=_nextBonus.GetValueOrDefault(play.Card)+_nextAnyCard.Values.Sum();_nextAnyCard.Clear();}
        if(play.Card.Type==CardType.Attack)
        {
            _furyDiscount=false;
            if(zero&&_nextZero!=0){_nextDamageBonus[play.Card]=_nextDamageBonus.GetValueOrDefault(play.Card)+_nextZero;_nextZero=0;}
            if(CardMembership.IsGenerated(play.Card)&&_nextGenerated!=0){_nextDamageBonus[play.Card]=_nextDamageBonus.GetValueOrDefault(play.Card)+_nextGenerated;_nextGenerated=0;}
            if(CardMembership.IsStrikeCard(play.Card)&&_nextStrike!=0){_nextDamageBonus[play.Card]=_nextDamageBonus.GetValueOrDefault(play.Card)+_nextStrike;_nextStrike=0;}
        }
        if(play.Card.Type==CardType.Power)_ritualDiscountThisTurn=false;
        if(play.Resources.EnergySpent>0&&Advanced(Bond.Reflow,play.Card))await ResolveReflowDamage();
    }
    public override async Task AfterCardPlayed(PlayerChoiceContext c,CardPlay play)
    {
        if(play.Player==Owner)
        {
            if(play.Card is Shiv&&play.IsFirstInSeries)ApplyNextShiv(play.Card);
            var arrows=Scale(Bond.Arrows);
            if(arrows.Layers>=BondCatalog.ArrowsPlayAimLayers&&Active(Bond.Arrows,play.Card))
                await Power<AimPower>(c,arrows.A/BondCatalog.ArrowsPlayAimDivisor);
            _ancientFree.Remove(play.Card);
            if(play.IsLastInSeries)_creationFree.Remove(play.Card);
            await RevisionOwnPlay(play.Card);
            await ResolveBreachHit(c,play.Card);
            ObserveRotationPlay(play.Card);
            await ReworkAfterPlay(c,play);
            await ResolveFatePlay(c,play.Card);
            await ResolveAncientPlay(play.Card);
            if(play.Card.Type==CardType.Curse&&Advanced(Bond.Curse,play.Card))
            {
                await Burst(c,2+Scale(Bond.Curse).A);
                await Draw(c);
            }
        }
        if(play.Player!=Owner||!play.IsLastInSeries)return;
        var card=play.Card;var bonds=CardMembership.Of(card);var attack=card.Type==CardType.Attack;
        _rotationAttack.Remove(card);
        ApplyAidAndSnakebite(card,bonds);
        CardModel? discardedStrike=null;
        _traitPaidEnergy=play.Resources.EnergySpent;
        await FireTraits("any_play");
        if(card.Type==CardType.Power&&CardMembership.Definition(card) is {} def)_powerTraits[card]=def.Trait;
        await FireTraits("play",card);
        ResolveSpectrumPlay(card);
        await ResolveFoolPlay(c,play);
        await ResolveSpecialPlay(c,play);
        if(attack)
        {
            var attacks=Inc("attacks");
            if(Advanced(Bond.Fury,card)&&attacks%3==0){await Power<CovenantStrengthPower>(c,1+Scale(Bond.Fury).A/4);await Draw(c);}
        }
        if(CardMembership.IsStrikeCard(card))
        {
            var strikes=Inc("strikes");
            if(strikes==1&&Active(Bond.Strike,card))
                discardedStrike=RandomFor(CovenantRandom.CombatCard).NextItem(Owner.PlayerCombatState!.DiscardPile.Cards
                    .Where(x=>x!=card&&CardMembership.IsStrikeCard(x)&&!x.Keywords.Contains(CardKeyword.Unplayable)));
            if(Advanced(Bond.Strike,card)&&strikes%(Scale(Bond.Strike).A>=5?2:3)==0)
            {
                var refund=Once("strike_energy");
                await Draw(c);
                if(refund)await CovenantEnergy();
                _nextStrike+=3+Scale(Bond.Strike).A/2;
            }
        }
        if(_resolvingZero.Remove(card))
        {
            await FireTraits("zero_play");var zero=Inc("zero");
            if(Active(Bond.Zero,card)&&zero==1)
            {
                await Block(3+Scale(Bond.Zero).A);
                var state=Owner.PlayerCombatState!;
                if(state.Hand.Cards.Count<CardPile.MaxCardsInHand)
                {
                    var candidate=RandomFor(CovenantRandom.CombatCard).NextItem(state.DiscardPile.Cards.Where(x=>x!=card&&CardMembership.IsCurrentZero(x)));
                    if(candidate!=null)await CardPileCmd.Add(candidate,PileType.Hand);
                }
            }
            if(Advanced(Bond.Zero,card)&&zero%(Scale(Bond.Zero).A>=5?2:3)==0)
            {
                if(Once("zero_draw_energy"))
                {
                    await Draw(c);
                    await CovenantEnergy();
                }
                _nextZero+=3+Scale(Bond.Zero).A;
            }
        }
        if(CardMembership.IsGenerated(card)&&attack)
        {
            await FireTraits("generated_attack");var count=Inc("generated_attacks");
            if(Active(Bond.Blades,card)&&count==1)await Draw(c);
            if(Advanced(Bond.Blades,card)&&count%4==0)
            {
                await Draw(c);_nextGenerated+=2+Scale(Bond.Blades).A/2;
                if(Once("blade_retrieve"))
                {
                    var state=Owner.PlayerCombatState!;
                    var discarded=state.DiscardPile.Cards.Where(IsGeneratedAttack).ToArray();
                    await ReturnRandom(discarded.Length>0?discarded:state.ExhaustPile.Cards.Where(IsGeneratedAttack));
                }
            }
        }
        if(card.Type==CardType.Power&&Active(Bond.Ritual,card)&&Once("ritual"))
        {
            _ritualDiscountPending=true;
            await Block(4+Scale(Bond.Ritual).A);
        }
        if(card.IsUpgraded)
        {
            var upgraded=Inc("upgraded");
            if(Active(Bond.Mastery,card)&&Once("mastery"))
            {
                GrantLayers(Bond.Mastery,4);
                await Block(3+Scale(Bond.Mastery).A);
                var candidates=Owner.PlayerCombatState!.Hand.Cards.Where(x=>x.IsUpgradable).ToArray();
                var upgrades=Scale(Bond.Mastery).A>=5?2:1;
                if(candidates.Length>upgrades)
                {
                    var prompt=Presentation.Literal($"选择{upgrades}张手牌进行升级。");
                    var prefs=upgrades==1?CovenantSelection.Single(prompt):CovenantSelection.Pair(prompt,optional:false);
                    candidates=(await CardSelectCmd.FromSimpleGrid(c,candidates,Owner,prefs)).ToArray();
                }
                foreach(var selected in candidates)CardCmd.Upgrade(selected);
            }
            if(Advanced(Bond.Mastery,card)&&upgraded%3==0)
            {
                var candidate=RandomFor(CovenantRandom.CombatCard).NextItem(Owner.PlayerCombatState!.Hand.Cards
                    .Where(handCard=>!handCard.EnergyCost.CostsX&&CardMembership.CurrentPlayCost(handCard)>0));
                candidate?.EnergyCost.AddUntilPlayed(-1,reduceOnly:true);
            }
        }
        if(CardMembership.IsPrintedHeavy(card))
        {
            var heavy=Inc("heavy");if(Active(Bond.Heavy,card)&&heavy==1)await RetainOne(c,CardMembership.IsPrintedHeavy);
            if(Advanced(Bond.Heavy,card)&&heavy%2==0){await Draw(c);await CovenantEnergy();await Block(4+Scale(Bond.Heavy).A);}
        }
        var oldType=_previousType;
        if(card.Type is CardType.Attack or CardType.Skill)_alternating=oldType!=null&&oldType!=card.Type?_alternating+1:1;else _alternating=0;
        if(Active(Bond.Chain,card)&&_alternating>=3&&_alternating%3==0)
        {
            var firstChain=Once("chain");
            var advancedChain=Advanced(Bond.Chain,card);
            if(firstChain||advancedChain)
            {
                await Draw(c);await Block(3+Scale(Bond.Chain).A);
            }
            if(advancedChain)
            {
                var bonus=2+Scale(Bond.Chain).A;
                await Power<VigorPower>(c,bonus);
                await Power<StretchPower>(c,bonus);
            }
        }
        _previousType=card.Type;
        if(_retained.Contains(card)&&Active(Bond.Retain,card)&&Once("retained"))
        {GrantLayers(Bond.Retain,4);await Draw(c);await RetainOne(c);}
        if(card is SovereignBlade)await FireTraits("sword_play");
        // Reserve the discarded card before covenant draws and finish the original play's sequence before replaying it.
        if(discardedStrike?.Pile?.Type is PileType.Discard or PileType.Draw or PileType.Hand)
            await PlayForCovenant(c,discardedStrike);
    }
    public override async Task AfterCardPlayedLate(PlayerChoiceContext c,CardPlay play)
    {
        if(play.Player!=Owner||!play.IsLastInSeries)return;
        var replace=play.ResultPile!=PileType.Exhaust&&Advanced(Bond.Solitude,play.Card);
        _ancientPlayBonds.Remove(play.Card);
        _playCounts.Remove(play.Card);_nextBonus.Remove(play.Card);_nextDamageBonus.Remove(play.Card);_resolving.Remove(play.Card);_currentCard=_resolving.LastOrDefault();
        foreach(var key in _pendingCardBonuses.Keys.Where(key=>key.Card==play.Card).ToArray())_pendingCardBonuses.Remove(key);
        if(replace)await TransformSolitudeMatches(play.Card);
        RefreshBattleStatuses();
    }
    public override decimal ModifyDamageAdditive(Creature? target,decimal amount,ValueProp props,Creature? dealer,CardModel? source,CardPlay? play)
    {
        if(dealer?.IsEnemy==true)return 0;
        if(target==Owner.Creature&&dealer==target&&source is Bloodletting&&_turn.ContainsKey("lost_health"))
            return -2m;
        if(RevisionTrace.OrbPassiveBonus(Owner) is var orbBonus&&orbBonus>0)return orbBonus;
        if(RevisionTrace.Current is {Model:PowerModel power}&&RevisionTrace.Sources(power).Any(c=>c.Owner==Owner))
            return RevisionNativeBonuses.CurrentPowerBonus("power_damage_bonus");
        if((dealer!=Owner.Creature&&dealer?.PetOwner!=Owner)||source==null||source.Owner!=Owner||props.HasFlag(ValueProp.Unpowered))return 0;
        var targeted=target?.Powers.Where(p=>RevisionTrace.Sources(p).Any(c=>c.Owner==Owner))??[];
        var supported=targeted.Sum(p=>props.HasFlag(ValueProp.FromArrow)?RevisionNativeBonuses.PowerBonus(p,"power_target_arrow_bonus",subject:source):0);
        if(dealer?.PetOwner==Owner)supported+=targeted.Sum(p=>RevisionNativeBonuses.PowerBonus(p,"power_target_osty_bonus",subject:source));
        return CardBonus(source,target:target)+supported+ShivsRetainedBonus(source);
    }
    public override decimal ModifyBlockMultiplicative(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (!target.IsEnemy) return 1m;
        // Effects such as Plating already scale their stored Block amount when applied.
        if (props.HasFlag(ValueProp.Unpowered) && RevisionTrace.Current?.Model is PowerModel { ShouldScaleInMultiplayer: true } power && power.Owner?.IsEnemy == true)
            return 1m;
        return CovenantFloorScaling.CombatMultiplier(this);
    }

    public override decimal ModifyBlockAdditive(Creature target,decimal amount,ValueProp props,CardModel? source,CardPlay? play)
    {
        if(target==Owner.Creature&&RevisionTrace.Current is {Model:PowerModel power}&&RevisionTrace.Sources(power).Any(c=>c.Owner==Owner))
            return RevisionNativeBonuses.CurrentPowerBonus("power_block_bonus");
        if(target!=Owner.Creature||source==null||source.Owner!=Owner||props.HasFlag(ValueProp.Unpowered))return 0;
        var parry=source.GetType().Name=="SovereignBlade"?Owner.Creature.GetPower<ParryPower>():null;
        return CardBonus(source,block:true)+(parry==null?0:RevisionNativeBonuses.PowerBonus(parry,"power_sword_block_bonus",subject:source));
    }
    private decimal CardBonus(CardModel card,bool block=false,Creature? target=null)
    {
        decimal bonus=_nextBonus.GetValueOrDefault(card)+RevisionBonus(card,block,target);
        if(!block)bonus+=_nextDamageBonus.GetValueOrDefault(card)+_rotationAttack.GetValueOrDefault(card);
        foreach(var effect in CardMembership.Definition(card)?.Trait.Effects??[])
            if(effect.Kind==(block?"self_block":"self_damage")&&TraitCondition(card,effect))bonus+=TraitAmount(card,effect)+Scale(effect.Bond??CardMembership.Definition(card)!.Trait.Bond).A;
        // These bonuses only read state. Reuse the play snapshot, or one live count for the entire preview.
        _playCounts.TryGetValue(card,out var counts);
        bool HasBond(Bond bond)=>TierAtCount(bond,(counts??=Counts()).GetValueOrDefault(bond))>0;
        if(!block&&CardMembership.IsStrikeCard(card)&&HasBond(Bond.Strike))bonus+=2+Scale(Bond.Strike).A/2;
        if(!block&&CardMembership.IsGenerated(card)&&card.Type==CardType.Attack&&HasBond(Bond.Blades))bonus+=1+Scale(Bond.Blades).A/2;
        if(card.Keywords.Contains(CardKeyword.Ethereal)&&HasBond(Bond.Ethereal))bonus+=2+Scale(Bond.Ethereal).A;
        if(!block&&Scale(Bond.Melody).Layers>=250&&Melodies.Any(card.Tags.Contains)&&HasBond(Bond.Melody))
            bonus+=Scale(Bond.Melody).A/2;
        if(CardMembership.IsPrintedHeavy(card)&&HasBond(Bond.Heavy))bonus+=3+Scale(Bond.Heavy).A;
        if(CardMembership.IsEncounter(card)&&HasBond(Bond.Encounter))bonus+=2+Scale(Bond.Encounter).A;
        if(_retained.Contains(card)&&HasBond(Bond.Retain))bonus+=2+Scale(Bond.Retain).A;
        return bonus;
    }
    public override bool TryModifyEnergyCostInCombat(CardModel card,decimal originalCost,out decimal modifiedCost)
    {
        modifiedCost=originalCost;if(card.Owner!=Owner)return false;
        if(Scale(Bond.Heavy).A>=5&&card.EnergyCost.GetWithModifiers(CostModifiers.None)>=3&&_turn.GetValueOrDefault("heavy_discount")==0&&CostTier(Bond.Heavy,card)>0)modifiedCost--;
        if(_furyDiscount&&card.Type==CardType.Attack)modifiedCost--;
        if(IsHeavyAttack(card))modifiedCost-=_bludgeonDiscount;
        if(_ritualDiscountThisTurn&&card.Type==CardType.Power)modifiedCost--;
        if(_encounterDiscount.Contains(card))modifiedCost--;
        if(card is Reanimate&&_ostyWasDowned)modifiedCost-=2;
        modifiedCost=Math.Max(0,modifiedCost);return modifiedCost!=originalCost;
    }
    public override async Task AfterBlockGained(Creature creature,decimal amount,ValueProp props,CardModel? source)
    {
        if(creature!=Owner.Creature||source==null||source.Owner!=Owner||!Active(Bond.Guard,source))return;
        var scale=Scale(Bond.Guard);
        if(Once("guard"))await Block(2+scale.A);
        if(scale.Layers>=250)await Block(scale.A/3);
    }
    public override async Task AfterCardDrawn(PlayerChoiceContext c,CardModel card,bool fromHandDraw)
    {
        if(card.Owner!=Owner||CardMembership.IsMarker(card))return;
        await ObserveCovenantDraw(card,fromHandDraw);
        if(card==_channelOpeningCard)await CovenantEnergy();
        var curseTier=card.Type==CardType.Curse?CardMembership.CurseTier(Owner):0;
        await FireTraits("draw",card);
        if(curseTier>0)
            await Block(2+Scale(Bond.Curse).A/2);
        if(card.Type==CardType.Status&&card.Pile?.Type==PileType.Hand&&Active(Bond.Status)&&Once("status"))
        {
            var block=3+Scale(Bond.Status).A;
            if(await CovenantSelection.ConfirmStatusDiscard(c,card,block,this)&&card.Pile?.Type==PileType.Hand&&!CombatManager.Instance.IsOverOrEnding)
            {
                await CardCmd.Discard(c,card);
                await Block(block);
            }
        }
    }
    public override async Task AfterCardExhausted(PlayerChoiceContext c,CardModel card,bool causedByEthereal)
    {
        if(card.Owner!=Owner)return;
        var replace=SolitudeCanReplaceExhaustedPlayCard(card);
        if(CardMembership.IsMarker(card))
        {
            if(replace)await TransformSolitudeMatches(card);
            return;
        }
        await ReworkExhausted(c,card);
        await FireTraits("self_exhaust",card);await FireTraits("exhaust");
        if(Active(Bond.Ember))
        {
            var count=Inc("exhausts");
            if(count==1||Scale(Bond.Ember).Layers>=250)
            {
                var source=c.ModelStack?.OfType<CardModel>().FirstOrDefault()??_currentCard;
                var paid=source?.Owner==Owner?_playEnergy.GetValueOrDefault(source):0;
                var chance=(long)paid*Math.Min(50,10+4*Scale(Bond.Ember).A);
                if(chance>0&&RandomFor(CovenantRandom.CombatRoll).NextInt(100)<chance)
                    await ReturnRandom(Owner.PlayerCombatState!.ExhaustPile.Cards.Where(x=>x.Type is CardType.Attack or CardType.Skill));
            }
            if(Advanced(Bond.Ember))await Block(2+Scale(Bond.Ember).A/2);
            if(Advanced(Bond.Ember)&&count%3==0)await CovenantEnergy();
        }
        if(causedByEthereal)await FireTraits("ethereal");
        if(replace)await TransformSolitudeMatches(card);
        RefreshBattleStatuses();
    }
    public override async Task AfterCardDiscarded(PlayerChoiceContext c,CardModel card)
    {
        if(card.Owner!=Owner||_insideReward)return;_discarded.Add(card);await FireTraits("self_discard",card);await FireTraits("discard");var count=Inc("discards");
        if(Active(Bond.Sleight)&&count==1){await Block(3+Scale(Bond.Sleight).A);await DrawThenDiscard(c);}
        if(Advanced(Bond.Sleight)&&count%3==0)
        {
            if(Once("sleight_retrieve"))await Retrieve(c,_discarded.Where(x=>x.Pile?.Type==PileType.Discard),discount:1);
            await Draw(c);AddNextCardBonus(Bond.Sleight,2+Scale(Bond.Sleight).A);
        }
        RefreshBattleStatuses();
    }
    public override async Task AfterPowerAmountChanged(PlayerChoiceContext c,PowerModel power,decimal amount,Creature? applier,CardModel? source)
    {
        if(amount<=0)return;
        await ApplyBreachBuff(c,power,applier,source);
        if(applier==Owner.Creature&&power is PoisonPower&&(source==null||source.Owner==Owner))await FireTraits("poison");
        if(applier==Owner.Creature&&power is DoomPower&&power.Owner.Side!=Owner.Creature.Side)await FireTraits("doom");
        if(power is StrengthPower&&power.Owner==Owner.Creature&&Active(Bond.Fury,source)&&Once("fury")){_furyDiscount=true;RefreshBattleStatuses();}
        if(applier!=Owner.Creature)return;
        if(power is PoisonPower&&!_augmentingPoison&&(source==null||source.Owner==Owner))
        {
            var wasUnpoisoned=power.Amount-amount<=0;
            if(Active(Bond.Poison,source)&&Once("poison_bonus",3))await ApplyCovenantPoison(c,power.Owner,1+Scale(Bond.Poison).A/2);
            if(Active(Bond.Poison,source)&&power.Owner.Side!=Owner.Creature.Side&&(wasUnpoisoned||Scale(Bond.Poison).Layers>=250)&&Once("poison_weak"))
                await PowerCmd.Apply<WeakPower>(c,power.Owner,1,Owner.Creature,null);
        }
        if(power is DoomPower&&power.Owner.IsEnemy&&!_augmentingDoom)
        {
            if(Active(Bond.Doom,source))
            {
                var first=Once("doom");
                if(first||Advanced(Bond.Doom,source))
                {
                    _augmentingDoom=true;
                    try{await PowerCmd.Apply<DoomPower>(c,power.Owner,1+Scale(Bond.Doom).A/2,Owner.Creature,null);}
                    finally{_augmentingDoom=false;}
                }
                if(first)
                {
                    await Draw(c);
                    if(Owner.PlayerCombatState!.Hand.Cards.Count==0)return;
                    var prefs=CovenantSelection.Single(Presentation.Literal("选择1张手牌放到抽牌堆顶部。"));
                    var selected=(await CardSelectCmd.FromHand(c,Owner,prefs,null,(AbstractModel?)source??this)).FirstOrDefault();
                    if(selected!=null)await CardPileCmd.Add(selected,PileType.Draw,CardPilePosition.Top);
                }
            }
        }
    }
    public override Task BeforeSideTurnEndVeryEarly(PlayerChoiceContext c,CombatSide side,IEnumerable<Creature> participants)
        =>side==CombatSide.Player&&participants.Contains(Owner.Creature)&&!Owner.Creature.IsDead?EndFoolTurn(c):Task.CompletedTask;
    public override async Task AfterFlush(PlayerChoiceContext c,Player player,IReadOnlyCollection<CardModel> flushed,IReadOnlyCollection<CardModel> retained)
    {
        if(player!=Owner)return;
        _retained=new HashSet<CardModel>(retained,ReferenceEqualityComparer.Instance);
        _rotationRetained.UnionWith(retained.Where(_rotationRetention.ContainsKey));
        ReworkAfterFlush(player,retained);
        await ResolveSnakebiteRetention(c,retained);
    }
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext c,CombatSide side,IEnumerable<Creature> participants)
    {
        if(side==CombatSide.Player&&Advanced(Bond.Resonance))
        {
            var orb=Owner.PlayerCombatState!.OrbQueue.Orbs.FirstOrDefault();
            if(orb!=null)for(var i=0;i<1+Scale(Bond.Resonance).A/4;i++)await OrbCmd.Passive(c,orb,null);
        }
        if(side==CombatSide.Player&&Advanced(Bond.Doom))
        {
            var enemy=Owner.Creature.CombatState?.HittableEnemies.OrderByDescending(e=>e.GetPowerAmount<DoomPower>()).FirstOrDefault();
            if(enemy!=null)await Burst(c,4+Scale(Bond.Doom).A+Math.Floor(enemy.GetPowerAmount<DoomPower>()/4m),new[]{enemy});
        }
    }
    public override async Task AfterCombatEnd(CombatRoom room)
    {
        // The native cleanup removes powers between AfterCombatEnd and AfterCombatVictory.
        _growthLockedAtCombatEnd=Owner.Creature.GetPower<CovenantAttritionPower>()?.GrowthLocked==true;
        await ResolveApprovedCombatEnd();
        await ResolvePerfectedStrikes();
        if(!Owner.Creature.IsDead&&BondCatalog.Get(Bond.Blood).Tier(Counts(deck:true).GetValueOrDefault(Bond.Blood))>=2)
            await CreatureCmd.Heal(Owner.Creature,1+Scale(Bond.Blood).A/2);
        ClearBattleStatuses();
        _revisionExhaustAtEnd=Owner.PlayerCombatState?.ExhaustPile.Cards.Select(Original).ToHashSet<CardModel>(ReferenceEqualityComparer.Instance)??new(ReferenceEqualityComparer.Instance);
        _storedEnergy=0;_channelOpeningCard=null;_channelStarPayments.Clear();
        _ritualCombatTier=0;
        HarmonyAtCombatStart=null;
        _ledger.ClearBattle();ResetSecondary();Presentation.RefreshCardTraits();
    }
    public override async Task AfterCombatVictory(CombatRoom room)
    {
        if(_victoryTraitsDone)return;
        _victoryTraitsDone=true;
        try
        {
            if(BondCatalog.Get(Bond.Profit).Tier(Counts(deck:true).GetValueOrDefault(Bond.Profit))>=2)
            {
                var scale=Scale(Bond.Profit);
                var multiplier=scale.Layers>=BondCatalog.ProfitVictoryLayers&&room.RoomType is RoomType.Elite or RoomType.Boss?2:1;
                await PlayerCmd.GainGold((6+scale.A)*multiplier,Owner);
            }
            await RevisionObserve("victory");
            var bankedGold=Math.Min(int.MaxValue,_revisionBankedGold.Values.Sum(amount=>(long)amount));
            if(bankedGold>0)await PlayerCmd.GainGold(bankedGold,Owner);
            await FireTraits("victory");
        }
        finally{ResetTransient();_victoryTraitsDone=true;Presentation.RefreshCardTraits();}
    }
}
