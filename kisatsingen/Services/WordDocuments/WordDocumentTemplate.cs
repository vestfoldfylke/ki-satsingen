using System.Text.Json.Serialization;

namespace kisatsingen.Services.WordDocuments;

// The free-text templates create_word_document can build on. What each is for
// is told to the model in the tool's template parameter.
[JsonConverter(typeof(JsonStringEnumConverter<WordDocumentTemplate>))]
public enum WordDocumentTemplate
{
    Simple,
    Report
}
