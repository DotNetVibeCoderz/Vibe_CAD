using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gravicode.CAD.Parametric;

/// <summary>
/// A numeric expression such as <c>"width/2 + 5"</c> or a literal number. Supports + - * / % ^,
/// parentheses, unary minus, constants (pi, e) and functions: sin cos tan asin acos atan atan2 (radians),
/// sind cosd tand (degrees), sqrt abs min max floor ceil round pow rad deg clamp.
/// </summary>
[JsonConverter(typeof(ExprJsonConverter))]
public readonly record struct Expr(string Text)
{
    public static implicit operator Expr(double v) => new(v.ToString("R", CultureInfo.InvariantCulture));
    public static implicit operator Expr(int v) => new(v.ToString(CultureInfo.InvariantCulture));
    public static implicit operator Expr(string s) => new(s);
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
    public double Eval(IReadOnlyDictionary<string, double> vars) => ExpressionEvaluator.Evaluate(Text, vars);
    public override string ToString() => Text;
}

public sealed class ExprJsonConverter : JsonConverter<Expr>
{
    public override Expr Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.Number => new Expr(reader.GetDouble().ToString("R", CultureInfo.InvariantCulture)),
        JsonTokenType.String => new Expr(reader.GetString() ?? "0"),
        JsonTokenType.Null => new Expr("0"),
        _ => throw new JsonException("Expression must be a number or a string.")
    };

    public override void Write(Utf8JsonWriter writer, Expr value, JsonSerializerOptions options)
    {
        if (double.TryParse(value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) writer.WriteNumberValue(d);
        else writer.WriteStringValue(value.Text);
    }
}

public sealed class ExpressionException(string message) : Exception(message);

