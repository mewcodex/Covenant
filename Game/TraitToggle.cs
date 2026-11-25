using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace Covenant.Game;

internal static class TraitToggle
{
    internal static NFastModeTickbox Create(string name=SettingsUi.TraitToggleName)
    {
        // Duplicate directly: re-packing this inherited scene also instantiates its inherited tick images,
        // producing a second pair of indicators which no longer follows IsTicked.
        var settings=GD.Load<PackedScene>("res://scenes/screens/settings_screen.tscn").Instantiate<Control>();
        var original=Descendants(settings).OfType<NFastModeTickbox>().First();
        var box=(NFastModeTickbox)original.Duplicate(6);box.Name=name;SettingsUi.FixOwner(box,box);settings.Free();
        box.CustomMinimumSize=new(64,64);
        box.FocusNeighborLeft="";box.FocusNeighborRight="";box.FocusNeighborTop="";box.FocusNeighborBottom="";
        return box;
    }
    private static IEnumerable<Node> Descendants(Node root)
    {foreach(var child in root.GetChildren()){yield return child;foreach(var item in Descendants(child))yield return item;}}
}
