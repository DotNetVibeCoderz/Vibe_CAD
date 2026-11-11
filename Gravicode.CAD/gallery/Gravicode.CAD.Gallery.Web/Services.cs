using System.Collections.Concurrent;

namespace Gravicode.CAD.Gallery.Web;

/// <summary>Builds each sample once per process (some samples run booleans or FEM solves).</summary>
public sealed class ModelCache
{
    private readonly ConcurrentDictionary<string, Lazy<CadModel>> _models = new();
    public CadModel Get(Sample s) => _models.GetOrAdd(s.Id, _ => new Lazy<CadModel>(s.Build)).Value;
    public Task<CadModel> GetAsync(Sample s) => Task.Run(() => Get(s));
}

/// <summary>EN / ID toggle for sample descriptions and interface copy.</summary>
public sealed class LanguageState
{
    public bool Indonesian { get; private set; }
    public event Action? Changed;
    public void Set(bool indonesian) { Indonesian = indonesian; Changed?.Invoke(); }
    public string T(string en, string id) => Indonesian ? id : en;
}
