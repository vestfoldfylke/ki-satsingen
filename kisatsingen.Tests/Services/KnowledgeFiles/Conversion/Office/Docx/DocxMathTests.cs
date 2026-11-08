using Xunit;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.ConvertDocx;
using static kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx.TestDocx;

namespace kisatsingen.Tests.Services.KnowledgeFiles.Conversion.Office.Docx;

// Word's equations (OMML) are written as LaTeX, between $ in the text and $$ on a line of their own.
public sealed class DocxMathTests
{
    private const string MathNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    private static string InlineMath(string omml) => $"<m:oMath xmlns:m=\"{MathNamespace}\">{omml}</m:oMath>";

    private static string MathRun(string text) => $"<m:r><m:t>{text}</m:t></m:r>";

    private static string LatexOf(string omml) => ToMarkdown(WithBody($"<w:p>{InlineMath(omml)}</w:p>")).Trim('$');

    public static TheoryData<string, string> Structures() => new()
    {
        { $"<m:f><m:num>{MathRun("a")}</m:num><m:den>{MathRun("b")}</m:den></m:f>", @"\frac{a}{b}" },
        { $"<m:sSub><m:e>{MathRun("x")}</m:e><m:sub>{MathRun("i")}</m:sub></m:sSub>", "x_{i}" },
        { $"<m:sSubSup><m:e>{MathRun("x")}</m:e><m:sub>{MathRun("i")}</m:sub><m:sup>{MathRun("2")}</m:sup></m:sSubSup>", "x_{i}^{2}" },
        { $"<m:rad><m:radPr><m:degHide m:val=\"1\"/></m:radPr><m:deg/><m:e>{MathRun("x")}</m:e></m:rad>", @"\sqrt{x}" },
        { $"<m:rad><m:deg>{MathRun("3")}</m:deg><m:e>{MathRun("x")}</m:e></m:rad>", @"\sqrt[3]{x}" },
        { $"<m:nary><m:naryPr><m:chr m:val=\"∑\"/></m:naryPr><m:sub>{MathRun("i=1")}</m:sub><m:sup>{MathRun("n")}</m:sup><m:e>{MathRun("i")}</m:e></m:nary>", @"\sum_{i=1}^{n} i" },
        // Word leaves the character out of an integral.
        { $"<m:nary><m:sub>{MathRun("0")}</m:sub><m:sup>{MathRun("1")}</m:sup><m:e>{MathRun("f")}</m:e></m:nary>", @"\int_{0}^{1} f" },
        { $"<m:nary><m:e>{MathRun("f")}</m:e></m:nary>", @"\int f" },
        { $"<m:d><m:e>{MathRun("a+b")}</m:e></m:d>", "(a+b)" },
        { $"<m:d><m:dPr><m:begChr m:val=\"[\"/><m:endChr m:val=\"]\"/></m:dPr><m:e>{MathRun("a")}</m:e><m:e>{MathRun("b")}</m:e></m:d>", "[a|b]" },
        { $"<m:func><m:fName>{MathRun("sin")}</m:fName><m:e>{MathRun("x")}</m:e></m:func>", @"\sin x" },
        { $"<m:func><m:fName><m:limLow><m:e>{MathRun("lim")}</m:e><m:lim>{MathRun("n→∞")}</m:lim></m:limLow></m:fName><m:e>{MathRun("a")}</m:e></m:func>", @"\lim_{n→∞} a" },
        { $"<m:acc><m:e>{MathRun("x")}</m:e></m:acc>", @"\hat{x}" },
        { $"<m:bar><m:barPr><m:pos m:val=\"top\"/></m:barPr><m:e>{MathRun("x")}</m:e></m:bar>", @"\overline{x}" },
        { $"<m:bar><m:e>{MathRun("x")}</m:e></m:bar>", @"\underline{x}" },
        { $"<m:groupChr><m:e>{MathRun("a+b")}</m:e></m:groupChr>", @"\underbrace{a+b}" },
        { $"<m:limUpp><m:e>{MathRun("x")}</m:e><m:lim>{MathRun("n")}</m:lim></m:limUpp>", @"\overset{n}{x}" },
        { $"<m:sPre><m:sub>{MathRun("1")}</m:sub><m:sup>{MathRun("2")}</m:sup><m:e>{MathRun("X")}</m:e></m:sPre>", "{}_{1}^{2}X" },
        { $"<m:eqArr><m:e>{MathRun("a=1")}</m:e><m:e>{MathRun("b=2")}</m:e></m:eqArr>", @"\begin{array}{l} a=1 \\ b=2 \end{array}" },
        { $"<m:m><m:mr><m:e>{MathRun("1")}</m:e><m:e>{MathRun("0")}</m:e></m:mr><m:mr><m:e>{MathRun("0")}</m:e><m:e>{MathRun("1")}</m:e></m:mr></m:m>", @"\begin{matrix} 1 & 0 \\ 0 & 1 \end{matrix}" }
    };

