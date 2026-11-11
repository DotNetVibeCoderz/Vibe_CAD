// Made with Gravicode.CAD — Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
using Gravicode.CAD.Parametric;
using Gravicode.CAD.Rendering;

namespace CadApp;

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(new MainPage());
}

public class MainPage : ContentPage
{
    private readonly Image _image = new() { Aspect = Aspect.AspectFit };
    private readonly Slider _teeth = new(8, 60, 24);
    private readonly Label _label = new() { HorizontalOptions = LayoutOptions.Center, TextColor = Colors.White };

    public MainPage()
    {
        BackgroundColor = Color.FromArgb("#0D1117");
        _teeth.ValueChanged += async (_, _) => await RenderAsync();
        var grid = new Grid { RowDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)] };
        grid.Add(_image, 0, 0); grid.Add(_label, 0, 1); grid.Add(_teeth, 0, 2);
        Content = grid;
        _ = RenderAsync();
    }

    private async Task RenderAsync()
    {
        int teeth = (int)_teeth.Value;
        _label.Text = $"Spur gear · {teeth} teeth";
        var png = await Task.Run(() =>
        {
            var model = Part.Create("Gear").Gear(2, teeth, 10, 8).Build();
            return SoftwareRenderer.Render(model, new RenderOptions { Width = 900, Height = 700 }).EncodePng();
        });
        _image.Source = ImageSource.FromStream(() => new MemoryStream(png));
    }
}
