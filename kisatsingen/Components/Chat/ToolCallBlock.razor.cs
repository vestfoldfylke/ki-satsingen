using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using kisatsingen.Services.Chat;
using kisatsingen.Services.Presentations;

namespace kisatsingen.Components.Chat;

public partial class ToolCallBlock : ComponentBase
{
    private const string BusyMessage = "Mange lager filer akkurat nå. Prøv igjen om litt.";
    private const string FailedMessage = "Filen kunne ikke lagres. Prøv igjen, eller last inn siden på nytt hvis det ikke går.";

    [Inject]
    public required IJSRuntime JS { get; set; }

    [Inject]
    public required PresentationBuildLimiter PresentationBuilds { get; set; }

    [Inject]
    public required ILogger<ToolCallBlock> Logger { get; set; }

    [Parameter, EditorRequired] public required ToolCall ToolCall { get; set; }

    private ToolCallView? _view;
    private bool _isShowingRawData;
    private bool _isDownloading;
    private string? _downloadProblem;

    // Built once per tool call, not per render: a result can be pages of text.
    private ToolCallView View
    {
        get
        {
            if (_view is null || !ReferenceEquals(_view.ToolCall, ToolCall))
            {
                _view = ToolCallView.Build(ToolCall);
            }

            return _view;
        }
    }

    private string ShownText => _isShowingRawData || View.UserFriendlyResult is null
        ? View.FormatToolCallJson()
        : View.UserFriendlyResult;

    // A refusal is neutral: the model usually corrects its call in the next step.
    private string OutcomeColor => View.Outcome is ToolOutcome.Failed ? "danger" : "neutral";

    // Built on each click rather than kept: the tool call's arguments are all
    // that is stored. The button is aria-disabled rather than disabled while
    // busy, so focus stays on it; this guard is what stops a second click.
    private async Task DownloadAsync(PresentationDownload download)
    {
        if (_isDownloading)
        {
            return;
        }

        _isDownloading = true;
        _downloadProblem = null;
        try
        {
            switch (await PresentationBuilds.BuildAsync(download.Spec))
            {
                case PresentationBuildResult.Built built:
                    await SaveAsync(download.FileName, built.Content);
                    break;
                case PresentationBuildResult.Busy:
                    Logger.LogWarning("Presentation {FileName} not built: every build slot stayed taken for the whole wait", download.FileName);
                    _downloadProblem = BusyMessage;
                    break;
            }
        }
        finally
        {
            _isDownloading = false;
        }
    }

    private async Task SaveAsync(string fileName, byte[] content)
    {
        try
        {
            using var stream = new DotNetStreamReference(new MemoryStream(content));
            await JS.InvokeVoidAsync("chatClient.downloadFile", fileName, VestfoldPresentationBuilder.ContentType, stream);
        }
        catch (JSDisconnectedException)
        {
            // The page is gone; there is no one to give the file to.
        }
        catch (JSException ex)
        {
            Logger.LogWarning(ex, "The browser could not save the presentation {FileName}", fileName);
            _downloadProblem = FailedMessage;
        }
    }
}
