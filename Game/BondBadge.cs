using System.Globalization;
using System.Text;
using Covenant.Core;
using Godot;

namespace Covenant.Game;

/// <summary>The same silhouette, ring and foreground caption on cards and on the HUD.</summary>
internal static class BondBadge
{
    internal const float Width=72, Height=84;
    internal const float DiscRadius=21, RingInnerRadius=24.5f, RingOuterRadius=27.5f;
    internal const float RingRadius=(RingInnerRadius+RingOuterRadius)/2;
    // Reference 02/04: a 52 px outer diameter, 1 px solid white core,
    // 11 px stroke height and 5 px spacing between the two stage strokes.
    private const float StageReferenceScale=(RingOuterRadius*2)/52;
    private const float BackgroundHeight=62;
    private const int RasterScale=2;
    private static readonly Vector2 Center=new(36,30);
    private static readonly Vector2 StageCenter=new(Center.X,Center.Y-RingRadius);
    private const float StageHalfHeight=5.5f*StageReferenceScale;
    private const float StageStroke=StageReferenceScale;
    private const float StageSpacing=5*StageReferenceScale;
    private readonly record struct BackgroundStyle(Bond? Palette,bool Card,bool Suppressed,int Tier,int Segments,int Filled);
    private static readonly Dictionary<BackgroundStyle,Texture2D> Backgrounds=new();
    internal static readonly Color Cyan=new("43efd0"), Dim=new("657b79"), Ink=new("122126");
    private static readonly Color Gold=new("f5c75b"), PaleGold=new("e5cf94"), MutedGold=new("ad9865");
    private static readonly Color OracleRed=new("f77c62"), OracleDim=new("ca6757"), MutedRed=new("8c493e");
    private static readonly Color InactiveIcon=new("a2b7b4"), InactiveName=new("bdcecb");
    private static readonly Color BadgeShadow=new(0,0,0,.68f), BadgeOutline=new("121c1e");
    private static readonly Color InactiveDisc=new("233135"), SuppressedFill=new("9aa9a7");

    internal static bool HasSpecialPalette(Bond bond) => bond is Bond.Fate or Bond.Oracle;
    internal static Color Accent(Bond bond,bool active) => bond switch
    {
        Bond.Fate=>active?Gold:PaleGold,
        Bond.Oracle=>active?OracleRed:OracleDim,
        _=>active?Cyan:Dim
    };

    internal static Control Create(BondDefinition bond,int count,int layers,bool card=false,bool suppressed=false,int? activeTier=null)
    {
        var tier=suppressed?0:activeTier??bond.Tier(count);
        var active=tier>0;
        var emphasizeInactive=!active&&!card;
        var segments=active?bond.Segments:bond.Basic;
        var hasSpecialPalette=HasSpecialPalette(bond.Id);
        var accent=Accent(bond.Id,active);
        var badge=new Control{Name="Badge",Size=new(Width,card?62:Height),MouseFilter=Control.MouseFilterEnum.Ignore};
        var filled=Math.Clamp(count,0,segments);
        var background=BackgroundTexture(new(hasSpecialPalette?bond.Id:null,card,suppressed,tier,segments,filled));
        badge.AddChild(new TextureRect{Name="Background",ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,
            Texture=background,Size=new(Width,BackgroundHeight),TextureFilter=CanvasItem.TextureFilterEnum.Linear,
            MouseFilter=Control.MouseFilterEnum.Ignore});
        if(tier>0)
        {
            var stage=new Control{Name="Stage",Size=new(Width,16),MouseFilter=Control.MouseFilterEnum.Ignore};badge.AddChild(stage);
            stage.Draw+=()=>
            {
                for(var i=0;i<tier;i++)
                {
                    var x=StageCenter.X+(i-(tier-1)/2f)*StageSpacing;
                    stage.DrawLine(new(x,StageCenter.Y-StageHalfHeight),new(x,StageCenter.Y+StageHalfHeight),Colors.White,StageStroke,true);
                }
            };
        }
        // Set the sizing policy before the texture/size: otherwise Godot clamps Size to the texture's minimum
        // and retains that larger rectangle even after IgnoreSize is selected, moving the artwork off-center.
        var icon=new TextureRect{Name="Logo",ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,Texture=Presentation.Icon(bond.Id),Size=new(28,28),Position=new(22,16),
            Modulate=active||hasSpecialPalette?Ink:emphasizeInactive?InactiveIcon:Dim,
            TextureFilter=CanvasItem.TextureFilterEnum.Nearest,MouseFilter=Control.MouseFilterEnum.Ignore};
        badge.AddChild(icon);
        var caption=new Label{Name="Caption",Text=card?bond.Name:bond.UsesLayers||bond.ShowsCounter?layers.ToString():"",Position=new(0,37),Size=new(72,25),
            HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,
            MouseFilter=Control.MouseFilterEnum.Ignore};
        caption.AddThemeFontSizeOverride("font_size",card?22:Math.Clamp(110/layers.ToString().Length,13,21));
        if(card)caption.UseBundledChinese();
        caption.AddThemeColorOverride("font_color",hasSpecialPalette?accent:active?new Color("ecfffa"):emphasizeInactive?InactiveName:new Color("aabbb8"));
        caption.AddThemeColorOverride("font_outline_color",Ink);
        caption.AddThemeConstantOverride("outline_size",6);
        badge.AddChild(caption);
        if(!card)
        {
            var name=new Label{Name="BondName",Text=bond.Name,Position=new(0,62),Size=new(Width,22),
                HorizontalAlignment=HorizontalAlignment.Center,MouseFilter=Control.MouseFilterEnum.Ignore};
            name.AddThemeFontSizeOverride("font_size",17);
            name.UseBundledChinese();
            name.AddThemeColorOverride("font_color",hasSpecialPalette?accent:active?new Color("ecfffa"):InactiveName);
            name.AddThemeColorOverride("font_outline_color",Ink);name.AddThemeConstantOverride("outline_size",active?3:4);
            if(emphasizeInactive)
            {
                name.AddThemeColorOverride("font_shadow_color",BadgeShadow);
                name.AddThemeConstantOverride("shadow_offset_y",2);
                caption.AddThemeColorOverride("font_shadow_color",BadgeShadow);
                caption.AddThemeConstantOverride("shadow_offset_y",2);
            }
            badge.AddChild(name);
        }
        badge.SetMeta("segments",segments);badge.SetMeta("filled_segments",filled);
        return badge;
    }

