using kisatsingen.Services.WordDocuments;
using Xunit;

namespace kisatsingen.Tests.Services.WordDocuments;

public sealed class WordDocumentSpecValidatorTests
{
    private static readonly DocumentBlockSpec Paragraph = new(DocumentBlockKind.Paragraph, Text: "Brødtekst.");

    private static WordDocumentSpec Spec(params DocumentBlockSpec[] blocks) => new("Tittel", WordDocumentTemplate.Simple, blocks);

    private static WordDocumentSpec Report(params DocumentBlockSpec[] blocks) => new("Tittel", WordDocumentTemplate.Report, blocks);

    [Fact]
    public void A_spec_using_every_kinds_own_fields_has_no_problems()
    {
        var spec = Spec(
            new(DocumentBlockKind.Kicker, Text: "23.11.2026"),
            new(DocumentBlockKind.Heading1, Text: "Hovedtittel"),
            new(DocumentBlockKind.Lead, Text: "Ingress."),
            new(DocumentBlockKind.Heading2, Text: "Kapittel"),
            new(DocumentBlockKind.Heading3, Text: "Underkapittel"),
            Paragraph,
            new(DocumentBlockKind.BulletList, Items: ["En", "To"]),
            new(DocumentBlockKind.NumberedList, Items: ["Først", "Så"]),
            new(DocumentBlockKind.Table, HeaderRow: ["A", "B"], Rows: [["a", ""]]),
            new(DocumentBlockKind.Caption, Text: "Tabell 1"),
            new(DocumentBlockKind.PageBreak));

        Assert.Empty(WordDocumentSpecValidator.Validate(spec));
    }

    [Fact]
    public void A_table_without_a_header_row_takes_its_column_count_from_the_first_row()
    {
        var spec = Spec(new DocumentBlockSpec(DocumentBlockKind.Table, Rows: [["a", "b"], ["c", "d"]]));

        Assert.Empty(WordDocumentSpecValidator.Validate(spec));
    }

    [Fact]
    public void A_field_the_kind_does_not_take_is_refused_naming_the_fields_it_does()
    {
        var problems = WordDocumentSpecValidator.Validate(Spec(new DocumentBlockSpec(DocumentBlockKind.PageBreak, Text: "Ny side")));

        Assert.Equal(["Blokk 1 er av typen PageBreak, som ikke har feltet text. Feltene PageBreak tar: ingen (bare kind)."], problems);
    }

    [Fact]
    public void A_text_kind_without_text_is_refused()
    {
        var problems = WordDocumentSpecValidator.Validate(Spec(new DocumentBlockSpec(DocumentBlockKind.Heading2)));

        Assert.Contains(problems, problem => problem.StartsWith("Blokk 1: text er tom", StringComparison.Ordinal));
    }

    [Fact]
    public void More_than_one_main_heading_is_refused_pointing_to_section_headings()
    {
        var problems = WordDocumentSpecValidator.Validate(Spec(
            new(DocumentBlockKind.Heading1, Text: "En"),
            new(DocumentBlockKind.Heading1, Text: "To")));

        Assert.Contains(problems, problem => problem.Contains("Heading2", StringComparison.Ordinal));
    }

    [Fact]
    public void A_table_row_with_another_cell_count_than_the_header_row_is_refused()
    {
        var problems = WordDocumentSpecValidator.Validate(Spec(new DocumentBlockSpec(DocumentBlockKind.Table, HeaderRow: ["A", "B"], Rows: [["a", "b"], ["c"]])));

        Assert.Equal(["Blokk 1: rad 2 har 1 celler; alle rader må ha 2. Fyll tomme celler med en tom tekst (\"\")."], problems);
    }

