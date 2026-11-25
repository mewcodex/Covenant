using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace Covenant.Game;

internal static class RevisionLateDamage
{
    internal static void Install(Harmony harmony)=>harmony.Patch(AccessTools.Method(typeof(Hook),nameof(Hook.ModifyDamage)),postfix:new HarmonyMethod(typeof(RevisionLateDamage),nameof(Finished)));
    private static void Finished(IRunState runState,ICombatState? combatState,Creature? target,Creature? dealer,ValueProp props,CardModel? cardSource,CardPlay? cardPlay,ModifyDamageHookType modifyDamageHookType,ref IEnumerable<AbstractModel> modifiers,ref decimal __result)
    {
        if(!modifyDamageHookType.HasFlag(ModifyDamageHookType.Multiplicative))return;
        var extra=RevisionTrace.ExtraPoisonDamage(target,dealer,cardSource,props);
        if(props.IsPoweredAttack()&&props.HasFlag(ValueProp.FromArrow)&&dealer?.GetPower<HoningPower>() is {} honing)
            extra+=RevisionNativeBonuses.PowerBonus(honing,"power_arrow_flat_bonus",subject:cardSource);
        if(extra>0)
        {
            var changed=__result+extra;var used=modifiers.ToList();
            if(modifyDamageHookType.HasFlag(ModifyDamageHookType.Cap))
                foreach(var model in runState.IterateHookListeners(combatState))
                {
                    var cap=model.ModifyDamageCap(target,props,dealer,cardSource,cardPlay);
                    if(changed>cap){changed=cap;if(!used.Contains(model))used.Add(model);}
                }
            __result=Math.Max(0,changed);modifiers=used;
        }
        RevisionTrace.PoisonDamageFinished(target,dealer,cardSource,props,true);
    }
}
