using System.Text.Json;
using Covenant.Core;
using Covenant.Game;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

internal static class AuthoredTraitProbe
{
    private sealed class Choices:ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> cards,int min,int max)=>Task.FromResult(cards.Take(min));
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> cards,IReadOnlyList<CardRewardAlternative> alternatives)=>new();
    }
    internal static async Task Run(SceneTree tree,string output)
    {
        var player=RunManager.Instance.DebugOnlyGetState()!.Players.Single();var terminal=CovenantTerminal.For(player)!;
        using var choices=RunManager.Instance.CardSelectorStack.UseSelector(player,new Choices());
        var context=new ThrowingPlayerChoiceContext();var passed=new List<string>();
        void Check(bool value,string text){if(!value)throw new Exception(text);passed.Add(text);Console.WriteLine("COVENANT_AUTHORED_PASS "+text);}
        async Task Clear()
        {
            await CardPileCmd.RemoveFromCombat(player.PlayerCombatState!.AllCards.ToArray());
            foreach(var power in player.Creature.Powers.Where(p=>p is not CovenantAttritionPower).ToArray())await PowerCmd.Remove(power);
            terminal.ResetTransient();
            foreach(var enemy in player.Creature.CombatState!.HittableEnemies)
            {enemy.SetMaxHpInternal(10000);enemy.SetCurrentHpInternal(10000);foreach(var power in enemy.Powers.ToArray())await PowerCmd.Remove(power);}
        }
        async Task<T> Add<T>(PileType pile=PileType.Hand) where T:CardModel
        {
            var deck=player.RunState.CreateCard<T>(player);await CardPileCmd.Add(deck,PileType.Deck,skipVisuals:true);
            var card=player.Creature.CombatState!.CreateCard<T>(player);card.DeckVersion=deck;await CardPileCmd.Add(card,pile);return card;
        }
        Task Play(CardModel card)=>CardCmd.AutoPlay(context,card,player.Creature.CombatState!.HittableEnemies.First()).WaitAsync(TimeSpan.FromSeconds(30));
        await Clear();var poison=await Add<DeadlyPoison>();
        Check(terminal.Tier(Bond.Poison)==0,"a single poison card does not activate Poison");
        var before=terminal.Ledger.Get(Bond.Poison);await Play(poison);
        Check(terminal.Ledger.Get(Bond.Poison)==before+2,"real Deadly Poison grants exactly its one declared growth effect without activation or a hidden legacy trait");
        Check(TraitDisplay.Active(poison),"independent growth remains bright with an inactive covenant");

        await Clear();var claw=await Add<Claw>();await Add<Claw>();await Add<Claw>(PileType.Exhaust);await Add<Claw>(PileType.Exhaust);
        before=terminal.Ledger.Get(Bond.Zero);await Play(claw);
        Check(terminal.Ledger.Get(Bond.Zero)==before+3,$"one native Claw play awards only its own single trait, not a pile-wide legacy zero-play award; growth={terminal.Ledger.Get(Bond.Zero)-before}");
        await Clear();var hologram=await Add<Hologram>();await Add<Claw>();
        before=terminal.Ledger.Get(Bond.Zero);await Play(hologram);
        Check(terminal.Ledger.Get(Bond.Zero)==before+2,"real Hologram supports another zero-cost card's covenant without playing that card");

        await Clear();var venom=await Add<Envenom>();poison=await Add<DeadlyPoison>();
        Check(!terminal.EffectiveTraits().Any(e=>e.Source==venom.Title),"a power still in hand has no attached support in the sidebar");
        await Play(venom);before=terminal.Ledger.Get(Bond.Poison);await Play(poison);
        Check(venom.Pile==null&&terminal.Ledger.Get(Bond.Poison)==before+3,"a genuinely played power's single growth aura survives leaving the piles and boosts a separate card");

        await Clear();var learning=await Add<MachineLearning>();await Add<DefendIronclad>(PileType.Draw);await Play(learning);
        var hand=player.PlayerCombatState!.Hand.Cards.Count;await terminal.AfterPlayerTurnStart(context,player);
        Check(player.PlayerCombatState.Hand.Cards.Count==hand+1,"the single attached circulation trait draws an actual card");
        Check(terminal.Tier(Bond.Chain)==0&&terminal.EffectiveTraits().Any(e=>e.Source==learning.Title&&e.Kind.Contains("draw")),"effective support appears in the sidebar independently of covenant activation");

        await Clear();var shiv=player.Creature.CombatState!.CreateCard<Shiv>(player);var other=player.Creature.CombatState.CreateCard<Shiv>(player);
        await CardPileCmd.AddGeneratedCardToCombat(shiv,PileType.Hand,player);await CardPileCmd.AddGeneratedCardToCombat(other,PileType.Hand,player);
        before=terminal.Ledger.Get(Bond.Blades);await Play(shiv);
        Check(terminal.Ledger.Get(Bond.Blades)==before+1&&shiv.Pile?.Type==PileType.Exhaust,"a generated Shiv provides exactly its declared permanent growth");

        await Clear();var obtained=player.RunState.CreateCard<FlashOfSteel>(player);var members=CardMembership.Of(obtained);var previous=members.ToDictionary(b=>b,b=>terminal.Ledger.Get(b));
        await CardPileCmd.Add(obtained,PileType.Deck,skipVisuals:true);
        Check(members.All(b=>terminal.Ledger.Get(b)==previous[b]+3),"actually obtaining a card awards its acquisition trait to its memberships exactly once");
        var generated=player.Creature.CombatState!.CreateCard<FlashOfSteel>(player);members=CardMembership.Of(generated);previous=members.ToDictionary(b=>b,b=>terminal.Ledger.Get(b));
        await CardPileCmd.AddGeneratedCardToCombat(generated,PileType.Hand,player);
        Check(members.All(b=>terminal.Ledger.Get(b)==previous[b]+3),"randomly generated cards receive the same permanent acquisition trait, including their live generated memberships");

        await Clear();await PlayerCmd.GainStars(5,player);var aegis=await Add<NeutronAegis>();before=terminal.Ledger.Get(Bond.Stars);var block=player.Creature.Block;
        Check(Presentation.TraitText(aegis).Length<65,"the paired weak-card exception stays one short sentence");
        await Play(aegis);
        Check(terminal.Ledger.Get(Bond.Stars)==before+12&&player.Creature.Block==block+6+2*terminal.Scale(Bond.Stars).A,"Neutron Aegis grants its two declared paired effects, without another ongoing trait");

        await Clear();var quest=await Add<ByrdonisEgg>();CovenantSettings.SetShowCardTraits(true);
        var deckQuest=player.RunState.CreateCard<Dowsing>(player);await CardPileCmd.Add(deckQuest,PileType.Deck,skipVisuals:true);
        Check(terminal.EffectiveTraits().Any(e=>e.Source==deckQuest.Title&&e.Text.Contains("战斗胜利")),"a quest remaining only in the permanent deck exposes its effective victory support");
        await tree.ToSignal(tree.CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
        var holder=Walk(NCombatRoom.Instance!).OfType<NCardHolder>().First(h=>h.CardModel==quest);var node=holder.CardNode!;
        Check(node.Body.GetNodeOrNull<Control>("CovenantTraitTip")?.Visible!=true,"combat idle hides a card's unrelated victory support");
        AccessTools.Method(typeof(NCardHolder),"CreateHoverTips").Invoke(holder,[]);
        Check(node.Body.GetNode<Control>("CovenantTraitTip").Visible,"combat hover always shows the trait above the card");
        CovenantSettings.SetShowCardTraits(false);Check(node.Body.GetNode<Control>("CovenantTraitTip").Visible,"disabling persistent traits preserves the above-card hover");
        AccessTools.Method(typeof(NCardHolder),"ClearHoverTips").Invoke(holder,[]);Check(!node.Body.GetNode<Control>("CovenantTraitTip").Visible,"leaving hover hides the trait when persistence is off");CovenantSettings.SetShowCardTraits(true);
        var inspection=NGame.Instance!.GetInspectCardScreen();inspection.Open([quest],0);
        await tree.ToSignal(tree.CreateTimer(.4),SceneTreeTimer.SignalName.Timeout);
        Check(Walk(inspection).OfType<NCard>().First().Body.GetNodeOrNull<Control>("CovenantTraitTip")?.Visible!=true,"actual inspection suppresses the above-card trait");
        var questIcon=Presentation.Icon(TraitDisplay.MainBond(CardMembership.Definition(quest)!.Trait));
        Check(Walk(NGame.Instance!.HoverTipsContainer!).OfType<Control>().Any(t=>t.GetNodeOrNull<TextureRect>("%Icon")?.Texture==questIcon&&t.GetNodeOrNull<MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel>("%Description")?.Text.Contains("战斗胜利")==true),"actual inspection places the trait beside native keywords");
        inspection.Close();await tree.ToSignal(tree.CreateTimer(.4),SceneTreeTimer.SignalName.Timeout);
        Check((NRun.Instance!.FindChild("TraitSidebar",true,false) as Godot.Control)?.Visible==false,"the outside trait sidebar stays hidden in combat");
        NRun.Instance.GetViewport().GetTexture().GetImage().SavePng(output+"/authored-traits-native.png");
        File.WriteAllText(output+"/authored-runtime.json",JsonSerializer.Serialize(new{status="pass",completedUtc=DateTimeOffset.UtcNow,checks=passed,modSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CovenantTerminal).Assembly.Location)))},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("COVENANT_AUTHORED_NATIVE_COMPLETE checks="+passed.Count);
    }
    internal static async Task Click(SceneTree tree,Control control)
    {
        var point=control.GetGlobalRect().GetCenter();var viewport=control.GetViewport();
        viewport.PushInput(new InputEventMouseMotion{Position=point,GlobalPosition=point},true);
        await tree.ToSignal(tree.CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);
        viewport.PushInput(new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true},true);
        viewport.PushInput(new InputEventMouseButton{Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false},true);
        await tree.ToSignal(tree.CreateTimer(.3),SceneTreeTimer.SignalName.Timeout);
    }
    private static IEnumerable<Node> Walk(Node root){foreach(var child in root.GetChildren()){yield return child;foreach(var nested in Walk(child))yield return nested;}}
}
