using Covenant.Core;
using Covenant.Game;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;

internal static partial class MechanismChecks
{
    private static async Task FullTraitAmounts(Action<bool,string> check)
    {
        var f=new Battle();var upgraded=f.Add<AdaptiveStrike>();upgraded.UpgradeInternal();f.Copies<AdaptiveStrike>(3);
        // Legacy saves may retain this obsolete entry. It must not block an award
        // or truncate the amount printed in the source card's tooltip.
        f.Terminal.Ledger.Uses["growth:Creation"]=11;
        for(int i=1;i<=12;i++)
        {
            await CardPileCmd.AddGeneratedCardToCombat(f.State.CreateCard<DefendIronclad>(f.Player),PileType.Hand,f.Player);
            check(f.Terminal.Ledger.Get(Bond.Creation)==9*i,"Every generation awards three full two-layer traits and one full upgraded three-layer trait: "+i);
            check(f.Terminal.TraitUses(upgraded)==i,"The upgraded source counter advances once per complete award: "+i);
        }
        check(Presentation.TraitText(upgraded).Contains("增加3层")&&!Presentation.TraitText(upgraded).Contains("至多"),"The shown amount and counter match the three actual upgraded awards");
        await CardPileCmd.AddGeneratedCardToCombat(f.State.CreateCard<DefendIronclad>(f.Player),PileType.Hand,f.Player);
        check(f.Terminal.Ledger.Get(Bond.Creation)==117,"The fourth event respects each card's continuous cadence without a hidden shared limit");
        Console.WriteLine("COVENANT_FULL_TRAIT_AMOUNT_OK no-shared-cap=pass upgraded-amount-not-truncated=pass continuous-cadence=pass legacy-ledger-compatible=pass");
    }
}

