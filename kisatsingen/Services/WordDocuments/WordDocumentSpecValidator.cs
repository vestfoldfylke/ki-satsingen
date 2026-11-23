using kisatsingen.Services.GeneratedFiles;

namespace kisatsingen.Services.WordDocuments;

// Checks a spec before anything is built, and names every problem at once so
// the model can correct its call in one retry. Empty when the spec is valid.
// The size limits and the single main heading are policy for new calls; the
// structure is what the builder needs, and is all that a stored call is
// checked against.
internal static class WordDocumentSpecValidator
{
    public const int MaxBlocks = 300;
    public const int MaxDocumentTitleLength = 200;
    public const int MaxShortTextLength = 300;
    public const int MaxTextLength = 4000;
    public const int MaxCoverTextLength = 400;
    public const int MaxItemsPerList = 30;
    public const int MaxItemLength = 1000;
    public const int MaxTableColumns = 8;
    public const int MaxTableRows = 60;
    public const int MaxCellLength = 500;
    public const int MaxMainHeadings = 1;

    private const string TextField = "text";
    private const string SubtitleField = "subtitle";
    private const string DateField = "date";
    private const string CoverTextField = "coverText";
    private const string ItemsField = "items";
    private const string HeaderRowField = "headerRow";
    private const string RowsField = "rows";

    public static IReadOnlyList<string> Validate(WordDocumentSpec spec) => Check(spec, Limits.Enforced);

    public static IReadOnlyList<string> ValidateStructure(WordDocumentSpec spec) => Check(spec, Limits.None);

    // The model's JSON can hold a null block although WordDocumentSpec cannot,
    // so this is checked on the arguments, before a spec is made of them.
    public static IReadOnlyList<string> FindMissingBlocks(IReadOnlyList<DocumentBlockSpec?> blocks) =>
    [
        .. blocks
            .Select((block, index) => (block, index))
            .Where(entry => entry.block is null)
            .Select(entry => $"Blokk {entry.index + 1} er tom (null). Gi blokken en type, eller fjern den.")
    ];

    private static List<string> Check(WordDocumentSpec spec, Limits limits)
    {
        var problems = new List<string>();

        CheckText(problems, "Dokumentets tittel", spec.Title, limits.DocumentTitleLength, isRequired: true);

        if (!Enum.IsDefined(spec.Template))
        {
            problems.Add($"Malen {spec.Template} finnes ikke. Velg en av: {string.Join(", ", Enum.GetNames<WordDocumentTemplate>())}.");
            return problems;
        }

        CheckCover(problems, spec, limits);

        if (spec.Blocks.Count == 0 || spec.Blocks.Count > limits.Blocks)
        {
            problems.Add($"Dokumentet har {spec.Blocks.Count} blokker; det må ha fra 1 til {MaxBlocks}. Del innholdet på flere dokumenter, eller slå sammen avsnitt.");
        }

        var mainHeadingCount = spec.Blocks.Count(block => block.Kind == DocumentBlockKind.Heading1);
        if (mainHeadingCount > limits.MainHeadings)
        {
            problems.Add($"Dokumentet har {mainHeadingCount} blokker av typen {DocumentBlockKind.Heading1}; det kan ha {MaxMainHeadings}. Bruk {DocumentBlockKind.Heading2} for kapitler.");
        }

        for (var i = 0; i < spec.Blocks.Count; i++)
        {
            CheckBlock(problems, $"Blokk {i + 1}", spec.Blocks[i], spec.Template, limits);
        }

        return problems;
    }

    // Only the report has a cover; the simple template's date is a Kicker block.
    private static void CheckCover(List<string> problems, WordDocumentSpec spec, Limits limits)
    {
        if (spec.Template == WordDocumentTemplate.Report)
        {
            CheckOptionalText(problems, $"Forsidens {SubtitleField}", spec.Subtitle, limits.ShortTextLength);
            CheckOptionalText(problems, $"Forsidens {DateField}", spec.Date, limits.ShortTextLength);
            CheckOptionalText(problems, $"Forsidens {CoverTextField}", spec.CoverText, limits.CoverTextLength);
            return;
        }

        string?[] coverFields =
        [
            spec.Subtitle is null ? null : SubtitleField,
            spec.Date is null ? null : DateField,
            spec.CoverText is null ? null : CoverTextField
        ];
        foreach (var field in coverFields.OfType<string>())
        {
            problems.Add($"Feltet {field} hører til rapportens forside og kan bare brukes med malen {WordDocumentTemplate.Report}. Bruk en {DocumentBlockKind.Kicker}-blokk for dato, eller velg malen {WordDocumentTemplate.Report}.");
        }
    }

    private static void CheckBlock(List<string> problems, string blockName, DocumentBlockSpec block, WordDocumentTemplate template, Limits limits)
    {
        if (block.Kind is not { } kind)
        {
            problems.Add($"{blockName} mangler typen (kind). Velg en av: {string.Join(", ", KindsIn(template))}.");
            return;
        }

        if (!Enum.IsDefined(kind))
        {
            problems.Add($"{blockName} har typen {kind}, som ikke finnes. Velg en av: {string.Join(", ", Enum.GetNames<DocumentBlockKind>())}.");
            return;
        }

        if (!DocumentBlockFields.IsAvailableIn(kind, template))
        {
            problems.Add($"{blockName} har typen {kind}, som malen {template} ikke har. Typene {template} har: {string.Join(", ", KindsIn(template))}.");
            return;
        }

        var fields = DocumentBlockFields.For(kind);
        var allowed = AllowedFields(fields);

        CheckAllowed(problems, blockName, kind, TextField, block.Text is not null, fields.HasText, allowed);
        CheckAllowed(problems, blockName, kind, ItemsField, block.Items is not null, fields.HasItems, allowed);
        CheckAllowed(problems, blockName, kind, HeaderRowField, block.HeaderRow is not null, fields.HasTable, allowed);
        CheckAllowed(problems, blockName, kind, RowsField, block.Rows is not null, fields.HasTable, allowed);

        if (fields.HasText)
        {
            var textLength = DocumentBlockFields.IsShortText(kind) ? limits.ShortTextLength : limits.TextLength;
            CheckText(problems, $"{blockName}: {TextField}", block.Text, textLength, isRequired: true);
        }

        if (fields.HasItems)
        {
            CheckList(problems, $"{blockName}: {ItemsField}", block.Items, limits);
        }

        if (fields.HasTable)
        {
            CheckTable(problems, blockName, block, limits);
        }
    }

