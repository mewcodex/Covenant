using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Covenant.Game;

/// <summary>Ephemeral options for native synchronized selection screens; never added to any card pool or pile.</summary>
public sealed class CovenantOption:CardModel
{
    [SavedProperty] public string OptionTitle {get;set;}="盟约选择";
    [SavedProperty] public string OptionDescription {get;set;}="请选择一个选项。";
    [SavedProperty] public string OptionPortrait {get;set;}="";
    [SavedProperty] public int OptionValue {get;set;}
    public override string Title=>OptionTitle;
    public override bool CanBeGeneratedInCombat=>false;
    public override int MaxUpgradeLevel=>0;
    public override CardPoolModel Pool=>ModelDb.CardPool<ColorlessCardPool>();
    public override string PortraitPath=>OptionPortrait.Length>0?OptionPortrait:MissingPortraitPath;
    public override string BetaPortraitPath=>PortraitPath;
    public CovenantOption():base(-1,CardType.Skill,CardRarity.Token,TargetType.Self){}
}