/// <summary>Recursive-descent evaluator (no allocations beyond the token cursor).</summary>
public static class ExpressionEvaluator
{
    public static double Evaluate(string text, IReadOnlyDictionary<string, double>? vars = null)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var lit)) return lit;
        var p = new Parser(text, vars ?? new Dictionary<string, double>());
        var v = p.ParseExpression();
        p.SkipWs();
        if (!p.End) throw new ExpressionException($"Unexpected '{text[p.Pos]}' at {p.Pos} in '{text}'.");
        return v;
    }

    /// <summary>Identifiers referenced by an expression (for dependency ordering).</summary>
    public static IEnumerable<string> Identifiers(string text)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsLetter(text[i]) || text[i] == '_')
            {
                int s = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.')) i++;
                int j = i; while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
                if (j >= text.Length || text[j] != '(') yield return text[s..i];
            }
            else i++;
        }
    }

    private ref struct Parser(string s, IReadOnlyDictionary<string, double> vars)
    {
        private readonly ReadOnlySpan<char> _s = s;
        private readonly string _src = s;
        public int Pos;
        public readonly bool End => Pos >= _s.Length;

        public void SkipWs() { while (Pos < _s.Length && char.IsWhiteSpace(_s[Pos])) Pos++; }
        private bool Match(char c) { SkipWs(); if (Pos < _s.Length && _s[Pos] == c) { Pos++; return true; } return false; }

        public double ParseExpression()
        {
            double v = ParseTerm();
            while (true)
            {
                if (Match('+')) v += ParseTerm();
                else if (Match('-')) v -= ParseTerm();
                else return v;
            }
        }

        private double ParseTerm()
        {
            double v = ParseUnary();
            while (true)
            {
                if (Match('*')) v *= ParseUnary();
                else if (Match('/')) { var d = ParseUnary(); v /= d; }
                else if (Match('%')) v %= ParseUnary();
                else return v;
            }
        }

        private double ParseUnary()
        {
            if (Match('-')) return -ParseUnary();
            if (Match('+')) return ParseUnary();
            return ParsePower();
        }

        private double ParsePower()
        {
            double b = ParsePrimary();
            if (Match('^')) return Math.Pow(b, ParseUnary()); // right associative
            return b;
        }

        private double ParsePrimary()
        {
            SkipWs();
            if (End) throw new ExpressionException($"Unexpected end of expression '{_src}'.");
            char c = _s[Pos];
            if (c == '(') { Pos++; var v = ParseExpression(); if (!Match(')')) throw new ExpressionException($"Missing ')' in '{_src}'."); return v; }
            if (char.IsDigit(c) || c == '.')
            {
                int st = Pos;
                while (Pos < _s.Length && (char.IsDigit(_s[Pos]) || _s[Pos] == '.')) Pos++;
                if (Pos < _s.Length && (_s[Pos] == 'e' || _s[Pos] == 'E') && Pos + 1 < _s.Length && (char.IsDigit(_s[Pos + 1]) || _s[Pos + 1] is '-' or '+'))
                {
                    Pos += 2;
                    while (Pos < _s.Length && char.IsDigit(_s[Pos])) Pos++;
                }
                return double.Parse(_s[st..Pos], NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            if (char.IsLetter(c) || c == '_')
            {
                int st = Pos;
                while (Pos < _s.Length && (char.IsLetterOrDigit(_s[Pos]) || _s[Pos] == '_' || _s[Pos] == '.')) Pos++;
                var name = _s[st..Pos].ToString();
                if (Match('('))
                {
                    var args = new List<double>();
                    if (!Match(')'))
                    {
                        do args.Add(ParseExpression()); while (Match(','));
                        if (!Match(')')) throw new ExpressionException($"Missing ')' after arguments of {name}.");
                    }
                    return Call(name, args);
                }
                if (vars.TryGetValue(name, out var val)) return val;
                return name.ToLowerInvariant() switch
                {
                    "pi" => Math.PI,
                    "e" => Math.E,
                    "tau" => 2 * Math.PI,
                    _ => throw new ExpressionException($"Unknown parameter '{name}' in '{_src}'.")
                };
            }
            throw new ExpressionException($"Unexpected '{c}' in '{_src}'.");
        }

        private static double Call(string name, List<double> a)
        {
            double A(int i) => i < a.Count ? a[i] : throw new ExpressionException($"{name}() expects more arguments.");
            const double d2r = Math.PI / 180;
            return name.ToLowerInvariant() switch
            {
                "sin" => Math.Sin(A(0)), "cos" => Math.Cos(A(0)), "tan" => Math.Tan(A(0)),
                "sind" => Math.Sin(A(0) * d2r), "cosd" => Math.Cos(A(0) * d2r), "tand" => Math.Tan(A(0) * d2r),
                "asin" => Math.Asin(A(0)), "acos" => Math.Acos(A(0)), "atan" => Math.Atan(A(0)), "atan2" => Math.Atan2(A(0), A(1)),
                "sqrt" => Math.Sqrt(A(0)), "abs" => Math.Abs(A(0)), "floor" => Math.Floor(A(0)), "ceil" => Math.Ceiling(A(0)),
                "round" => Math.Round(A(0), a.Count > 1 ? (int)a[1] : 0), "pow" => Math.Pow(A(0), A(1)),
                "min" => a.Count == 0 ? throw new ExpressionException("min() needs arguments") : a.Min(),
                "max" => a.Count == 0 ? throw new ExpressionException("max() needs arguments") : a.Max(),
                "rad" => A(0) * d2r, "deg" => A(0) / d2r, "clamp" => Math.Clamp(A(0), A(1), A(2)),
                "log" => Math.Log(A(0)), "exp" => Math.Exp(A(0)),
                _ => throw new ExpressionException($"Unknown function '{name}'.")
            };
        }
    }
}

/// <summary>Named parameters whose values may be expressions of other parameters.</summary>
public sealed class ParameterSet
{
    private readonly Dictionary<string, Expr> _defs = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, Expr> Definitions => _defs;

    public ParameterSet Set(string name, Expr value) { _defs[name] = value; return this; }
    public bool Remove(string name) => _defs.Remove(name);

    /// <summary>Resolves all parameters in dependency order; detects cycles.</summary>
    public Dictionary<string, double> Resolve()
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        var visiting = new HashSet<string>();
        foreach (var k in _defs.Keys) Visit(k);
        return result;

        double Visit(string name)
        {
            if (result.TryGetValue(name, out var v)) return v;
            if (!_defs.TryGetValue(name, out var e)) throw new ExpressionException($"Unknown parameter '{name}'.");
            if (!visiting.Add(name)) throw new ExpressionException($"Circular parameter reference involving '{name}'.");
            foreach (var dep in ExpressionEvaluator.Identifiers(e.Text))
                if (_defs.ContainsKey(dep)) Visit(dep);
            v = e.Eval(result);
            visiting.Remove(name);
            result[name] = v;
            return v;
        }
    }
}
