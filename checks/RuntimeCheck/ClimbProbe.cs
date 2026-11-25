using System.Security.Cryptography;
using System.Text.Json;
using Covenant.Game;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.AutoSlay;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

/// <summary>Full map/act stability check using the game's own assisted AutoSlay driver.</summary>
internal static class ClimbProbe
{
    private sealed record Room(int Act,int Floor,string Type,bool AllCardPools,int DeckSize,string[] DeckPools,string Ledger);
    private static readonly List<Room> Rooms=new();
    private static string Output="";
    private static string? Outcome;
    private static int Wins,BossWins;
    internal static void Start(string output)
    {
        if(!OS.GetUserDataDir().Contains("CovenantSmoke-1131-Climb"))throw new InvalidOperationException("Full climb requires its own disposable profile");
        Output=output+"/climb";Directory.CreateDirectory(Output);
        CovenantSettings.SetAllCardPools(true);CovenantSettings.SetShowCardTraits(true);
        var h=new Harmony("Covenant.RuntimeCheck.Climb");
        h.Patch(AccessTools.Method(typeof(AutoSlayer),"LogRunSummary"),postfix:new HarmonyMethod(typeof(ClimbProbe),nameof(Summary)));
        h.Patch(AccessTools.Method(typeof(AutoSlayLog),nameof(AutoSlayLog.RunCompleted)),postfix:new HarmonyMethod(typeof(ClimbProbe),nameof(Completed)));
        h.Patch(AccessTools.Method(typeof(AutoSlayLog),nameof(AutoSlayLog.RunFailed)),postfix:new HarmonyMethod(typeof(ClimbProbe),nameof(Failed)));
        RunManager.Instance.RoomEntered+=Entered;
        CombatManager.Instance.CombatWon+=Won;
        Write("starting");new AutoSlayer().Start("covenant-six-pools-1131","standard",Output+"/native-autoslay.log");
    }
    private static void Entered()
    {
        var run=RunManager.Instance.DebugOnlyGetState();if(run?.CurrentRoom==null)return;
        var player=run.Players.Single();var terminal=CovenantTerminal.For(player);
        Rooms.Add(new(run.CurrentActIndex+1,run.TotalFloor,run.CurrentRoom.RoomType.ToString(),terminal?.AllCardPools==true,
            player.Deck.Cards.Count,player.Deck.Cards.Select(c=>c.Pool.GetType().Name).Distinct().Order().ToArray(),terminal?.CovenantLedger??""));
        Write("running");Console.WriteLine($"COVENANT_CLIMB_ROOM act={run.CurrentActIndex+1} floor={run.TotalFloor} room={run.CurrentRoom.RoomType} six-pools={terminal?.AllCardPools}");
        if(run.CurrentRoom.IsVictoryRoom)CaptureVictory();
    }
    private static async void CaptureVictory()
    {
        var tree=NGame.Instance!.GetTree();await tree.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        NGame.Instance.GetViewport().GetTexture().GetImage().SavePng(Output+"/victory-room.png");
    }
    private static void Won(CombatRoom room)
    {
        Wins++;if(room.RoomType==RoomType.Boss)BossWins++;Write("running");
    }
    private static void Summary(RunState runState,string outcome)
    {Outcome=outcome;Write("finishing");}
    private static void Completed()
    {
        var pass=Outcome=="victory"&&BossWins>=3&&Rooms.Select(r=>r.Act).Distinct().Count()>=3&&Rooms.All(r=>r.AllCardPools);
        Write(pass?"pass":"fail",pass?null:"Native completion did not prove three-act victory with the six-pool flag throughout.");
        Console.WriteLine($"COVENANT_CLIMB_COMPLETE status={(pass?"pass":"fail")} outcome={Outcome} wins={Wins} boss-wins={BossWins}");
    }
    private static void Failed(string seed,Exception ex)
    {Write("fail",ex.ToString());Console.WriteLine("COVENANT_CLIMB_FAILED "+ex.Message);}
    private static void Write(string status,string? error=null)
    {
        var sha=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(CovenantTerminal).Assembly.Location)));
        File.WriteAllText(Output+"/climb-runtime.json",JsonSerializer.Serialize(new{status,updatedUtc=DateTimeOffset.UtcNow,modSha256=sha,outcome=Outcome,
            assistedNativeAutoSlay=true,limitations="Native AutoSlay grants defensive buffs, ramps Strength and auto-plays cards. This verifies full-route integration, not ordinary-play balance or energy-payment coverage.",
            wins=Wins,bossWins=BossWins,rooms=Rooms,error,userData=OS.GetUserDataDir()},new JsonSerializerOptions{WriteIndented=true}));
    }
}
