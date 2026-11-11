using Gravicode.CAD.IO;
using Gravicode.CAD.Native;
using Gravicode.CAD.Studio.Web;
using Gravicode.CAD.Studio.Web.Components;

// Gravicode CAD Studio (web) — Made by Gravicode Studios, led by Kang Fadhil.
CadIO.RegisterAll();
NativeEngine.Enable();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 128 * 1024 * 1024);   // project / CAD file uploads
builder.Services.AddScoped<StudioSession>();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/error", createScopeForErrors: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// open the browser when started from an installer / double-click (not in development)
if (!app.Environment.IsDevelopment() && args.Contains("--open"))
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var url = app.Urls.FirstOrDefault() ?? "http://localhost:5000";
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    });

app.Run();
