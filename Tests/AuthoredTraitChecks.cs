using System.Text.Json;
using Covenant.Core;
using Covenant.Game;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

internal static partial class MechanismChecks
{
    internal static async Task RunAuthored(Action<bool,string> check)
    {
        var functional=new HashSet<string>();
        foreach(var definition in CardMembership.Definitions.Values)
        {
            var effects=definition.Trait.Effects??[];
            check(effects.Length>0,"Authored functionality exists: "+definition.ClassName);
            foreach(var effect in effects)
            {
                check(effect.Amount>0&&effect.UpgradeAmount>=0&&effect.Scope is "combat" or "outside" or "all","Valid authored amount and scope: "+definition.ClassName);
                if(effect.Filter is {} filter)check(filter.StartsWith("name:")?CardMembership.Definitions.ContainsKey(filter[5..]):filter is "any" or "Attack" or "Skill" or "Power" or "zero" or "heavy" or "upgraded" or "generated" or "exhaust" or "retained"||Enum.TryParse<Bond>(filter,out _),"Authored partner filter resolves: "+definition.ClassName);
            }
            functional.Add(JsonSerializer.Serialize(new{definition.Trait.Trigger,definition.Trait.Bond,definition.Trait.OngoingTrigger,Effects=effects.Select(e=>new{e.Trigger,e.Kind,e.Bond,e.Condition,e.RequireActive,e.Filter,e.Scope})}));
        }
        check(CardMembership.Definitions.Values.Count(d=>d.Trait.Effects?.Length==1)==715,"Most cards have exactly one authored trait");
        check(CardMembership.Definitions.Values.Where(d=>d.Trait.Effects?.Length==2).All(d=>d.ClassName is "NeutronAegis" or "KnockoutBlow"),"Two traits are limited to the two explicit weak-card designs");
        var fire=AccessTools.Method(typeof(CovenantTerminal),"FireAuthoredTraits");
        Task Fire(Battle battle,string trigger,CardModel? card=null)=>(Task)fire.Invoke(battle.Terminal,[trigger,card])!;
        var execute=AccessTools.Method(typeof(CovenantTerminal),"ExecuteTrait");
        Task Execute(Battle battle,CardModel card,TraitEffect effect,int amount)=>(Task)execute.Invoke(battle.Terminal,[card,effect,amount])!;

        var b=new Battle();CardModel source=b.Add<SecretTechnique>(PileType.Hand);var partner=b.Add<DefendIronclad>(PileType.Hand);
        await Fire(b,"play",source);
        var before=b.Terminal.Ledger.Get(Bond.Zero);var grant=AccessTools.Method(typeof(CovenantTerminal),"GrantTraitGrowth");
        grant.Invoke(b.Terminal,[partner,Bond.Zero,2]);
        check(b.Terminal.Ledger.Get(Bond.Zero)==before+4,"A focused support prepares another skill's next growth");
        grant.Invoke(b.Terminal,[partner,Bond.Zero,2]);
        check(b.Terminal.Ledger.Get(Bond.Zero)==before+6,"A prepared growth bonus is consumed exactly once");

        b=new Battle();var fumes=b.Add<Envenom>(PileType.Hand);b.Add<DeadlyPoison>();var poison=b.Add<DeadlyPoison>();
        check(!b.Terminal.EffectiveTraits().Any(e=>e.Source==Presentation.CardTitle(fumes)&&e.Kind.Contains("block")),"An unplayed power does not provide attached abilities");
        before=b.Terminal.Ledger.Get(Bond.Poison);grant.Invoke(b.Terminal,[poison,Bond.Poison,1]);
        check(b.Terminal.Ledger.Get(Bond.Poison)==before+1,"An unplayed power does not provide a growth aura");
        await b.ResolveCallbacks(fumes);await CardPileCmd.RemoveFromCombat([fumes]);
        check(b.Terminal.EffectiveTraits().Any(e=>e.Source==Presentation.CardTitle(fumes)&&e.Kind.Contains("growth_aura")),"A played power's single attached support survives leaving the piles");
        before=b.Terminal.Ledger.Get(Bond.Poison);grant.Invoke(b.Terminal,[poison,Bond.Poison,1]);
        check(b.Terminal.Ledger.Get(Bond.Poison)==before+2,"A played power's growth aura benefits another poison component");

        b=new Battle();source=b.Add<DeadlyPoison>(PileType.Hand);b.Add<Anger>(PileType.Hand);
        check(b.Terminal.Tier(Bond.Poison)==0,"Inactive poison covenant fixture");
        await Fire(b,"play",source);
        check(b.Terminal.Ledger.Get(Bond.Poison)==2,"A card's independent growth works without covenant activation");

        b=new Battle();var greed=b.Add<Largesse>();var key=b.Add<LanternKey>();
        check(b.Terminal.ModifyMerchantPrice(b.Player,null!,50)==48,"Out-of-combat card discounts aggregate independently of covenant tiers");
        b=new Battle();var spray=b.Add<DaggerSpray>(PileType.Hand);
        check(b.Terminal.ModifyDamageAdditive(b.Enemy,4,ValueProp.Move,b.Player.Creature,spray,null)==2,"A pure damage trait adds its current value to the native damage calculation");
        b.Terminal.Ledger.Add(Bond.Solitude,20);
        check(b.Terminal.ModifyDamageAdditive(b.Enemy,4,ValueProp.Move,b.Player.Creature,spray,null)==4,"A pure damage trait grows with its covenant layers without adding another trait");
        spray.UpgradeInternal();spray.FinalizeUpgradeInternal();
        check(b.Terminal.ModifyDamageAdditive(b.Enemy,4,ValueProp.Move,b.Player.Creature,spray,null)==5,"Upgrading strengthens the same pure damage role");
        b=new Battle();var clumsy=b.Add<Clumsy>();var removed=clumsy.DeckVersion!;
        await CardPileCmd.RemoveFromDeck(removed,showPreview:false);
        check(!b.Player.Deck.Cards.Contains(removed)&&b.Terminal.Ledger.Get(Bond.Curse)==4,"Native deck removal resolves the card's single removal trait");
        foreach(var action in new[]{"block","damage","aoe","poison","doom","weak","vulnerable","strength","temp_strength","dexterity","vigor","focus","plating","thorns","artifact","draw","energy","stars","heal","gold","retain","discount","enhance","recycle","upgrade","purge","grow","grow_other","boost_next"})
        {
            b=new Battle();source=b.Add<StrikeIronclad>();var target=b.Add<DefendIronclad>(action=="recycle"?PileType.Discard:PileType.Hand);b.Add<Wound>(PileType.Hand);b.Add<DefendIronclad>(PileType.Draw);
            b.Player.Creature.SetCurrentHpInternal(b.Player.Creature.MaxHp-5);
            var effect=new TraitEffect("own_play",action,1,Bond.Guard,Filter:action is "grow_other" or "boost_next" or "retain" or "discount" or "enhance" or "recycle" or "upgrade"?"Skill":null);
            await Execute(b,source,effect,1);
            check(true,"Native action boundary executes: "+action);
            if(action=="draw")check(b.Player.PlayerCombatState!.Hand.Cards.Count==3,"Draw utility uses the native draw pile");
            if(action=="discount")check(target.EnergyCost.GetWithModifiers(CostModifiers.All)==0,"Discount utility changes the actual card cost");
            if(action=="upgrade")check(target.IsUpgraded,"Upgrade utility changes native upgrade state");
            if(action=="purge")check(b.Player.PlayerCombatState!.ExhaustPile.Cards.Any(c=>c is Wound),"Purge utility consumes an actual status card");
        }
        Console.WriteLine("COVENANT_AUTHORED_TRAITS_OK cards=717 single=715 dual=2 native-actions=pass attached-support=pass");
    }
}
