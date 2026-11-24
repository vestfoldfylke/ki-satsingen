namespace kisatsingen.Services.WordDocuments;

// How a template's tables are styled: by the template's table style alone, or
// with borders and a header colour the builder adds.
internal sealed record WordTableFormat(string StyleId, bool IsStyledByTemplate);
