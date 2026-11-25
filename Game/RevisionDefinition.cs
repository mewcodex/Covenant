using Covenant.Core;

namespace Covenant.Game;

public sealed record RevisionInstruction(string Event,string Action,Bond Bond,string Amount="0",string Condition="1",string Scope="own",string Limit="",string? Parameter=null,string Lifetime="battle",bool RequireActive=true,string? Accumulate=null);
public sealed record RevisionProgram(string Text,string[] NativePowers,RevisionInstruction[] Rules,string? AmountText=null,string[]? DisplayAmounts=null,string[]? DisplayFormats=null,string[]? CausalPowers=null);
public sealed record RevisionTrait(RevisionProgram Base,RevisionProgram Upgraded,string SourceSha256,Dictionary<string,RevisionTrait>? Variants=null);
