using Gravicode.CAD;
using Gravicode.CAD.Gallery;
using Gravicode.CAD.Gallery.Web;
using Gravicode.CAD.Gallery.Web.Components;
using Gravicode.CAD.IO;
using Gravicode.CAD.Native;

CadIO.RegisterAll();
NativeEngine.Enable();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 64 * 1024 * 1024);
builder.Services.AddSingleton<ModelCache>();
builder.Services.AddScoped<LanguageState>();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/error", createScopeForErrors: true);

app.UseAntiforgery();
app.MapStaticAssets();

// file downloads for every sample in every export format
app.MapGet("/download/{id}.{ext}", (string id, string ext, ModelCache cache) =>
{
    var sample = SampleCatalog.Find(id);
    if (sample is null || CadFormats.FindExporter(ext) is null) return Results.NotFound();
    var bytes = cache.Get(sample).SaveToBytes(ext, new ExportOptions { Title = sample.Title });
    var mime = ext switch { "pdf" => "application/pdf", "svg" => "image/svg+xml", "glb" => "model/gltf-binary", "stl" => "model/stl", _ => "application/octet-stream" };
    return Results.File(bytes, mime, $"{sample.Id}.{ext}");
});

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
