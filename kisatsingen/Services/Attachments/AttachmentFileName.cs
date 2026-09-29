using kisatsingen.Data.Entities;

namespace kisatsingen.Services.Attachments;

// For display and for the stored file name only; it never reaches the file
// system (see TempFileStore).
public static class AttachmentFileName
{
    private const string Fallback = "fil";

    public static string Normalize(string clientFileName)
    {
        // Browsers send a base name, but the value is the client's to choose.
        var name = new string(Path.GetFileName(clientFileName).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0)
        {
            return Fallback;
        }

        if (name.Length <= KnowledgeFile.MaxFileNameLength)
        {
            return name;
        }

        // The extension decides the type, so it survives the cut.
        var extension = Path.GetExtension(name);
        return extension.Length >= KnowledgeFile.MaxFileNameLength
            ? TextTruncation.Prefix(name, KnowledgeFile.MaxFileNameLength)
            : TextTruncation.Prefix(name, KnowledgeFile.MaxFileNameLength - extension.Length) + extension;
    }
}
