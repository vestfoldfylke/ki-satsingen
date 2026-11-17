using kisatsingen.Components.Chat;
using kisatsingen.Services.Presentations;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace kisatsingen.Components.Presentation;

public partial class PresentationDownloadButton : ComponentBase
{
    private const string BusyMessage = "Mange lager filer akkurat nå. Prøv igjen om litt.";
    private const string BuildFailedMessage = "Presentasjonen kunne ikke lages på grunn av en feil i systemet, ikke i innholdet ditt. Si fra til brukerstøtte.";
    private const string FailedMessage = "Filen kunne ikke lagres. Prøv igjen, eller last inn siden på nytt hvis det ikke går.";

    [Inject]
    public required IJSRuntime JS { get; set; }

    // Scoped to the circuit, not this component: opening another chat disposes
    // this component, but the user is still here and the file should arrive.
    [Inject]
    public required PresentationDownloads PresentationDownloads { get; set; }

    [Inject]
    public required ILogger<PresentationDownloadButton> Logger { get; set; }

    [Parameter, EditorRequired]
    public required PresentationDownload Download { get; set; }

    private bool _isDownloading;
    private string? _downloadProblem;

    // Built on each click rather than kept: the tool call's arguments are all
    // that is stored. The button is aria-disabled rather than disabled while
    // busy, so focus stays on it; this guard is what stops a second click.
    private async Task DownloadAsync()
    {
        if (_isDownloading)
        {
            return;
        }

        _isDownloading = true;
        _downloadProblem = null;
        try
        {
            switch (await PresentationDownloads.BuildAsync(Download.Spec))
            {
                case PresentationBuildResult.Built built:
                    await SaveAsync(Download.FileName, built.Content);
                    break;
                case PresentationBuildResult.Busy:
                    Logger.LogWarning("Presentation {FileName} not built: every build slot stayed taken for the whole wait", Download.FileName);
                    _downloadProblem = BusyMessage;
                    break;
                case PresentationBuildResult.Failed failed:
                    Logger.LogError(failed.Error, "Presentation {FileName} could not be built; check that the template still matches SlideLayouts and PresentationTheme", Download.FileName);
                    _downloadProblem = BuildFailedMessage;
                    break;
            }
        }
        catch (OperationCanceledException) when (PresentationDownloads.IsCancelled)
        {
            // The circuit is gone; there is no one to give the file to.
            Logger.LogInformation("Presentation {FileName} not built since the operation was cancelled", Download.FileName);
        }
        finally
        {
            _isDownloading = false;
        }
    }

    private async Task SaveAsync(string fileName, ArraySegment<byte> content)
    {
        // Blazor disposes the stream once it has sent it. It is disposed here
        // only where the transfer has ended or never began; see the catches.
        var stream = new DotNetStreamReference(new MemoryStream(content.Array!, content.Offset, content.Count, writable: false), leaveOpen: false);
        try
        {
            await JS.InvokeVoidAsync("chatClient.downloadFile", fileName, VestfoldPresentationBuilder.ContentType, stream);
        }
        catch (JSDisconnectedException)
        {
            // The page is gone; there is no one to give the file to.
            stream.Dispose();
        }
        catch (JSException ex)
        {
            // The browser's call has returned, so nothing is reading the stream.
            stream.Dispose();
            Logger.LogWarning(ex, "The browser could not save the presentation {FileName}", fileName);
            _downloadProblem = FailedMessage;
        }
        catch (OperationCanceledException ex)
        {
            // The interop call timed out, typically while streaming the file
            // over a slow connection. Not disposed: the browser may still be
            // reading it, and disposing it would cut the transfer off mid-file.
            // One the browser never claimed is disposed by Blazor itself, once
            // the default interop call timeout has passed.
            Logger.LogWarning(ex, "Saving the presentation {FileName} timed out before the browser had it", fileName);
            _downloadProblem = FailedMessage;
        }
    }
}
