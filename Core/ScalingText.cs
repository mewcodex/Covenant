using System.Globalization;
using System.Text.RegularExpressions;

namespace Covenant.Core;

internal static class ScalingText
{
    private static readonly Regex Layer = new(@"\b(?:A|L|total_layers)\b", RegexOptions.Compiled);
    private static readonly Regex FormulaTerms = new(@"\bfloor\(A/(?<divisor>\d+)\)|\bA/(?<divisor>\d+)\b(?!\.)|(?<![/\w])(?<factor>\d+)\*A\b(?!/)|\bA\*(?<factor>\d+)\b(?![./])|\b(?<name>[a-zA-Z_]\w*)\b(?!\s*\()", RegexOptions.Compiled);
    private static readonly Regex Formula = new(@"\[formula\](.*?)\[/formula\]", RegexOptions.Compiled);
    private static readonly Regex PercentageFormula = new(
        @"\[scaled\](?<value>[^\[]+)\[/scaled\]\[formula\] \(=(?<expression>.*?)\)\[/formula\]%", RegexOptions.Compiled);

    internal static bool UsesLayers(string expression) => Layer.IsMatch(expression);

    internal static string Amount(Scale scale, string expression) => Amount(
        RuleExpression.Evaluate(expression, name => name=="L"?scale.Layers:scale.A).ToString(CultureInfo.InvariantCulture),
        scale, expression);

    internal static string Amount(string result, Scale scale, string expression, Func<string, decimal>? resolve = null)
    {
        expression = Regex.Replace(expression, @"\s+", "");
        expression = Regex.Replace(expression, @"\*1(?![\d.])", "");
        expression = FormulaTerms.Replace(expression, match =>
        {
            if (match.Groups["divisor"].Success)
                return Ratio(scale, int.Parse(match.Groups["divisor"].Value, CultureInfo.InvariantCulture));
            if (match.Groups["factor"].Success)
                return Ratio(scale, 1, int.Parse(match.Groups["factor"].Value, CultureInfo.InvariantCulture));
            var name = match.Groups["name"].Value;
            return name == "A" ? Ratio(scale, 1) : name=="L" ? Red(scale.Layers) : name=="total_layers" ? Red(resolve?.Invoke(name)??0) : (resolve?.Invoke(name) ?? 0).ToString(CultureInfo.InvariantCulture);
        });
        expression = expression.Replace("floor(", "(", StringComparison.Ordinal).Replace("*", "×");
        return WithFormula(result, expression);
    }

    internal static string Render(string text, bool showFormulas, string colorTag) => Formula
        .Replace(PercentageFormula.Replace(text, match =>
            Red(match.Groups["value"].Value+"%")+(showFormulas?$" =({match.Groups["expression"].Value})%":"")),
            match => showFormulas ? match.Groups[1].Value : "")
        .Replace("[scaled]", colorTag).Replace("[/scaled]", "[/color]");

    private static string Ratio(Scale scale, int divisor, int factor = 1)
    {
        var denominator = (long)Math.Max(1, scale.Unit) * divisor;
        var common = (long)System.Numerics.BigInteger.GreatestCommonDivisor(factor, denominator);
        var numerator = Red(Math.Max(0, scale.Layers));
        if (factor != common) numerator = $"{factor / common}×{numerator}";
        return denominator == common ? numerator : $"{numerator}/{denominator / common}";
    }

    private static string WithFormula(string result, string expression) => $"{Red(result)}[formula] (={expression})[/formula]";
    private static string Red(object number) => $"[scaled]{number}[/scaled]";
}

internal readonly record struct LayerChance(int BasePercent, int LayersPerPoint, int? Cap = null)
{
    internal decimal At(Scale scale)
    {
        var chance = BasePercent + scale.Layers / (decimal)LayersPerPoint;
        return Cap is {} cap ? Math.Min(cap, chance) : chance;
    }

    internal string Text(Scale scale)
    {
        var expression = $"{BasePercent}+L/{LayersPerPoint}";
        if (Cap is {} cap) expression = $"min({cap},{expression})";
        var value = At(scale).ToString("0.##", CultureInfo.InvariantCulture);
        return ScalingText.Amount(value, scale, expression) + "%";
    }
}
