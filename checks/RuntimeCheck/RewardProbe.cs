using System.Text.Json;
using HarmonyLib;
using Covenant.Core;
using Covenant.Game;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

internal static class RewardProbe
{
    internal static async Task ProfitSpending(Player player)
    {
        var terminal=CovenantTerminal.For(player)!;
        var startingHp=player.Creature.MaxHp;
        player.Gold=1000;
        await PlayerCmd.LoseGold(150,player,GoldLossType.Spent);
        Check(terminal.Ledger.ProfitGoldProgress==0&&player.Creature.MaxHp==startingHp,"inactive Profit does not count spending");

        var required=BondCatalog.Get(Bond.Profit).Basic;
        for(var i=0;i<required;i++)
            await CardPileCmd.Add(player.Creature.CombatState!.CreateCard<HiddenGem>(player),PileType.Exhaust);
        Check(terminal.Tier(Bond.Profit)==1,"the test enables only tier one");
        terminal.Ledger.Add(Bond.Profit,200);
        await PlayerCmd.LoseGold(149,player,GoldLossType.Spent);
        await PlayerCmd.LoseGold(5,player,GoldLossType.Lost);
        await PlayerCmd.LoseGold(5,player,GoldLossType.Stolen);
        Check(terminal.Ledger.ProfitGoldProgress==149&&player.Creature.MaxHp==startingHp,"only actual payments advance the threshold");

        var restored=(CovenantTerminal)RelicModel.FromSerializable(terminal.ToSerializable());
        Check(restored.Ledger.ProfitGoldProgress==149&&Ledger.Load("{\"Schema\":3}").ProfitGoldProgress==0,
            "the partial threshold survives native relic serialization and older saves default to zero");
        terminal.CovenantLedger=restored.CovenantLedger;
        await PlayerCmd.LoseGold(301,player,GoldLossType.Spent);
        Check(terminal.Ledger.ProfitGoldProgress==0&&player.Creature.MaxHp==startingHp+9,
            "a single payment can cross three thresholds at the current layer-scaled amount");

        var eventModel=ModelDb.Event<ThermaeNonupeipe>();
        var option=new EventOption(eventModel,async ()=>
        {
            await Task.Yield();
            await PlayerCmd.LoseGold(88,player);
        },Presentation.Literal(""),Presentation.Literal(""),"THERMAE_NONUPEIPE.pages.INITIAL.options.THERMAL_BATHS",[]);
        await option.Chosen();
        await PlayerCmd.LoseGold(1,player);
        Check(terminal.Ledger.ProfitGoldProgress==88,"an explicit event purchase counts despite the original loss type");
        player.Gold=62;
        await PlayerCmd.LoseGold(200,player,GoldLossType.Spent);
        Check(terminal.Ledger.ProfitGoldProgress==0&&player.Gold==0&&player.Creature.MaxHp==startingHp+12,
            "a discounted or balance-limited payment advances only by the gold actually deducted");
        Console.WriteLine("COVENANT_PROFIT_SPENDING_COMPLETE");
    }

