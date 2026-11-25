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
    private static Shader? _stageCutout;
    internal static readonly Color Cyan=new("43efd0"), Dim=new("657b79"), Ink=new("122126");
    private static readonly Color Gold=new("f5c75b"), PaleGold=new("e5cf94"), MutedGold=new("ad9865");
    private static readonly Color OracleRed=new("f77c62"), OracleDim=new("ca6757"), MutedRed=new("8c493e");
    private static readonly Color InactiveIcon=new("a2b7b4"), InactiveName=new("bdcecb");
    private static readonly Color BadgeShadow=new(0,0,0,.68f), BadgeOutline=new("121c1e");

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
        var oracle=bond.Id==Bond.Oracle;
        var hasSpecialPalette=HasSpecialPalette(bond.Id);
        var accent=Accent(bond.Id,active);
        var unfilled=oracle?MutedRed:hasSpecialPalette?MutedGold:Dim;
        var badge=new Control{Name="Badge",Size=new(Width,card?62:Height),MouseFilter=Control.MouseFilterEnum.Ignore};
        var stageCenter=new Vector2(36,30-RingRadius);
        var stageHalfHeight=5.5f*StageReferenceScale;
        var stageStroke=StageReferenceScale;
        var stageSpacing=5*StageReferenceScale;
        if(tier>0)
        {
            // Only the disc and ring use this material. The stage is a separate child, so the
            // margin around its strokes reveals the real scene rather than a painted background.
            _stageCutout??=new Shader{Code="""
                shader_type canvas_item;
                uniform vec4 cutout;
                varying vec2 local_position;
                void vertex() { local_position = VERTEX; }
                void fragment() {
                    if (local_position.x >= cutout.x && local_position.y >= cutout.y &&
                        local_position.x <= cutout.z && local_position.y <= cutout.w) {
                        discard;
                    }
                }
                """};
            const float margin=1.5f;
            var halfWidth=(stageStroke+(tier-1)*stageSpacing)/2;
            var cutout=new Vector4(stageCenter.X-halfWidth-margin,stageCenter.Y-stageHalfHeight-margin,
                stageCenter.X+halfWidth+margin,stageCenter.Y+stageHalfHeight+margin);
            var material=new ShaderMaterial{Shader=_stageCutout};material.SetShaderParameter("cutout",cutout);badge.Material=material;
            badge.SetMeta("stage_cutout",cutout);
        }
        // Before activation, the ring measures progress toward the first threshold.
        badge.Draw+=()=>
        {
            var center=new Vector2(36,30);
            if(emphasizeInactive)
            {
                badge.DrawCircle(center+new Vector2(0,1.4f),DiscRadius+1.2f,BadgeShadow,antialiased:true);
                badge.DrawCircle(center,DiscRadius+.85f,hasSpecialPalette?unfilled:Dim,antialiased:true);
            }
            // Do not paint the annulus: the space between the inner disc and outer segments is transparent.
            badge.DrawCircle(center,DiscRadius,active||hasSpecialPalette?accent:new Color("233135"));
            var step=Mathf.Tau/segments;
            // Match the visible radial clearance (26 - 1.5 - 21), with a little extra breathing room.
            var gap=Math.Min(2*Mathf.Asin((RingInnerRadius-DiscRadius+1)/(2*RingRadius)),step*.35f);
            for(var i=0;i<segments;i++)
            {
                var start=-Mathf.Pi/2+i*step+gap/2;
                var end=start+step-gap;
                if(emphasizeInactive)
                {
                    badge.DrawArc(center+new Vector2(0,1.2f),RingRadius,start,end,20,BadgeShadow,4.5f,true);
                    badge.DrawArc(center,RingRadius,start,end,20,BadgeOutline,4.5f,true);
                }
                if(oracle)
                {
                    badge.DrawArc(center,RingOuterRadius+1.6f,start,end,20,new Color(Gold,.09f),2.5f,true);
                    badge.DrawArc(center,RingOuterRadius+.6f,start,end,20,new Color(Gold,active?.25f:.17f),1.4f,true);
                }
                var color=i<count?(suppressed?new Color("9aa9a7"):accent):unfilled;
                badge.DrawArc(center,RingRadius,start,end,20,color,RingOuterRadius-RingInnerRadius,true);
            }
        };
        if(tier>0)
        {
            var stage=new Control{Name="Stage",Size=new(Width,16),MouseFilter=Control.MouseFilterEnum.Ignore};badge.AddChild(stage);
            stage.Draw+=()=>
            {
                for(var i=0;i<tier;i++)
                {
                    var x=stageCenter.X+(i-(tier-1)/2f)*stageSpacing;
                    stage.DrawLine(new(x,stageCenter.Y-stageHalfHeight),new(x,stageCenter.Y+stageHalfHeight),Colors.White,stageStroke,true);
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
        badge.SetMeta("segments",segments);badge.SetMeta("filled_segments",Math.Min(count,segments));
        return badge;
    }
}
