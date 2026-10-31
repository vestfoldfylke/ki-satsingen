namespace kisatsingen.OfficeConversion;

// Office writes a relationship's type as a URI under one of two bases,
// depending on whether the file was saved as Transitional Open XML (the
// default) or Strict.
public sealed class RelationshipType
{
    private const string TransitionalBaseUri = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string StrictBaseUri = "http://purl.oclc.org/ooxml/officeDocument/relationships/";

    public static readonly RelationshipType OfficeDocument = new("officeDocument");
    public static readonly RelationshipType Styles = new("styles");

    private readonly string _name;

    private RelationshipType(string name) => _name = name;

    public bool Matches(string typeUri) =>
        typeUri == TransitionalBaseUri + _name || typeUri == StrictBaseUri + _name;
}
