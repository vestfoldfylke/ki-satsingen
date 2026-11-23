namespace kisatsingen.Services.GeneratedFiles;

// Reads a file embedded in this assembly, such as an Office template.
internal static class EmbeddedResourceBytes
{
    public static byte[] Read(string resourceName)
    {
        using var resource = typeof(EmbeddedResourceBytes).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource {resourceName} is missing, so no file can be built from it. Check its EmbeddedResource entry in the solution project file.");
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    }
}
