using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.TestSupport;

namespace Covenant.Game;

public static class CovenantSettings
{
    private sealed record Data(int Schema=1,bool EnableAllCardPools=true,bool ShowCardTraits=true,bool HideMerchantHand=false,bool ShowFormulas=false,bool EnemyFloorScaling=false);
    private static bool _loaded;
    public static bool EnableAllCardPools {get;private set;}=true;
    public static bool ShowCardTraits {get;private set;}=true;
    public static bool ShowFormulas {get;private set;}
    public static bool HideMerchantHand {get;private set;}
    public static bool EnemyFloorScaling {get;private set;}
    private static string PathName=>System.IO.Path.Combine(OS.GetUserDataDir(),"Covenant","settings.json");
    public static void EnsureLoaded()
    {
        if(_loaded)return;_loaded=true;if(TestMode.IsOn)return;
        if(!File.Exists(PathName))return;
        Data? data;
        try{data=JsonSerializer.Deserialize<Data>(File.ReadAllText(PathName));}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"[Covenant] Could not load preferences. Using defaults. {error.Message}");
            return;
        }
        if(data is not {Schema:1})
        {
            Log.Warn("[Covenant] Unsupported preference format. Using defaults.");
            return;
        }
        EnableAllCardPools=data.EnableAllCardPools;
        ShowCardTraits=data.ShowCardTraits;
        ShowFormulas=data.ShowFormulas;
        HideMerchantHand=data.HideMerchantHand;
        EnemyFloorScaling=data.EnemyFloorScaling;
    }
    public static void SetAllCardPools(bool value)
    {
        EnsureLoaded();EnableAllCardPools=value;Save();
    }
    public static void SetShowCardTraits(bool value)
    {
        EnsureLoaded();if(ShowCardTraits==value)return;
        ShowCardTraits=value;Save();LibraryGridSpacing.RefreshPreference();Presentation.RefreshCardTraits();
    }
    public static void SetShowFormulas(bool value)
    {
        EnsureLoaded();ShowFormulas=value;Save();Presentation.RefreshCardTraits();
    }
    public static void SetEnemyFloorScaling(bool value)
    {
        EnsureLoaded();EnemyFloorScaling=value;Save();
    }
    private static void Save()
    {
        if(TestMode.IsOn)return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName)!);
            var temporary=PathName+".tmp";
            var data=new Data(EnableAllCardPools:EnableAllCardPools,ShowCardTraits:ShowCardTraits,HideMerchantHand:HideMerchantHand,ShowFormulas:ShowFormulas,EnemyFloorScaling:EnemyFloorScaling);
            File.WriteAllText(temporary,JsonSerializer.Serialize(data,new JsonSerializerOptions{WriteIndented=true}));
            File.Move(temporary,PathName,true);
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"[Covenant] Could not save preferences. {error.Message}");
        }
    }
    public static void SetHideMerchantHand(bool value){EnsureLoaded();HideMerchantHand=value;Save();MerchantHandOption.Refresh();}
}
