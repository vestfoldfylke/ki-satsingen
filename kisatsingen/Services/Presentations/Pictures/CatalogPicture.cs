using kisatsingen.Services.KnowledgeFiles.Conversion;

namespace kisatsingen.Services.Presentations.Pictures;

// One picture the model can put on a slide. AltText is written into the file
// for screen readers; Description is only for the model, to choose by.
public sealed record CatalogPicture(
    string Id,
    string Description,
    string AltText,
    string ContentType,
    ImageDimensions Dimensions,
    byte[] Bytes);