    [Fact]
    public void A_table_wider_than_the_page_allows_is_refused()
    {
        var header = Enumerable.Range(1, WordDocumentSpecValidator.MaxTableColumns + 1).Select(i => $"K{i}").ToList();
        var problems = WordDocumentSpecValidator.Validate(Spec(new DocumentBlockSpec(DocumentBlockKind.Table, HeaderRow: header, Rows: [header])));

        Assert.Contains(problems, problem => problem.Contains("kolonner", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_list_is_refused()
    {
        var problems = WordDocumentSpecValidator.Validate(Spec(new DocumentBlockSpec(DocumentBlockKind.BulletList, Items: [])));

        Assert.Equal(["Blokk 1: items har 0 punkter; den må ha fra 1 til 30. Del punktene på flere lister."], problems);
    }

    [Fact]
    public void Text_over_its_kinds_limit_is_refused()
    {
        var problems = WordDocumentSpecValidator.Validate(Spec(new DocumentBlockSpec(DocumentBlockKind.Heading2, Text: new string('x', WordDocumentSpecValidator.MaxShortTextLength + 1))));

        Assert.Single(problems);
    }

    [Fact]
    public void Control_characters_a_word_file_cannot_hold_are_refused()
    {
        var problems = WordDocumentSpecValidator.Validate(Spec(new DocumentBlockSpec(DocumentBlockKind.Paragraph, Text: "Før\u0001etter")));

        Assert.Equal(["Blokk 1: text inneholder kontrolltegn som ikke kan stå i en Word-fil. Fjern dem."], problems);
    }

    [Fact]
    public void A_document_without_blocks_is_refused()
    {
        var problems = WordDocumentSpecValidator.Validate(Spec());

        Assert.Single(problems);
    }

    [Fact]
    public void Every_problem_is_named_at_once()
    {
        var problems = WordDocumentSpecValidator.Validate(new WordDocumentSpec("", WordDocumentTemplate.Simple, [new(DocumentBlockKind.Heading2), new(DocumentBlockKind.BulletList, Items: [])]));

        Assert.Equal(3, problems.Count);
    }

    [Fact]
    public void The_structure_check_accepts_a_spec_that_is_only_over_the_policy_limits()
    {
        var spec = Spec([
            new(DocumentBlockKind.Heading1, Text: "En"),
            new(DocumentBlockKind.Heading1, Text: "To"),
            .. Enumerable.Repeat(new DocumentBlockSpec(DocumentBlockKind.Paragraph, Text: new string('x', WordDocumentSpecValidator.MaxTextLength + 1)), WordDocumentSpecValidator.MaxBlocks)]);

        Assert.Empty(WordDocumentSpecValidator.ValidateStructure(spec));
    }

    [Fact]
    public void Missing_blocks_are_named_by_position()
    {
        var problems = WordDocumentSpecValidator.FindMissingBlocks([Paragraph, null]);

        Assert.Equal(["Blokk 2 er tom (null). Gi blokken en type, eller fjern den."], problems);
    }

    [Fact]
    public void A_report_using_its_own_kinds_and_cover_has_no_problems()
    {
        var blocks = Report(
            new(DocumentBlockKind.Heading2, Text: "Kapittel"),
            new(DocumentBlockKind.Lead, Text: "Ingress."),
            new(DocumentBlockKind.Heading3, Text: "Del"),
            new(DocumentBlockKind.Heading4, Text: "Underdel"),
            new(DocumentBlockKind.Heading5, Text: "Avsnitt"),
            new(DocumentBlockKind.FactBox, Text: "Fakta."),
            Paragraph);
        var spec = blocks with { Subtitle = "Undertittel", Date = "23.11.2026", CoverText = "Utarbeidet av økonomi." };

        Assert.Empty(WordDocumentSpecValidator.Validate(spec));
    }

    [Theory]
    [InlineData(DocumentBlockKind.Heading1)]
    [InlineData(DocumentBlockKind.Kicker)]
    public void A_report_refuses_the_kinds_its_cover_takes_the_place_of(DocumentBlockKind kind)
    {
        var problems = WordDocumentSpecValidator.Validate(Report(new DocumentBlockSpec(kind, Text: "Tekst")));

        Assert.Equal([$"Blokk 1 har typen {kind}, som malen Report ikke har. Typene Report har: Heading2, Heading3, Heading4, Heading5, Lead, Paragraph, BulletList, NumberedList, Table, Caption, FactBox, PageBreak."], problems);
    }

    [Theory]
    [InlineData(DocumentBlockKind.Heading4)]
    [InlineData(DocumentBlockKind.FactBox)]
    public void A_simple_document_refuses_the_reports_own_kinds(DocumentBlockKind kind)
    {
        var problems = WordDocumentSpecValidator.Validate(Spec(new DocumentBlockSpec(kind, Text: "Tekst")));

        Assert.Single(problems);
    }

    [Fact]
    public void A_simple_document_refuses_the_report_cover_fields_pointing_to_the_kicker()
    {
        var problems = WordDocumentSpecValidator.Validate(Spec(Paragraph) with { Date = "23.11.2026" });

        Assert.Equal(["Feltet date hører til rapportens forside og kan bare brukes med malen Report. Bruk en Kicker-blokk for dato, eller velg malen Report."], problems);
    }

    [Fact]
    public void An_unknown_template_is_refused_naming_the_templates()
    {
        var problems = WordDocumentSpecValidator.Validate(Spec(Paragraph) with { Template = (WordDocumentTemplate)9 });

        Assert.Contains("Malen 9 finnes ikke. Velg en av: Simple, Report.", problems);
    }
}
