using kisatsingen.Data.Entities;
using kisatsingen.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace kisatsingen.Components.KnowledgeFiles;

// Blazor Server keeps every component's rendered output in memory, so text
// rendered through Razor would stay on the server for as long as the viewer is
// open. The browser gets it once instead, and the viewer keeps no copy.
public sealed partial class KnowledgeFileViewer : ComponentBase, IDisposable
{
    // About 250 000 tokens: nearly every file in full, while the configurable
    // token cap can allow files far larger than one message should carry.
    private const int MaxShownLength = 1_000_000;

    [Inject]
    public required IJSRuntime JS { get; init; }

    [Inject]
    public required ILogger<KnowledgeFileViewer> Logger { get; init; }

    [Parameter, EditorRequired]
    public required Guid FileId { get; set; }

    // Returns null when the file can't be reached.
    [Parameter, EditorRequired]
    public required Func<Guid, CancellationToken, Task<KnowledgeFileText?>> ReadFileText { get; set; }

    private readonly CancellationTokenSource _loadCancellation = new();
    private ElementReference _textElement;
    private bool _isLoaded;
    private bool _isTruncated;

    // Null once loaded means the file was not found.
    private KnowledgeFileMetadata? _file;

    // Held only until the render that shows the <pre> hands it to the browser.
    private string? _textForBrowser;

    protected override async Task OnInitializedAsync()
    {
        var text = await ReadFileText(FileId, _loadCancellation.Token);
        _isLoaded = true;

        if (text is null)
        {
            return;
        }

        _file = text.File;
        _textForBrowser = TextTruncation.Prefix(text.Markdown, MaxShownLength);
        _isTruncated = _textForBrowser.Length < text.Markdown.Length;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_textForBrowser is not { } text)
        {
            return;
        }

        _textForBrowser = null;

        try
        {
            await JS.InvokeVoidAsync("chatClient.showPlainText", _textElement, text);
        }
        catch (JSException ex)
        {
            Logger.LogWarning(ex, "JS interop failed for {Method}", "chatClient.showPlainText");
        }
    }

    public void Dispose()
    {
        _loadCancellation.Cancel();
        _loadCancellation.Dispose();
    }
}
