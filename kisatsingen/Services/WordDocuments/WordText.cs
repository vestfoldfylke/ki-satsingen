using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.WordDocuments;

// Text as Word holds it: a line break the model typed stays a line break,
// not a new paragraph.
internal static class WordText
{
    public static Run CreateRun(string text, bool isBold = false)
    {
        var run = new Run();
        if (isBold)
        {
            run.Append(new RunProperties(new Bold(), new BoldComplexScript()));
        }

        AppendText(run, text);
        return run;
    }

    // Appends after whatever the run already holds, such as its properties.
    public static void AppendText(Run run, string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                run.Append(new Break());
            }

            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
    }
}
