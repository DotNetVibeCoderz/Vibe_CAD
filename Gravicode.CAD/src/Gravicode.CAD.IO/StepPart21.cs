using System.Text;

namespace Gravicode.CAD.IO;

/// <summary>ISO 10303-21 ("Part 21") exchange file tokenizer/parser shared by the STEP reader.</summary>
public sealed class Part21File
{
    public abstract record Value;
    public sealed record Ref(int Id) : Value;
    public sealed record Str(string Text) : Value;
    public sealed record Num(double V) : Value;
    public sealed record Enum(string Name) : Value;
    public sealed record List(IReadOnlyList<Value> Items) : Value;
    /// <summary>Typed parameter such as LENGTH_MEASURE(1.E-07).</summary>
    public sealed record Typed(string Type, IReadOnlyList<Value> Args) : Value;
    public sealed record Unset : Value { public static readonly Unset I = new(); }

    public sealed record Entity(int Id, string Type, IReadOnlyList<Value> Args, IReadOnlyList<(string Type, IReadOnlyList<Value> Args)>? Parts = null)
    {
        /// <summary>For complex instances, finds the sub-record of a given type.</summary>
        public IReadOnlyList<Value>? Part(string type) =>
            Type == type ? Args : Parts?.FirstOrDefault(p => p.Type == type).Args;
        public bool Is(string type) => Type == type || (Parts?.Any(p => p.Type == type) ?? false);
    }

    public Dictionary<int, Entity> Entities { get; } = [];
    public List<string> Schemas { get; } = [];
    public string? FileName { get; private set; }

    public Entity? this[int id] => Entities.GetValueOrDefault(id);
    public Entity? Get(Value? v) => v is Ref r ? this[r.Id] : null;
    public IEnumerable<Entity> OfType(string type) => Entities.Values.Where(e => e.Is(type));

    public static Part21File Parse(string text)
    {
        var f = new Part21File();
        int i = 0;
        var s = text;
        int len = s.Length;
        int dataStart = s.IndexOf("DATA;", StringComparison.Ordinal);
        if (dataStart < 0) throw new InvalidDataException("Not a STEP Part 21 file (no DATA section).");
        var header = s[..dataStart];
        int fsIdx = header.IndexOf("FILE_SCHEMA", StringComparison.Ordinal);
        if (fsIdx >= 0)
        {
            int q1 = header.IndexOf('\'', fsIdx);
            while (q1 >= 0 && q1 < header.Length)
            {
                int q2 = header.IndexOf('\'', q1 + 1);
                if (q2 < 0) break;
                f.Schemas.Add(header[(q1 + 1)..q2]);
                int nx = header.IndexOf('\'', q2 + 1);
                int semi = header.IndexOf(';', q2);
                if (nx < 0 || (semi >= 0 && semi < nx)) break;
                q1 = nx;
            }
        }
        int fnIdx = header.IndexOf("FILE_NAME", StringComparison.Ordinal);
        if (fnIdx >= 0) { int q1 = header.IndexOf('\'', fnIdx); int q2 = q1 >= 0 ? header.IndexOf('\'', q1 + 1) : -1; if (q2 > q1) f.FileName = header[(q1 + 1)..q2]; }

        i = dataStart + 5;
        while (i < len)
        {
            SkipWsAndComments();
            if (i >= len) break;
            if (s[i] != '#') { if (s.AsSpan(i).StartsWith("ENDSEC")) break; i++; continue; }
            i++;
            int id = ReadInt();
            SkipWsAndComments();
            if (i >= len || s[i] != '=') continue;
            i++;
            SkipWsAndComments();
            if (s[i] == '(')
            {
                // complex instance: (TYPE1(args) TYPE2(args) ...)
                i++;
                var parts = new List<(string, IReadOnlyList<Value>)>();
                while (true)
                {
                    SkipWsAndComments();
                    if (s[i] == ')') { i++; break; }
                    var type = ReadKeyword();
                    SkipWsAndComments();
                    var args = ((List)ReadList()).Items;
                    parts.Add((type, args));
                }
                f.Entities[id] = new Entity(id, parts.Count > 0 ? parts[0].Item1 : "", parts.Count > 0 ? parts[0].Item2 : [], parts);
            }
            else
            {
                var type = ReadKeyword();
                SkipWsAndComments();
                var args = ((List)ReadList()).Items;
                f.Entities[id] = new Entity(id, type, args);
            }
            SkipWsAndComments();
            if (i < len && s[i] == ';') i++;
        }
        return f;

        void SkipWsAndComments()
        {
            while (i < len)
            {
                if (char.IsWhiteSpace(s[i])) { i++; continue; }
                if (s[i] == '/' && i + 1 < len && s[i + 1] == '*') { int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? len : e + 2; continue; }
                break;
            }
        }
        int ReadInt() { int st = i; while (i < len && char.IsDigit(s[i])) i++; return int.Parse(s.AsSpan(st, i - st)); }
        string ReadKeyword() { int st = i; while (i < len && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '!')) i++; return s[st..i].ToUpperInvariant(); }