    private static Texture2D BackgroundTexture(BackgroundStyle style)
    {
        if(Backgrounds.TryGetValue(style,out var result))return result;

        var active=style.Tier>0;
        var accent=style.Palette is {} palette?Accent(palette,active):active?Cyan:Dim;
        var unfilled=style.Palette switch{Bond.Oracle=>MutedRed,Bond.Fate=>MutedGold,_=>Dim};
        var outline=!active&&!style.Card;
        var svg=new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture,$"<svg xmlns='http://www.w3.org/2000/svg' width='{Width}' height='{BackgroundHeight}'>");
        void Disc(float y,float radius,Color color)=>svg.Append(CultureInfo.InvariantCulture,
            $"<circle cx='{Center.X}' cy='{y}' r='{radius}' fill='#{color.ToHtml(false)}' fill-opacity='{color.A}'/>");
        void Arc(float y,float radius,float start,float end,Color color,float width)
        {
            var first=new Vector2(Center.X,y)+Vector2.FromAngle(start)*radius;
            var last=new Vector2(Center.X,y)+Vector2.FromAngle(end)*radius;
            var largeArc=end-start>Mathf.Pi?1:0;
            svg.Append(CultureInfo.InvariantCulture,
                $"<path d='M {first.X} {first.Y} A {radius} {radius} 0 {largeArc} 1 {last.X} {last.Y}' fill='none' stroke='#{color.ToHtml(false)}' stroke-opacity='{color.A}' stroke-width='{width}'/>");
        }
        if(outline)
        {
            Disc(Center.Y+1.4f,DiscRadius+1.2f,BadgeShadow);
            Disc(Center.Y,DiscRadius+.85f,unfilled);
        }
        Disc(Center.Y,DiscRadius,active||style.Palette.HasValue?accent:InactiveDisc);
        // Keep both the annulus and the gaps between segments transparent.
        var step=Mathf.Tau/style.Segments;
        var gap=Math.Min(2*Mathf.Asin((RingInnerRadius-DiscRadius+1)/(2*RingRadius)),step*.35f);
        for(var i=0;i<style.Segments;i++)
        {
            var start=-Mathf.Pi/2+i*step+gap/2;
            var end=start+step-gap;
            if(outline)
            {
                Arc(Center.Y+1.2f,RingRadius,start,end,BadgeShadow,4.5f);
                Arc(Center.Y,RingRadius,start,end,BadgeOutline,4.5f);
            }
            if(style.Palette==Bond.Oracle)
            {
                Arc(Center.Y,RingOuterRadius+1.6f,start,end,new Color(Gold,.09f),2.5f);
                Arc(Center.Y,RingOuterRadius+.6f,start,end,new Color(Gold,active?.25f:.17f),1.4f);
            }
            var color=i<style.Filled?(style.Suppressed?SuppressedFill:accent):unfilled;
            Arc(Center.Y,RingRadius,start,end,color,RingOuterRadius-RingInnerRadius);
        }
        svg.Append("</svg>");
        using var image=new Image();
        if(image.LoadSvgFromString(svg.ToString(),RasterScale)!=Error.Ok)
            throw new InvalidOperationException("Could not rasterize covenant badge background.");
        if(active)
        {
            // Bake the stage notch into the alpha channel so the badge renders the same in all card grids.
            const float margin=1.5f;
            var halfWidth=(StageStroke+(style.Tier-1)*StageSpacing)/2+margin;
            var halfHeight=StageHalfHeight+margin;
            var left=Mathf.FloorToInt((StageCenter.X-halfWidth)*RasterScale);
            var right=Mathf.CeilToInt((StageCenter.X+halfWidth)*RasterScale);
            var top=Math.Max(0,Mathf.FloorToInt((StageCenter.Y-halfHeight)*RasterScale));
            var bottom=Mathf.CeilToInt((StageCenter.Y+halfHeight)*RasterScale);
            image.FillRect(new(left,top,right-left,bottom-top),Colors.Transparent);
        }
        return Backgrounds[style]=ImageTexture.CreateFromImage(image);
    }
}
