namespace kisatsingen.Services.Presentations;

// The Vestfold fylkeskommune PowerPoint template, embedded in the assembly so
// a deployment can never run without it. Read once; every build copies it.
internal static class PresentationTemplate
{
    public const string ResourceName = "kisatsingen.Presentations.VestfoldTemplate.potx";

    private static readonly Lazy<byte[]> Bytes = new(Load);

    public static MemoryStream OpenWritableCopy()
    {
        var stream = new MemoryStream();
        stream.Write(Bytes.Value);
        stream.Position = 0;
        return stream;
    }

    private static byte[] Load()
    {
        using var resource = typeof(PresentationTemplate).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource {ResourceName} is missing, so no presentation can be built. Check the EmbeddedResource entry for the template in kisatsingen.csproj.");
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    }
}
