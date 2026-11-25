using System.Reflection;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Covenant.Core;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Combat;

namespace Covenant.Game;

public sealed record TraitEffect(string Trigger,string Kind,int Amount=1,Bond? Bond=null,string? Condition=null,bool RequireActive=false,string? Filter=null,int UpgradeAmount=1,string Scope="combat");
public sealed record CardTrait(string Trigger,Bond Bond,int Amount,int Limit,int Every=1,string? OngoingTrigger=null,int OngoingEvery=1,int OngoingAmount=1,string? Extra=null,int UpgradeAmount=1,TraitEffect[]? Effects=null,RevisionTrait? Revision=null);
public sealed record CardDefinition(string ClassName,string[] Pools,string Role,string Type,string Rarity,int Cost,Bond[] Bonds,CardTrait Trait,bool DynamicType=false,bool RetainSupport=false,bool EtherealSupport=false,bool EmberSupport=false,bool ZeroSupport=false,bool SleightSupport=false,bool GuardSupport=false,bool? ForesightRequiresUpgrade=null,bool ZeroExcluded=false,bool EmberExcluded=false,bool TraitBondMember=false)
{
    public bool HeavyExcluded { get; init; }
    public bool ProfitExcluded { get; init; }
    internal ulong PrintedBonds { get; }=CardMembership.StaticMask(Bonds);
    internal ulong BaseTraitBonds { get; }=CardMembership.ProgramBonds(Trait.Revision?.Base,TraitBondMember?Trait.Bond:null);
    internal ulong UpgradedTraitBonds { get; }=CardMembership.ProgramBonds(Trait.Revision?.Upgraded,TraitBondMember?Trait.Bond:null);
    internal int? CursePlayCost { get; }=Trait.Revision?.Base.Rules.FirstOrDefault(rule=>rule.Event=="curse_play") is {} play
        ?int.Parse(play.Amount,System.Globalization.CultureInfo.InvariantCulture):null;
    internal bool InAcquisitionPool { get; }=Array.Exists(Pools,pool=>pool is "IroncladCardPool" or "SilentCardPool"
        or "DefectCardPool" or "RegentCardPool" or "NecrobinderCardPool" or "FoolCardPool" or "ColorlessCardPool" or "CurseCardPool" or "StatusCardPool");
}

