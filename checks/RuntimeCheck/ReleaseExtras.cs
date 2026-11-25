using System.Text.Json;
using Covenant.Core;
using Covenant.Game;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

internal static class ReleaseExtras
{
    internal static async Task<Action> Prepare(Player player,string output)
    {
        var checks=new List<string>();var terminal=CovenantTerminal.For(player)!;var run=(RunState)player.RunState;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add(text);Console.WriteLine("COVENANT_EXTRA_PASS "+text);}
        async Task<T> Deck<T>() where T:CardModel{var card=run.CreateCard<T>(player);await CardPileCmd.Add(card,PileType.Deck,skipVisuals:true);return card;}
        for(var i=0;i<4;i++)await CardPileCmd.Add(player.Creature.CombatState!.CreateCard<HandOfGreed>(player),PileType.Exhaust,skipVisuals:true);
        terminal.Ledger.Layers[Bond.Profit]=100;var creation=terminal.Ledger.Permanent(Bond.Creation);await Deck<Abundance>();
        Check(terminal.Ledger.Permanent(Bond.Creation)-creation==24,"advanced Profit invokes a real Abundance acquisition twice");
        terminal.Ledger.Layers[Bond.Profit]=500;creation=terminal.Ledger.Permanent(Bond.Creation);await Deck<Abundance>();
        Check(terminal.Ledger.Permanent(Bond.Creation)-creation==36,"five-hundred-layer Profit invokes a real Abundance acquisition three times");
        for(var i=0;i<6;i++)await Deck<DeadlyPoison>();
        if(terminal.Counts(deck:true).GetValueOrDefault(Bond.Foresight)<2){await Deck<Acrobatics>();await Deck<Acrobatics>();}
        terminal.Ledger.Layers[Bond.Poison]=900;terminal.Ledger.Layers[Bond.Foresight]=500;
        var boss=new CombatRoom(ModelDb.AllEncounters.First(e=>e.RoomType==RoomType.Boss).ToMutable(),run);var oldRoom=run.CurrentRoom;run.PushRoom(boss);
        try
        {
            for(ulong seed=1;seed<=8;seed++)
            {
                var options=CardCreationOptions.ForRoom(player,RoomType.Boss).WithFlags(CardCreationFlags.IsCardReward).WithRngOverride(new Rng(seed));
                var reward=CardFactory.CreateForReward(player,3,options).ToArray();
                Check(reward.Any(c=>CardMembership.Of(c.Card).Contains(Bond.Poison))&&reward.Select(c=>c.Card.Id).Distinct().Count()==3,"native boss reward factory includes the strongest active flow without duplicates: "+seed);
            }
        }
        finally{run.PopCurrentRoom();}
        Check(run.CurrentRoom==oldRoom,"boss reward factory fixture restores the native combat room");
        for(var i=0;i<3;i++)await Deck<DemonForm>();
        terminal.Ledger.Layers[Bond.Ritual]=250;terminal.Ledger.Layers[Bond.Poison]=100;
        return ()=>
        {
            Check(terminal.Ledger.Permanent(Bond.Poison)==100,"Ritual victory leaves existing permanent layers in place");
            File.WriteAllText(output+"/0.5/release-extras-native.json",JsonSerializer.Serialize(new{status="pass",checks,modSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(CovenantTerminal).Assembly.Location)))},new JsonSerializerOptions{WriteIndented=true}));
        };
    }
}
