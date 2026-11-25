using System.Numerics;
using System.Text.Json;

namespace Covenant.Core;

// Fractional migration residue preserves old progress; displayed layers and all grants are integers.
public sealed record LayerRemainder(long Numerator,long Denominator);

public sealed class Ledger
{
    public const int MaxLayers = 999;
    public int Schema { get; set; } = 3;
    public Dictionary<Bond,int> Layers { get; set; } = new();
    public Dictionary<Bond,int> Units { get; set; } = new();
    public Dictionary<Bond,LayerRemainder> Remainders { get; set; } = new();
    public Dictionary<string,int> Uses { get; set; } = new(StringComparer.Ordinal);
    public int ProfitGoldProgress { get; set; }
    public int Permanent(Bond bond)=>Math.Min(MaxLayers,Layers.GetValueOrDefault(bond));
    public int Get(Bond bond)=>Permanent(bond);
    public Scale Scale(Bond bond)=>new(Get(bond),BondCatalog.Get(bond).LayerUnit);
    public int Add(Bond bond,int amount)
    {
        if(amount<0||!Enum.IsDefined(bond))throw new ArgumentOutOfRangeException(nameof(amount));
        if(!BondCatalog.Get(bond).UsesLayers&&bond!=Bond.Tony)return 0;
        Layers[bond]=(int)Math.Min(MaxLayers,(long)Permanent(bond)+amount);
        if(Layers[bond]==MaxLayers)Remainders.Remove(bond);
        Units[bond]=BondCatalog.Get(bond).LayerUnit;
        return Layers[bond];
    }
    public void ClearBattle()=>Layers.Remove(Bond.Tony);
    internal int RecordProfitSpending(int gold)
    {
        var completed=Math.DivRem((long)ProfitGoldProgress+gold,BondCatalog.ProfitGoldThreshold,out var remainder);
        ProfitGoldProgress=(int)remainder;
        return (int)completed;
    }
    public bool Claim(string key,int limit=1)
    {
        if(Uses.GetValueOrDefault(key)>=limit)return false;
        Uses[key]=Uses.GetValueOrDefault(key)+1;return true;
    }
    public void ClearCombatUses()=>Uses.Clear();
    public string Save()
    {
        foreach(var bond in BondCatalog.All)Units[bond.Id]=bond.LayerUnit;
        ClampLayers();
        return JsonSerializer.Serialize(this);
    }
    private void ClampLayers()
    {
        foreach(var bond in Layers.Keys.ToArray())
        {
            if(Layers[bond]<MaxLayers)continue;
            Layers[bond]=MaxLayers;
            Remainders.Remove(bond);
        }
    }
    private void Store(Bond bond,BigInteger numerator,BigInteger denominator)
    {
        var whole=BigInteger.DivRem(numerator,denominator,out var remainder);
        Layers[bond]=(int)BigInteger.Min(int.MaxValue,whole);
        if(remainder==0||whole>=int.MaxValue)Remainders.Remove(bond);
        else
        {
            var gcd=BigInteger.GreatestCommonDivisor(remainder,denominator);
            Remainders[bond]=new((long)(remainder/gcd),(long)(denominator/gcd));
        }
    }
    public static Ledger Load(string json)
    {
        var value=JsonSerializer.Deserialize<Ledger>(json)??throw new InvalidDataException("Missing covenant ledger.");
        if(value.Schema is not (1 or 2 or 3)||value.Layers==null||value.Uses==null||value.Units==null||value.Remainders==null||
            value.ProfitGoldProgress is <0 or >=BondCatalog.ProfitGoldThreshold||
            value.Layers.Any(p=>!Enum.IsDefined(p.Key)||p.Value<0)||value.Units.Any(p=>!Enum.IsDefined(p.Key)||p.Value<=0)||
            value.Remainders.Any(p=>!Enum.IsDefined(p.Key)||p.Value==null||p.Value.Numerator<0||p.Value.Denominator<=0||p.Value.Numerator>=p.Value.Denominator))
            throw new InvalidDataException("Unsupported or invalid covenant ledger.");
        foreach(var definition in BondCatalog.All)
        {
            var bond=definition.Id;var unit=definition.LayerUnit;
            if(value.Schema==1)
            {
                var old=value.Layers.GetValueOrDefault(bond);var tier=global::Covenant.Core.Scale.LegacyTier(old);
                var lower=5L*tier*(tier+1);var width=10L*(tier+1);
                value.Store(bond,(BigInteger)tier*unit*width+(old-lower)*unit,width);
            }
            else if(value.Units.GetValueOrDefault(bond,unit) is var previous&&previous!=unit)
            {
                var fraction=value.Remainders.GetValueOrDefault(bond)??new(0,1);
                value.Store(bond,((BigInteger)value.Layers.GetValueOrDefault(bond)*fraction.Denominator+fraction.Numerator)*unit,(BigInteger)fraction.Denominator*previous);
            }
            value.Units[bond]=unit;
            if(!definition.UsesLayers&&bond!=Bond.Tony){value.Layers.Remove(bond);value.Remainders.Remove(bond);}
        }
        value.ClampLayers();
        value.Schema=3;return value;
    }
}
