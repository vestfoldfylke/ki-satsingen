using System.Text.Json;
using System.Text.RegularExpressions;
using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;

namespace kisatsingen.Services.Presentations.Pictures;

// The pictures presentations can use, embedded in the assembly with their
// manifest so a deployment always has the files its manifest promises.
//
// One process-wide instance, Embedded: the presentation tool is part of the
// static tool list given to every model, so it cannot take the catalog from
// DI. Loading checks every entry and throws on the first broken one.
public sealed partial class PictureCatalog
{
    public const string ResourcePrefix = "kisatsingen.Presentations.Pictures.";
    public const string ManifestResourceName = ResourcePrefix + "pictures.json";

    private static readonly Lazy<PictureCatalog> LazyEmbedded = new(() => Load(OpenEmbeddedResource));

    private readonly Dictionary<string, CatalogPicture> _byId;

    public static PictureCatalog Embedded => LazyEmbedded.Value;

    public IReadOnlyList<CatalogPicture> All { get; }

    private PictureCatalog(IReadOnlyList<CatalogPicture> pictures)
    {
        All = pictures;
        _byId = pictures.ToDictionary(picture => picture.Id, StringComparer.Ordinal);
    }

    // Null when the id is unknown, such as a stored call naming a picture
    // that has since been removed from the catalog.
    public CatalogPicture? Find(string id) => _byId.GetValueOrDefault(id);

    // Takes the resource opener so a test can feed it a broken catalog.
    internal static PictureCatalog Load(Func<string, Stream?> openResource)
    {
        using var manifestStream = openResource(ManifestResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource {ManifestResourceName} is missing. Check the EmbeddedResource entry for the presentation pictures in kisatsingen.csproj.");

        var entries = JsonSerializer.Deserialize<List<ManifestEntry>>(manifestStream, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"{ManifestResourceName} is empty; it must be a JSON array of pictures, even an empty one.");

        var pictures = new List<CatalogPicture>(entries.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var picture = LoadPicture(entry, openResource);
            if (!seenIds.Add(picture.Id))
            {
                throw new InvalidOperationException($"{ManifestResourceName} lists the picture id \"{picture.Id}\" more than once. Give each picture its own id.");
            }
            pictures.Add(picture);
        }

        return new PictureCatalog(pictures);
    }

    private static CatalogPicture LoadPicture(ManifestEntry entry, Func<string, Stream?> openResource)
    {
        var id = RequireField(entry.Id, "id", entry);
        if (!PictureIdPattern().IsMatch(id))
        {
            throw new InvalidOperationException($"Picture id \"{id}\" in {ManifestResourceName} is not allowed. Use lowercase letters a–z, digits and hyphens only.");
        }

        var file = RequireField(entry.File, "file", entry);
        var altText = RequireField(entry.AltText, "altText", entry);
        var description = RequireField(entry.Description, "description", entry);
        var contentType = ContentTypeOf(file);

        using var resource = openResource(ResourcePrefix + file)
            ?? throw new InvalidOperationException(
                $"Picture \"{id}\" names the file {file}, but it is not embedded. Add the file to Services/Presentations/Pictures, or remove the entry from {ManifestResourceName}.");
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        var bytes = copy.ToArray();

        // Synchronous over a MemoryStream, which completes every read at once.
        var dimensions = ImageHeaderReader.ReadAsync(new MemoryStream(bytes, writable: false), contentType, CancellationToken.None).GetAwaiter().GetResult();
        if (dimensions is not { Width: > 0, Height: > 0 })
        {
            throw new InvalidOperationException($"Picture \"{id}\" ({file}) is not a readable {contentType} image. Re-export the file.");
        }

        return new CatalogPicture(id, description, altText, contentType, dimensions, bytes);
    }

    private static string RequireField(string? value, string field, ManifestEntry entry) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"A picture in {ManifestResourceName} (id \"{entry.Id}\") has no {field}. Every picture needs id, file, altText and description.")
            : value.Trim();

    private static string ContentTypeOf(string file) =>
        Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => AttachmentContentTypes.Jpeg.ContentType,
            ".png" => AttachmentContentTypes.Png.ContentType,
            _ => throw new InvalidOperationException($"Picture file {file} in {ManifestResourceName} is neither JPEG nor PNG. Convert it to JPEG.")
        };

    private static Stream? OpenEmbeddedResource(string name) =>
        typeof(PictureCatalog).Assembly.GetManifestResourceStream(name);

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex PictureIdPattern();

    private sealed record ManifestEntry(string? Id, string? File, string? AltText, string? Description);
}
