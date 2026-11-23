using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.WordDocuments;

// The numbering the lists use. Bulleted lists share the template's own list
// ("FK Liste"). The template has no numbered list, so one is added with the
// same indents, and each numbered list gets an instance of its own: Word
// continues the count across paragraphs that share an instance.
internal sealed class WordNumbering
{
    private const string NumberedListText = "%1.";

    private readonly Numbering _numbering;
    private readonly int _numberedAbstractId;
    private int _nextInstanceId;

    public int BulletInstanceId { get; }

    private WordNumbering(Numbering numbering, int bulletInstanceId, int numberedAbstractId, int nextInstanceId)
    {
        _numbering = numbering;
        BulletInstanceId = bulletInstanceId;
        _numberedAbstractId = numberedAbstractId;
        _nextInstanceId = nextInstanceId;
    }

    public static WordNumbering Prepare(MainDocumentPart mainPart)
    {
        var numbering = mainPart.NumberingDefinitionsPart?.Numbering
            ?? throw new InvalidOperationException("The Word template has no numbering part, so lists cannot be made. The template changed; restore its \"FK Liste\" list.");

        var bulletInstanceId = FindBulletInstanceId(mainPart);
        var bulletLevel = FindFirstLevel(numbering, bulletInstanceId);

        var numberedAbstractId = numbering.Elements<AbstractNum>().Select(a => a.AbstractNumberId?.Value ?? 0).DefaultIfEmpty(-1).Max() + 1;
        var numberedAbstract = BuildNumberedAbstract(numberedAbstractId, bulletLevel);
        InsertAbstract(numbering, numberedAbstract);

        var nextInstanceId = numbering.Elements<NumberingInstance>().Select(n => n.NumberID?.Value ?? 0).DefaultIfEmpty(0).Max() + 1;
        return new WordNumbering(numbering, bulletInstanceId, numberedAbstractId, nextInstanceId);
    }

    // A new instance per list, restarted at 1.
    public int AddNumberedList()
    {
        var instanceId = _nextInstanceId++;
        _numbering.Append(new NumberingInstance(
            new AbstractNumId { Val = _numberedAbstractId },
            new LevelOverride(new StartOverrideNumberingValue { Val = 1 }) { LevelIndex = 0 })
        {
            NumberID = instanceId
        });
        return instanceId;
    }

    private static int FindBulletInstanceId(MainDocumentPart mainPart)
    {
        var style = mainPart.StyleDefinitionsPart?.Styles?.Elements<Style>()
            .FirstOrDefault(s => s.StyleId?.Value == WordStyles.BulletNumbering);

        return style?.StyleParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value
            ?? throw new InvalidOperationException(
                $"The Word template's numbering style {WordStyles.BulletNumbering} has no list. The template changed; update {nameof(WordStyles)}.{nameof(WordStyles.BulletNumbering)} to match it.");
    }

    private static Level FindFirstLevel(Numbering numbering, int instanceId)
    {
        var abstractId = numbering.Elements<NumberingInstance>()
            .FirstOrDefault(n => n.NumberID?.Value == instanceId)?.AbstractNumId?.Val?.Value;

        return numbering.Elements<AbstractNum>()
            .FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId)?
            .Elements<Level>()
            .FirstOrDefault(level => level.LevelIndex?.Value == 0)
            ?? throw new InvalidOperationException(
                $"The Word template's list {instanceId} has no first level. The template changed; update {nameof(WordStyles)}.{nameof(WordStyles.BulletNumbering)} to match it.");
    }

    // One level is enough: the blocks have no nested lists.
    private static AbstractNum BuildNumberedAbstract(int abstractId, Level bulletLevel)
    {
        var indentation = bulletLevel.PreviousParagraphProperties?.Indentation?.CloneNode(deep: true) as Indentation
            ?? new Indentation { Left = "680", Hanging = "340" };

        return new AbstractNum(
            new MultiLevelType { Val = MultiLevelValues.SingleLevel },
            new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = NumberedListText },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(indentation))
            {
                LevelIndex = 0
            })
        {
            AbstractNumberId = abstractId
        };
    }

    // The schema puts every abstractNum before the first num.
    private static void InsertAbstract(Numbering numbering, OpenXmlElement numberedAbstract)
    {
        if (numbering.Elements<AbstractNum>().LastOrDefault() is { } lastAbstract)
        {
            lastAbstract.InsertAfterSelf(numberedAbstract);
            return;
        }

        numbering.PrependChild(numberedAbstract);
    }
}
