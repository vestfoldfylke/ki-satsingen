using System.Runtime.CompilerServices;
using System.Text;
using ElBruno.MarkItDotNet;
using kisatsingen.Services.Attachments;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace kisatsingen.Services.KnowledgeFiles.Conversion;

public class PdfDocumentConverter : IDocumentConverter
{
    private readonly ILogger<PdfDocumentConverter> _logger;
    private readonly MarkdownService _markdownService;

    public PdfDocumentConverter(ILogger<PdfDocumentConverter> logger, MarkdownService markdownService)
    {
        _logger = logger;
        _markdownService = markdownService;
    }

    public IReadOnlyList<string> ContentTypes { get; } = [AttachmentContentTypes.Pdf.ContentType];

    public async Task<ConversionResult> ConvertAsync(ConversionRequest request, CancellationToken ct)
    {
        var result = await _markdownService.ConvertAsync(request.Content, Path.GetExtension(request.FileName), ct);
        if (!result.Success)
        {
            var errorMessage = result.ErrorMessage ?? "Ukjent feil";
            _logger.LogError("PdfDocumentConverter -> Failed to convert PDF file '{Filename}' with ContentType '{ContentType}' to Markdown. ErrorMessage: {ErrorMessage}. MetaData: {@MetaData}", request.FileName, request.ContentType, errorMessage, result.Metadata);
            return new ConversionResult.Rejected(errorMessage);
        }

        if (result.Markdown.Length == 0)
        {
            return new ConversionResult.Rejected("Tomt innhold returnert");
        }

        var pageCount = GetPageCount(result.Markdown);
        return new ConversionResult.Converted(result.Markdown, ContentOrigin.Pdf, pageCount);
    }

