using Microsoft.JSInterop;

namespace ControlR.Web.Client.Components.Pages;

/// <summary>
/// The read-only co-viewer for a shared remote-control (fan-out) session. An invited technician opens
/// /shared-view/{SessionId}?token={viewerToken}; this connects to the fan-out endpoint as a viewer and
/// decodes the producer's frames onto a canvas. It sends no input — the primary technician drives.
/// </summary>
public partial class SharedSessionViewer : ComponentBase, IAsyncDisposable
{
  private const string ModulePath = "./Components/Pages/SharedSessionViewer.razor.js";

  private readonly string _canvasId = $"shared-view-{Guid.NewGuid():N}";
  private readonly CancellationTokenSource _cts = new();

  private bool _connected;
  private string? _error;
  private IJSObjectReference? _module;
  private Task? _streamTask;

  [Parameter]
  public Guid SessionId { get; set; }

  [SupplyParameterFromQuery(Name = "token")]
  public string? Token { get; set; }

  [Inject]
  public required ISharedSessionViewerClient ViewerClient { get; init; }

  [Inject]
  public required NavigationManager Navigation { get; init; }

  [Inject]
  public required IJSRuntime Js { get; init; }

  [Inject]
  public required ILogger<SharedSessionViewer> Logger { get; init; }

  public async ValueTask DisposeAsync()
  {
    await _cts.CancelAsync();

    if (_streamTask is not null)
    {
      try
      {
        await _streamTask;
      }
      catch
      {
        // Shutting down.
      }
    }

    try
    {
      if (_module is not null)
      {
        await _module.InvokeVoidAsync("disposeViewer", _canvasId);
        await _module.DisposeAsync();
      }
    }
    catch
    {
      // The circuit may already be gone.
    }

    await ViewerClient.DisposeAsync();
    _cts.Dispose();
    GC.SuppressFinalize(this);
  }

  protected override async Task OnAfterRenderAsync(bool firstRender)
  {
    if (!firstRender)
    {
      return;
    }

    if (string.IsNullOrWhiteSpace(Token))
    {
      _error = "This co-view link is missing its access token.";
      StateHasChanged();
      return;
    }

    try
    {
      _module = await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);
      await _module.InvokeAsync<bool>("initializeViewer", _canvasId);
      _streamTask = StreamAsync();
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Failed to initialize the shared-session co-viewer.");
      _error = "Could not start the co-viewer.";
      StateHasChanged();
    }
  }

  private async Task StreamAsync()
  {
    var baseUri = new Uri(Navigation.BaseUri);
    try
    {
      await foreach (var packet in ViewerClient.StreamFramesAsync(baseUri, SessionId, Token!, _cts.Token))
      {
        if (_module is null)
        {
          return;
        }

        if (!_connected)
        {
          _connected = true;
          await InvokeAsync(StateHasChanged);
        }

        await _module.InvokeAsync<bool>(
          "drawVideoPacket",
          _canvasId,
          packet.PacketData,
          packet.Timestamp,
          packet.IsKeyFrame);
      }

      // The stream ended (producer left or session ended).
      if (!_cts.IsCancellationRequested)
      {
        _error = "The session ended.";
        _connected = false;
        await InvokeAsync(StateHasChanged);
      }
    }
    catch (OperationCanceledException)
    {
      // Leaving the page.
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error while streaming the shared session.");
      _error = "The co-view connection was lost.";
      await InvokeAsync(StateHasChanged);
    }
  }
}
