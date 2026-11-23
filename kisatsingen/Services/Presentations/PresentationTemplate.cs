using kisatsingen.Services.GeneratedFiles;

namespace kisatsingen.Services.Presentations;

// The Vestfold fylkeskommune PowerPoint template, embedded in the assembly so
// a deployment can never run without it. A singleton: read once on first use,
// and every build copies it.
public sealed class PresentationTemplate
{
    public const string ResourceName = "kisatsingen.Presentations.VestfoldTemplate.potx";

    private readonly Lazy<byte[]> _bytes = new(Load);

    public MemoryStream OpenWritableCopy()
    {
        var stream = new MemoryStream();
        stream.Write(_bytes.Value);
        stream.Position = 0;
        return stream;
    }

    private static byte[] Load() => EmbeddedResourceBytes.Read(ResourceName);
}
