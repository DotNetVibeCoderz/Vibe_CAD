// Made with Gravicode.CAD — Dibuat oleh Gravicode Studios dipimpin oleh Kang Fadhil
using Gravicode.CAD.Parametric;
using Gravicode.CAD.Viewer.WinForms;

ApplicationConfiguration.Initialize();
var form = new Form { Text = "CadApp", Width = 1100, Height = 720 };
var viewport = new CadViewport { Dock = DockStyle.Fill };
var open = new Button { Text = "Open…", Dock = DockStyle.Top, Height = 32 };
open.Click += (_, _) =>
{
    using var dlg = new OpenFileDialog { Filter = "CAD files|*.step;*.stp;*.stl;*.obj;*.glb;*.3mf;*.dxf;*.igs" };
    if (dlg.ShowDialog(form) == DialogResult.OK) viewport.Load(dlg.FileName);   // e.g. viewport.Load("engine.step")
};
form.Controls.Add(viewport);
form.Controls.Add(open);
viewport.Model = Part.Create("Demo").Tube(30, 22, 40).Build();
Application.Run(form);
