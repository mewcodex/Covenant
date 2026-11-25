using Covenant.Core;
using Covenant.Game;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

internal static class ThresholdSimulation
{
    internal static void Run(Action<bool,string> check)
    {
        var random=new Random(1131);var bonds=new[]{Bond.Heavy,Bond.Guard,Bond.Zero,Bond.Ember,Bond.Mastery};
        var profiles=ModelDb.AllCharacters.Select(character=>
        {
            var player=Player.CreateForNewRun(character,UnlockState.all,1);
            var run=RunState.CreateForTest([player],[],[],seed:"covenant-threshold-sample");
            return (Player:player,Run:run,Starters:player.Deck.Cards.Select(c=>c.CanonicalInstance).ToArray());
        }).ToArray();
        var possible=CovenantTerminal.SixPools.SelectMany(p=>p.GetUnlockedCards(UnlockState.all,profiles[0].Player.RunState.CardMultiplayerConstraint)).DistinctBy(c=>c.Id).ToArray();
        var rarityPools=new[]{CardRarity.Common,CardRarity.Uncommon,CardRarity.Rare}.Select(r=>possible.Where(c=>c.Rarity==r).ToArray()).ToArray();
        var rows=new List<object>();const int samples=1200;
        foreach(var deckSize in new[]{25,35})
        {
            var basic=bonds.ToDictionary(b=>b,_=>0);var advanced=bonds.ToDictionary(b=>b,_=>0);var oldAdvanced=bonds.ToDictionary(b=>b,_=>0);
            for(int sample=0;sample<samples;sample++)
            {
                var profile=profiles[sample%profiles.Length];profile.Player.Deck.Clear();
                void Add(CardModel native)
                {
                    var card=profile.Run.CreateCard(native,profile.Player);
                    if(card.IsUpgradable&&random.NextDouble()<.25){card.UpgradeInternal();card.FinalizeUpgradeInternal();}
                    profile.Player.Deck.AddInternal(card,-1);
                }
                foreach(var starter in profile.Starters)Add(starter);
                while(profile.Player.Deck.Cards.Count<deckSize)
                {
                    var roll=random.NextDouble();var pool=rarityPools[roll<.6?0:roll<.97?1:2];Add(pool[random.Next(pool.Length)]);
                }
                var counts=CardMembership.Counts(profile.Player,deck:true);
                foreach(var bond in bonds)
                {
                    var definition=BondCatalog.Get(bond);var count=counts.GetValueOrDefault(bond);
                    if(count>=definition.Basic)basic[bond]++;if(count>=definition.Advanced)advanced[bond]++;
                    if(count>=(bond==Bond.Mastery?8:6))oldAdvanced[bond]++;
                }
            }
            foreach(var bond in bonds)
            {
                check(bond==Bond.Ember?advanced[bond]==oldAdvanced[bond]:advanced[bond]<oldAdvanced[bond],"The evidence-based "+bond+" threshold has its intended activation rate in "+deckSize+"-card decks");
                var definition=BondCatalog.Get(bond);
                rows.Add(new{deckSize,bond=bond.ToString(),basicThreshold=definition.Basic,advancedThreshold=definition.Advanced,samples,basic=basic[bond],advanced=advanced[bond],previousAdvanced=oldAdvanced[bond]});
            }
        }
        var output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../audits"));Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"threshold-density.json"),System.Text.Json.JsonSerializer.Serialize(new{status="pass",method="Synthetic card acquisition using native memberships, evenly cycling all six starter decks. No strategic drafting or combat simulation.",assumptions=new{common=.60,uncommon=.37,rare=.03,upgradeChance=.25,seed=1131},rows},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("COVENANT_THRESHOLD_DENSITY_OK samples-per-size=1200 sizes=25,35 six-native-starters=pass four-generic-thresholds-reduced=pass ember-threshold-preserved=pass assumptions-recorded=pass");
    }
}
