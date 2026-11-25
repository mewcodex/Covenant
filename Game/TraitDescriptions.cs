using Covenant.Core;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

internal static class TraitDescriptions
{
    internal static string Filter(string? filter)=>filter switch
    {
        null or "any"=>"牌","Attack"=>"攻击牌","Skill"=>"技能牌","Power"=>"能力牌","zero"=>"能量费用为0{energyPrefix:energyIcons(1)}的牌",
        "heavy"=>"原始费用至少为2{energyPrefix:energyIcons(1)}的牌","upgraded"=>"已升级的牌","generated"=>"衍生牌","exhaust"=>"具有消耗的牌","retained"=>"具有保留的牌",
        _=>filter.StartsWith("name:",StringComparison.Ordinal)?$"【{Presentation.CardTitle(ModelDb.AllCards.First(c=>c.GetType().Name==filter[5..]))}】":$"【{BondCatalog.Get(Enum.Parse<Bond>(filter)).Name}】牌"
    };
    internal static string Text(CardModel card,TraitEffect effect,bool preview=false)
    {
        var trait=CardMembership.Definition(card)!.Trait;var bond=effect.Bond??trait.Bond;var name=BondCatalog.Get(bond).Name;
        var amount=effect.Amount+(card.IsUpgraded||preview&&card.IsUpgradable?effect.UpgradeAmount:0);
        var when=effect.Trigger switch
        {
            "own_play"=>"打出时","own_draw"=>"抽到这张牌时","own_exhaust"=>"这张牌被消耗时","own_discard"=>"主动弃置这张牌时",
            "own_acquire"=>"获得或在战斗中生成此牌时","own_remove"=>"从牌组移除时",
            "turn_start"=>"你的回合开始时","battle_start"=>"战斗开始时","victory"=>"战斗胜利时",
            "attack_play"=>"打出攻击牌时","skill_play"=>"打出技能牌时","power_play"=>"打出能力牌时",
            "attached_turn"=>"打出后，在你的回合开始时","any_play"=>"打出卡牌时","poison"=>"施加中毒时","doom"=>"向敌人施加灾厄时",
            "exhaust"=>"消耗卡牌时","discard"=>"主动弃牌时","generate"=>"生成既不是状态也不是诅咒的牌时",
            "forge"=>"锻造时","channel"=>"生成充能球时","evoke"=>"激发充能球时","summon"=>"召唤时",
            "stars"=>"花费{starIcon}时","arrow"=>"发射箭矢时","melody"=>"获得新的旋律时","emotion"=>"生成悲伤或打出喜悦时",
            "self_loss"=>"因自己的效果失去生命时","ethereal"=>"因虚无消耗卡牌时","zero_play"=>"打出能量费用为0{energyPrefix:energyIcons(1)}的牌时",
            "generated_attack"=>"打出衍生攻击牌时","sword_play"=>"打出君王之剑时",
            "passive"=>"","attached_passive"=>"打出后",_=>throw new InvalidDataException("Unknown authored trigger: "+effect.Trigger)
        };
        if(effect.Condition=="first")when="每回合首次"+when;
        var condition=effect.Condition switch
        {
            null or "first"=>"","low_hp"=>"若你的生命值不高于一半，","blocked"=>"若你有格挡，","unblocked"=>"若你没有格挡，",
            "upgraded"=>"若这张牌已升级，","retained"=>"若这张牌具有保留，","generated"=>"若这张牌在战斗中生成，",
            "poisoned_enemy"=>"若有敌人具有中毒，","weak_enemy"=>"若有敌人具有虚弱，","empty_hand"=>"若你的手牌为空，",
            "paid_two"=>"若此次出牌花费至少2{energyPrefix:energyIcons(1)}，","zero_play"=>"若此次打出的牌能量费用为0{energyPrefix:energyIcons(1)}，",
            "upgraded_play"=>"若此次打出的牌已升级，","generated_play"=>"若此次打出的牌在战斗中生成，",_=>throw new InvalidDataException("Unknown authored condition.")
        };
        if(card.Type==MegaCrit.Sts2.Core.Entities.Cards.CardType.Power&&!TraitDisplay.Own(effect.Trigger)&&effect.Trigger is not ("attached_turn" or "attached_passive")&&effect.Scope!="outside")when="打出后，"+when;
        if(effect.RequireActive&&effect.Kind!="grow")condition+=$"若【{name}】已激活，";
        var filter=Filter(effect.Filter);var selected=$"另一张随机{filter.Replace("牌","")}手牌";
        if(effect.Kind=="aegis_block")
        {
            var terminal=card.IsMutable?CovenantTerminal.For(card.Owner):null;
            var future=trait.Effects!.Where(e=>e.Kind=="grow"&&e.Bond==bond).Sum(e=>e.Amount+(card.IsUpgraded||preview?e.UpgradeAmount:0));
            amount+=2*new Scale((int)Math.Min(Ledger.MaxLayers,(long)(terminal?.Ledger.Get(bond)??0)+future),BondCatalog.Get(bond).LayerUnit).A;
        }
        var action=effect.Kind switch
        {
            "grow"=>$"使{(effect.RequireActive?"已激活的":"")}【{name}】增加{amount}层","grow_other"=>$"使{selected}的目标盟约增加{amount}层",
            "grow_memberships"=>$"使此牌所属的盟约各增加{amount}层",
            "boost_next"=>$"使{selected}下次叠层额外增加{amount}层",
            "growth_aura"=>$"{(card.Type==MegaCrit.Sts2.Core.Entities.Cards.CardType.Power?"":"战斗中，")}其他{filter}通过特性叠层时，额外增加{amount}层",
            "block" or "aegis_block"=>$"获得{amount}点格挡","damage"=>$"对最前方的敌人造成{amount}点伤害","aoe"=>$"对所有敌人造成{amount}点伤害",
            "self_damage" or "self_block"=>$"此牌额外{(effect.Kind=="self_damage"?"造成":"获得")}{amount+(card.IsMutable?CovenantTerminal.For(card.Owner)?.Scale(bond).A??0:0)}点{(effect.Kind=="self_damage"?"伤害":"格挡")}",
            "poison"=>$"对所有敌人施加{amount}层中毒","doom"=>$"对所有敌人施加{amount}层灾厄","weak"=>$"对所有敌人施加{amount}层虚弱","vulnerable"=>$"对所有敌人施加{amount}层易伤",
            "strength"=>$"获得{amount}点力量","temp_strength"=>$"在本回合获得{amount}点力量","dexterity"=>$"获得{amount}点敏捷","vigor"=>$"获得{amount}层活力",
            "focus"=>$"获得{amount}点集中","plating"=>$"获得{amount}层镀层","thorns"=>$"获得{amount}层荆棘","artifact"=>$"获得{amount}层人工制品",
            "draw"=>$"抽{amount}张牌","energy"=>$"获得{{energyPrefix:energyIcons({amount})}}","stars"=>$"获得{amount}{{starIcon}}",
            "heal"=>$"回复{amount}点生命值","gold"=>$"获得{amount}金币","shop_discount"=>$"商店商品的价格降低{amount}金币",
            "retain"=>$"使{selected}在本回合获得保留","discount"=>$"使{selected}在本回合的能量消耗减少{{energyPrefix:energyIcons({amount})}}",
            "enhance"=>$"使{selected}下次打出时造成的伤害和获得的格挡增加{amount}点",
            "recycle"=>$"将弃牌堆中的另一张随机{filter}放入手牌，并给予其本回合的保留",
            "upgrade"=>$"升级{selected}","purge"=>"消耗手牌中最左侧的1张状态牌或诅咒牌",
            "shiv"=>"将1张小刀加入手牌","soul"=>"将1张灵魂加入手牌","forge"=>$"锻造{amount}次",
            "frost"=>"生成1个冰霜充能球","lightning"=>"生成1个闪电充能球","summon"=>$"召唤{amount}次",
            _=>throw new InvalidDataException("Unknown authored action: "+effect.Kind)
        };
        return (when.Length==0?"":when+"，")+condition+action+"。";
    }
}
