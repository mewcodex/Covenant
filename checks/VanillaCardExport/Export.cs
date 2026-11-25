using System.Reflection;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

[ModInitializer(nameof(Initialize))]
public static class VanillaExport
{
    private static string Output=>System.Environment.GetEnvironmentVariable("COVENANT_CARD_EXPORT_OUTPUT")??throw new InvalidOperationException("Missing export directory.");
    private static bool Started;
    private static readonly TaskCompletionSource<MegaCrit.Sts2.Core.Assets.AssetLoadingSession> Common=new();
    public static void Initialize()
    {
        if(!Path.GetFileName(OS.GetUserDataDir()).StartsWith("CovenantVanillaExport-",StringComparison.Ordinal))throw new Exception("Exporter requires its dedicated disposable profile.");
        var h=new Harmony("VanillaCardExport");
        h.Patch(AccessTools.Method(typeof(NMainMenu),nameof(NMainMenu._Ready)),postfix:new HarmonyMethod(typeof(VanillaExport),nameof(Run)));
        h.Patch(AccessTools.Method(typeof(NAssetLoader),nameof(NAssetLoader.LoadInTheBackground)),prefix:new HarmonyMethod(typeof(VanillaExport),nameof(Loading)));
    }
    private static void Loading(MegaCrit.Sts2.Core.Assets.AssetLoadingSession session)
    {if((string?)AccessTools.Field(session.GetType(),"_name").GetValue(session)=="Common")Common.TrySetResult(session);}
    private static readonly JsonSerializerOptions Options=new(){WriteIndented=true,Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    private static string Plain(string text)
    {
        text=Regex.Replace(text,@"\[img(?:=[^\]]*)?\](.*?)\[/img\]",m=>m.Groups[1].Value.Contains("energy")?"【能量】":m.Groups[1].Value.Contains("star")?"【蓝星】":"【图标："+Path.GetFileNameWithoutExtension(m.Groups[1].Value)+"】");
        return Regex.Replace(text,@"\[/?[A-Za-z_][^\]\r\n]*\]","").Trim();
    }
    private static object Keyword(CardKeyword keyword)
    {
        var key=keyword.ToString().ToUpperInvariant();
        var title=new LocString("card_keywords",key+".title").GetFormattedText();
        var description=new LocString("card_keywords",key+".description");description.Add("energyPrefix","colorless");
        return new{id=keyword.ToString(),name=title,description=Plain(description.GetFormattedText())};
    }
    private static object State(CardModel card)
    {
        var formatted=card.GetDescriptionForPile(PileType.Deck);
        var hand=card.GetDescriptionForPile(PileType.Hand);
        var energy=card.EnergyCost.GetWithModifiers(CostModifiers.None);
        return new{
            level=card.CurrentUpgradeLevel,name=card.Title,type=card.Type.ToString(),rarity=card.Rarity.ToString(),
            energy=new{kind=card.EnergyCost.CostsX?"x":energy<0?"none":"fixed",value=energy<0||card.EnergyCost.CostsX?(int?)null:energy,rawValue=energy},
            stars=new{kind=card.HasStarCostX?"x":card.BaseStarCost<0?"none":"fixed",value=card.BaseStarCost<0||card.HasStarCostX?(int?)null:card.BaseStarCost,rawValue=card.BaseStarCost},
            description=Plain(formatted),descriptionFormatted=formatted,handDescription=Plain(hand),
            template=card.Description.GetRawText(),keywords=card.Keywords.Order().Select(Keyword).ToArray(),
            tags=card.Tags.Order().Select(t=>t.ToString()).ToArray(),
            variables=card.DynamicVars.Values.ToDictionary(v=>v.Name,v=>new{value=v.BaseValue,preview=v.PreviewValue}),
            pool=card.Pool.GetType().Name,visualPool=card.VisualCardPool.GetType().Name,
            frame=card.VisualCardPool.CardFrameMaterialPath,energyColor=card.VisualCardPool.EnergyColorName,isColorless=card.VisualCardPool.IsColorless,
            target=card.TargetType.ToString()
        };
    }
    private static object[] Variants(CardModel canonical)
    {
        if(canonical is not MadScience)return [];
        var variants=new List<object>();
        foreach(var type in new[]{CardType.Attack,CardType.Skill,CardType.Power})
        {
            var riders=type switch{CardType.Attack=>new[]{TinkerTime.RiderEffect.Sapping,TinkerTime.RiderEffect.Violence,TinkerTime.RiderEffect.Choking},CardType.Skill=>new[]{TinkerTime.RiderEffect.Energized,TinkerTime.RiderEffect.Wisdom,TinkerTime.RiderEffect.Chaos},_=>new[]{TinkerTime.RiderEffect.Expertise,TinkerTime.RiderEffect.Curious,TinkerTime.RiderEffect.Improvement}};
            foreach(var rider in riders.Prepend(TinkerTime.RiderEffect.None))
            {
                var card=(MadScience)canonical.ToMutable();card.TinkerTimeType=type;card.TinkerTimeRider=rider;
                var before=State(card);card.UpgradeInternal();card.FinalizeUpgradeInternal();
                variants.Add(new{variantId=type+"/"+rider,type=type.ToString(),rider=rider.ToString(),isEventPreviewOnly=rider==TinkerTime.RiderEffect.None,states=new[]{before,State(card)}});
            }
        }
        return variants.ToArray();
    }
    private static async void Run(NMainMenu __instance)
    {
        var menu=__instance;
        if(Started)return;Started=true;Directory.CreateDirectory(Output);
        try
        {
            var session=await Common.Task.WaitAsync(TimeSpan.FromSeconds(60));await session.Task.WaitAsync(TimeSpan.FromSeconds(60));
            if(System.Environment.GetEnvironmentVariable("COVENANT_REFERENCE_EXPORT")=="1")
            {
                var directory="G:/sts2modding/Covenant/audits/0.5/icon-references";Directory.CreateDirectory(directory);
                var paths=new List<string>();
                void Scan(string path){foreach(var file in DirAccess.GetFilesAt(path)){var full=path+"/"+file;if(full.Contains("vakuu",StringComparison.OrdinalIgnoreCase)||full.Contains("bowlbug",StringComparison.OrdinalIgnoreCase)||full.Contains("workbug_rock",StringComparison.OrdinalIgnoreCase))paths.Add(full);}foreach(var child in DirAccess.GetDirectoriesAt(path))Scan(path+"/"+child);}
                Scan("res://images");File.WriteAllLines(directory+"/native-paths.txt",paths);
                foreach(var path in paths.Where(p=>p.EndsWith(".png")||p.EndsWith(".tres")))
                {
                    try{var texture=GD.Load<Texture2D>(path);if(texture!=null)texture.GetImage().SavePng(directory+"/"+Path.GetFileNameWithoutExtension(path)+".png");}catch(Exception){}
                }
                Console.WriteLine("VANILLA_REFERENCE_EXPORT_COMPLETE paths="+paths.Count);menu.GetTree().Quit(0);return;
            }
            // No Covenant assembly is referenced or deployed into this isolated game.
            if(AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="Covenant"))throw new Exception("Covenant must not be loaded into the vanilla export process.");
            var native=typeof(CardModel).Assembly;var cards=ModelDb.AllCards.Where(c=>c.GetType().Assembly==native).OrderBy(c=>c.GetType().Name).ToArray();
            var rows=new List<object>();var errors=new List<object>();
            foreach(var language in new[]{"zhs","eng"})
            {
                LocManager.Instance.SetLanguage(language);
                foreach(var canonical in cards)
                {
                    try
                    {
                        var card=canonical.ToMutable();var states=new List<object>{State(card)};
                        var levels=canonical.MaxUpgradeLevel>1?new[]{1,2,3,4,5,6,10}:canonical.IsUpgradable?new[]{1}:Array.Empty<int>();
                        foreach(var level in levels)
                        {
                            while(card.CurrentUpgradeLevel<level){card.UpgradeInternal();card.FinalizeUpgradeInternal();}
                            states.Add(State(card));
                        }
                        rows.Add(new{id=canonical.Id.ToString(),className=canonical.GetType().Name,language,
                            maxUpgradeLevel=canonical.MaxUpgradeLevel,
                            pools=ModelDb.AllCardPools.Where(p=>p.AllCardIds.Contains(canonical.Id)).Select(p=>p.GetType().Name).ToArray(),states,variants=Variants(canonical)});
                    }
                    catch(Exception ex){errors.Add(new{card=canonical.GetType().Name,language,error=ex.ToString()});}
                }
            }
            var extra=native.GetTypes().Where(t=>!t.IsAbstract&&t.IsSubclassOf(typeof(CardModel))&&!cards.Any(c=>c.GetType()==t)).Select(t=>t.FullName).Order().ToArray();
            var data=new{createdUtc=DateTimeOffset.UtcNow,game=System.Environment.GetEnvironmentVariable("COVENANT_CARD_EXPORT_GAME"),nativeAssemblySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(native.Location))),
                exporterOnly=true,context="Fresh unowned cards outside combat; no Covenant, relic, enchantment, affliction or battle-state modifiers. Full native GetDescriptionForPile includes runtime-appended card keywords.",
                nativeCardCount=cards.Length,extraNativeTypesOutsideAllCards=extra,rows,errors};
            File.WriteAllText(Output+"/native-runtime-export.json",JsonSerializer.Serialize(data,Options));
            Console.WriteLine($"VANILLA_EXPORT_COMPLETE cards={cards.Length} rows={rows.Count} errors={errors.Count}");
            menu.GetTree().Quit(errors.Count==0?0:1);
        }
        catch(Exception ex){File.WriteAllText(Output+"/export-failure.txt",ex.ToString());Console.Error.WriteLine(ex);menu.GetTree().Quit(1);}
    }
}
