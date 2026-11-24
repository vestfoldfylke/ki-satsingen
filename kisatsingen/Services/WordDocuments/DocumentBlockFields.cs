namespace kisatsingen.Services.WordDocuments;

// Which fields each block kind takes, and which templates have the kind.
internal sealed record DocumentBlockFields(bool HasText, bool HasItems, bool HasTable)
{
    private static readonly DocumentBlockFields Text = new(HasText: true, HasItems: false, HasTable: false);
    private static readonly DocumentBlockFields List = new(HasText: false, HasItems: true, HasTable: false);
    private static readonly DocumentBlockFields Table = new(HasText: false, HasItems: false, HasTable: true);
    private static readonly DocumentBlockFields None = new(HasText: false, HasItems: false, HasTable: false);

    public static DocumentBlockFields For(DocumentBlockKind kind) => kind switch
    {
        DocumentBlockKind.Kicker or DocumentBlockKind.Heading1 or DocumentBlockKind.Heading2 or DocumentBlockKind.Heading3
            or DocumentBlockKind.Heading4 or DocumentBlockKind.Heading5 or DocumentBlockKind.Lead or DocumentBlockKind.Paragraph
            or DocumentBlockKind.Caption or DocumentBlockKind.FactBox => Text,
        DocumentBlockKind.BulletList or DocumentBlockKind.NumberedList => List,
        DocumentBlockKind.Table => Table,
        DocumentBlockKind.PageBreak => None,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A block kind with no fields; add it to DocumentBlockFields.")
    };

    // The report's title, date and Heading1 are on its cover, and it has no
    // Kicker style; Heading4, Heading5 and fact boxes are the report's alone.
    public static bool IsAvailableIn(DocumentBlockKind kind, WordDocumentTemplate template) => kind switch
    {
        DocumentBlockKind.Kicker or DocumentBlockKind.Heading1 => template == WordDocumentTemplate.Simple,
        DocumentBlockKind.Heading4 or DocumentBlockKind.Heading5 or DocumentBlockKind.FactBox => template == WordDocumentTemplate.Report,
        _ => true
    };

    // Short text kinds get a tighter limit than body text.
    public static bool IsShortText(DocumentBlockKind kind) =>
        kind is DocumentBlockKind.Kicker or DocumentBlockKind.Heading1 or DocumentBlockKind.Heading2 or DocumentBlockKind.Heading3
            or DocumentBlockKind.Heading4 or DocumentBlockKind.Heading5 or DocumentBlockKind.Caption;
}
