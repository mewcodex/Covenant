using System.Globalization;
using Covenant.Core;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace Covenant.Game;

internal static class RevisionDescriptions
{
    internal static RevisionProgram? Program(CardModel card,bool preview=false)=>CardMembership.TraitRevision(card) is {} trait
        ?card.IsUpgraded||preview?trait.Upgraded:trait.Base:null;
    internal static bool Direct(RevisionInstruction rule)=>rule.Scope=="hand"||rule.Scope=="own"&&
        (rule.Event is "play" or "play_override" or "curse_play" or "curse_effect" or "native_card" or "draw" or "discard" or "exhaust" or "ethereal" or "self_damage" or "self_power" or "transform" or "retained" or "damage_bonus" or "block_bonus" or "variable" or "return_hand" or "own_orb_evoked" or "orb_passive_bonus" ||
         rule.Action=="modifier"&&rule.Event.StartsWith("power_",StringComparison.Ordinal)&&rule.Event is not ("power_target_arrow_bonus" or "power_target_osty_bonus"));
    internal static string Text(CardModel card,bool preview=false,bool directOnly=false,bool annotateScaling=false,RevisionInstruction? selectedRule=null)
    {
        var program=Program(card,preview);if(program==null||directOnly&&!program.Rules.Any(Direct)&&card is not (BootSequence or Reanimate or Bloodletting))return "";
        var raw=program.AmountText??program.Text;
        if(card is Speedster&&raw.StartsWith("打出后，",StringComparison.Ordinal))raw=raw[4..];
        var result=Simplify(raw,program.Rules.FirstOrDefault()?.Bond);
        for(var i=0;program.AmountText!=null&&i<program.Rules.Length;i++)
        {
            var placeholder="{"+i+"}";if(!result.Contains(placeholder,StringComparison.Ordinal))continue;
            var rule=program.Rules[i];
            var terminal=card.IsMutable&&card.Owner!=null?CovenantTerminal.For(card.Owner):null;
            var expression=card is DivineShot&&terminal?.LayerGrowthLocked==true
                ?rule.Amount:program.DisplayAmounts?.ElementAtOrDefault(i)??rule.Amount;
            var scale=terminal?.Scale(rule.Bond)??new Scale(0,BondCatalog.Get(rule.Bond).LayerUnit);
            RevisionFrame? frame=null;
            decimal Resolve(string key)
            {
                if(key=="A")return scale.A;
                if(key=="L")return scale.Layers;
                if(key=="total_layers")return terminal?.TotalCovenantLayers()??0;
                if(key=="experience")return CovenantTerminal.GetBirdExperience(card);
                if(key=="upgrade_level")return (card.IsMutable?card.CurrentUpgradeLevel:0)+(preview?1:0);
                if(key=="source_bonus")return terminal?.RevisionSourceBonus(card)??0;
                if(!card.IsMutable||card.Owner==null)return key=="act"?1:0;
                frame??=new RevisionFrame(card,"display");return frame.Resolve(key);
            }
            var value=RuleExpression.Evaluate(expression,Resolve);
            var format=program.DisplayFormats?.ElementAtOrDefault(i);
            var number=format==null?Math.Floor(value).ToString(CultureInfo.InvariantCulture):value.ToString(format,CultureInfo.InvariantCulture);
            if(annotateScaling&&BondCatalog.Get(rule.Bond).UsesLayers&&ScalingText.UsesLayers(expression))
                number=ScalingText.Amount(number,scale,expression,Resolve);
            result=result.Replace(placeholder,number,StringComparison.Ordinal);
        }
        if(card.IsMutable&&card is DivineShot&&CovenantTerminal.For(card.Owner)?.HasDivineMark(card)==true)
            result+="\n下一场战斗获得固有和保留。";
        if(directOnly&&program.Rules.Any(rule=>!Direct(rule)))selectedRule=program.Rules.FirstOrDefault(Direct);
        return selectedRule==null?result:RuleText(card,selectedRule,result);
    }

    private static string RuleText(CardModel card,RevisionInstruction rule,string text)
    {
        if(card is SicEm)
            return text.Replace("此牌及本回合奥斯提对同一目标的攻击",rule.Event=="damage_bonus"?"此牌":"本回合，奥斯提对下列目标的攻击",StringComparison.Ordinal);
        if((card is Jackpot or Veilpiercer or Severance or DislikeYou or Salvo or ThrummingHatchet)&&text.IndexOf('。') is var first&&first>=0)
        {
            if(rule.Event=="return_hand"||!Direct(rule))
            {
                const string prefix="此牌额外造成";
                var remaining=text[(first+1)..];
                if(text.StartsWith(prefix,StringComparison.Ordinal)&&text.LastIndexOf("点伤害",first,StringComparison.Ordinal) is var numberEnd&&numberEnd>prefix.Length)
                {
                    var damage=text[prefix.Length..numberEnd];
                    remaining=remaining.Replace("获得这个伤害加成",$"额外造成{damage}点伤害",StringComparison.Ordinal)
                        .Replace("获得这个加成",$"额外造成{damage}点伤害",StringComparison.Ordinal);
                }
                if(card is DislikeYou)remaining=remaining.Replace("对同一目标","对下列目标",StringComparison.Ordinal);
                if(card is Severance or ThrummingHatchet)
                    remaining=remaining.Replace("本场战斗中再增加","此牌本场战斗造成的伤害增加",StringComparison.Ordinal);
                return remaining;
            }
            return card is ThrummingHatchet?text:text[..(first+1)];
        }
        if(card is not (Abundance or Dowsing or Debt or Greed or WraithForm))return text;
        var split=text.IndexOf('；');
        if(split<0)return text;
        var firstRule=card is Dowsing?rule.Event=="victory":rule.Event is "acquire" or "draw" or "play";
        if(firstRule)return text[..split]+"。";
        return Simplify(text[(split+1)..],null);
    }

    private static string Simplify(string text,Bond? bond)
    {
        if(text.StartsWith("打出时，",StringComparison.Ordinal))text=text[4..];
        if(text.StartsWith('它'))text="此牌"+text[1..];
        else if(text.StartsWith("每当它",StringComparison.Ordinal))text="每当此牌"+text[3..];
        else if(text.StartsWith("由它",StringComparison.Ordinal))text="由此牌"+text[2..];
        if(bond is {} id)
        {
            var suffix=$"（【{BondCatalog.Get(id).Name}】）。";
            if(text.EndsWith(suffix,StringComparison.Ordinal))text=text[..^suffix.Length]+"。";
        }
        return text;
    }
}