        Value ReadList()
        {
            // expects '('
            if (s[i] != '(') throw new InvalidDataException($"Expected '(' at {i}.");
            i++;
            var items = new List<Value>();
            while (true)
            {
                SkipWsAndComments();
                if (i >= len) break;
                if (s[i] == ')') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                items.Add(ReadValue());
            }
            return new List(items);
        }

        Value ReadValue()
        {
            SkipWsAndComments();
            char c = s[i];
            switch (c)
            {
                case '(': return ReadList();
                case '#': i++; return new Ref(ReadInt());
                case '$': i++; return Unset.I;
                case '*': i++; return Unset.I;
                case '\'':
                    {
                        var sb = new StringBuilder();
                        i++;
                        while (i < len)
                        {
                            if (s[i] == '\'') { if (i + 1 < len && s[i + 1] == '\'') { sb.Append('\''); i += 2; continue; } i++; break; }
                            sb.Append(s[i++]);
                        }
                        return new Str(DecodeString(sb.ToString()));
                    }
                case '.':
                    {
                        int st = ++i; while (i < len && s[i] != '.') i++;
                        var name = s[st..i]; i++;
                        return new Enum(name);
                    }
                case '"': { int st = ++i; while (i < len && s[i] != '"') i++; i++; return new Str(s[st..(i - 1)]); }
                default:
                    if (char.IsLetter(c))
                    {
                        var t = ReadKeyword(); SkipWsAndComments();
                        return new Typed(t, ((List)ReadList()).Items);
                    }
                    {
                        int st = i;
                        while (i < len && (char.IsDigit(s[i]) || s[i] is '-' or '+' or '.' or 'E' or 'e')) i++;
                        var num = s[st..i];
                        if (num.EndsWith('.')) num += "0";
                        return new Num(double.Parse(num.Replace(".E", ".0E").Replace(".e", ".0e"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture));
                    }
            }
        }
    }

    /// <summary>Decodes \X2\....\X0\ (UTF-16 hex) and \X\hh sequences.</summary>
    private static string DecodeString(string s)
    {
        if (!s.Contains('\\')) return s;
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 3 < s.Length && s[i + 1] == 'X' && s[i + 2] == '2' && s[i + 3] == '\\')
            {
                int end = s.IndexOf("\\X0\\", i + 4, StringComparison.Ordinal);
                if (end < 0) break;
                var hex = s[(i + 4)..end];
                for (int k = 0; k + 3 < hex.Length; k += 4) sb.Append((char)Convert.ToInt32(hex.Substring(k, 4), 16));
                i = end + 3;
            }
            else if (s[i] == '\\' && i + 4 < s.Length && s[i + 1] == 'X' && s[i + 2] == '\\')
            {
                sb.Append((char)Convert.ToInt32(s.Substring(i + 3, 2), 16)); i += 4;
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
