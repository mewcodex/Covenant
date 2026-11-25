using Covenant.Core;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace Covenant.Game;

public sealed partial class CovenantTerminal
{
    private Dictionary<(Bond Bond,string Name),CovenantEffectPower> _effectPowers=new();
    private Dictionary<(Bond Bond,CardModel Card,bool DamageOnly),decimal> _pendingCardBonuses=new();

    private void AddNextCardBonus(Bond bond,decimal amount)=>_nextAnyCard[bond]=_nextAnyCard.GetValueOrDefault(bond)+amount;

    private void QueueCardBonus(Bond bond,CardModel card,decimal amount,bool damageOnly=false)
    {
        var bonus=damageOnly?_nextDamageBonus:_nextBonus;
        bonus[card]=bonus.GetValueOrDefault(card)+amount;
        var key=(bond,card,damageOnly);
        _pendingCardBonuses[key]=_pendingCardBonuses.GetValueOrDefault(key)+amount;
    }

    internal static void InstallStatusRefresh(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(PowerCmd),nameof(PowerCmd.Remove),[typeof(PowerModel)]),
            postfix:new HarmonyMethod(typeof(CovenantTerminal),nameof(AfterNativePowerRemoved)));
        harmony.Patch(AccessTools.Method(typeof(CardPileCmd),nameof(CardPileCmd.RemoveFromCombat),[typeof(IEnumerable<CardModel>),typeof(bool)]),
            postfix:new HarmonyMethod(typeof(CovenantTerminal),nameof(AfterNativeCardsRemoved)));
    }

    private static void AfterNativeCardsRemoved(IEnumerable<CardModel> cards)
    {
        // Native removal clears the pile before its first await.
        foreach(var player in cards.Select(card=>card.Owner).Distinct())For(player)?.RefreshBattleStatuses();
    }

    private static void AfterNativePowerRemoved(PowerModel? power)
    {
        if(power is null or CovenantEffectPower||power.Owner.CombatState is not {} combat)return;
        foreach(var player in combat.Players)For(player)?.RefreshBattleStatuses();
    }

    private void ClearBattleStatuses()
    {
        if(IsMutable&&Owner?.Creature is {} creature)
            foreach(var power in _effectPowers.Values.Where(creature.Powers.Contains).ToArray())power.RemoveInternal();
        _effectPowers=new();
    }

    internal void RefreshBattleStatuses()
    {
        if(!TraitDisplay.InCombat(Owner)||CombatManager.Instance.IsOverOrEnding)return;
        var counts=Counts();
        var active=BattleTraitStatus(counts).Concat(PendingCovenantEffects(counts)).GroupBy(entry=>(entry.Bond,entry.Source))
            .ToDictionary(group=>group.Key,group=>(
                Text:string.Join("\n",group.GroupBy(StatusDescription).Select(effect=>
                    effect.Count()==1?effect.Key:$"{effect.Key}\n此效果有{effect.Count()}份。")),
                Counter:group.FirstOrDefault(entry=>entry.Counter.HasValue)?.Counter));
        var creature=Owner.Creature;
        foreach(var (key,power) in _effectPowers.ToArray())
        {
            if(active.ContainsKey(key)&&creature.Powers.Contains(power))continue;
            if(creature.Powers.Contains(power))power.RemoveInternal();
            _effectPowers.Remove(key);
        }
        foreach(var (key,effect) in active)
        {
            if(!_effectPowers.TryGetValue(key,out var power))
            {
                power=(CovenantEffectPower)ModelDb.Power<CovenantEffectPower>().ToMutable();
                power.IconBond=key.Bond;
                power.EffectName=key.Source;
                power.EffectText=effect.Text;
                power.SetCounter(effect.Counter);
                // A display-only power uses native status UI without creating a power-grant gameplay event.
                power.ApplyInternal(creature,1,silent:true);
                _effectPowers.Add(key,power);
            }
            else
            {
                power.EffectText=effect.Text;
                power.SetCounter(effect.Counter);
            }
        }
    }

    private static string StatusDescription(TraitEntry entry)=>string.IsNullOrEmpty(entry.State)?entry.Text:$"{entry.Text}\n{entry.State}";

    private IEnumerable<TraitEntry> PendingCovenantEffects(IReadOnlyDictionary<Bond,int> counts)
    {
        static TraitEntry Entry(Bond bond,string text)=>new(bond,text,BondCatalog.Get(bond).Name,"");
        foreach(var entry in ApprovedStatus(counts))yield return entry;
        foreach(var entry in FateStatus(counts))yield return entry;
        if(TierAtCount(Bond.Spectrum,counts.GetValueOrDefault(Bond.Spectrum))>0)
            yield return Entry(Bond.Spectrum,SpectrumProgress());
        if(TierAtCount(Bond.Reflow,counts.GetValueOrDefault(Bond.Reflow))>1)
        {
            var damage=ReflowDamage;
            var text=Scale(Bond.Reflow).Layers>=250
                ?$"下次花费能量打出牌时，对所有敌人造成{damage}点伤害。若只有1名敌人，额外对其造成{damage/2}点伤害。"
                :$"下次花费能量打出牌时，对随机敌人造成{damage}点伤害。";
            yield return Entry(Bond.Reflow,text+"回合结束时复原。");
        }
        if(FirstNormalityPriority() is {} priority)
            yield return new(Bond.Chain,$"你必须先打出【{Presentation.CardTitle(priority)}】，这次可以免费打出。","凡庸","");
        if(_extraTurnPending)yield return Entry(Bond.Rotation,"本回合结束后，进行一个额外回合。");
        if(!_initiativeDiscountUsed&&_turnEpoch==1&&TierAtCount(Bond.Initiative,counts.GetValueOrDefault(Bond.Initiative))>1)
            yield return Entry(Bond.Initiative,"本回合，你打出的第一张能量消耗为2或3的牌少消耗{energyPrefix:energyIcons(1)}。");
        if(TierAtCount(Bond.Guard,counts.GetValueOrDefault(Bond.Guard))>1&&LiveRework<MegaCrit.Sts2.Core.Models.Cards.Barricade>() is {} barricade)
            yield return new(Bond.Guard,$"每回合开始时获得{(barricade.IsUpgraded?2:1)+Scale(Bond.Guard).A}点格挡。","壁垒","");
        foreach(var (card,layers) in _rotationTop.Where(pair=>pair.Key.Pile?.Type==PileType.Draw))
            yield return new(Bond.Rotation,$"下次抽到【{Presentation.CardTitle(card)}】时，使其获得保留，并使已激活的【轮转】增加{layers}层。","光子切割","");
        foreach(var (card,layers) in _rotationRetention.Where(pair=>pair.Key.Pile?.IsCombatPile==true))
            yield return new(Bond.Rotation,$"【{Presentation.CardTitle(card)}】{(_rotationRetained.Contains(card)?"下次打出时":"被保留后打出时")}，使已激活的【轮转】增加{layers}层。","独门技术","");
        foreach(var (card,damage) in _rotationAttack.Where(pair=>pair.Key.Pile?.IsCombatPile==true))
            yield return new(Bond.Rotation,$"【{Presentation.CardTitle(card)}】下次打出时每次额外造成{damage}点伤害。","扫荡射线","");
        if(_storedEnergy>0)
            yield return new(Bond.Channel,$"你已积蓄{_storedEnergy}点储能。倾注处于Ⅱ阶段时，打出X费牌前，将所有储能转回能量。","储能","",Counter:_storedEnergy);
        if(_channelOpeningCard is {} chosen&&chosen.Pile?.IsCombatPile==true)
            yield return Entry(Bond.Channel,$"每次抽到【{Presentation.CardTitle(chosen)}】时，获得{{energyPrefix:energyIcons(1)}}。");
        foreach(var (bond,amount) in _nextAnyCard)
            yield return Entry(bond,$"你打出的下一张牌造成的伤害和获得的格挡增加{amount}点。");
        if(_furyDiscount)yield return Entry(Bond.Fury,"你本回合打出的下一张攻击牌的能量消耗减少{energyPrefix:energyIcons(1)}。");
        if(_nextZero>0)yield return Entry(Bond.Zero,$"你打出的下一张能量费用为0{{energyPrefix:energyIcons(1)}}的攻击牌额外造成{_nextZero}点伤害。");
        if(_nextGenerated>0)yield return Entry(Bond.Blades,$"你打出的下一张战斗中生成的攻击牌额外造成{_nextGenerated}点伤害。");
        if(_nextStrike>0)yield return Entry(Bond.Strike,$"你打出的下一张打击牌每次额外造成{_nextStrike}点伤害。");
        if(_ritualDiscountPending)yield return Entry(Bond.Ritual,"你下回合打出的第一张能力牌的能量消耗减少{energyPrefix:energyIcons(1)}。");
        if(_ritualDiscountThisTurn)yield return Entry(Bond.Ritual,"你本回合打出的第一张能力牌的能量消耗减少{energyPrefix:energyIcons(1)}。");
        if(_nextMelodyPending)yield return Entry(Bond.Melody,"下回合开始时，随机获得一种旋律。");
        foreach(var entry in StarTraitStatuses())yield return entry;
        if(_boneRevivePending)yield return Entry(Bond.Bones,"下回合开始时，若役骨仍处于Ⅱ阶段，召唤奥斯提。");
        if(_boneSummonNextTurn)yield return Entry(Bond.Bones,"下回合开始时，召唤1。");
        if(_boneBonus)yield return Entry(Bond.Bones,$"你本回合的下一次召唤额外增加{2+Scale(Bond.Bones).A}点奥斯提生命值。");
        foreach(var card in _creationFree.Where(CreationFreeThisTurn))
            yield return Entry(Bond.Creation,$"【{Presentation.CardTitle(card)}】本回合可以免费打出一次。");
        if(_tonyPendingPenalty>0)yield return Entry(Bond.Tony,$"额外回合开始时，少获得{{energyPrefix:energyIcons({_tonyPendingPenalty})}}。");
        if(_vakuuControlled)yield return Entry(Bond.Vakuu,"瓦库正在接管战斗。之后每回合开始时，额外获得{energyPrefix:energyIcons(1)}。");
        foreach(var card in _ancientFree.Keys.Where(AncientFreeThisTurn))
            yield return Entry(Bond.Ancient,$"【{Presentation.CardTitle(card)}】本回合可以免费打出一次，X取你打出时的可用能量。");
        foreach(var card in _encounterDiscount.Where(card=>card.Pile?.IsCombatPile==true))
            yield return Entry(Bond.Encounter,$"【{Presentation.CardTitle(card)}】下次打出时，能量消耗减少{{energyPrefix:energyIcons(1)}}。");
        foreach(var ((bond,card,damageOnly),amount) in _pendingCardBonuses)
            if(card.Pile?.Type is PileType.Hand or PileType.Draw or PileType.Discard or PileType.Exhaust)
            {
                yield return Entry(bond,$"【{Presentation.CardTitle(card)}】下次打出时，{(damageOnly?"造成的伤害":"造成的伤害和获得的格挡")}增加{amount}点。");
            }
    }

    private static string CardNames(IEnumerable<CardModel> cards)=>string.Join("、",cards.GroupBy(Presentation.CardTitle)
        .Select(group=>group.Count()==1?group.Key:$"{group.Key} ×{group.Count()}"));

    internal IEnumerable<TraitEntry> BattleTraitStatus(IReadOnlyDictionary<Bond,int>? active=null)
    {
        if(!TraitDisplay.InCombat(Owner))yield break;
        foreach(var entry in ReworkStatus())yield return entry;
        var deck=Owner.Deck.Cards;
        var sources=_powerTraits.Keys.AsEnumerable()
            .Concat(_revisionAttachments.Values.SelectMany(entries=>entries).Select(entry=>entry.Origin))
            .Concat(RevisionTrace.TraitStatusSources(Owner))
            .Concat(_revisionBankedGold.Keys)
            .Concat(CardMembership.Cards(Owner).Where(card=>ReturnStatus(card)!=null))
            .Concat(deck).Distinct();
        active??=Counts();
        IReadOnlyDictionary<Bond,int>? deckCounts=null;
        foreach(var card in sources)
        {
            var program=Program(card);if(program==null)continue;
            var fromDeck=deck.Contains(card);
            var rows=new HashSet<TraitEntry>();
            for(var index=0;index<program.Rules.Length;index++)
            {
                var rule=program.Rules[index];
                if((rule.Event=="victory")!=fromDeck)continue;
                var timing=StatusTiming(rule);
                if(timing==null)continue;
                var counts=fromDeck?(deckCounts??=Counts(deck:true)):active;
                if(rule.Event=="victory"&&rule.RequireActive&&BondCatalog.Get(rule.Bond).Tier(counts.GetValueOrDefault(rule.Bond))==0)continue;
                var state=StatusState(card,program,rule,index);
                if(state==null)continue;
                if(rule.RequireActive&&rule.Event!="victory"&&TierAtCount(rule.Bond,counts.GetValueOrDefault(rule.Bond))==0)
                    state=$"{BondCatalog.Get(rule.Bond).Name}未激活"+(state.Length==0?"":" · "+state);
                var entry=new TraitEntry(rule.Bond,RevisionDescriptions.Text(card,annotateScaling:true,selectedRule:rule),Presentation.CardTitle(card),timing,state);
                if(rows.Add(entry))yield return entry;
            }
        }
    }

    internal IEnumerable<TraitEntry> OutsideTraitStatus(IEnumerable<CardModel> rewardCards)
    {
        if(TraitDisplay.InCombat(Owner))yield break;
        if(Owner.RunState.CurrentRoom is MegaCrit.Sts2.Core.Rooms.MerchantRoom&&Owner.Deck.Cards.OfType<HandOfGreed>().Where(HasRework)
            .OrderByDescending(card=>card.IsUpgraded).FirstOrDefault() is {} credit)
            yield return ReworkEntry(credit,"商店");
        foreach(var divine in Owner.Deck.Cards.OfType<DivineShot>().Where(HasDivineMark))
            yield return ReworkEntry(divine,"下一场战斗","此牌下一场战斗获得固有和保留。");
        var counts=Counts(deck:true);
        foreach(var (card,preview) in Owner.Deck.Cards.Select(card=>(card,false)).Concat(rewardCards.Select(card=>(card,true))))
        {
            var program=Program(card);if(program==null)continue;
            foreach(var rule in program.Rules)
            {
                var timing=rule.Event switch
                {
                    "shop_discount" when !preview=>"商店",
                    "remove" when !preview=>"移除时",
                    "transform" when !preview&&card is Dowsing=>"变化时",
                    "acquire" when preview=>"领取时",
                    _=>null
                };
                if(timing==null||rule.RequireActive&&BondCatalog.Get(rule.Bond).Tier(counts.GetValueOrDefault(rule.Bond))==0)continue;
                if(rule.Event=="shop_discount"&&!RevisionSupportEffective(card,program,rule,combat:false))continue;
                var state=card is Dowsing dowsing?$"已探索 {dowsing.RoomsEntered}/{Dowsing.maxRooms} 个问号房":null;
                yield return new(rule.Bond,RevisionDescriptions.Text(card,annotateScaling:true,selectedRule:rule),Presentation.CardTitle(card),timing,state);
            }
        }
    }

    private static string? StatusTiming(RevisionInstruction rule)
    {
        if(rule.Action is "unrelenting_power" or "synthesis_power"||rule.Scope=="hand")return null;
        if(rule.Action=="bank_gold")return "胜利结算";
        return rule.Event switch
        {
            "play" or "play_override" or "native_card" or "variable" or "acquire" or "remove" or "shop_discount" or "own_orb_evoked" or "orb_passive_bonus"=>null,
            "damage_bonus" or "block_bonus" when rule.Scope=="own"=>null,
            "draw"=>"抽到时",
            "discard" or "subject_discard" or "observe_discard"=>"弃牌时",
            "exhaust" or "ethereal" or "subject_exhaust" or "observe_exhaust"=>"消耗时",
            "self_damage" or "self_power" or "retained" or "subject_endturn" or "observe_retained"=>"回合结束",
            "return_hand"=>"回到手牌时",
            "turn_start"=>"回合开始",
            "victory"=>"胜利结算",
            "transform" or "subject_transform" or "observe_transform" or "entropy_transform"=>"变化时",
            "resource_spent"=>"资源花费时",
            "orb_origin_block" or "orb_origin_evoked"=>"充能球触发时",
            "subject_play" or "damage_bonus" or "block_bonus" or "growth_bonus"=>"关联牌生效时",
            "observe_play" or "after_play"=>"其他牌打出时",
            _=>"持续效果"
        };
    }

    private string? StatusState(CardModel card,RevisionProgram program,RevisionInstruction rule,int index)
    {
        var key=(card,index);
        if(rule.Limit=="battle"&&_revisionBattleUses.ContainsKey(key))return null;
        if(rule.Action=="bank_gold")return _revisionBankedGold.TryGetValue(card,out var banked)?$"已累计 {banked} 金币":null;
        if(rule.Event=="victory")return VictoryStatus(card,rule);
        if(rule.Event=="return_hand")return ReturnStatus(card);
        var native=RevisionTrace.HasLivePower(card,program);
        if(rule.Scope=="resource")
        {
            var resources=RevisionTrace.StatusResources(card,rule.Parameter=="once_grant",rule.Condition);
            return resources.Length>0?$"可用资源 {resources}":native?"等待获得资源":null;
        }
        if(rule.Scope=="orb")
        {
            var orbs=RevisionTrace.StatusOrbs(card,rule.Condition);
            return orbs.Length>0?$"已关联 {orbs}":native&&card.Type==CardType.Power?"等待生成充能球":null;
        }
        if(rule.Scope is not ("own" or "live"))
        {
            var subjects=_revisionAttachments.Where(pair=>pair.Key.Pile?.Type is PileType.Hand or PileType.Draw or PileType.Discard or PileType.Exhaust)
                .Where(pair=>rule.Event!="subject_endturn"||pair.Key.Pile?.Type==PileType.Hand)
                .Where(pair=>pair.Value.Any(entry=>entry.Origin==card&&entry.RuleIndex==index&&RevisionAttachmentCurrent(entry))).ToArray();
            if(subjects.Length>0)
            {
                return "已关联 " + CardNames(subjects.Select(pair=>pair.Key));
            }
            return native?"等待关联牌":null;
        }
        if(rule.Event=="native"||rule.Scope=="live"||rule.Event.StartsWith("power_",StringComparison.Ordinal))
        {
            if(!native)return null;
            if(rule.Limit=="turn"&&_revisionTurnUses.ContainsKey(key))return "本回合已触发";
            if(rule.Accumulate!=null)
            {
                var totals=rule.Lifetime=="turn"?_revisionTurnAccumulations:_revisionBattleAccumulations;
                return $"{(rule.Lifetime=="turn"?"本回合":"本场战斗")}已累计 {totals.GetValueOrDefault(key)}";
            }
            var targets=RevisionTrace.LivePowers(Owner).Where(power=>power.Owner.IsEnemy&&power.Owner.IsAlive&&RevisionTrace.IsPowerSource(power,card,program))
                .Select(power=>power.Owner).Distinct().Select(target=>target.Name).ToArray();
            return targets.Length>0?"当前目标 "+string.Join("、",targets):"";
        }
        return null;
    }

    private string VictoryStatus(CardModel card,RevisionInstruction rule)
    {
        if(rule.Condition=="battle_exhausted>0")
            return Owner.PlayerCombatState!.ExhaustPile.Cards.Any(other=>Original(other)==card)?"已消耗，胜利时结算。":"需先消耗此牌。";
        if(rule.Condition=="battle_played>0")return _revisionPlayedCards.Contains(card)?"已打出，胜利时结算。":"需先打出此牌。";
        if(card is GoldAxe)return $"此牌本场战斗已造成{_revisionDamageTotals.GetValueOrDefault(card)}点伤害。";
        return "战斗胜利时结算";
    }

    private string? ReturnStatus(CardModel card)
    {
        if(card.Pile?.Type is not (PileType.Draw or PileType.Discard or PileType.Exhaust))return null;
        if(card is Bolas or Bounce or ThrummingHatchet)
            return _revisionCardTurnCounts.ContainsKey(card)?"下回合抽牌前回到手牌。":null;
        if(card is RightHandHand&&card.Pile.Type==PileType.Discard)
            return $"打出能量值至少为{card.DynamicVars.Energy.IntValue}的牌后，此牌回到手牌。";
        if(card is WhistlingArrows&&card.Pile.Type==PileType.Discard)return "发射箭矢后，此牌回到手牌。";
        if(card is MakeItSo)
        {
            var needed=card.DynamicVars.Cards.IntValue;
            var played=CombatManager.Instance.History.CardPlaysFinished.Count(entry=>entry.CardPlay.Player==Owner&&entry.CardPlay.Card.Type==CardType.Skill&&entry.HappenedThisTurn(card.CombatState));
            return $"再打出{needed-played%needed}张技能牌时，此牌回到手牌。";
        }
        return null;
    }
}
