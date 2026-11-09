using System.IO.Compression;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office;

// What an Office file's zip says about itself, checked before the Open XML
// SDK opens it: the SDK limits neither the number of entries nor sizes that
// make no sense.
internal static class PackageCheck
{
    // A large deck with its media has a few thousand entries. ZipArchive
    // reads the whole zip directory before this is checked, but that memory
    // is bounded by the upload's size; this stops the work per entry after it.
    private const int MaxEntries = 10_000;

    // Whether the zip's entries can unpack to more than the limit, so a large
    // file is refused before anything is unpacked. An entry unpacks to at most
    // the larger of its declared and compressed sizes: a stored entry is read
    // to its compressed size, whatever size it declares. A size below zero is
    // ZIP64's "unknown", which could be anything.
    //
    // Throws InvalidDataException when the stream is not a zip or has more
    // than MaxEntries entries. Leaves the stream open, at no set position.
    public static bool CanUnpackToMoreThan(Stream package, long maxUncompressedBytes)
    {
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxEntries)
        {
            throw new InvalidDataException($"The package has more than {MaxEntries} entries.");
        }

        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var entryBytes = Math.Max(entry.Length, entry.CompressedLength);
            if (entry.Length < 0 || entry.CompressedLength < 0 || entryBytes > maxUncompressedBytes - totalBytes)
            {
                return true;
            }

            totalBytes += entryBytes;
        }

        return false;
    }
}