public static class CardMembership
{
    private static readonly JsonSerializerOptions Options=new(){PropertyNameCaseInsensitive=true,Converters={new JsonStringEnumConverter()}};
    private static readonly ulong DynamicBonds=Mask([Bond.Harmony,Bond.Mastery,Bond.Ancient,Bond.Encounter,Bond.Zero,
        Bond.Ethereal,Bond.Ritual,Bond.Retain,Bond.Ember,Bond.Sleight,Bond.Strike,Bond.Guard,Bond.Foresight,
        Bond.Curse,Bond.Status,Bond.Solitude]);
    public static readonly IReadOnlyDictionary<string,CardDefinition> Definitions=Load();
    [ThreadStatic] private static CostCountScope? _costCountScope;
    private sealed class CostCountScope(Player player)
    {
        internal Player Player { get; }=player;
        internal int? HeavyCount;
        internal int? InitiativeCount;
    }
    internal static ulong Bit(Bond bond)=>1UL<<(int)bond;
    private static ulong Mask(IEnumerable<Bond> bonds)
    {
        ulong mask=0;
        foreach(var bond in bonds)mask|=Bit(bond);
        return mask;
    }
    internal static ulong StaticMask(Bond[] bonds)=>Mask(bonds)&~DynamicBonds;
    internal static ulong ProgramBonds(RevisionProgram? program,Bond? legacy=null)
        =>program!=null?Mask(program.Rules.Where(rule=>rule.Bond!=Bond.Mastery).Select(rule=>rule.Bond))
            :legacy is {} bond&&bond!=Bond.Mastery?Bit(bond):0;
    private static IReadOnlyDictionary<string,CardDefinition> Load()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Covenant.Data.cards.json")??throw new InvalidDataException("Missing card membership catalog.");
        return JsonSerializer.Deserialize<CardDefinition[]>(stream,Options)!.ToDictionary(x=>x.ClassName,StringComparer.Ordinal);
    }
    internal static bool IsMarker(CardModel card)=>card is Sloth or Disintegration or MindRot or Amnesia;
    internal static bool IsBird(CardModel card)=>card is ByrdonisEgg or ByrdSwoop or GodByrd;
    public static CardDefinition? Definition(CardModel card)=>IsMarker(card)?null:Definitions.GetValueOrDefault(card.GetType().Name);
    internal static RevisionTrait? TraitRevision(CardModel card)
    {
        var revision=Definition(card)?.Trait.Revision;
        return card is MadScience science?revision?.Variants?.GetValueOrDefault(science.TinkerTimeRider.ToString())??revision:revision;
    }
    public static bool IsPrintedZero(CardModel card)=>!card.EnergyCost.CostsX&&card.EnergyCost.GetWithModifiers(CostModifiers.None)==0;
    internal static bool IsXCost(CardModel card)=>card.EnergyCost.CostsX||card.HasStarCostX;
    [ThreadStatic] private static bool _quotingZero;
    internal static bool IsCurrentZero(CardModel card)=>CurrentPlayCost(card)==0;
    private static bool CountsForZero(CardModel card,CardDefinition? definition)
    {
        if(definition?.ZeroExcluded==true||card.HasStarCostX||card.BaseStarCost>0||card.CurrentStarCost>0)return false;
        if(card.Type==CardType.Power)
            return definition?.ZeroSupport==true&&!IsPrintedZero(card)&&!IsCurrentZero(card);
        return definition?.ZeroSupport==true||(card.Type is CardType.Attack or CardType.Skill)&&IsCurrentZero(card);
    }
    internal static int CurrentPlayCost(CardModel card)
    {
        if(card.EnergyCost.CostsX)
            return CovenantTerminal.For(card.IsMutable?card.Owner:null)?.CreationFreeThisTurn(card)==true?0:
                Math.Max(1,card.IsMutable?card.Owner?.PlayerCombatState?.Energy??0:0);
        // Global price modifiers may query other covenant counts while evaluating this card.
        if(_quotingZero)return card.EnergyCost.GetWithModifiers(CostModifiers.Local);
        _quotingZero=true;
        try{return card.EnergyCost.GetWithModifiers(CostModifiers.All);}
        finally{_quotingZero=false;}
    }
    public static bool IsPrintedHeavy(CardModel card)=>!card.EnergyCost.CostsX&&card.EnergyCost.GetWithModifiers(CostModifiers.None)>=2;
    internal static bool IsStrikeCard(CardModel card)=>!IsMarker(card)&&NativeCapabilities.IsNamedStrike(card);
    public static bool IsGenerated(CardModel card)=>card.CombatState!=null&&card.DeckVersion==null;
    private static bool ExcludedInExhaust(CardModel card,bool combat)
        =>combat&&card.Pile?.Type==PileType.Exhaust&&(card.Type==CardType.Status||card.Rarity==CardRarity.Token);
    private static bool IsFixedCombatResult(CardModel card)=>card is
        Fuel or GiantRock or Joy or MinionDiveBomb or MinionSacrifice or MinionStrike or
        Shiv or Soul or SovereignBlade or SweepingGaze or TheShot;
    public static bool IsEncounter(CardModel card)
    {
        if(IsGenerated(card)||IsFixedCombatResult(card))return false;
        return Definition(card) is {InAcquisitionPool:false};
    }
    public static IReadOnlyList<Bond> Of(CardModel card)=>Of(card, null);

    internal static IReadOnlyList<Bond> Of(CardModel card, Player? ownerHint)
        => BondsFromMask(MembershipMask(card, ownerHint));

    internal static IReadOnlyList<Bond> CardBadges(CardModel card, Player? ownerHint)
        => BondsFromMask(VisibleBadgeMask(card, MembershipMask(card, ownerHint)), rosterFirst:true);

    internal static int PrintedBadgeCount(CardModel card)
        => BitOperations.PopCount(VisibleBadgeMask(card, MembershipMask(card, includeRunRosters:false)));

    private static ulong VisibleBadgeMask(CardModel card, ulong mask) => card.IsUpgraded?mask&~Bit(Bond.Mastery):mask;

    private static IReadOnlyList<Bond> BondsFromMask(ulong mask, bool rosterFirst=false)
    {
        if(mask==0)return [];
        var bonds=new Bond[BitOperations.PopCount(mask)];
        var index=0;
        if(rosterFirst)
        {
            if((mask&Bit(Bond.Oracle))!=0){bonds[index++]=Bond.Oracle;mask&=~Bit(Bond.Oracle);}
            if((mask&Bit(Bond.Fate))!=0){bonds[index++]=Bond.Fate;mask&=~Bit(Bond.Fate);}
        }
        for(;mask!=0;mask&=mask-1)bonds[index++]=(Bond)BitOperations.TrailingZeroCount(mask);
        return bonds;
    }
    private static ulong TraitMask(CardModel card,CardDefinition? definition)
    {
        if(definition==null)return 0;
        if(card is MadScience&&TraitRevision(card) is {} variant)
            return ProgramBonds(card.IsUpgraded?variant.Upgraded:variant.Base);
        return card.IsUpgraded?definition.UpgradedTraitBonds:definition.BaseTraitBonds;
    }
    private static ulong MembershipMask(CardModel card, Player? ownerHint=null, bool includeRunRosters=true)
    {
        if(card is CovenantOption||IsMarker(card))return 0;
        var keywords=card.Keywords;
        var innate=keywords.Contains(CardKeyword.Innate)?Bit(Bond.Initiative):0;
        if(card is Shiv)return innate|Bit(Bond.Blades);
        var definition=Definitions.GetValueOrDefault(card.GetType().Name);
        var mask=(definition?.PrintedBonds??0)|innate|TraitMask(card,definition)|SecondaryMembership.Mask(card);
        var owner = ownerHint ?? _costCountScope?.Player ?? (card.IsMutable ? card.Owner : null);
        if(includeRunRosters&&CovenantTerminal.For(owner) is {} terminal)mask|=terminal.RunRosterMask(card);
        var type=card.Type;
        var generated=IsGenerated(card);
        if(definition?.ProfitExcluded!=true&&card.Pool is ColorlessCardPool)mask|=Bit(Bond.Profit);
        if(type==CardType.Curse)mask|=Bit(Bond.Curse);
        if(type==CardType.Status||definition?.Bonds.Contains(Bond.Status)==true)mask|=Bit(Bond.Status);
        if(IsStrikeCard(card))mask|=Bit(Bond.Strike);
        if(definition?.GuardSupport==true||card.GainsBlock)mask|=Bit(Bond.Guard);
        if(definition?.Bonds.Contains(Bond.Foresight)==true&&(definition.ForesightRequiresUpgrade==null||definition.ForesightRequiresUpgrade==card.IsUpgraded))mask|=Bit(Bond.Foresight);
        if(type==CardType.Power)mask|=Bit(Bond.Ritual);
        if(card.GetType().Name=="Entropy")mask|=Bit(Bond.Harmony);
        if(card.IsUpgraded||definition?.Bonds.Contains(Bond.Mastery)==true||
            card is MadScience{TinkerTimeRider:TinkerTime.RiderEffect.Improvement})mask|=Bit(Bond.Mastery);
        if(card.Rarity==CardRarity.Ancient)mask|=Bit(Bond.Ancient);
        if(!generated&&definition is {InAcquisitionPool:false}&&!IsFixedCombatResult(card))mask|=Bit(Bond.Encounter);
        if(CountsForZero(card,definition))mask|=Bit(Bond.Zero);
        if(IsXCost(card))mask|=Bit(Bond.Channel);
        if(definition?.EmberExcluded!=true&&(definition?.EmberSupport==true||keywords.Contains(CardKeyword.Exhaust)))mask|=Bit(Bond.Ember);
        if(definition?.SleightSupport==true||card.IsSlyThisTurn)mask|=Bit(Bond.Sleight);
        if(definition?.EtherealSupport==true||keywords.Contains(CardKeyword.Ethereal))mask|=Bit(Bond.Ethereal);
        if(definition?.RetainSupport==true||card.ShouldRetainThisTurn)mask|=Bit(Bond.Retain);
        if(generated&&type is CardType.Attack or CardType.Skill or CardType.Power)mask|=Bit(Bond.Creation);
        if(generated&&type==CardType.Attack)mask|=Bit(Bond.Blades);
        return mask;
    }
    internal static IReadOnlyList<Bond> ReferenceBonds(CardModel card)
    {
        var bonds=Of(card);
        if(Definition(card)?.Trait.Revision?.Variants is not {} variants)return bonds;
        var possible=bonds.Concat(variants.Values.SelectMany(variant=>variant.Base.Rules.Select(rule=>rule.Bond)).Where(bond=>bond!=Bond.Mastery));
        if(card is MadScience)possible=possible.Append(Bond.Mastery);
        return possible.Distinct().Order().ToArray();
    }
    public static IEnumerable<CardModel> Cards(Player player,bool deck=false)=>PileCards(player,deck,includePlay:false);

    internal static int CurseTier(Player player,bool deck=false)
    {
        var bit=Bit(Bond.Curse);
        var combat=!deck&&TraitDisplay.InCombat(player);
        var names=new HashSet<string>(StringComparer.Ordinal);
        var harmony=0;
        foreach(var card in PileCards(player,deck,includePlay:true))
        {
            if(card is CovenantOption||IsMarker(card)||ExcludedInExhaust(card,combat))continue;
            if(card is Entropy)harmony=1;
            if(card.Type==CardType.Curse||(TraitMask(card,Definition(card))&bit)!=0)names.Add(NameKey(card));
        }
        return BondCatalog.Get(Bond.Curse).Tier(CountWithHarmony(player,Bond.Curse,names.Count,combat,harmony));
    }

    private static IEnumerable<CardModel> PileCards(Player player,bool deck,bool includePlay)
    {
        if(!deck&&player.PlayerCombatState is {} c&&player.Creature.CombatState!=null&&(CombatManager.Instance.IsInProgress||CombatManager.Instance.IsStarting))
            return includePlay?c.AllCards:c.Hand.Cards.Concat(c.DrawPile.Cards).Concat(c.DiscardPile.Cards).Concat(c.ExhaustPile.Cards);
        return player.Deck.Cards;
    }
    // The six color variants share the same base names, including their upgraded forms.
    internal static string NameKey(CardModel card)=>card.IsBasicStrikeOrDefend
        ?(card.Tags.Contains(CardTag.Strike)?"STRIKE":"DEFEND")
        :card.Id.Entry;
    internal static int SpectrumColor(CardModel card)=>card.Pool switch
    {
        IroncladCardPool=>1<<0, SilentCardPool=>1<<1, DefectCardPool=>1<<2,
        RegentCardPool=>1<<3, NecrobinderCardPool=>1<<4, FoolCardPool=>1<<5, ColorlessCardPool=>1<<6,
        _=>0
    };
    internal static bool SolitudeSuppressed(Player player,CardModel? acquisition=null)
    {
        var names=new HashSet<string>(StringComparer.Ordinal);
        foreach(var card in player.Deck.Cards)
            if(card.Type!=CardType.Curse&&!names.Add(NameKey(card)))return true;
        return acquisition!=null&&acquisition.Type!=CardType.Curse&&names.Contains(NameKey(acquisition));
    }
    internal static Dictionary<Bond,int> CountsAfterAcquisition(Player player,CardModel card)
        =>CountCards(player,player.Deck.Cards.Append(card),combat:false,suppressSolitude:false);

    // The native exhaust notification runs after the move. The card still participates in the effect of that move.
    internal static Dictionary<Bond,int> CountsForExhaust(CardModel card)
        =>CountForPlayer(card.Owner,deck:false,resolving:card,respectSuppression:true,exhausting:card);

    public static Dictionary<Bond,int> Counts(Player player,bool deck=false,CardModel? resolving=null,bool respectSuppression=true)
        =>CountForPlayer(player,deck,resolving,respectSuppression);

    private static Dictionary<Bond,int> CountForPlayer(Player player,bool deck,CardModel? resolving,bool respectSuppression,CardModel? exhausting=null)
    {
        // The native play pile holds cards until their effects finish, including nested plays.
        var cards=PileCards(player,deck,includePlay:true);
        if(!deck&&resolving!=null&&!cards.Contains(resolving))cards=cards.Append(resolving);
        var combat=!deck&&TraitDisplay.InCombat(player);
        return CountCards(player,cards,combat,respectSuppression&&combat&&SolitudeSuppressed(player),exhausting);
    }
    private static Dictionary<Bond,int> CountCards(Player player,IEnumerable<CardModel> cards,bool combat,bool suppressSolitude,CardModel? exhausting=null)
    {
        var previous=_costCountScope;
        _costCountScope=previous?.Player==player?previous:new CostCountScope(player);
        try{return CountCardsInScope(player,cards,combat,suppressSolitude,exhausting);}
        finally{_costCountScope=previous;}
    }
    private static Dictionary<Bond,int> CountCardsInScope(Player player,IEnumerable<CardModel> cards,bool combat,bool suppressSolitude,CardModel? exhausting)
    {
        var counts=new Dictionary<Bond,int>();
        var represented=new Dictionary<string,ulong>(StringComparer.Ordinal);
        var spectrum=0;
        foreach(var card in cards)
        {
            if(card is CovenantOption||IsMarker(card)||card!=exhausting&&ExcludedInExhaust(card,combat))continue;
            if(!combat)spectrum|=SpectrumColor(card);
            var name=NameKey(card);
            var mask=MembershipMask(card);
            if(!suppressSolitude)mask|=Bit(Bond.Solitude);
            if(mask==0)continue;
            ref var seen=ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault(represented,name,out _);
            var added=mask&~seen;
            seen|=mask;
            for(;added!=0;added&=added-1)
            {
                var bond=(Bond)BitOperations.TrailingZeroCount(added);
                counts[bond]=counts.GetValueOrDefault(bond)+1;
            }
        }
        if(combat)
            foreach(var card in player.Deck.Cards)spectrum|=SpectrumColor(card);
        if(spectrum!=0)counts[Bond.Spectrum]=BitOperations.PopCount((uint)spectrum);
        if(HasHarmony(player,combat,counts.GetValueOrDefault(Bond.Harmony)))
        {
            counts[Bond.Harmony]=1;
            foreach(var definition in BondCatalog.All)
                if(definition.Id is not (Bond.Harmony or Bond.Oracle or Bond.Spectrum)&&counts.GetValueOrDefault(definition.Id)>=definition.Basic)
                    counts[definition.Id]++;
        }
        else counts.Remove(Bond.Harmony);
        return counts;
    }
    private static bool HasHarmony(Player player,bool combat,int count)
        =>combat&&CovenantTerminal.For(player)?.HarmonyAtCombatStart is {} locked?locked:count>0;
    private static int CountWithHarmony(Player player,Bond bond,int count,bool combat,int harmony)
        =>count>=BondCatalog.Get(bond).Basic&&HasHarmony(player,combat,harmony)?count+1:count;

    internal static int CountForCost(Player player,Bond bond)
    {
        // Heavy and Initiative are independent of current costs.
        // A full membership scan quotes many prices. Share these live counts only within that synchronous scan.
        if(_costCountScope is not {} scope||scope.Player!=player)return ScanCostBond(player,bond);
        return bond==Bond.Heavy?(scope.HeavyCount??=ScanCostBond(player,bond)):
            (scope.InitiativeCount??=ScanCostBond(player,bond));
    }
    private static int ScanCostBond(Player player,Bond bond)
    {
        var combat=TraitDisplay.InCombat(player);
        var names=new HashSet<string>(StringComparer.Ordinal);
        var harmony=0;
        var bit=Bit(bond);
        var initiative=bond==Bond.Initiative;
        foreach(var card in PileCards(player,deck:false,includePlay:true))
        {
            if(card is CovenantOption||IsMarker(card)||ExcludedInExhaust(card,combat))continue;
            if(card is Entropy)harmony=1;
            var innate=initiative&&card.Keywords.Contains(CardKeyword.Innate);
            if(card is Shiv)
            {
                if(innate)names.Add(NameKey(card));
                continue;
            }
            var definition=Definitions.GetValueOrDefault(card.GetType().Name);
            if(innate||(((definition?.PrintedBonds??0)|TraitMask(card,definition))&bit)!=0)names.Add(NameKey(card));
        }
        return CountWithHarmony(player,bond,names.Count,combat,harmony);
    }
    internal static Bond? HighestActiveFlow(Player player,IReadOnlyDictionary<Bond,int> counts)=>BondCatalog.All
        .Where(d=>d.Id<=Bond.Emotion&&d.Tier(counts.GetValueOrDefault(d.Id))>0)
        .OrderByDescending(d=>CovenantTerminal.For(player)?.Ledger.Permanent(d.Id)??0).ThenBy(d=>d.Id)
        .Select(d=>(Bond?)d.Id).FirstOrDefault();
}
