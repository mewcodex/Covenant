using System.Reflection;
using System.Runtime.CompilerServices;
using Covenant.Core;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Cards;
using System.Security.Cryptography;
using System.Text;

namespace Covenant.Game;

internal static class Presentation
{
    private sealed record Raw(string Text);
    private static readonly ConditionalWeakTable<LocString,Raw> Literals=new();
    private static readonly Dictionary<Bond,Texture2D> Icons=new();
    private readonly record struct BadgeState(Bond Bond,int Count,int Tier);
    private sealed class CardView
    {
        internal BadgeState[] Badges=[];
    }
    private static readonly ConditionalWeakTable<NCard,CardView> Cards=new();
    private static bool _traitRefreshQueued;
    internal static string CardTitle(CardModel card)=>LocManager.Instance==null?card.GetType().Name:card.Title;
    internal static LocString Literal(string text){var loc=new LocString("relics","COVENANT_LITERAL_"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12]);Literals.Add(loc,new(text));return loc;}
    internal static Texture2D Icon(Bond bond)
    {
        if(Icons.TryGetValue(bond,out var texture))return texture;
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream($"Covenant.Assets.Icons.{bond}.png")!;
        using var bytes=new MemoryStream();stream.CopyTo(bytes);var image=new Image();image.LoadPngFromBuffer(bytes.ToArray());
        // Center the visible artwork, rather than the transparent padding of the source image.
        var atlas=ImageTexture.CreateFromImage(image);
        return Icons[bond]=new AtlasTexture{Atlas=atlas,Region=image.GetUsedRect()};
    }
    internal static void Install(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(LocString),nameof(LocString.GetRawText)),prefix:new HarmonyMethod(typeof(Presentation),nameof(GetLiteral)));
        h.Patch(AccessTools.PropertyGetter(typeof(RelicModel),nameof(RelicModel.Title)),prefix:new HarmonyMethod(typeof(Presentation),nameof(RelicTitle)));
        h.Patch(AccessTools.PropertyGetter(typeof(RelicModel),nameof(RelicModel.DynamicDescription)),prefix:new HarmonyMethod(typeof(Presentation),nameof(RelicDescription)));
        foreach(var name in new[]{nameof(RelicModel.Icon),nameof(RelicModel.IconOutline),nameof(RelicModel.BigIcon)})
            h.Patch(AccessTools.PropertyGetter(typeof(RelicModel),name),prefix:new HarmonyMethod(typeof(Presentation),nameof(RelicIcon)));
        h.Patch(AccessTools.Method(typeof(NRelic),"Reload"),postfix:new HarmonyMethod(typeof(Presentation),nameof(ReloadRelicArt)));
        var beginCardBatch=new HarmonyMethod(typeof(Presentation),nameof(BeginCardBatch));
        var endCardBatch=new HarmonyMethod(typeof(Presentation),nameof(EndCardBatch));
        h.Patch(AccessTools.Method(typeof(NCard),nameof(NCard.UpdateVisuals)),prefix:beginCardBatch,
            postfix:new HarmonyMethod(typeof(Presentation),nameof(CardVisuals)),finalizer:endCardBatch);
        h.Patch(AccessTools.DeclaredMethod(typeof(NCardGrid),"InitGrid",Type.EmptyTypes),prefix:beginCardBatch,finalizer:endCardBatch);
        h.Patch(AccessTools.DeclaredMethod(typeof(NCardGrid),"UpdateGridPositions"),prefix:beginCardBatch,finalizer:endCardBatch);
        h.Patch(AccessTools.Method(typeof(NCard),nameof(NCard._Ready)),postfix:new HarmonyMethod(typeof(Presentation),nameof(CardIcons)));
        h.Patch(AccessTools.PropertyGetter(typeof(CardModel),nameof(CardModel.Description)),prefix:new HarmonyMethod(typeof(Presentation),nameof(OptionDescription)));
        h.Patch(AccessTools.PropertyGetter(typeof(CardModel),nameof(CardModel.HoverTips)),postfix:new HarmonyMethod(typeof(Presentation),nameof(CardHoverTips)));
        foreach(var name in new[]{nameof(PowerModel.Icon),nameof(PowerModel.BigIcon)})h.Patch(AccessTools.PropertyGetter(typeof(PowerModel),name),prefix:new HarmonyMethod(typeof(Presentation),nameof(PowerIcon)));
        h.Patch(AccessTools.Method(typeof(NPower),"Reload"),prefix:new HarmonyMethod(typeof(Presentation),nameof(ReloadPowerArt)));
        CovenantFonts.Install(h);
        Hud.Install(h);
        CompactRelics.Install(h);
        CardTraitTip.Install(h);
        CovenantLibrary.Install(h);
        MerchantSaleLayout.Install(h);
    }
    private static bool GetLiteral(LocString __instance,ref string __result){if(!Literals.TryGetValue(__instance,out var raw))return true;__result=raw.Text;return false;}
    private static bool OptionDescription(CardModel __instance,ref LocString __result)
    {
        var text=__instance switch
        {
            CovenantOption option=>BondDescription.Highlight(option.OptionDescription),
            GrandFinale=>"对所有敌人造成{Damage:diff()}点伤害。",
            ByrdonisEgg=>"能在休息处被孵化。",
            _=>null
        };
        if(text==null)return true;
        __result=Literal(text);return false;
    }
    private static Bond? StatusBond(PowerModel? power)=>power switch
    {
        CovenantStrengthPower=>Bond.Fury,
        CovenantStatusStrengthPower=>Bond.Status,
        CovenantEffectPower status=>status.IconBond,
        CovenantAttritionPower or CovenantUnrelentingPower=>Bond.Heavy,
        CovenantSynthesisPower=>Bond.Ritual,
        CovenantTerraformingPower=>Bond.Aid,
        _=>null
    };
    private static bool PowerIcon(PowerModel __instance,ref Texture2D __result)
    {
        if(StatusBond(__instance) is not {} bond)return true;
        __result=Icon(bond);return false;
    }
    private static bool ReloadPowerArt(NPower __instance,PowerModel? ____model,TextureRect ____icon,CpuParticles2D ____powerFlash)
    {
        if(!__instance.IsNodeReady()||____icon==null||StatusBond(____model) is not {} bond)return true;
        // The native reload can inline PowerModel.Icon and bypass its Harmony getter patch.
        ____icon.Texture=____powerFlash.Texture=Icon(bond);
        AccessTools.Method(typeof(NPower),"RefreshAmount").Invoke(__instance,null);
        return false;
    }
    private static bool RelicTitle(RelicModel __instance,ref LocString __result){if(__instance is not CovenantTerminal)return true;__result=Literal("盟约终端");return false;}
    private static bool RelicDescription(RelicModel __instance,ref LocString __result)
    {
        if(__instance is not CovenantTerminal terminal)return true;
        var text="同一盟约内，同名牌只计一次。消耗牌堆也计入，状态牌和衍生牌除外。";
        if(terminal.IsMutable&&terminal.Owner?.RunState is {} run&&CovenantFloorScaling.IsEnabled(run))
            text+=$"\n敌人当前强化幅度为 {CovenantFloorScaling.CurrentBonusText(run)}。";
        __result=Literal(text);return false;
    }
    private static bool RelicIcon(RelicModel __instance,ref Texture2D __result){if(__instance is not CovenantTerminal)return true;__result=Icon(Bond.Harmony);return false;}
    private static void ReloadRelicArt(NRelic __instance,RelicModel? ____model)
    {
        // Native reload can inline the icon getters when it recreates relics after Continue.
        if(!__instance.IsNodeReady()||____model is not CovenantTerminal)return;
        __instance.Icon.Texture=__instance.Outline.Texture=Icon(Bond.Harmony);
    }
    internal static string TraitText(CardModel card,bool upgradePreview=false,bool directOnly=false,bool primaryOnly=false,bool annotateScaling=false)
    {
        var def=CardMembership.Definition(card);if(def==null)return "";
        var trait=def.Trait;var previewsUpgrade=upgradePreview&&card.IsUpgradable;
        if(trait.Revision!=null)
        {
            var text=RevisionDescriptions.Text(card,previewsUpgrade,directOnly,annotateScaling);
            if(!previewsUpgrade&&card.IsMutable&&CovenantTerminal.For(card.Owner) is {} current)
                text+=current.ReworkCardCue(card);
            return text;
        }
        if(TraitDisplay.UsesAuthored(trait))
        {
            var effects=trait.Effects!.Where(e=>!directOnly||TraitDisplay.Direct(e)).ToArray();
            var texts=effects.Select(e=>TraitDescriptions.Text(card,e,previewsUpgrade)).ToArray();
            if(effects.Length==2&&effects[0].Trigger==effects[1].Trigger&&effects[0].Condition==effects[1].Condition&&effects[0].RequireActive==effects[1].RequireActive)
            {
                var comma=texts[1].IndexOf('，');if(comma>=0)return texts[0].TrimEnd('。')+"，并"+texts[1][(comma+1)..];
            }
            return string.Join("\n",texts);
        }
        var amount=trait.Amount+(card.IsUpgraded||previewsUpgrade?trait.UpgradeAmount:0);
        var shownAmount=previewsUpgrade?$"[green]{amount}[/green]":amount.ToString();
        var trigger=trait.Trigger switch{"play"=>"打出时","self_exhaust"=>"这张牌被消耗时","victory"=>"战斗胜利时","draw"=>"抽到时","power_turn"=>"打出后，你的回合开始时","self_loss"=>"因自己的效果失去生命时","exhaust"=>"消耗卡牌时","poison"=>"施加中毒时","generated_attack"=>"打出战斗中生成的攻击牌时","discard"=>"主动弃牌时","evoke"=>"激发充能球时","channel"=>"生成充能球时","zero_play"=>"打出能量费用为0{energyPrefix:energyIcons(1)}的牌时","stars"=>"花费{starIcon}时","forge"=>"锻造时","generate"=>"生成既不是状态也不是诅咒的牌时","doom"=>"向敌人施加灾厄时","summon"=>"召唤时","ethereal"=>"因虚无消耗卡牌时","arrow"=>"发射箭矢时","melody"=>"获得新的旋律时","emotion"=>"生成悲伤或打出喜悦时",_=>throw new InvalidOperationException("Unknown trait trigger: "+trait.Trigger)};
        var terminal=CovenantTerminal.For(card.IsMutable?card.Owner:null);
        var used=terminal?.TraitUses(card)??0;
        if(trait.Trigger=="self_exhaust")trigger="这张牌被消耗时";
        var cadence=trait.Every<=1?trigger:TraitCadence(trait.Trigger,trait.Every);
        var progress=terminal?.TraitProgress(card)??0;
        var suffix=trait.Limit>0?$"每场战斗至多{trait.Limit}次（{used}/{trait.Limit}）。":trait.Every>1?$"（{progress}/{trait.Every}）":"";
        var name=BondCatalog.Get(trait.Bond).Name;
        var result=$"{cadence}，使已激活的【{name}】增加{shownAmount}层{(trait.Every>1&&trait.Limit==0?suffix:"")}。{(trait.Limit>0?suffix:"")}";
        if(trait.Extra=="aegis")
        {
            var projected=new Covenant.Core.Scale((int)Math.Min(Ledger.MaxLayers,(long)(terminal?.Ledger.Get(trait.Bond)??0)+amount),BondCatalog.Get(trait.Bond).LayerUnit);
            result+=$"若【{name}】已激活，还会获得{6+2*projected.A}点格挡。";
        }
        if(trait.Extra=="stars")result+=$"若【{name}】已激活，还会获得2{{starIcon}}。";
        if(!directOnly&&trait.OngoingTrigger is {} ongoing)
        {
            var condition=trait.OngoingEvery==1?ongoing switch
            {
                "power_turn"=>"在你的回合开始时","any_play"=>"每次打出卡牌时","sword_play"=>"每次打出君王之剑时",
                "generated_attack"=>"每次打出战斗中生成的攻击牌时","poison"=>"每次施加中毒时","exhaust"=>"每次消耗卡牌时",
                "generate"=>"每次生成既不是状态也不是诅咒的牌时","forge"=>"每次锻造时","ethereal"=>"每次因虚无消耗卡牌时",
                _=>throw new InvalidOperationException("Unknown ongoing support.")
            }:TraitCadence(ongoing,trait.OngoingEvery).TrimEnd('后');
            var ongoingAmount=trait.OngoingAmount+(card.IsUpgraded||previewsUpgrade?1:0);
            var ongoingProgress=terminal?.TraitProgress(card,true)??0;
            result+=$"打出后，{condition}，使已激活的【{name}】增加{ongoingAmount}层"+(trait.OngoingEvery>1?$"（{ongoingProgress}/{trait.OngoingEvery}）":"")+"。";
        }
        if(directOnly&&!TraitDisplay.Own(trait.Trigger))result="";
        foreach(var effect in primaryOnly?[]:trait.Effects??[])
            if(!directOnly||TraitDisplay.Own(effect.Trigger))result+=TraitDescriptions.Text(card,effect,previewsUpgrade);
        return result;
    }
    private static string TraitCadence(string trigger,int every)=>trigger switch
    {
        "play"=>$"每累计打出这张牌{every}次后",
        "any_play"=>$"每累计打出{every}张牌后",
        "sword_play"=>$"每累计打出君王之剑{every}次后",
        "power_turn"=>$"你每开始{every}个回合",
        "self_loss"=>$"每累计因自己的效果失去生命{every}次后",
        "exhaust"=>$"每累计消耗{every}张牌后",
        "poison"=>$"每累计施加中毒{every}次后",
        "generated_attack"=>$"每累计打出{every}张战斗中生成的攻击牌后",
        "discard"=>$"每累计主动弃牌{every}次后",
        "evoke"=>$"每累计激发充能球{every}次后",
        "channel"=>$"每累计生成{every}个充能球后",
        "zero_play"=>$"每累计打出{every}张能量费用为0{{energyPrefix:energyIcons(1)}}的牌后",
        "stars"=>$"每累计花费{{starIcon}}{every}次后",
        "forge"=>$"每累计锻造{every}次后",
        "generate"=>$"每累计生成{every}张既不是状态也不是诅咒的牌后",
        "doom"=>$"每累计向敌人施加灾厄{every}次后",
        "summon"=>$"每累计召唤{every}次后",
        "ethereal"=>$"每累计因虚无消耗{every}张牌后",
        "arrow"=>$"每累计发射箭矢{every}次后",
        "emotion"=>$"每累计生成悲伤或打出喜悦共{every}次后",
        _=>throw new InvalidOperationException("Unknown trait cadence: "+trigger)
    };
    private static void CardVisuals(NCard __instance,CardPreviewMode previewMode)
    {
        CardTraitTip.Update(__instance,previewMode);
        UpdateCardIcons(__instance);
    }
    private static void BeginCardBatch(out bool __state)=>__state=CardMembership.BeginPresentationBatch();
    private static void EndCardBatch(bool __state)=>CardMembership.EndPresentationBatch(__state);
    private static void CardHoverTips(CardModel __instance,ref IEnumerable<IHoverTip> __result)
    {
        if(CardMembership.Definition(__instance) is not {} definition)return;
        var tips=__result.ToList();
        if(definition.Trait.Effects?.Any(e=>e.Filter=="generated"||e.Trigger=="generated_attack")==true)
            tips.Add(new HoverTip(Literal("衍生牌"),"在战斗中生成的卡牌。战斗中产生的复制品也属于衍生牌。"));
        foreach(var effect in definition.Trait.Effects??[])
        {
            IHoverTip? tip=effect.Kind switch
            {
                "vigor"=>HoverTipFactory.FromPower<VigorPower>(),"strength" or "temp_strength"=>HoverTipFactory.FromPower<StrengthPower>(),
                "dexterity"=>HoverTipFactory.FromPower<DexterityPower>(),"focus"=>HoverTipFactory.FromPower<FocusPower>(),
                "plating"=>HoverTipFactory.FromPower<PlatingPower>(),"thorns"=>HoverTipFactory.FromPower<ThornsPower>(),
                "artifact"=>HoverTipFactory.FromPower<ArtifactPower>(),"weak"=>HoverTipFactory.FromPower<WeakPower>(),
                "vulnerable"=>HoverTipFactory.FromPower<VulnerablePower>(),_=>null
            };
            if(tip!=null&&!tips.Any(t=>t.Id==tip.Id))tips.Add(tip);
        }
        __result=tips;
        if(!CardTraitTip.Inspecting)return;
        var bond=TraitDisplay.MainBond(__instance);
        var text=TraitText(__instance,annotateScaling:true);
        __result=__result.Append(new HoverTip(Literal(TraitDisplay.TraitTitle(__instance,bond)),BondDescription.Format(text,__instance,CardTraitTip.InspectionInLibrary,splitSentences:false),Icon(bond)));
    }
    internal static void RefreshCardBadges(Player player,Dictionary<Bond,int> liveCounts)
    {
        var counts=new Dictionary<Player,Dictionary<Bond,int>>{{player,liveCounts}};
        foreach(var entry in Cards)
            if(GodotObject.IsInstanceValid(entry.Key)&&entry.Key.IsInsideTree()&&!entry.Key.IsQueuedForDeletion())UpdateCardIcons(entry.Key,counts);
        QueueCardTraitRefresh();
    }
    internal static void RefreshCardTraits()
    {
        _traitRefreshQueued=false;
        var counts=new Dictionary<Player,Dictionary<Bond,int>>();
        Dictionary<Bond,int> CountsFor(Player player)
        {
            if(!counts.TryGetValue(player,out var result))counts[player]=result=CardMembership.Counts(player);
            return result;
        }
        foreach(var entry in Cards)
            if(GodotObject.IsInstanceValid(entry.Key)&&entry.Key.IsInsideTree()&&!entry.Key.IsQueuedForDeletion())CardTraitTip.Update(entry.Key,countsFor:CountsFor);
    }
    internal static void QueueCardTraitRefresh()
    {
        if(_traitRefreshQueued)return;
        _traitRefreshQueued=true;
        Callable.From(()=>{if(_traitRefreshQueued)RefreshCardTraits();}).CallDeferred();
    }
    internal static void CardIcons(NCard __instance)
    {
        CardTraitTip.Update(__instance);
        UpdateCardIcons(__instance);
    }
    private static void UpdateCardIcons(NCard node,Dictionary<Player,Dictionary<Bond,int>>? cachedCounts=null)
    {
        if(!node.IsNodeReady()||node.Body==null)return;
        var root=node.Body.GetNodeOrNull<Control>("CovenantIcons");
        var card=node.Model;
        if(card==null||node.Visibility!=MegaCrit.Sts2.Core.Entities.UI.ModelVisibility.Visible)
        {
            if(root!=null){node.Body.RemoveChild(root);root.QueueFree();}
            return;
        }
        var view=Cards.GetOrCreateValue(node);
        var energy=node.GetNodeOrNull<Control>("%EnergyIcon");if(energy==null)return;
        var frame=node.GetNode<Control>("%Frame");
        var inverse=node.Body.GetGlobalTransform().AffineInverse();
        var center=inverse*(energy.GetGlobalTransform()*(energy.Size/2));
        var frameCenter=inverse*(frame.GetGlobalTransform()*(frame.Size/2));
        var terminal=card.IsMutable?CovenantTerminal.For(card.Owner):null;
        Dictionary<Bond,int>? counts=null;
        if(terminal!=null)
        {
            if(cachedCounts==null)counts=CardMembership.Counts(card.Owner,respectSuppression:false);
            else if(!cachedCounts.TryGetValue(card.Owner,out counts))cachedCounts[card.Owner]=counts=CardMembership.Counts(card.Owner,respectSuppression:false);
        }
        Span<BadgeState> states=stackalloc BadgeState[BondCatalog.All.Count];
        var length=0;
        foreach(var bond in CardMembership.CardBadges(card,terminal==null?CovenantLibrary.LocalRunPlayer:null))
        {
            var count=counts?.GetValueOrDefault(bond)??0;
            var definition=BondCatalog.Get(bond);
            var tier=terminal?.TierAtCount(bond,count)??definition.Tier(count);
            states[length++]=new(bond,Math.Min(count,tier>0?definition.Segments:definition.Basic),tier);
        }
        states=states[..length];
        if(root==null||!states.SequenceEqual(view.Badges))
        {
            if(root!=null){node.Body.RemoveChild(root);root.QueueFree();}
            root=new Control{Name="CovenantIcons",MouseFilter=Control.MouseFilterEnum.Ignore};node.Body.AddChild(root);
            foreach(var state in states)
            {
                var badge=BondBadge.Create(BondCatalog.Get(state.Bond),state.Count,0,card:true,activeTier:state.Tier);
                badge.Name=state.Bond.ToString();root.AddChild(badge);
            }
            view.Badges=states.ToArray();
        }
        root.Show();
        var bottom=frameCenter.Y+frame.Size.Y/2;
        var side=Math.Min(Math.Clamp(energy.Size.X,32f,44f),Math.Max(20,(bottom-center.Y-28)/Math.Max(1,length)-6));
        for(var i=0;i<length;i++)
        {
            var badge=root.GetChild<Control>(i);
            badge.Scale=Vector2.One*(side/60);
            badge.Position=new Vector2(2*frameCenter.X-center.X,center.Y+i*(side+6))-new Vector2(36,30)*badge.Scale;
        }
        root.SetMeta("mirror_x",2*frameCenter.X-center.X);root.SetMeta("energy_center_y",center.Y);
        MerchantSaleLayout.UpdateForCard(node);
    }
}
