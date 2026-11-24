using kisatsingen.Services.GeneratedFiles;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace kisatsingen.Components.GeneratedFiles;

public partial class FileDownloadButton : ComponentBase
{
    private const string BusyMessage = "Mange lager filer akkurat nå. Prøv igjen om litt.";
    private const string BuildFailedMessage = "Filen kunne ikke lages på grunn av en feil i systemet, ikke i innholdet ditt. Prøv igjen om litt.";
    private const string FailedMessage = "Filen kunne ikke lagres. Prøv igjen, eller last inn siden på nytt hvis det ikke går.";

    [Inject]
    public required IJSRuntime JS { get; set; }

    // Scoped to the circuit, not this component: opening another chat disposes
    // this component, but the user is still here and the file should arrive.
    [Inject]
    public required GeneratedFileBuilder GeneratedFileBuilder { get; set; }

    [Inject]
    public required ILogger<FileDownloadButton> Logger { get; set; }

    [Parameter, EditorRequired]
    public required GeneratedFile Download { get; set; }

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

        // Held for the whole download: the parameter can change while the build
        // is awaited, and the name and type must stay those of the bytes built.
        var download = Download;
        try
        {
            switch (await GeneratedFileBuilder.BuildAsync(download))
            {
                case FileBuildResult.Built built:
                    await SaveAsync(download.FileName, GeneratedFileBuilder.ContentTypeOf(download), built.Content);
                    break;
                case FileBuildResult.Busy:
                    Logger.LogWarning("File {FileName} not built: every build slot stayed taken for the whole wait", download.FileName);
                    _downloadProblem = BusyMessage;
                    break;
                case FileBuildResult.Failed failed:
                    Logger.LogError(failed.Error, "File {FileName} could not be built", download.FileName);
                    _downloadProblem = BuildFailedMessage;
                    break;
            }
        }
        catch (OperationCanceledException) when (GeneratedFileBuilder.IsCancelled)
        {
            // The circuit is gone; there is no one to give the file to.
            Logger.LogInformation("File {FileName} not built since the operation was cancelled", download.FileName);
        }
        finally
        {
            _isDownloading = false;
        }
    }

    private async Task SaveAsync(string fileName, string contentType, ArraySegment<byte> content)
    {
        // Blazor disposes the stream once it has sent it. It is disposed here
        // only where the transfer has ended or never began; see the catches.
        var stream = new DotNetStreamReference(new MemoryStream(content.Array!, content.Offset, content.Count, writable: false), leaveOpen: false);
        try
        {
            await JS.InvokeVoidAsync("chatClient.downloadFile", fileName, contentType, stream);
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
            Logger.LogWarning(ex, "The browser could not save the file {FileName}", fileName);
            _downloadProblem = FailedMessage;
        }
        catch (OperationCanceledException ex)
        {
            // The interop call timed out, typically while streaming the file
            // over a slow connection. Not disposed: the browser may still be
            // reading it, and disposing it would cut the transfer off mid-file.
            // One the browser never claimed is disposed by Blazor itself, once
            // the default interop call timeout has passed.
            Logger.LogWarning(ex, "Saving the file {FileName} timed out before the browser had it", fileName);
            _downloadProblem = FailedMessage;
        }
    }
}
