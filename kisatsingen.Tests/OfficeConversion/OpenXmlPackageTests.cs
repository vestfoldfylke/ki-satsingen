using System.Xml;
using kisatsingen.OfficeConversion;
using Xunit;
using static kisatsingen.Tests.OfficeConversion.TestPackage;

namespace kisatsingen.Tests.OfficeConversion;

public sealed class OpenXmlPackageTests
{
    private const string AnyPartContent = "<part/>";

    private static OpenXmlPackage Open(byte[] package) => OpenXmlPackage.Open(new MemoryStream(package));

    private static OpenXmlPackage OpenWithMainPart(string mainPartTarget, params (string Name, string Content)[] parts) =>
        Open(Zip([("_rels/.rels", Relationships(new Relationship("rId1", TransitionalOfficeDocument, mainPartTarget))), .. parts]));

    [Fact]
    public void The_main_part_is_found_through_the_package_relationships_whatever_its_name()
    {
        using var package = OpenWithMainPart("word/document2.xml", ("word/document2.xml", AnyPartContent));

        var mainPart = package.FindMainPart();

        Assert.Equal("word/document2.xml", mainPart);
    }

    [Fact]
    public void The_main_part_of_a_strict_open_xml_file_is_found()
    {
        using var package = Open(Zip(
            ("_rels/.rels", Relationships(new Relationship("rId1", StrictOfficeDocument, "word/document.xml"))),
            ("word/document.xml", AnyPartContent)));

        var mainPart = package.FindMainPart();

        Assert.Equal("word/document.xml", mainPart);
    }

    [Fact]
    public void A_package_without_relationships_has_no_main_part()
    {
        using var package = Open(Zip(("word/document.xml", AnyPartContent)));

        var mainPart = package.FindMainPart();

        Assert.Null(mainPart);
    }

    [Fact]
    public void A_main_part_the_relationships_point_to_but_the_package_lacks_is_not_found()
    {
        using var package = OpenWithMainPart("word/document.xml");

        var mainPart = package.FindMainPart();

        Assert.Null(mainPart);
    }

    [Theory]
    [InlineData("styles.xml", "word/styles.xml")]
    [InlineData("./styles.xml", "word/styles.xml")]
    [InlineData("../shared/styles.xml", "shared/styles.xml")]
    [InlineData("/word/styles.xml", "word/styles.xml")]
    [InlineData("my%20styles.xml", "word/my styles.xml")]
    public void A_related_part_is_found_relative_to_the_part_that_points_to_it(string target, string expectedPartName)
    {
        using var package = OpenWithMainPart(
            "word/document.xml",
            ("word/document.xml", AnyPartContent),
            ("word/_rels/document.xml.rels", Relationships(new Relationship("rId1", TransitionalStyles, target))),
            (expectedPartName, AnyPartContent));

        var stylesPart = package.FindRelatedPart("word/document.xml", RelationshipType.Styles);

        Assert.Equal(expectedPartName, stylesPart);
    }

    [Fact]
    public void A_part_is_found_whatever_the_case_of_its_name()
    {
        using var package = OpenWithMainPart("word/document.xml", ("Word/Document.XML", AnyPartContent));

        var mainPart = package.LoadPart(package.FindMainPart()!);

        Assert.NotNull(mainPart);
    }

    [Fact]
    public void An_external_relationship_keeps_its_address_and_is_never_a_part()
    {
        using var package = OpenWithMainPart(
            "word/document.xml",
            ("word/document.xml", AnyPartContent),
            ("word/_rels/document.xml.rels", Relationships(new Relationship("rId7", TransitionalHyperlink, "https://www.vestfoldfylke.no/", IsExternal: true))));

        var relationships = package.RelationshipsOf("word/document.xml");

        Assert.Equal([new PackageRelationship("rId7", TransitionalHyperlink, "https://www.vestfoldfylke.no/", IsExternal: true)], relationships);
    }

    [Fact]
    public void A_part_without_a_relationships_part_has_no_relationships()
    {
        using var package = OpenWithMainPart("word/document.xml", ("word/document.xml", AnyPartContent));

        var relationships = package.RelationshipsOf("word/document.xml");

        Assert.Empty(relationships);
    }

    [Fact]
    public void A_part_the_package_lacks_loads_as_null()
    {
        using var package = OpenWithMainPart("word/document.xml", ("word/document.xml", AnyPartContent));

        var stylesPart = package.LoadPart("word/styles.xml");

        Assert.Null(stylesPart);
    }

    // Word text can be nothing but spaces, and must survive even without xml:space="preserve".
    [Fact]
    public void Text_of_only_spaces_is_kept()
    {
        using var package = OpenWithMainPart("word/document.xml", ("word/document.xml", "<t>   </t>"));

        var mainPart = package.LoadPart("word/document.xml");

        Assert.Equal("   ", mainPart!.Root!.Value);
    }

    // The "billion laughs" attack nests entities like these until they expand to gigabytes.
    [Fact]
    public void A_part_with_a_dtd_is_refused()
    {
        const string partWithDtd = """<?xml version="1.0"?><!DOCTYPE lolz [<!ENTITY lol "lol"><!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;">]><lolz>&lol2;</lolz>""";
        using var package = OpenWithMainPart("word/document.xml", ("word/document.xml", partWithDtd));

        Assert.Throws<XmlException>(() => package.LoadPart("word/document.xml"));
    }

    [Fact]
    public void A_stream_that_is_not_a_zip_archive_is_refused()
    {
        var notAZip = "Dette er ikke et Word-dokument."u8.ToArray();

        Assert.Throws<InvalidDataException>(() => Open(notAZip));
    }

    [Fact]
    public void A_package_whose_parts_together_unpack_to_more_than_the_limit_exceeds_it()
    {
        var package = WithDeclaredUncompressedSizes(
            Zip(("word/document.xml", AnyPartContent), ("word/styles.xml", AnyPartContent)),
            ("word/document.xml", 600),
            ("word/styles.xml", 600));
        using var opened = Open(package);

        var exceeds = opened.ExceedsUncompressedBytes(1_000);

        Assert.True(exceeds);
    }

    [Fact]
    public void A_package_that_unpacks_to_exactly_the_limit_does_not_exceed_it()
    {
        var package = WithDeclaredUncompressedSizes(Zip(("word/document.xml", AnyPartContent)), ("word/document.xml", 1_000));
        using var opened = Open(package);

        var exceeds = opened.ExceedsUncompressedBytes(1_000);

        Assert.False(exceeds);
    }
}