    private static SceneTree Tree=null!;
    private static readonly List<string> Passed=new();
    private static void Check(bool ok,string text){if(!ok)throw new Exception(text);Passed.Add(text);Console.WriteLine("COVENANT_REWARD_ENGINE_PASS "+text);}
    private static async Task Delay(double seconds=.3)=>await Tree.ToSignal(Tree.CreateTimer(seconds),SceneTreeTimer.SignalName.Timeout);
    private static async Task<T> Top<T>() where T:Control
    {
        for(int i=0;i<150;i++)
        {
            if(NOverlayStack.Instance?.Peek() is T screen){await Delay(.5);return screen;}
            await Delay(.1);
        }
        throw new Exception("Native overlay did not open: "+typeof(T).Name);
    }
    internal static async Task<CardModel> PickGrid(SceneTree tree,string screenshot,Func<CardModel,bool>? predicate=null)
    {
        Tree=tree;
        var screen=await Top<NSimpleCardSelectScreen>();
        var grid=screen.GetNode<NCardGrid>("%CardGrid");var holder=grid.CurrentlyDisplayedCardHolders.First(h=>predicate?.Invoke(h.CardModel)??true);
        var card=holder.CardModel;
        NRun.Instance!.GetViewport().GetTexture().GetImage().SavePng(screenshot);
        grid.EmitSignal(NCardGrid.SignalName.HolderPressed,holder);
        // Mandatory one-card selection completes immediately; optional selection retains a confirm button.
        if(NOverlayStack.Instance?.Peek()==screen)
        {var confirm=screen.GetNode<NButton>("%Confirm");confirm.EmitSignal(NClickableControl.SignalName.Released,confirm);}
        return card;
    }
    private static void ClickAlternative(NCardRewardSelectionScreen screen,string optionId)
    {
        var alternatives=(IReadOnlyList<CardRewardAlternative>)AccessTools.Field(typeof(NCardRewardSelectionScreen),"_extraOptions").GetValue(screen)!;
        var index=alternatives.ToList().FindIndex(a=>a.OptionId==optionId);
        if(index<0)throw new Exception("Native reward screen is missing "+optionId+"; actual="+string.Join(",",alternatives.Select(a=>a.OptionId)));
        var button=screen.GetNode<Control>("UI/RewardAlternatives").GetChildren().OfType<NCardRewardAlternativeButton>().Where(b=>!b.IsQueuedForDeletion()).ElementAt(index);
        button.EmitSignal(NClickableControl.SignalName.Released,button);
    }
    internal static async Task Run(SceneTree tree,string output,bool victoryOnly=false,bool verifyTraitSidebar=false)
    {
        Tree=tree;var player=RunManager.Instance.DebugOnlyGetState()!.Players.Single();var terminal=CovenantTerminal.For(player)!;
        terminal.ResetTransient();terminal.Ledger.Layers.Clear();
        IReadOnlyList<CardCreationResult> affinityWithoutForesight=[];
        if(verifyTraitSidebar)
        {
            var flowComponent=player.RunState.CreateCard<Inflame>(player);
            await CardPileCmd.Add(flowComponent,PileType.Deck,skipVisuals:true);
            Check(terminal.Counts(deck:true).GetValueOrDefault(Bond.Foresight)==0,"reward fixture owns no Foresight components");
            for(ulong seed=1;seed<=24&&affinityWithoutForesight.Count==0;seed++)
            {
                var options=CardCreationOptions.ForRoom(player,RoomType.Monster).WithFlags(CardCreationFlags.IsCardReward).WithRngOverride(new Rng(seed));
                var sample=CardFactory.CreateForReward(player,3,options).ToArray();
                if(sample.Any(entry=>entry.ModifyingRelics.Contains(terminal)))affinityWithoutForesight=sample;
            }
            Check(affinityWithoutForesight.Count>0,"full-pool affinity can really replace a candidate with no Foresight cards");
            await CardPileCmd.RemoveFromDeck(flowComponent,showPreview:false);
        }
        for(int i=0;i<8;i++)
        {
            await CardPileCmd.Add(player.RunState.CreateCard<HandOfGreed>(player),PileType.Deck,skipVisuals:true);
            await CardPileCmd.Add(player.RunState.CreateCard<Acrobatics>(player),PileType.Deck,skipVisuals:true);
        }
        terminal.Ledger.Add(Bond.Profit,60);terminal.Ledger.Add(Bond.Foresight,80);
        player.AddRelicInternal(ModelDb.Relic<PaelsWing>().ToMutable(),silent:true);player.Gold=100;
        player.AddRelicInternal(ModelDb.Relic<Driftwood>().ToMutable(),silent:true);
        var room=(CombatRoom)player.RunState.CurrentRoom!;
        // Native extra rewards are serialized with the room; v113's serializer cannot store IsFromCombat flags.
        var exchangeReward=new CardReward(CardCreationOptions.ForRoom(player,RoomType.Monster),3,player);
        room.AddExtraReward(player,exchangeReward);
        var questCards=new CardModel[]{player.RunState.CreateCard<ByrdonisEgg>(player),player.RunState.CreateCard<Dowsing>(player),player.RunState.CreateCard<LanternKey>(player),player.RunState.CreateCard<SpoilsMap>(player)};
        foreach(var quest in questCards)await CardPileCmd.Add(quest,PileType.Deck,skipVisuals:true);
        terminal.Ledger.ClearCombatUses();var encounterLayers=terminal.Ledger.Get(Bond.Encounter);
        var foresightLayers=terminal.Ledger.Get(Bond.Foresight);var victoryGold=player.Gold;
        var dowsings=player.Deck.Cards.Count(c=>c is Dowsing);
        var expectedVictoryGold=6+terminal.Scale(Bond.Profit).A+player.Deck.Cards.OfType<SpoilsMap>().Sum(c=>5+(c.IsUpgraded?1:0)+terminal.Scale(Bond.Profit).A);
        // Axebots with Stock spawn replacements when killed; finish those waves too.
        for(var wave=0;wave<8&&CombatManager.Instance.IsInProgress;wave++)
        {
            var enemies=player.Creature.CombatState!.Enemies.Where(enemy=>enemy.IsAlive).ToArray();
            await CreatureCmd.Kill(enemies,true);
            if(await CombatManager.Instance.CheckWinCondition())break;
        }
        Check(!CombatManager.Instance.IsInProgress&&!CombatManager.Instance.IsEnding,"native combat reaches victory");
        Check(terminal.Ledger.Get(Bond.Encounter)==encounterLayers&&terminal.Ledger.Get(Bond.Foresight)==foresightLayers+4*dowsings&&player.Gold==victoryGold+expectedVictoryGold,$"real victory skips generic covenant growth and awards the explicit quest/economic traits; Encounter={terminal.Ledger.Get(Bond.Encounter)-encounterLayers}, Foresight={terminal.Ledger.Get(Bond.Foresight)-foresightLayers}/{4*dowsings}, gold={player.Gold-victoryGold}/{expectedVictoryGold}");
        var onceGold=player.Gold;var onceForesight=terminal.Ledger.Get(Bond.Foresight);await terminal.AfterCombatVictory(room);
        Check(player.Gold==onceGold&&terminal.Ledger.Get(Bond.Foresight)==onceForesight,"a repeated victory hook does not award quest traits twice");
        if(verifyTraitSidebar)
        {
            var rewards=await Top<NRewardsScreen>();
            var sidebar=NRun.Instance!.GlobalUi.GetNode<Control>("CovenantRoot/TraitSidebar");
            bool ShowsLanternKey()=>sidebar.IsVisibleInTree()&&Descendants(sidebar).OfType<Label>().Any(label=>label.Text==$"{Presentation.CardTitle(questCards[2])}（{BondCatalog.Get(Bond.Encounter).Name}）");
            Check(ShowsLanternKey(),"the actual victory rewards screen shows owned Lantern Key in the outside trait list");
            var choice=Descendants(rewards).OfType<NRewardButton>().First(button=>button.Reward is CardReward);
            var selectionTask=(Task)AccessTools.Method(typeof(NRewardButton),"GetReward").Invoke(choice,[])!;
            var cardScreen=await Top<NCardRewardSelectionScreen>();
            Check(ShowsLanternKey(),"the outside trait list stays visible over the actual card reward picker");
            NRun.Instance.GetViewport().GetTexture().GetImage().SavePng(output+"/0.5/trait-reward-sidebar.png");
            Check(!player.Creature.Powers.OfType<CovenantEffectPower>().Any(),"combat trait statuses are removed before rewards");
            cardScreen.RefreshOptions(affinityWithoutForesight,[]);
            var affinityCard=affinityWithoutForesight.Single(result=>result.ModifyingRelics.Contains(terminal)).Card;
            var node=cardScreen.GetCardHolder(affinityCard).CardNode!;
            Check(!node.Body.GetChildren().OfType<NRelicFlashVfx>().Any(),"the affinity replacement without Foresight has no large relic mark");
            Check(affinityWithoutForesight.Where(result=>result.Card!=affinityCard).All(result=>!cardScreen.GetCardHolder(result.Card).CardNode!.Body.GetChildren().OfType<NRelicFlashVfx>().Any()),
                "ordinary reward cards do not show that flash");
            node.FlashRelicOnCard(ModelDb.Relic<BurningBlood>());
            Check(node.Body.GetChildren().OfType<NRelicFlashVfx>().Count()==1,"a normal relic can still flash on a reward card");
            await Delay();
            var icon=node.Body.GetNode<Control>("CovenantTraitTip").GetNode<TextureRect>("%Icon");
            Check(icon.Size.X<=22.5f&&icon.Size.Y<=22.5f,"the reward trait's title icon retains its intended size");
            cardScreen.RefreshOptions(affinityWithoutForesight,CardRewardAlternative.Generate((CardReward)choice.Reward!));
            ClickAlternative(cardScreen,"Skip");
            await selectionTask.WaitAsync(TimeSpan.FromSeconds(15));
        }
        if(victoryOnly)return;
        var loot=await Top<NRewardsScreen>();
        terminal.Ledger.Layers[Bond.Foresight]=600;terminal.Ledger.Layers[Bond.Profit]=60;
        var affinityHits=0;var affinityValid=true;var offColorReplacement=false;
        var heldFlows=terminal.Counts(deck:true).Where(p=>p.Key<=Bond.Emotion&&p.Value>0).Select(p=>p.Key).ToHashSet();
        for(ulong seed=1;seed<=80;seed++)
        {
            var options=CardCreationOptions.ForRoom(player,RoomType.Monster).WithFlags(CardCreationFlags.IsCardReward).WithRngOverride(new Rng(seed));
            var sample=CardFactory.CreateForReward(player,3,options).ToArray();
            var changed=sample.Where(c=>c.ModifyingRelics.Contains(terminal)).ToArray();
            if(changed.Length>0)affinityHits++;
            offColorReplacement|=changed.Any(c=>c.Card.Pool!=player.Character.CardPool);
            affinityValid&=changed.Length<=1&&sample.Select(c=>c.Card.Id).Distinct().Count()==3&&changed.All(c=>
                c.Card.Rarity==c.originalCard.Rarity&&c.Card.CurrentUpgradeLevel==c.originalCard.CurrentUpgradeLevel&&
                !c.Card.EnergyCost.WasJustUpgraded&&c.Card.DynamicVars.Values.All(v=>!v.WasJustUpgraded)&&CardMembership.Of(c.Card).Any(heldFlows.Contains));
        }
        Check(affinityHits>0&&affinityHits<80&&affinityValid&&offColorReplacement,
            $"full-pool affinity occasionally replaces a distinct same-rarity candidate from all six pools and preserves upgrades ({affinityHits}/80, valid={affinityValid}, other color={offColorReplacement})");
        var buttons=Descendants(loot).OfType<NRewardButton>().Where(b=>b.Reward is CardReward).ToArray();
        var normalButton=buttons.First(b=>b.Reward!=exchangeReward);var exchangeButton=buttons.Single(b=>b.Reward==exchangeReward);
        var reward=(CardReward)normalButton.Reward!;var original=reward.Cards.ToArray();
        var gold=player.Gold;var deck=player.Deck.Cards.Count;
        // Use the real loot button's routine: it selects through RewardsSetSynchronizer and updates its parent UI.
        var selection=(Task)AccessTools.Method(typeof(NRewardButton),"GetReward").Invoke(normalButton,[])!;
        var screen=await Top<NCardRewardSelectionScreen>();
        var alternatives=CardRewardAlternative.Generate(reward);
        Check(alternatives.Count==4,"native skip, sacrifice, Profit and Foresight buttons coexist");
        NRun.Instance!.GetViewport().GetTexture().GetImage().SavePng(output+"/reward-choices.png");
        var visibility=NRun.Instance.FindChild(SettingsUi.TraitToggleName,true,false) as NFastModeTickbox??throw new Exception("Native HUD checkbox missing");
        visibility.ForceToggleTick();var hovered=screen.GetCardHolder(reward.Cards.First());
        var point=hovered.GetGlobalRect().GetCenter();hovered.GetViewport().PushInput(new InputEventMouseMotion{Position=point,GlobalPosition=point},true);
        AccessTools.Method(typeof(NCardHolder),"CreateHoverTips").Invoke(hovered,[]);await Delay(.4);
        // Native card-preview tips also contain NCard children with above-card traits.
        // Only standalone keyword text is a side-keyword description.
        var traitIcon=Presentation.Icon(TraitDisplay.MainBond(CardMembership.Definition(hovered.CardModel!)!.Trait));
        var side=Descendants(MegaCrit.Sts2.Core.Nodes.NGame.Instance!.HoverTipsContainer!).OfType<TextureRect>().Count(icon=>!HasCardAncestor(icon)&&icon.Texture==traitIcon);
        var above=hovered.CardNode!.Body.GetNode<Control>("CovenantTraitTip").Visible;
        Check(above&&side==0,$"with persistence disabled, actual hover keeps the trait above its card and leaves side keywords separate; above={above}, sideTraits={side}");
        NRun.Instance.GetViewport().GetTexture().GetImage().SavePng(output+"/trait-hover-mode.png");
        NHoverTipSet.Remove(hovered);visibility.ForceToggleTick();
        ClickAlternative(screen,"REROLL");
        screen=await Top<NCardRewardSelectionScreen>();await Delay(.5);
        Check(reward.Cards.Count()==3&&!reward.CanReroll&&CardRewardAlternative.Generate(reward).Any(a=>a.OptionId=="REROLL"),"native Driftwood reroll consumes only its own charge while Foresight remains available");
        ClickAlternative(screen,"REROLL");
        screen=await Top<NCardRewardSelectionScreen>();await Delay(.5);
        Check(reward.Cards.Count()==7&&!reward.Cards.Intersect(original).Any(),"native reroll replaces all candidates with seven new options without a lock selection");
        NRun.Instance.GetViewport().GetTexture().GetImage().SavePng(output+"/reward-rerolled.png");
        var page=screen.GetNode<Control>("UI/CovenantRewardPages");
        Check(page.Visible&&screen.GetNode<Control>("UI/CardRow").GetChildren().OfType<NGridCardHolder>().Count(c=>c.Visible&&!c.IsQueuedForDeletion())==5,"seven candidates use a visible native-control pager with five cards on the first page");
        var firstPageCard=screen.GetCardHolder(reward.Cards.First());
        var discountDescription=BondDescription.Format("能量消耗减少1点。",firstPageCard.CardModel!);
        Check(discountDescription.Contains("_energy_icon.png")&&!discountDescription.Contains("点。"),
            "native discount text replaces both the written amount and its unit with an energy icon");
        var showTraits=CovenantSettings.ShowCardTraits;
        CovenantSettings.SetShowCardTraits(false);
        AccessTools.Method(typeof(NCardHolder),"CreateHoverTips").Invoke(firstPageCard,[]);
        var firstPageTrait=firstPageCard.CardNode!.Body.GetNode<Control>("CovenantTraitTip");
        Check(firstPageTrait.Visible,"hover displays the first page card's trait with persistence off");
        var next=page.GetNode<NCardRewardAlternativeButton>("Next");next.EmitSignal(NClickableControl.SignalName.Released,next);await Delay(.5);
        Check(screen.GetNode<Control>("UI/CardRow").GetChildren().OfType<NGridCardHolder>().Count(c=>c.Visible&&!c.IsQueuedForDeletion())==2,"the native next-page button exposes the remaining two candidates");
        Check(!firstPageTrait.Visible,"changing reward pages clears the old hover-only trait");
        CovenantSettings.SetShowCardTraits(showTraits);
        NRun.Instance.GetViewport().GetTexture().GetImage().SavePng(output+"/reward-rerolled-page2.png");
        var claimed=reward.Cards.Last();var holder=screen.GetCardHolder(claimed);holder.EmitSignal(NCardHolder.SignalName.Pressed,holder);
        var bought=await PickGrid(Tree,output+"/reward-purchase.png");
        await selection.WaitAsync(TimeSpan.FromSeconds(30));
        Check(reward.SuccessfullySelected,"normal reward claim and native purchase selection finish through the actual rewards set");
        Check(player.Deck.Cards.Contains(claimed)&&player.Deck.Cards.Contains(bought)&&player.Deck.Cards.Count==deck+2,"both selected candidates enter the real deck");
        Check(player.Gold==gold-28,"the real purchase spends 28 gold at the twelve-tier Foresight milestone");

        reward=exchangeReward;gold=player.Gold;deck=player.Deck.Cards.Count;
        selection=(Task)AccessTools.Method(typeof(NRewardButton),"GetReward").Invoke(exchangeButton,[])!;screen=await Top<NCardRewardSelectionScreen>();
        ClickAlternative(screen,"COVENANT_EXCHANGE");
        await selection.WaitAsync(TimeSpan.FromSeconds(30));
        Check(reward.SuccessfullySelected,"native Profit exchange closes and completes its reward through the actual rewards set");
        Check(player.Gold==gold+16&&player.Deck.Cards.Count==deck&&terminal.Ledger.Permanent(Bond.Profit)==68,"advanced Profit exchanges the whole reward for sixteen gold and eight permanent layers");
        File.WriteAllText(output+"/reward-runtime.json",JsonSerializer.Serialize(new{game="v0.113.1",status="pass",checks=Passed,modSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CovenantTerminal).Assembly.Location)))},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("COVENANT_REWARD_ENGINE_COMPLETE checks="+Passed.Count);
    }
    private static IEnumerable<Node> Descendants(Node root)
    {foreach(var child in root.GetChildren()){yield return child;foreach(var item in Descendants(child))yield return item;}}
    private static bool HasCardAncestor(Node node){for(var p=node.GetParent();p!=null;p=p.GetParent())if(p is NCard)return true;return false;}
}
