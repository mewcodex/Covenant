using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace Covenant.Game;

/// <summary>A battle rule, rather than a removable debuff. Only enemy-side turns advance its counter.</summary>
public sealed class CovenantAttritionPower:PowerModel
{
    [SavedProperty] public int EnemyTurnLimit {get;set;}=20;
    [SavedProperty] public int EnemyTurns {get;set;}
    internal bool GrowthLocked=>EnemyTurns>EnemyTurnLimit/2;
    internal static int LimitFor(RoomType? type)=>type switch{RoomType.Boss=>50,RoomType.Elite=>30,_=>20};
    public override PowerType Type=>PowerType.None;
    public override PowerStackType StackType=>PowerStackType.Counter;
    public override int DisplayAmount=>EnemyTurns;
    public override LocString Title=>Presentation.Literal("鏖战");
    public override LocString Description=>Presentation.Literal($"已进行{EnemyTurns}个敌人回合。"+
        (GrowthLocked?"本场战斗已停止增加盟约层数。":$"从第{EnemyTurnLimit/2+1}个敌人回合开始，本场战斗无法增加盟约层数。")+
        $"从第{EnemyTurnLimit+1}个敌人回合开始，在敌人回合开始时受到伤害。首次受到5点伤害，此后每次增加5点。");
    public override async Task BeforeSideTurnStart(PlayerChoiceContext c,CombatSide side,IReadOnlyList<Creature> participants,ICombatState combatState)
    {
        if(side!=CombatSide.Enemy||Owner.IsDead||!combatState.Enemies.Any(enemy=>!enemy.IsDead))return;
        EnemyTurns++;InvokeDisplayAmountChanged();
        if(EnemyTurns>EnemyTurnLimit)
            await CreatureCmd.Damage(c,Owner,5m*(EnemyTurns-EnemyTurnLimit),ValueProp.Unpowered,null,null,null);
    }
}
