using System.Text.Json;
using Covenant.Core;
using Covenant.Game;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

internal static class SaveProbe
{
    private sealed record Snapshot(string Ledger, bool AllCardPools, int Gold, string[] Deck, int Reloads);
    private static string[] Deck(MegaCrit.Sts2.Core.Entities.Players.Player player)=>player.Deck.Cards.Select(c=>$"{c.Id}:{c.CurrentUpgradeLevel}").Order().ToArray();
    internal static async Task Write(string output)
    {
        var state=RunManager.Instance.DebugOnlyGetState()!;var player=state.Players.Single();
        var terminal=CovenantTerminal.For(player)!;
        if(state.CurrentRoom is not CombatRoom {IsPreFinished:true} room)throw new Exception("Save probe requires a genuinely finished combat");
        // Change next-run defaults, keeping this run's saved six-pool flag deliberately different.
        CovenantSettings.SetAllCardPools(false);CovenantSettings.SetShowCardTraits(false);CovenantSettings.SetHideMerchantHand(true);
        await SaveManager.Instance.SaveRun(room);
        var saved=SaveManager.Instance.LoadRunSave();
        if(!saved.Success||saved.SaveData==null)throw new Exception("Native run save cannot be read after writing: "+saved.Status);
        var snapshot=new Snapshot(terminal.CovenantLedger,terminal.AllCardPools,player.Gold,Deck(player),saved.SaveData.NumReloads);
        File.WriteAllText(output+"/save-expected.json",JsonSerializer.Serialize(snapshot,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(output+"/save-runtime.json",JsonSerializer.Serialize(new{status="awaiting-fresh-process-resume",savedUtc=DateTimeOffset.UtcNow,userData=OS.GetUserDataDir()},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("COVENANT_SAVE_WRITTEN fresh-process resume required");
    }
    internal static async Task Resume(NMainMenu menu,string output)
    {
        var passed=new List<string>();
        void Check(bool ok,string message){if(!ok)throw new Exception(message);passed.Add(message);Console.WriteLine("COVENANT_SAVE_PASS "+message);}
        var expected=JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(output+"/save-expected.json"))!;
        Check(!CovenantSettings.EnableAllCardPools&&!CovenantSettings.ShowCardTraits&&CovenantSettings.HideMerchantHand,"all three local preferences survive a fresh process");
        var previousReloads=SaveManager.Instance.LoadRunSave().SaveData?.NumReloads??throw new Exception("Missing native save before Continue");
        // Execute exactly the native Continue action, including canonical load, setup, transitions and room restoration.
        await ((Task)AccessTools.Method(typeof(NMainMenu),"OnContinueButtonPressedAsync").Invoke(menu,[])!).WaitAsync(TimeSpan.FromSeconds(60));
        var tree=(SceneTree)Engine.GetMainLoop();var state=RunManager.Instance.DebugOnlyGetState()!;var player=state.Players.Single();
        var terminal=CovenantTerminal.For(player)!;
        Check(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(Ledger.Load(terminal.CovenantLedger).Save()),System.Text.Json.Nodes.JsonNode.Parse(Ledger.Load(expected.Ledger).Save())),"all covenant layers and ledger fields survive native Continue, including normalized zero balances");
        Check(terminal.AllCardPools==expected.AllCardPools&&terminal.AllCardPools,"saved six-pool flag takes precedence over a changed next-run preference");
        Check(player.Gold==expected.Gold&&Deck(player).SequenceEqual(expected.Deck),"post-reward deck, upgrades and gold survive native Continue");
        Check(state.CurrentRoom is CombatRoom {IsPreFinished:true},"native Continue restores the finished combat reward room");
        var checkbox=NRun.Instance!.FindChild(SettingsUi.TraitToggleName,true,false) as NFastModeTickbox;
        Check(checkbox!=null&&!CovenantSettings.ShowCardTraits,"restored HUD provides the native trait checkbox with its saved preference");
        var saved=SaveManager.Instance.LoadRunSave();
        Check(saved.Success&&previousReloads>=expected.Reloads&&saved.SaveData!.NumReloads==previousReloads+1,"native Continue records exactly one reload relative to the pre-Continue file");
        NRun.Instance.GetViewport().GetTexture().GetImage().SavePng(output+"/save-continued.png");
        // Traverse a real adjacent map node so the next combat is not entered through the debug-room API.
        var next=state.CurrentMapPoint!.Children.First(p=>p.PointType==MegaCrit.Sts2.Core.Map.MapPointType.Monster);
        await RunManager.Instance.EnterMapCoord(next.coord).WaitAsync(TimeSpan.FromSeconds(45));
        for(int i=0;i<100&&!CombatManager.Instance.IsInProgress;i++)await tree.ToSignal(tree.CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
        Check(CombatManager.Instance.IsInProgress&&player.Creature.GetPower<CovenantAttritionPower>() is {EnemyTurns:0,EnemyTurnLimit:20},"real map travel starts a fresh enemy-turn attrition counter");
        var oldLayers=Ledger.Load(expected.Ledger).Layers;
        Check(oldLayers.All(pair=>terminal.Ledger.Get(pair.Key)>=pair.Value),"next combat preserves every permanent layer total; opening draw traits may add new layers");
        await ExtraTurnProbe.Run(tree,Check);
        // Restore disposable-profile defaults after proving both process and per-run persistence.
        CovenantSettings.SetAllCardPools(true);CovenantSettings.SetShowCardTraits(true);
        File.WriteAllText(output+"/save-runtime.json",JsonSerializer.Serialize(new{status="pass",completedUtc=DateTimeOffset.UtcNow,checks=passed,userData=OS.GetUserDataDir(),modSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CovenantTerminal).Assembly.Location)))},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("COVENANT_SAVE_RESUME_COMPLETE checks="+passed.Count);
        var finishExtras=await ReleaseExtras.Prepare(player,output);await MerchantProbe.Run(tree,output);finishExtras();
    }
}
