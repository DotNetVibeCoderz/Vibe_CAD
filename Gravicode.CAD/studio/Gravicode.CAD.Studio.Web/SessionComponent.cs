using Microsoft.AspNetCore.Components;

namespace Gravicode.CAD.Studio.Web;

/// <summary>Base for editor panels: re-renders whenever the session, document or command engine changes.</summary>
public abstract class SessionComponent : ComponentBase, IDisposable
{
    [Inject] protected StudioSession S { get; set; } = default!;

    protected override void OnInitialized() => S.Changed += Refresh;
    private void Refresh() => InvokeAsync(StateHasChanged);
    public virtual void Dispose() { S.Changed -= Refresh; GC.SuppressFinalize(this); }
}