    private static IEnumerable<DocumentBlockKind> KindsIn(WordDocumentTemplate template) =>
        Enum.GetValues<DocumentBlockKind>().Where(kind => DocumentBlockFields.IsAvailableIn(kind, template));

    private static void CheckOptionalText(List<string> problems, string name, string? text, int maxLength)
    {
        if (text is not null)
        {
            CheckText(problems, name, text, maxLength, isRequired: true);
        }
    }

    private static void CheckAllowed(List<string> problems, string blockName, DocumentBlockKind kind, string field, bool isGiven, bool isAllowed, string allowed)
    {
        if (isGiven && !isAllowed)
        {
            problems.Add($"{blockName} er av typen {kind}, som ikke har feltet {field}. Feltene {kind} tar: {allowed}.");
        }
    }

    private static string AllowedFields(DocumentBlockFields fields)
    {
        string?[] names =
        [
            fields.HasText ? TextField : null,
            fields.HasItems ? ItemsField : null,
            fields.HasTable ? HeaderRowField : null,
            fields.HasTable ? RowsField : null
        ];
        var given = names.OfType<string>().ToList();
        return given.Count == 0 ? "ingen (bare kind)" : string.Join(", ", given);
    }

    private static void CheckList(List<string> problems, string name, IReadOnlyList<string>? items, Limits limits)
    {
        items ??= [];
        if (items.Count == 0 || items.Count > limits.ItemsPerList)
        {
            problems.Add($"{name} har {items.Count} punkter; den må ha fra 1 til {MaxItemsPerList}. Del punktene på flere lister.");
        }

        for (var i = 0; i < items.Count; i++)
        {
            CheckText(problems, $"{name}, punkt {i + 1}", items[i], limits.ItemLength, isRequired: true);
        }
    }

    // Every row must have the column count, or the table's grid does not hold;
    // that is structure, checked on stored calls too.
    private static void CheckTable(List<string> problems, string blockName, DocumentBlockSpec block, Limits limits)
    {
        var rows = block.Rows ?? [];
        if (rows.Count == 0 || rows.Count > limits.TableRows)
        {
            problems.Add($"{blockName}: {RowsField} har {rows.Count} rader; tabellen må ha fra 1 til {MaxTableRows}. Del tabellen i flere.");
        }

        var columnCount = block.HeaderRow?.Count ?? (rows.Count > 0 ? rows[0]?.Count ?? 0 : 0);
        if (columnCount == 0 || columnCount > limits.TableColumns)
        {
            problems.Add($"{blockName}: tabellen har {columnCount} kolonner; den må ha fra 1 til {MaxTableColumns}. Del tabellen i flere.");
        }

        if (block.HeaderRow is { } headerRow)
        {
            CheckRow(problems, $"{blockName}: {HeaderRowField}", headerRow, columnCount, limits);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            CheckRow(problems, $"{blockName}: rad {i + 1}", rows[i], columnCount, limits);
        }
    }

    private static void CheckRow(List<string> problems, string rowName, IReadOnlyList<string>? row, int columnCount, Limits limits)
    {
        if (row is null || row.Count != columnCount)
        {
            problems.Add($"{rowName} har {row?.Count ?? 0} celler; alle rader må ha {columnCount}. Fyll tomme celler med en tom tekst (\"\").");
            return;
        }

        for (var i = 0; i < row.Count; i++)
        {
            CheckText(problems, $"{rowName}, celle {i + 1}", row[i], limits.CellLength, isRequired: false);
        }
    }

    // A cell may be empty, but not null: an empty string is how a model fills
    // a blank cell, and null is an argument it lost.
    private static void CheckText(List<string> problems, string name, string? text, int maxLength, bool isRequired)
    {
        if (text is null || (isRequired && string.IsNullOrWhiteSpace(text)))
        {
            problems.Add($"{name} er tom. Skriv inn tekst.");
            return;
        }

        if (text.Length > maxLength)
        {
            problems.Add($"{name} har {text.Length} tegn; maks er {maxLength}. Kort ned teksten, eller del den på flere blokker.");
        }

        if (!XmlText.IsValid(text))
        {
            problems.Add($"{name} inneholder kontrolltegn som ikke kan stå i en Word-fil. Fjern dem.");
        }
    }

    private sealed record Limits(
        int Blocks,
        int DocumentTitleLength,
        int ShortTextLength,
        int TextLength,
        int CoverTextLength,
        int ItemsPerList,
        int ItemLength,
        int TableColumns,
        int TableRows,
        int CellLength,
        int MainHeadings)
    {
        public static readonly Limits Enforced = new(
            MaxBlocks, MaxDocumentTitleLength, MaxShortTextLength, MaxTextLength, MaxCoverTextLength, MaxItemsPerList, MaxItemLength,
            MaxTableColumns, MaxTableRows, MaxCellLength, MaxMainHeadings);

        public static readonly Limits None = new(
            int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue,
            int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue);
    }
}
