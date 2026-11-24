using System.ComponentModel;

namespace kisatsingen.Services.WordDocuments;

// Flat rather than one type per kind: models fill a flat shape more reliably.
// Which fields a kind takes is decided by DocumentBlockFields and enforced by
// WordDocumentSpecValidator. Kind is nullable so a block sent without one is
// refused instead of bound as the enum's default (Kicker).
public sealed record DocumentBlockSpec(
    [property: Description(
        "The block's kind. Kicker (Simple only): a small line above the main heading, typically a date. " +
        "Heading1 (Simple only): the document's main heading; at most one per document. In a Report the title is on the cover, so chapters start at Heading2. " +
        "Heading2: a section or chapter heading. Heading3: a subsection heading. Heading4 and Heading5 (Report only): lower subheadings. " +
        "Lead: a short introduction in larger type. Paragraph: body text. FactBox (Report only): a paragraph in a coloured fact box. " +
        "BulletList: a bulleted list. NumberedList: a numbered list; each starts at 1. " +
        "Table: a table with an optional header row. Caption: a caption for a table, placed under it. PageBreak: starts a new page.")]
    DocumentBlockKind? Kind,
    [property: Description(
        "The block's text, used by every kind except the lists, Table and PageBreak. " +
        "One paragraph per block; make a new Paragraph block for a new paragraph.")]
    string? Text = null,
    [property: Description("The list items, one each, used only by BulletList and NumberedList. Do not start items with \"-\" or \"1.\".")]
    IReadOnlyList<string>? Items = null,
    [property: Description("The table's column headings, used only by Table. Leave it out for a table without a header row.")]
    IReadOnlyList<string>? HeaderRow = null,
    [property: Description("The table's rows, each a list of cell texts, used only by Table. Every row has the same number of cells as the header row.")]
    IReadOnlyList<IReadOnlyList<string>>? Rows = null);
