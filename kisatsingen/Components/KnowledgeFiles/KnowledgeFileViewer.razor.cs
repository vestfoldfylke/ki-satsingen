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

    // Formatting blocks the browser's main thread: about 1 second at this length
    // with Chrome's CPU slowed 6x (a slow laptop), and it grows linearly.
    // Longer text shows plain only.
    private const int MaxFormattedLength = 200_000;

    private enum TextView
    {
        Reading,
        PlainText
    }

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
    private ElementReference _readingElement;
    private ElementReference _plainTextElement;
    private bool _isLoaded;
    private bool _isTruncated;
    private bool _canFormat;
    private TextView _view = TextView.Reading;

    // Null once loaded means the file was not found.
    private KnowledgeFileMetadata? _file;

    // Held only until the render that shows the text's elements hands it to the browser.
    private string? _textForBrowser;

    // Unique per file, so the toggle's radios never join another viewer's group.
    private string ViewToggleName => $"knowledge-file-view-{FileId:N}";

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
        _canFormat = _textForBrowser.Length <= MaxFormattedLength;
        _view = _canFormat ? TextView.Reading : TextView.PlainText;
    }

    private void ShowView(TextView view) => _view = view;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_textForBrowser is not { } text)
        {
            return;
        }

        _textForBrowser = null;

        // Both views are filled now, so switching between them needs no interop.
        try
        {
            await JS.InvokeVoidAsync("chatClient.showPlainText", _plainTextElement, text);

            if (_canFormat)
            {
                await JS.InvokeVoidAsync("chatClient.renderMarkdown", _readingElement, text);
            }
        }
        catch (JSException ex)
        {
            Logger.LogWarning(ex, "JS interop failed while showing knowledge file {FileId}", FileId);
        }
    }

    public void Dispose()
    {
        _loadCancellation.Cancel();
        _loadCancellation.Dispose();
    }
}
