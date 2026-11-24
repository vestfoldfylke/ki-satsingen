using kisatsingen.Services.MeetingDocuments;
using kisatsingen.Services.Presentations;
using kisatsingen.Services.WordDocuments;

namespace kisatsingen.Services.GeneratedFiles;

// The files built for one tab's download buttons. Scoped, so it lives as long
// as the circuit: opening another chat leaves a build running, and the file
// still arrives, while closing the tab gives up the build's place in the queue
// that the singleton FileBuildLimiter keeps for everyone.
public sealed class GeneratedFileBuilder(
    FileBuildLimiter limiter,
    VestfoldPresentationBuilder presentationBuilder,
    VestfoldWordDocumentBuilder wordDocumentBuilder,
    VestfoldMeetingDocumentBuilder meetingDocumentBuilder) : IDisposable
{
    // Cancelled, never disposed: a build may still be waiting on its token, and
    // a source without a timer holds nothing that needs releasing.
    private readonly CancellationTokenSource _cancellation = new();

    // Whether the circuit has ended. Lets a caller tell this cancellation apart
    // from an OperationCanceledException that came from somewhere else.
    public bool IsCancelled => _cancellation.IsCancellationRequested;

    // Throws OperationCanceledException when the circuit ends first.
    public Task<FileBuildResult> BuildAsync(GeneratedFile file) =>
        limiter.BuildAsync(BuildFor(file), _cancellation.Token);

    public static string ContentTypeOf(GeneratedFile file) => file switch
    {
        GeneratedFile.Presentation => VestfoldPresentationBuilder.ContentType,
        GeneratedFile.WordDocument or GeneratedFile.MeetingDocument => VestfoldWordDocumentBuilder.ContentType,
        _ => throw new ArgumentOutOfRangeException(nameof(file), file.GetType().Name, "A generated file with no content type; add it to GeneratedFileBuilder.")
    };

    public void Dispose() => _cancellation.Cancel();

    private Func<ArraySegment<byte>> BuildFor(GeneratedFile file) => file switch
    {
        GeneratedFile.Presentation presentation => () => presentationBuilder.Build(presentation.Spec),
        GeneratedFile.WordDocument wordDocument => () => wordDocumentBuilder.Build(wordDocument.Spec),
        GeneratedFile.MeetingDocument meetingDocument => () => meetingDocumentBuilder.Build(meetingDocument.Spec),
        _ => throw new ArgumentOutOfRangeException(nameof(file), file.GetType().Name, "A generated file with no builder; add it to GeneratedFileBuilder.")
    };
}
