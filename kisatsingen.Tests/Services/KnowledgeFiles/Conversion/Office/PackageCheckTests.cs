using kisatsingen.Services.KnowledgeFiles.Conversion.Office;
using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.TestPackage;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office;

public sealed class PackageCheckTests
{
    private const string AnyPartContent = "<part/>";

    private static bool CanUnpackToMoreThan(byte[] package, long maxUncompressedBytes) =>
        PackageCheck.CanUnpackToMoreThan(new MemoryStream(package), maxUncompressedBytes);

    [Fact]
    public void A_package_whose_parts_together_declare_more_than_the_limit_exceeds_it()
    {
        var package = WithDeclaredUncompressedSizes(
            Zip(("word/document.xml", AnyPartContent), ("word/styles.xml", AnyPartContent)),
            ("word/document.xml", 600),
            ("word/styles.xml", 600));

        var exceeds = CanUnpackToMoreThan(package, maxUncompressedBytes: 1_000);

        Assert.True(exceeds);
    }

    [Fact]
    public void A_package_that_declares_exactly_the_limit_does_not_exceed_it()
    {
        var package = WithDeclaredUncompressedSizes(Zip(("word/document.xml", AnyPartContent)), ("word/document.xml", 1_000));

        var exceeds = CanUnpackToMoreThan(package, maxUncompressedBytes: 1_000);

        Assert.False(exceeds);
    }

    // A stored entry is read to its compressed size, whatever size it declares.
    [Fact]
    public void A_stored_entry_declaring_less_than_it_holds_counts_what_it_holds()
    {
        var package = WithDeclaredUncompressedSizes(WithStoredEntries(Zip(("word/document.xml", new string('a', 2_000)))), ("word/document.xml", 10));

        var exceeds = CanUnpackToMoreThan(package, maxUncompressedBytes: 1_000);

        Assert.True(exceeds);
    }

    // ZIP64's -1 means "unknown", which leaves .NET reading the entry without a bound.
    [Theory]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void A_package_declaring_a_size_below_zero_exceeds_any_limit(long declaredSize)
    {
        var package = WithZip64DeclaredSize("word/document.xml", AnyPartContent, declaredSize);

        var exceeds = CanUnpackToMoreThan(package, maxUncompressedBytes: long.MaxValue);

        Assert.True(exceeds);
    }

    [Fact]
    public void A_package_with_more_entries_than_the_limit_is_refused()
    {
        var entries = Enumerable.Range(0, 10_001).Select(index => ($"media/{index}.xml", AnyPartContent)).ToArray();
        var package = Zip(entries);

        Assert.Throws<InvalidDataException>(() => CanUnpackToMoreThan(package, maxUncompressedBytes: long.MaxValue));
    }

    [Fact]
    public void A_stream_that_is_not_a_zip_archive_is_refused()
    {
        var notAZip = "Dette er ikke et Word-dokument."u8.ToArray();

        Assert.Throws<InvalidDataException>(() => CanUnpackToMoreThan(notAZip, maxUncompressedBytes: long.MaxValue));
    }
}
