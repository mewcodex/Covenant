using System.Reflection;
using System.Text.Json;
using Covenant.Core;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

internal static class SecondaryMembership
{
    private sealed record Entry(string Card,string Reason);
    private sealed record Catalog(Entry[] Vakuu,Entry[] Tony);
    private static readonly IReadOnlyDictionary<string,ulong> Bonds=Load();
    private static IReadOnlyDictionary<string,ulong> Load()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Covenant.Data.secondary-covenants.json")!;
        var catalog=JsonSerializer.Deserialize<Catalog>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var bonds=new Dictionary<string,ulong>(StringComparer.Ordinal);
        foreach(var entry in catalog.Vakuu)bonds[entry.Card]=CardMembership.Bit(Bond.Vakuu);
        foreach(var entry in catalog.Tony)bonds[entry.Card]=bonds.GetValueOrDefault(entry.Card)|CardMembership.Bit(Bond.Tony);
        return bonds;
    }
    internal static ulong Mask(CardModel card)=>Bonds.GetValueOrDefault(card.GetType().Name);
}
