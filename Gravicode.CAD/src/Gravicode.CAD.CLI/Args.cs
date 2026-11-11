using System.Globalization;

namespace Gravicode.CAD.CLI;

/// <summary>Tiny argument parser: first token = command, "--name value" / "-o value" options, "--flag" switches.</summary>
internal sealed class Args
{
    private readonly Dictionary<string, List<string>> _opts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);
    public string Command { get; } = "help";
    public List<string> Positional { get; } = [];

    private static readonly HashSet<string> Switches = new(StringComparer.OrdinalIgnoreCase) { "ascii", "light", "transparent", "no-edges", "verbose", "version" };

    public Args(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var t = args[i];
            if (i == 0 && !t.StartsWith('-')) { Command = t.ToLowerInvariant(); continue; }
            if (i == 0 && (t is "--version" or "-v")) { Command = t; continue; }
            if (t.StartsWith('-') && t.Length > 1 && !double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                var name = t.TrimStart('-');
                if (Switches.Contains(name) || i + 1 >= args.Length) { _flags.Add(name); continue; }
                if (!_opts.TryGetValue(name, out var l)) _opts[name] = l = [];
                l.Add(args[++i]);
                continue;
            }
            Positional.Add(t);
        }
    }

    public string Pos(int i, string what) => i < Positional.Count ? Positional[i] : throw new ArgumentException($"missing {what}. Run 'gcad help'.");
    public string? Opt(string name) => _opts.TryGetValue(name, out var l) ? l[^1] : null;
    public IEnumerable<string> All(string name) => _opts.TryGetValue(name, out var l) ? l : [];
    public bool Flag(string name) => _flags.Contains(name);
    public int Int(string name, int d) => Opt(name) is { } s ? int.Parse(s, CultureInfo.InvariantCulture) : d;
    public double Dbl(string name, double d) => Opt(name) is { } s ? double.Parse(s, CultureInfo.InvariantCulture) : d;
}

internal static class Ui
{
    private static readonly bool Color = !Console.IsOutputRedirected && Environment.GetEnvironmentVariable("NO_COLOR") is null;
    private static string C(string code, string s) => Color ? $"\u001b[{code}m{s}\u001b[0m" : s;

    public static void Banner()
    {
        Console.WriteLine(C("38;5;208;1", "  ◆ gcad") + C("2", $"  {GravicodeCad.ProductName} {GravicodeCad.Version}"));
        Console.WriteLine(C("2", $"  {GravicodeCad.Credits}"));
        Console.WriteLine();
    }
    public static void Title(string s) { Console.WriteLine(); Console.WriteLine(C("1", "  " + s)); }
    public static void Ok(string s) => Console.WriteLine(C("32", "  ✔ ") + s);
    public static void Warn(string s) => Console.WriteLine(C("33", "  ! ") + s);
    public static void Error(string s) => Console.Error.WriteLine(C("31", "  ✖ ") + s);
    public static void Dim(string s) => Console.WriteLine(C("2", s));
}