    [Fact]
    public void An_equation_in_the_text_is_written_as_latex_between_dollars()
    {
        var equation = InlineMath($"{MathRun("E=m")}<m:sSup><m:e>{MathRun("c")}</m:e><m:sup>{MathRun("2")}</m:sup></m:sSup>");
        var docx = WithBody($"<w:p>{Run("I linje: ")}{equation}{Run(".")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("I linje: $E=mc^{2}$.", markdown);
    }

    [Fact]
    public void An_equation_paragraph_is_written_between_double_dollars()
    {
        var docx = WithBody($"<w:p><m:oMathPara xmlns:m=\"{MathNamespace}\"><m:oMath>{MathRun("x=1")}</m:oMath></m:oMathPara></w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("$$x=1$$", markdown);
    }

    [Theory]
    [MemberData(nameof(Structures))]
    public void An_equation_s_structure_is_written_as_its_latex(string omml, string expected)
    {
        var latex = LatexOf(omml);

        Assert.Equal(expected, latex);
    }

    // c^{2} after ab would raise only the b.
    [Fact]
    public void A_raised_base_of_more_than_one_character_is_grouped()
    {
        var latex = LatexOf($"<m:sSup><m:e>{MathRun("ab")}</m:e><m:sup>{MathRun("2")}</m:sup></m:sSup>");

        Assert.Equal("{ab}^{2}", latex);
    }

    [Theory]
    [InlineData("{a}", @"\{a\}")]
    [InlineData("50%", @"50\%")]
    [InlineData("a_b", @"a\_b")]
    [InlineData(@"a\b", @"a\backslash b")]
    public void Characters_with_a_meaning_in_latex_are_escaped(string text, string expected)
    {
        var latex = LatexOf(MathRun(text));

        Assert.Equal(expected, latex);
    }

    [Fact]
    public void Equations_in_one_equation_paragraph_are_separate_lines()
    {
        var docx = WithBody($"<w:p><m:oMathPara xmlns:m=\"{MathNamespace}\"><m:oMath>{MathRun("a")}</m:oMath><m:oMath>{MathRun("b")}</m:oMath></m:oMathPara></w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal(@"$$a \\ b$$", markdown);
    }

    // The schema lets a math run hold Word's own text element.
    [Fact]
    public void Word_text_in_a_math_run_is_kept()
    {
        var latex = LatexOf("<m:r><w:t>x</w:t></m:r>");

        Assert.Equal("x", latex);
    }

    [Fact]
    public void Text_deleted_with_tracked_changes_is_left_out_of_an_equation()
    {
        var latex = LatexOf($"{MathRun("a")}<w:del w:id=\"1\" w:author=\"A\">{MathRun("b")}</w:del>");

        Assert.Equal("a", latex);
    }

    [Fact]
    public void A_structure_without_latex_of_its_own_keeps_its_text()
    {
        var latex = LatexOf($"<m:box><m:e>{MathRun("x")}</m:e></m:box>");

        Assert.Equal("x", latex);
    }

    [Fact]
    public void An_empty_equation_is_left_out()
    {
        var docx = WithBody($"<w:p>{Run("Før")}{InlineMath("")}{Run(" etter")}</w:p>");

        var markdown = ToMarkdown(docx);

        Assert.Equal("Før etter", markdown);
    }

    // Without it, the text-length warning would report every equation as lost text.
    [Fact]
    public void An_equation_s_text_counts_as_written_and_as_the_document_s_text()
    {
        var docx = WithBody($"<w:p>{InlineMath($"<m:f><m:num>{MathRun("a")}</m:num><m:den>{MathRun("b")}</m:den></m:f>")}</w:p>");

        var result = Convert(docx);

        Assert.Equal((2, 2), (result.WrittenTextCharacters, result.SourceTextCharacters));
    }
}
