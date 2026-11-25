using Covenant.Game;

// Test-only fixture. The preexisting covenant regression suite uses the retained
// legacy metadata; new trait checks and the native route use the shipped single traits.
internal sealed class LegacyTraitIsolation:IDisposable
{
    private readonly Dictionary<string,CardDefinition> _original;
    private readonly Dictionary<string,CardDefinition> _catalog;
    internal LegacyTraitIsolation()
    {
        _catalog=(Dictionary<string,CardDefinition>)CardMembership.Definitions;
        _original=new(_catalog);
        foreach(var (name,card) in _original)_catalog[name]=card with{Trait=card.Trait with{Effects=null}};
    }
    public void Dispose(){foreach(var (name,card) in _original)_catalog[name]=card;}
}
