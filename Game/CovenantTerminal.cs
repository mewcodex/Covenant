using Covenant.Core;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Covenant.Game;

public sealed partial class CovenantTerminal : RelicModel
{
    public override RelicRarity Rarity=>RelicRarity.Event;
    private const string FallbackIconPath="res://images/powers/missing_power.png";
    public override string PackedIconPath=>FallbackIconPath;
    protected override string PackedIconOutlinePath=>FallbackIconPath;
    protected override string BigIconPath=>FallbackIconPath;
    public override bool ShowCounter=>IsMutable&&Owner?.RunState is {} run&&CovenantFloorScaling.IsEnabled(run);
    public override int DisplayAmount=>ShowCounter?Owner.RunState.TotalFloor:0;
    private Ledger _ledger=new();
    public Ledger Ledger=>_ledger;
    [SavedProperty] public string CovenantLedger {get=>_ledger.Save();set=>_ledger=Ledger.Load(value);}
    [SavedProperty(SerializationCondition.SaveIfNotTypeDefault)] public bool EnemyFloorScaling {get;set;}
    public override Task BeforeRoomEntered(AbstractRoom room)
    {
        if(ShowCounter)InvokeDisplayAmountChanged();
        return Task.CompletedTask;
    }
    protected override void DeepCloneFields()
    {
        base.DeepCloneFields();
        _ledger=Ledger.Load(_ledger.Save());
        _randomStreams=_randomStreams.ToDictionary(pair=>pair.Key,pair=>new Rng(pair.Value.ToSerializable()));
        AllCardPools=CovenantSettings.EnableAllCardPools;
        _runRosters=null;
        ResetTransient();
    }
    public static CovenantTerminal? For(MegaCrit.Sts2.Core.Entities.Players.Player? player)=>player?.Relics.OfType<CovenantTerminal>().FirstOrDefault();
    public Dictionary<Bond,int> Counts(bool deck=false)=>CardMembership.Counts(Owner,deck);
    public int Tier(Bond bond)=>TierAtCount(bond,Counts().GetValueOrDefault(bond));
    internal bool? HarmonyAtCombatStart {get;private set;}
    internal int TierAtCount(Bond bond,int count)
    {
        var tier=BondCatalog.Get(bond).Tier(count);
        if(bond==Bond.Oracle&&_oracleActiveAtStart is {} oracle&&TraitDisplay.InCombat(Owner))return oracle?1:0;
        return bond==Bond.Ritual&&TraitDisplay.InCombat(Owner)?Math.Max(tier,_ritualCombatTier):tier;
    }
    public Scale Scale(Bond bond)=>_ledger.Scale(bond);
    private bool _growthLockedAtCombatEnd;
    internal bool LayerGrowthLocked=>_growthLockedAtCombatEnd||Owner.Creature.GetPower<CovenantAttritionPower>()?.GrowthLocked==true;
    internal void GrantLayers(Bond bond,int amount)
    {
        if(LayerGrowthLocked)return;
        _ledger.Add(bond,amount);
    }
}
