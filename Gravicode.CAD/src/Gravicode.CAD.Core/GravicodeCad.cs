namespace Gravicode.CAD;

/// <summary>Product information shown by the CLI, the gallery apps and the documentation.</summary>
public static class GravicodeCad
{
    public const string ProductName = "Gravicode.CAD";
    public const string Tagline = "Enterprise-grade CAD, Geometry, Parametric Modeling, Rendering, Simulation, Interoperability, and AI-CAD Framework for .NET";
    public const string Credits = "Made by Gravicode Studios, led by Kang Fadhil";
    public const string CreditsId = "Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil";
    public const string Website = "https://github.com/DotNetVibeCoderz/Vibe_CAD";

    public static string Version =>
        typeof(GravicodeCad).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}
