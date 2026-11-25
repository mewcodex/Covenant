using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

internal sealed record NativeCardCapabilities(bool Orbs=false,bool Doom=false,bool Forge=false,bool Stretch=false,bool GainStars=false,bool StrikeName=false);
internal static class NativeCapabilities
{
    private static readonly IReadOnlyDictionary<string,NativeCardCapabilities> Values=Load();
    private static IReadOnlyDictionary<string,NativeCardCapabilities> Load()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Covenant.Data.native-capabilities.json")??throw new InvalidDataException("Missing native card capability evidence");
        return JsonSerializer.Deserialize<Dictionary<string,NativeCardCapabilities>>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    }
    internal static NativeCardCapabilities Of(CardModel card)=>Values.GetValueOrDefault(card.GetType().Name)??new();
    internal static bool IsNamedStrike(CardModel card)=>Values.TryGetValue(card.GetType().Name,out var capabilities)
        ?capabilities.StrikeName:card.Title.Contains("打击",StringComparison.Ordinal);
}
