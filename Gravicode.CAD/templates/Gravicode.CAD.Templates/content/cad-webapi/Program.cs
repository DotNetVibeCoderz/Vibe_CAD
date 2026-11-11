// Made with Gravicode.CAD — Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
using Gravicode.CAD;
using Gravicode.CAD.AI;
using Gravicode.CAD.IO;
using Gravicode.CAD.Parametric;
using Gravicode.CAD.Rendering;

CadIO.RegisterAll();
var app = WebApplication.CreateBuilder(args).Build();

static string Mime(string ext) => ext switch
{
    "pdf" => "application/pdf", "png" => "image/png", "svg" => "image/svg+xml", "glb" => "model/gltf-binary",
    _ => "application/octet-stream"
};

app.MapGet("/", () => "Gravicode.CAD API: /formats, /parts/plate.step?w=150, POST /convert/stl, POST /ai/generate");

app.MapGet("/formats", () => new
{
    import = CadFormats.Importers.SelectMany(i => i.Extensions),
    export = CadFormats.Exporters.SelectMany(e => e.Extensions)
});

// GET /parts/plate.step?w=120&d=80&t=10&hole=8.5   (also .stl .glb .pdf .png ...)
app.MapGet("/parts/plate.{ext}", (string ext, double w = 120, double d = 80, double t = 10, double hole = 8.5) =>
{
    double x = w / 2 - 10, y = d / 2 - 10;
    var model = Part.Create("Plate").Box(w, d, t).Holes(hole, (-x, -y), (x, -y), (x, y), (-x, y)).Build();
    if (ext == "png") return Results.File(SoftwareRenderer.Render(model).EncodePng(), "image/png");
    return Results.File(model.SaveToBytes(ext), Mime(ext), $"plate.{ext}");
});

// POST /convert/stl   (multipart form field "file")
app.MapPost("/convert/{ext}", async (string ext, IFormFile file) =>
{
    await using var s = file.OpenReadStream();
    var model = CadModel.Load(s, Path.GetExtension(file.FileName));
    return Results.File(model.SaveToBytes(ext), Mime(ext), Path.ChangeExtension(file.FileName, ext));
}).DisableAntiforgery();

// POST /ai/generate   { "prompt": "Create a gearbox housing with four mounting holes" }
app.MapPost("/ai/generate", async (GenerateRequest req) =>
{
    var agent = new TextToCadAgent(ProviderFactory.FromEnvironment()); // ANTHROPIC_API_KEY → Claude, else offline
    var r = await agent.GenerateAsync(req.Prompt);
    return Results.Text(r.Definition.ToJson(), "application/json");
});

app.Run();

record GenerateRequest(string Prompt);
