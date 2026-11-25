using System.Globalization;

namespace Covenant.Core;

/// <summary>Small arithmetic/condition language for compiled rule data, never localized game text.</summary>
internal sealed class RuleExpression
{
    private readonly string _text;
    private readonly Func<string,decimal> _resolve;
    private int _at;
    private RuleExpression(string text,Func<string,decimal> resolve){_text=text;_resolve=resolve;}
    internal static decimal Evaluate(string text,Func<string,decimal> resolve)
    {
        var parser=new RuleExpression(text,resolve);var result=parser.Or();parser.Space();
        if(parser._at!=text.Length)throw new InvalidDataException("Unparsed rule expression: "+text[parser._at..]);
        return result;
    }
    private void Space(){while(_at<_text.Length&&char.IsWhiteSpace(_text[_at]))_at++;}
    private bool Take(string token)
    {
        Space();if(!_text.AsSpan(_at).StartsWith(token,StringComparison.Ordinal))return false;_at+=token.Length;return true;
    }
    private decimal Or(){var value=And();while(Take("||")){var other=And();value=value!=0||other!=0?1:0;}return value;}
    private decimal And(){var value=Compare();while(Take("&&")){var other=Compare();value=value!=0&&other!=0?1:0;}return value;}
    private decimal Compare()
    {
        var a=Add();
        foreach(var op in new[]{">=","<=","==","!=",">","<"})
            if(Take(op)){var b=Add();return (op switch{">="=>a>=b,"<="=>a<=b,"=="=>a==b,"!="=>a!=b,">"=>a>b,"<"=>a<b,_=>false})?1:0;}
        return a;
    }
    private decimal Add()
    {
        var a=Multiply();while(true){if(Take("+"))a+=Multiply();else if(Take("-"))a-=Multiply();else return a;}
    }
    private decimal Multiply()
    {
        var a=Primary();while(true)
        {
            if(Take("*"))a*=Primary();else if(Take("/")){var b=Primary();if(b==0)throw new InvalidDataException("Zero divisor in rule: "+_text);a/=b;}
            else if(Take("%")){var b=Primary();if(b==0)throw new InvalidDataException("Zero divisor in rule: "+_text);a%=b;}else return a;
        }
    }
    private decimal Primary()
    {
        if(Take("-"))return -Primary();if(Take("!"))return Primary()==0?1:0;
        if(Take("(")){var value=Or();if(!Take(")"))throw new InvalidDataException("Missing ) in rule: "+_text);return value;}
        Space();var start=_at;
        if(_at<_text.Length&&(char.IsDigit(_text[_at])||_text[_at]=='.'))
        {
            while(_at<_text.Length&&(char.IsDigit(_text[_at])||_text[_at]=='.'))_at++;
            return decimal.Parse(_text[start.._at],CultureInfo.InvariantCulture);
        }
        while(_at<_text.Length&&(char.IsLetterOrDigit(_text[_at])||_text[_at]=='_'))_at++;
        if(start==_at)throw new InvalidDataException("Expected value in rule: "+_text);
        var name=_text[start.._at];
        if(!Take("("))return _resolve(name);
        var first=Or();decimal? second=Take(",")?Or():null;
        if(!Take(")"))throw new InvalidDataException("Missing function ) in rule: "+_text);
        return name switch{"floor"=>Math.Floor(first),"ceil"=>Math.Ceiling(first),"abs"=>Math.Abs(first),"min"=>Math.Min(first,second??throw new InvalidDataException(name)),"max"=>Math.Max(first,second??throw new InvalidDataException(name)),_=>throw new InvalidDataException("Unknown rule function: "+name)};
    }
}
