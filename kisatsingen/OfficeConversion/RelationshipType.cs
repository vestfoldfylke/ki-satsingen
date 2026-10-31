namespace kisatsingen.OfficeConversion;

// Office writes a relationship's type under one of two addresses, depending on
// whether the file was saved as Transitional Open XML (the default) or Strict.
public sealed class RelationshipType
{
    private const string TransitionalAddress = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string StrictAddress = "http://purl.oclc.org/ooxml/officeDocument/relationships/";

    public static readonly RelationshipType OfficeDocument = new("officeDocument");
    public static readonly RelationshipType Styles = new("styles");

    private readonly string _name;

    private RelationshipType(string name) => _name = name;

    public bool Matches(string type) =>
        type == TransitionalAddress + _name || type == StrictAddress + _name;
}
