using Godot;
using Covenant.Core;
using MegaCrit.Sts2.Core.Models;

namespace Covenant.Game;

internal static class TraitListView
{
    internal static PanelContainer Panel(string name)
    {
        var panel=new PanelContainer{Name=name,MouseFilter=Control.MouseFilterEnum.Stop};
        panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat
        {
            BgColor=new("101d22f7"),BorderColor=new("496660"),BorderWidthLeft=2,BorderWidthRight=1,BorderWidthTop=1,BorderWidthBottom=1,
            ContentMarginLeft=12,ContentMarginRight=12,ContentMarginTop=10,ContentMarginBottom=12
        });
        return panel;
    }

    internal static Label Heading(string text,int size)
    {
        var label=new Label{Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        label.UseBundledChinese();
        label.AddThemeFontSizeOverride("font_size",size);
        label.AddThemeColorOverride("font_color",BondBadge.Cyan);
        return label;
    }

    internal static VBoxContainer ScrollList(Node parent)
    {
        var scroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,VerticalScrollMode=ScrollContainer.ScrollMode.Auto,SizeFlagsVertical=Control.SizeFlags.ExpandFill};
        parent.AddChild(scroll);
        var bar=scroll.GetVScrollBar();
        bar.AddThemeStyleboxOverride("scroll",new StyleBoxFlat{BgColor=new("1c3137"),ContentMarginLeft=5,ContentMarginRight=5});
        foreach(var key in new[]{"grabber","grabber_highlight","grabber_pressed"})
            bar.AddThemeStyleboxOverride(key,new StyleBoxFlat{BgColor=new("548a83"),CornerRadiusTopLeft=3,CornerRadiusBottomLeft=3,CornerRadiusTopRight=3,CornerRadiusBottomRight=3});
        var list=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        list.AddThemeConstantOverride("separation",9);scroll.AddChild(list);
        return list;
    }

    internal static string Key(IEnumerable<TraitEntry> entries,float width)=>CovenantSettings.ShowFormulas+"|"+width+"|"+string.Join("|",entries);

    internal static void Fill(VBoxContainer list,IEnumerable<TraitEntry> entries,AbstractModel model,float width)
    {
        foreach(var child in list.GetChildren()){list.RemoveChild(child);child.QueueFree();}
        foreach(var group in entries.GroupBy(entry=>(entry.Bond,entry.Text,entry.Source,entry.Kind,entry.State)))
        {
            var entry=group.First();
            var row=new VBoxContainer();row.AddThemeConstantOverride("separation",3);list.AddChild(row);
            var heading=new HBoxContainer();heading.AddThemeConstantOverride("separation",7);row.AddChild(heading);
            heading.AddChild(new TextureRect{Texture=Presentation.Icon(entry.Bond),CustomMinimumSize=new(17,17),
                SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=Control.MouseFilterEnum.Ignore});
            var copies=group.Count();
            var source=entry.Text.Contains("[scaled]",StringComparison.Ordinal)?$"{entry.Source}（{BondCatalog.Get(entry.Bond).Name}）":entry.Source;
            heading.AddChild(Heading(copies==1?source:$"{source} ×{copies}",18));
            var state=Heading(entry.State==null?entry.Kind:$"{entry.Kind} · {entry.State}",14);
            state.AddThemeColorOverride("font_color",new Color("aabbc0"));row.AddChild(state);
            var description=BondDescription.Create("Description");
            description.CustomMinimumSize=new(width,0);description.AddThemeFontSizeOverride("normal_font_size",17);
            description.Text=BondDescription.Format(entry.Text,model,splitSentences:false);row.AddChild(description);
        }
    }
}
