using System.Collections.Frozen;
using kisatsingen.Services.GeneratedFiles;

namespace kisatsingen.Services.WordDocuments;

// Vestfold fylkeskommune's Word templates, embedded in the assembly so a
// deployment can never run without them. A singleton: each is read once on
// first use, and every build copies it.
public sealed class WordTemplateFiles
{
    private const string ResourcePrefix = "kisatsingen.WordDocuments.Templates.";

    private static readonly FrozenDictionary<WordTemplateFile, string> ResourceNames = new Dictionary<WordTemplateFile, string>
    {
        [WordTemplateFile.Simple] = ResourcePrefix + "vestfold-enkel-wordmal.dotx",
        [WordTemplateFile.Report] = ResourcePrefix + "vestfold_rapportmal.dotx",
        [WordTemplateFile.Meeting] = ResourcePrefix + "vestfold-moteinnkalling-referatmal.dotx"
    }.ToFrozenDictionary();

    private readonly FrozenDictionary<WordTemplateFile, Lazy<byte[]>> _bytes = ResourceNames.ToFrozenDictionary(
        entry => entry.Key,
        entry => new Lazy<byte[]>(() => EmbeddedResourceBytes.Read(entry.Value)));

    public static string ResourceNameOf(WordTemplateFile file) => ResourceNames[file];

    public MemoryStream OpenWritableCopy(WordTemplateFile file)
    {
        var stream = new MemoryStream();
        stream.Write(_bytes[file].Value);
        stream.Position = 0;
        return stream;
    }
}
