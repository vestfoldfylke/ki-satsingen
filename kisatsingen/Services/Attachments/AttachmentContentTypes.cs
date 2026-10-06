namespace kisatsingen.Services.Attachments;

// Signature is the bytes a file of this type must start with, or null for a
// type that has none (text). Checked while uploading, so a renamed file is
// refused after its first bytes rather than after all of them.
public sealed record AttachmentType(string ContentType, byte[]? Signature);

// From the extension, not the browser's Content-Type: browsers report .md as
// anything from text/markdown to application/octet-stream to nothing at all.
public static class AttachmentContentTypes
{
    // In the plural, since they name what may be attached.
    private const string TextFiles = "tekstfiler";
    private const string Images = "bilder";

    public static readonly AttachmentType PlainText = new("text/plain", Signature: null);
    public static readonly AttachmentType Markdown = new("text/markdown", Signature: null);
    public static readonly AttachmentType Png = new("image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    public static readonly AttachmentType Jpeg = new("image/jpeg", [0xFF, 0xD8, 0xFF]);

    private sealed record AllowedExtension(string Extension, AttachmentType Type, string KindName);

    // An array rather than the dictionary alone, because a dictionary doesn't
    // promise an order and users are told about the types in this one.
    private static readonly AllowedExtension[] AllowedExtensions =
    [
        new(".txt", PlainText, TextFiles),
        new(".md", Markdown, TextFiles),
        new(".markdown", Markdown, TextFiles),
        new(".png", Png, Images),
        new(".jpg", Jpeg, Images),
        new(".jpeg", Jpeg, Images),
    ];

    private static readonly Dictionary<string, AttachmentType> ByExtension =
        AllowedExtensions.ToDictionary(allowed => allowed.Extension, allowed => allowed.Type, StringComparer.OrdinalIgnoreCase);

    // For the file picker's accept attribute. Convenience only: the picker
    // can be told to show all files, so the upload checks again.
    public static string AcceptAttribute { get; } = string.Join(',', AllowedExtensions.Select(allowed => allowed.Extension));

    // "tekstfiler og bilder": what may be attached, before a file is picked.
    public static string AllowedKindsText { get; } =
        JoinAsNorwegianList(GroupByKind().Select(kind => kind.Key));

    // "tekstfiler (.txt, .md) og bilder (.png)": after a refusal, the user is
    // checking one file, so the extensions are what helps.
    public static string AllowedKindsWithExtensionsText { get; } =
        JoinAsNorwegianList(GroupByKind().Select(kind => $"{kind.Key} ({string.Join(", ", kind.Select(allowed => allowed.Extension))})"));

    public static AttachmentType? FromFileName(string fileName) =>
        ByExtension.GetValueOrDefault(Path.GetExtension(fileName));

    private static IEnumerable<IGrouping<string, AllowedExtension>> GroupByKind() =>
        AllowedExtensions.GroupBy(allowed => allowed.KindName);

    // "a, b og c"
    private static string JoinAsNorwegianList(IEnumerable<string> items)
    {
        var itemList = items.ToList();
        return itemList.Count switch
        {
            0 => "",
            1 => itemList[0],
            _ => $"{string.Join(", ", itemList[..^1])} og {itemList[^1]}",
        };
    }
}
