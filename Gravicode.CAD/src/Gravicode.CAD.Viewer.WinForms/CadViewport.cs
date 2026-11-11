using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Gravicode.CAD.IO;
using Gravicode.CAD.Rendering;

namespace Gravicode.CAD.Viewer.WinForms;

/// <summary>
/// WinForms CAD viewport: <c>viewport.Load("engine.step")</c> or set <see cref="Model"/>.
/// Left-drag orbits, right/middle-drag pans, wheel zooms, double-click fits.
/// </summary>
public class CadViewport : Control
{
    private readonly InteractiveView _view = new();
    private Bitmap? _frame;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CadModel? Model { get => _view.Model; set => _view.Model = value; }

    [Browsable(false)]
    public InteractiveView View => _view;

    public CadViewport()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(0x0D, 0x11, 0x17);
        _view.FrameReady += img =>
        {
            var bmp = ToBitmap(img);
            if (IsHandleCreated) BeginInvoke(() => { var old = _frame; _frame = bmp; old?.Dispose(); Invalidate(); });
        };
    }

    /// <summary>Loads any supported CAD file asynchronously.</summary>
    public async void Load(string path)
    {
        CadIO.RegisterAll();
        try { Model = await Task.Run(() => CadModel.Load(path)); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Gravicode.CAD", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); _view.Resize(ClientSize.Width, ClientSize.Height); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Capture = true; _view.PointerDown(e.X, e.Y, e.Button != MouseButtons.Left); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); _view.PointerMove(e.X, e.Y); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; _view.PointerUp(); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); _view.Wheel(e.Delta); }
    protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); _view.Fit(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_frame != null) e.Graphics.DrawImage(_frame, ClientRectangle);
        else TextRenderer.DrawText(e.Graphics, "Gravicode.CAD — load a model", Font, ClientRectangle, Color.FromArgb(0x8B, 0x97, 0xA8), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static Bitmap ToBitmap(RgbaImage img)
    {
        var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, img.Width, img.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var bgra = new byte[img.Pixels.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = img.Pixels[i + 2]; bgra[i + 1] = img.Pixels[i + 1]; bgra[i + 2] = img.Pixels[i]; bgra[i + 3] = img.Pixels[i + 3];
        }
        for (int y = 0; y < img.Height; y++) Marshal.Copy(bgra, y * img.Width * 4, data.Scan0 + y * data.Stride, img.Width * 4);
        bmp.UnlockBits(data);
        return bmp;
    }

    protected override void Dispose(bool disposing) { if (disposing) _frame?.Dispose(); base.Dispose(disposing); }
}