    private static int GetPageCount(string text) => text.Split("\n---\n", StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>
/// Kopi av ElBruno.MarkItDotNet sin PdfConverter (MIT-lisens, Copyright (c) 2025 Bruno Capuano)
/// med rettet avsnittsgjenkjenning. Original: src/ElBruno.MarkItDotNet/Converters/PdfConverter.cs
/// Endringer er merket med "ENDRET".
///
/// Converts PDF (.pdf) files to Markdown using UglyToad.PdfPig.
/// Uses word-level extraction with line/paragraph grouping and heading detection.
/// Implements <see cref="IStreamingMarkdownConverter"/> for page-by-page streaming.
/// </summary>
public class VestfoldPdfConverter : IStreamingMarkdownConverter
{
    // TODO: Remove this class if/when MarkItDotNet fixes the issue raised here: https://github.com/elbruno/ElBruno.MarkItDotNet/issues/30
    private const double LineYTolerance = 3.0;
    // ENDRET: nå relativt til dokumentets vanlige linjeavstand, ikke ordboksens høyde.
    private const double ParagraphSpacingFactor = 1.25;
    private const double HeadingMinimumSizeRatio = 1.15;
    private const double HeadingMinimumBoldSizeRatio = 1.05;
    private const int MaxHeadingLevel = 6; // ENDRET: var 3, så #### og lavere ble aldri gjenkjent

    /// <inheritdoc />
    public bool CanHandle(string fileExtension) =>
        fileExtension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<string> ConvertAsync(Stream fileStream, string fileExtension, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);

        using var document = PdfDocument.Open(fileStream);
        var sb = new StringBuilder();
        var pageCount = document.NumberOfPages;

        for (var i = 1; i <= pageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = document.GetPage(i);

            if (i > 1)
            {
                sb.AppendLine();
                sb.AppendLine("---");
                sb.AppendLine();
            }

            var pageMarkdown = ExtractPageMarkdown(page);
            if (!string.IsNullOrWhiteSpace(pageMarkdown))
            {
                sb.AppendLine(pageMarkdown.TrimEnd());
            }
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ConvertStreamingAsync(
        Stream fileStream,
        string fileExtension,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);

        using var document = PdfDocument.Open(fileStream);
        var pageCount = document.NumberOfPages;

        for (var i = 1; i <= pageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = document.GetPage(i);
            var chunkSb = new StringBuilder();

            if (i > 1)
            {
                chunkSb.AppendLine();
                chunkSb.AppendLine("---");
                chunkSb.AppendLine();
            }

            var pageMarkdown = ExtractPageMarkdown(page);
            if (!string.IsNullOrWhiteSpace(pageMarkdown))
            {
                chunkSb.AppendLine(pageMarkdown.TrimEnd());
            }

            yield return chunkSb.ToString();
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
    
    /// <summary>
    /// Extracts structured markdown from a single page using word-level extraction.
    /// Groups words into lines by Y-coordinate, lines into paragraphs by spacing,
    /// and detects headings by font size.
    /// </summary>
    private static string ExtractPageMarkdown(Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0)
        {
            // Fallback to raw text if word extraction yields nothing
            var rawText = page.Text;
            return string.IsNullOrWhiteSpace(rawText) ? string.Empty : rawText.Trim();
        }

        var lines = GroupWordsIntoLines(words);
        if (lines.Count == 0)
            return string.Empty;

        var bodyFontSize = DetectBodyFontSize(lines);
        var paragraphs = GroupLinesIntoParagraphs(lines);
        var headingLevelMap = BuildHeadingLevelMap(paragraphs, bodyFontSize);

        var sb = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            var text = paragraph.Text;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var roundedFontSize = Math.Round(paragraph.FontSize, 1);
            if (headingLevelMap.TryGetValue(roundedFontSize, out var level))
            {
                sb.Append(new string('#', level));
                sb.Append(' ');
            }

            sb.AppendLine(text);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static List<TextLine> GroupWordsIntoLines(List<Word> words)
    {
        // Sort words by Y descending (top of page first), then X ascending
        var sorted = words
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var lines = new List<TextLine>();
        var currentLineWords = new List<Word>();
        var currentY = double.MaxValue;

        foreach (var word in sorted)
        {
            var wordY = word.BoundingBox.Bottom;

            if (currentLineWords.Count == 0 || Math.Abs(wordY - currentY) <= LineYTolerance)
            {
                currentLineWords.Add(word);
                if (currentLineWords.Count == 1)
                    currentY = wordY;
                else
                    currentY = currentLineWords.Average(w => w.BoundingBox.Bottom);
            }
            else
            {
                lines.Add(CreateTextLine(currentLineWords));
                currentLineWords = [word];
                currentY = wordY;
            }
        }

        if (currentLineWords.Count > 0)
            lines.Add(CreateTextLine(currentLineWords));

        return lines;
    }

    private static TextLine CreateTextLine(List<Word> words)
    {
        var sorted = words.OrderBy(w => w.BoundingBox.Left).ToList();
        var text = string.Join(" ", sorted.Select(w => w.Text));
        var letters = sorted.SelectMany(w => w.Letters).ToList();
        var avgFontSize = letters
            .Select(l => l.PointSize)
            .DefaultIfEmpty(0)
            .Average();
        var boldRatio = letters.Count == 0
            ? 0
            : letters.Count(l => IsBoldFontName(l.FontName)) / (double)letters.Count;
        var y = sorted.Average(w => w.BoundingBox.Bottom);
        var height = sorted.Max(w => w.BoundingBox.Height);

        // ENDRET (ny): linjer med store hull midt i (tabellrader, tekst i kolonner) skal ikke slås sammen med andre.
        var hasColumnGap = false;
        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i].BoundingBox.Left - sorted[i - 1].BoundingBox.Right > avgFontSize * 1.5)
            {
                hasColumnGap = true;
                break;
            }
        }

        return new TextLine(text, y, height, avgFontSize, boldRatio, hasColumnGap);
    }

    /// <summary>
    /// Detects the most common (body) font size across all lines.
    /// </summary>
    private static double DetectBodyFontSize(List<TextLine> lines)
    {
        var fontSizes = lines
            .Where(l => l.FontSize > 0)
            .GroupBy(l => Math.Round(l.FontSize, 1))
            .OrderByDescending(g => g.Sum(l => l.Text.Length))
            .ToList();

        return fontSizes.Count > 0 ? fontSizes[0].Key : 0;
    }

    private static List<ParagraphInfo> GroupLinesIntoParagraphs(List<TextLine> lines)
    {
        var paragraphs = new List<ParagraphInfo>();
        if (lines.Count == 0)
            return paragraphs;

        var currentLines = new List<TextLine> { lines[0] };

        // ENDRET: Originalen sammenlignet linjeavstanden med høyden på ordenes boks (ca. 0,7 x fontstørrelsen).
        // Vanlig linjeavstand er ca. 1,2 x fontstørrelsen, altså ca. 1,7 x boksens høyde, som er over
        // grensen på 1,5. Derfor ble HVER linje et eget avsnitt. Nå måles avstanden relativt til
        // fontstørrelsen, og grensen tilpasses dokumentets faktiske linjeavstand (enkel, 1,15, 1,5 osv.).
        var typicalLineSpacing = DetectTypicalLineSpacingRatio(lines);

        for (var i = 1; i < lines.Count; i++)
        {
            var prevLine = lines[i - 1];
            var currentLine = lines[i];

            // Lines are ordered top-to-bottom (Y descending), so spacing = prev.Y - current.Y
            var spacing = prevLine.Y - currentLine.Y;
            var fontSize = Math.Max(prevLine.FontSize, currentLine.FontSize);
            var isNewParagraph = fontSize > 0 && spacing > fontSize * typicalLineSpacing * ParagraphSpacingFactor;

            // Also break paragraph if font size changes significantly
            var fontSizeChanged = Math.Abs(prevLine.FontSize - currentLine.FontSize) > 1.0;

            // ENDRET (ny): nytt avsnitt også når linjen går fra fet til normal skrift eller omvendt
            // (overskrift rett over tekst i samme størrelse), ved listepunkter, og rundt tabellrader.
            var boldChanged = Math.Abs(prevLine.BoldRatio - currentLine.BoldRatio) > 0.5;
            var startsListItem = ListItemStart.IsMatch(currentLine.Text);
            var isTabular = prevLine.HasColumnGap || currentLine.HasColumnGap;

            if (isNewParagraph || fontSizeChanged || boldChanged || startsListItem || isTabular)
            {
                paragraphs.Add(CreateParagraph(currentLines));
                currentLines = [currentLine];
            }
            else
            {
                currentLines.Add(currentLine);
            }
        }

        if (currentLines.Count > 0)
            paragraphs.Add(CreateParagraph(currentLines));

        return paragraphs;
    }

    /// <summary>
    /// ENDRET (ny): Finner vanligste linjeavstand på siden, som andel av fontstørrelsen.
    /// Bare avstander under 1,9 x fontstørrelsen regnes som linjeavstand (større er avsnittsskift).
    /// Faller tilbake til 1,2 (vanlig enkel linjeavstand) når siden har for få linjer til å avgjøre.
    /// </summary>
    private static double DetectTypicalLineSpacingRatio(List<TextLine> lines)
    {
        var ratios = new List<double>();
        for (var i = 1; i < lines.Count; i++)
        {
            var fontSize = Math.Max(lines[i - 1].FontSize, lines[i].FontSize);
            if (fontSize <= 0 || Math.Abs(lines[i - 1].FontSize - lines[i].FontSize) > 1.0)
                continue;

            var ratio = (lines[i - 1].Y - lines[i].Y) / fontSize;
            if (ratio > 0.8 && ratio < 1.9)
                ratios.Add(Math.Round(ratio, 1));
        }

        return ratios.Count < 2
            ? 1.2
            : ratios.GroupBy(r => r).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
    }

    private static ParagraphInfo CreateParagraph(List<TextLine> lines)
    {
        // ENDRET: slå sammen linjer med håndtering av bindestrek på slutten av linjen.
        var builder = new StringBuilder();
        foreach (var line in lines)
            AppendLine(builder, line.Text);
        var text = builder.ToString();
        var avgFontSize = lines.Average(l => l.FontSize);
        var avgBoldRatio = lines.Average(l => l.BoldRatio);

        return new ParagraphInfo(text, avgFontSize, avgBoldRatio, lines.Count);
    }

    // ENDRET (ny): punkt-, tall- og bokstavlister.
    private static readonly System.Text.RegularExpressions.Regex ListItemStart =
        new(@"^([•●▪◦‣·*–-]|\d{1,3}[.)]|[a-z]\))\s", System.Text.RegularExpressions.RegexOptions.Compiled);

    // ENDRET (ny): ord etter bindestrek som betyr at streken skal stå ("kultur- og idrett").
    private static readonly HashSet<string> WordsKeepingHyphen =
        new(StringComparer.OrdinalIgnoreCase) { "og", "eller", "samt", "til", "and", "or", "to" };

    /// <summary>
    /// ENDRET (ny): "avde-" + "lingen" blir "avdelingen", "IT-" + "avdelingen" blir "IT-avdelingen",
    /// "kultur-" + "og idrett" blir "kultur- og idrett". Ellers skilles linjer med mellomrom.
    /// </summary>
    private static void AppendLine(StringBuilder sb, string line)
    {
        if (sb.Length == 0 || line.Length == 0)
        {
            sb.Append(line);
            return;
        }

        var endsWithHyphen = sb[^1] == '-' && sb.Length >= 2 && char.IsLetter(sb[^2]);
        var firstWord = line.Split(' ', 2)[0];

        if (endsWithHyphen && char.IsLetter(line[0]) && !WordsKeepingHyphen.Contains(firstWord))
        {
            if (char.IsLower(sb[^2]) && char.IsLower(line[0]))
                sb.Length--; // orddeling: fjern streken
            sb.Append(line);
        }
        else
        {
            sb.Append(' ').Append(line);
        }
    }

    private static Dictionary<double, int> BuildHeadingLevelMap(List<ParagraphInfo> paragraphs, double bodyFontSize)
    {
        if (bodyFontSize <= 0 || paragraphs.Count == 0)
            return [];

        var roundedBodyFontSize = Math.Round(bodyFontSize, 1);
        var headingSizes = paragraphs
            .Where(p => IsHeadingCandidate(p, bodyFontSize))
            .Select(p => Math.Round(p.FontSize, 1))
            .Where(size => size > roundedBodyFontSize)
            .Distinct()
            .OrderByDescending(size => size)
            .Take(MaxHeadingLevel)
            .ToList();

        if (headingSizes.Count == 0)
            return [];

        return headingSizes
            .Select((size, index) => new { Size = size, Level = index + 1 })
            .ToDictionary(x => x.Size, x => x.Level);
    }

    private static bool IsHeadingCandidate(ParagraphInfo paragraph, double bodyFontSize)
    {
        if (paragraph.LineCount > 3 || bodyFontSize <= 0 || paragraph.FontSize <= 0)
            return false;

        var isLargeBySize = paragraph.FontSize >= bodyFontSize * HeadingMinimumSizeRatio;
        var isBoldAndLarger = paragraph.BoldRatio >= 0.6 &&
                              paragraph.FontSize >= bodyFontSize * HeadingMinimumBoldSizeRatio;

        return isLargeBySize || isBoldAndLarger;
    }

    private static bool IsBoldFontName(string? fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
            return false;

        var normalized = fontName.ToLowerInvariant();
        return normalized.Contains("bold", StringComparison.Ordinal) ||
               normalized.Contains("black", StringComparison.Ordinal) ||
               normalized.Contains("heavy", StringComparison.Ordinal) ||
               normalized.Contains("semibold", StringComparison.Ordinal) ||
               normalized.Contains("demi", StringComparison.Ordinal);
    }

    private sealed record TextLine(string Text, double Y, double Height, double FontSize, double BoldRatio, bool HasColumnGap);
    private sealed record ParagraphInfo(string Text, double FontSize, double BoldRatio, int LineCount);
}
