namespace kisatsingen.Services.Attachments;

// Signature is the bytes a file of this type must start with, or null for a
// type that has none (text). Checked while uploading, so a renamed file is
// refused after its first bytes rather than after all of them.
public sealed record AttachmentType(string ContentType, byte[]? Signature);

// From the extension, not the browser's Content-Type: browsers report .md as
// anything from text/markdown to application/octet-stream to nothing at all.
public static class AttachmentContentTypes
{
    public static readonly AttachmentType PlainText = new("text/plain", Signature: null);
    public static readonly AttachmentType Markdown = new("text/markdown", Signature: null);
    public static readonly AttachmentType Png = new("image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    public static readonly AttachmentType Jpeg = new("image/jpeg", [0xFF, 0xD8, 0xFF]);

    private static readonly Dictionary<string, AttachmentType> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = PlainText,
        [".md"] = Markdown,
        [".markdown"] = Markdown,
        [".png"] = Png,
        [".jpg"] = Jpeg,
        [".jpeg"] = Jpeg,
    };

    // For the file picker's accept attribute. Convenience only: the picker
    // can be told to show all files, so the upload checks again.
    public static string AcceptAttribute { get; } = string.Join(',', ByExtension.Keys);

    public static string AllowedExtensionsText { get; } = string.Join(", ", ByExtension.Keys);

    public static AttachmentType? FromFileName(string fileName) =>
        ByExtension.GetValueOrDefault(Path.GetExtension(fileName));
}
