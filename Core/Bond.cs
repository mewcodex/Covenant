namespace Covenant.Core;

public enum Bond
{
    Fury, Blood, Ember, Poison, Blades, Sleight, Resonance, Overload, Zero,
    Stars, Forge, Creation, Doom, Bones, Ethereal, Arrows, Melody, Emotion,
    Strike, Guard, Chain, Heavy, Retain, Ritual,
    Mastery, Harmony, Ancient, Encounter, Curse, Status, Profit, Foresight, Solitude,
    Vakuu, Tony, Aid, Channel, Breach, Rotation, Initiative, Fate, Reflow, Oracle, Spectrum
}

public readonly record struct Scale(int Layers,int Unit=10)
{
    public int A=>Math.Max(0,Layers)/Math.Max(1,Unit);
    public int B => A / 3;
    internal static int LegacyTier(int layers)
    {
        var tier=(int)((Math.Sqrt(1+.8*Math.Max(0,layers))-1)/2);
        while(5L*(tier+1)*(tier+2)<=layers)tier++;
        while(5L*tier*(tier+1)>layers)tier--;
        return tier;
    }
}

public sealed record BondDefinition(Bond Id, string Name, string Group, int Basic, int Advanced,
    Func<Scale, string> BasicText, Func<Scale, string> AdvancedText,int LayerUnit=10)
{
    public bool Secondary=>Group=="副盟约";
    public bool UsesLayers=>Id is not (Bond.Harmony or Bond.Ancient or Bond.Solitude or Bond.Vakuu or Bond.Tony or Bond.Oracle or Bond.Spectrum);
    public bool ShowsCounter=>Id==Bond.Tony;
    public int Segments=>Advanced>0?Advanced:Basic;
    public int Tier(int count) => Advanced>0&&count >= Advanced ? 2 : count >= Basic ? 1 : 0;
}
