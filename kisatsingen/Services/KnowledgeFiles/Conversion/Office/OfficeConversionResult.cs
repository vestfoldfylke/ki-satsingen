namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office;

internal enum OfficeConversionResultErrorType
{
    TooLarge,
    TooLong,
    Unreadable
}

internal class OfficeConversionResult
{
    public bool Success { get; }
    public string Markdown { get; }
    public string? Error { get; }
    public OfficeConversionResultErrorType? ErrorType { get; }
    public Exception? Exception { get; }

    // For noticing text lost without a sound: the document text the
    // converter wrote, and how much the document has, both without whitespace.
    public int WrittenTextCharacters { get; }
    public int SourceTextCharacters { get; }

    private OfficeConversionResult(bool success, string markdown, string? error = null, OfficeConversionResultErrorType? errorType = null, Exception? exception = null, int writtenTextCharacters = 0, int sourceTextCharacters = 0)
    {
        Success = success;
        Markdown = markdown;
        Error = error;
        ErrorType = errorType;
        Exception = exception;
        WrittenTextCharacters = writtenTextCharacters;
        SourceTextCharacters = sourceTextCharacters;
    }

    public static OfficeConversionResult Converted(string markdown, int writtenTextCharacters, int sourceTextCharacters) =>
        new(true, markdown, writtenTextCharacters: writtenTextCharacters, sourceTextCharacters: sourceTextCharacters);

    public static OfficeConversionResult Unreadable(string error, Exception? exception = null) =>
        new(false, "", error, OfficeConversionResultErrorType.Unreadable, exception);

    public static OfficeConversionResult TooLarge(string error, Exception? exception = null) =>
        new(false, "", error, OfficeConversionResultErrorType.TooLarge, exception);

    public static OfficeConversionResult TooLong(string error, Exception? exception = null) =>
        new(false, "", error, OfficeConversionResultErrorType.TooLong, exception);
}
