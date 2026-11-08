using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Math;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office.Docx;

// Writes Word's equations (OMML) as LaTeX, which a reader of Markdown, and
// the model, know: "E=mc^{2}", "\frac{a}{b}". Symbols such as ± and ∞ are
// kept as the characters they are; LaTeX renderers and the model read them.
//
// A structure it doesn't know is written as the text inside it, so no text is
// lost. Recursive, which is safe: CheckedParts refuses a part nested more
// than 256 deep. ct is checked per element, since an equation can be large.
internal sealed class DocxMath(DocxTextTally tally, CancellationToken ct)
{
    private const string DefaultIntegral = "∫";
    private const string DefaultAccent = "̂";
    private const string DefaultOpeningDelimiter = "(";
    private const string DefaultClosingDelimiter = ")";
    private const string DefaultDelimiterSeparator = "|";
    private const string TopPosition = "top";

    // Characters with a meaning of their own in LaTeX.
    private const string LatexSpecialCharacters = "{}_^$#%&";

    private static readonly Dictionary<string, string> NaryOperators = new()
    {
        ["∑"] = @"\sum",
        ["∏"] = @"\prod",
        ["∐"] = @"\coprod",
        ["∫"] = @"\int",
        ["∬"] = @"\iint",
        ["∭"] = @"\iiint",
        ["∮"] = @"\oint",
        ["⋃"] = @"\bigcup",
        ["⋂"] = @"\bigcap"
    };

    private static readonly Dictionary<string, string> Accents = new()
    {
        ["̂"] = @"\hat",
        ["̃"] = @"\tilde",
        ["̄"] = @"\bar",
        ["̅"] = @"\bar",
        ["̇"] = @"\dot",
        ["̈"] = @"\ddot",
        ["⃗"] = @"\vec"
    };

    // Function names LaTeX writes upright with a command of their own.
    private static readonly HashSet<string> NamedFunctions =
    [
        "sin", "cos", "tan", "cot", "sec", "csc", "arcsin", "arccos", "arctan",
        "sinh", "cosh", "tanh", "log", "ln", "lg", "exp", "lim", "max", "min",
        "sup", "inf", "det", "gcd", "deg", "dim", "arg"
    ];

    // An oMathPara is shown on a line of its own, an oMath in the text.
    public string Display(Paragraph mathParagraph) =>
        string.Join(@" \\ ", mathParagraph.Elements<OfficeMath>().Select(Latex).Where(latex => latex.Length > 0));

    public string Latex(OpenXmlElement? element)
    {
        ct.ThrowIfCancellationRequested();

        return element switch
        {
            null => "",
            // Tracked changes inside an equation are read as accepted, as in the text.
            _ when DocxSkippedText.IsSkipped(element) => "",
            Text text => TextOf(text.Text),
            W.Text text => TextOf(text.Text),
            Fraction fraction => $@"\frac{{{Latex(fraction.Numerator)}}}{{{Latex(fraction.Denominator)}}}",
            Superscript superscript => $"{Group(Latex(superscript.Base))}^{{{Latex(superscript.SuperArgument)}}}",
            Subscript subscript => $"{Group(Latex(subscript.Base))}_{{{Latex(subscript.SubArgument)}}}",
            SubSuperscript both => $"{Group(Latex(both.Base))}_{{{Latex(both.SubArgument)}}}^{{{Latex(both.SuperArgument)}}}",
            PreSubSuper pre => $"{{}}_{{{Latex(pre.SubArgument)}}}^{{{Latex(pre.SuperArgument)}}}{Latex(pre.Base)}",
            Radical radical => RadicalOf(radical),
            Nary nary => NaryOf(nary),
            Delimiter delimiter => DelimiterOf(delimiter),
            MathFunction function => FunctionOf(function),
            Accent accent => AccentOf(accent),
            Bar bar => $@"{(bar.BarProperties?.Position?.Val?.InnerText == TopPosition ? @"\overline" : @"\underline")}{{{Latex(bar.Base)}}}",
            GroupChar group => $@"{(group.GroupCharProperties?.Position?.Val?.InnerText == TopPosition ? @"\overbrace" : @"\underbrace")}{{{Latex(group.Base)}}}",
            LimitLower limit => LimitOf(Latex(limit.Base), Latex(limit.Limit), "_", @"\underset"),
            LimitUpper limit => LimitOf(Latex(limit.Base), Latex(limit.Limit), "^", @"\overset"),
            Matrix matrix => Rows(@"\begin{matrix}", matrix.Elements<MatrixRow>().Select(row => string.Join(" & ", row.Elements<Base>().Select(Latex))), @"\end{matrix}"),
            EquationArray equations => Rows(@"\begin{array}{l}", equations.Elements<Base>().Select(Latex), @"\end{array}"),
            _ => ChildrenOf(element)
        };
    }

    private string ChildrenOf(OpenXmlElement element)
    {
        var latex = new StringBuilder();
        foreach (var child in element.ChildElements)
        {
            latex.Append(Latex(child));
        }

        return latex.ToString();
    }

    // Counted as written text, since DocxTextCount counts it as the document's.
    private string TextOf(string text)
    {
        tally.Add(text);
        return Escape(text);
    }

    private static string Escape(string text)
    {
        var latex = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character == '\\')
            {
                latex.Append(@"\backslash ");
                continue;
            }

            if (LatexSpecialCharacters.Contains(character))
            {
                latex.Append('\\');
            }

            latex.Append(character);
        }

        return latex.ToString();
    }

    // A base of more than one character is grouped, so the exponent is the
    // whole base's: {ab}^{2}, not a times b^{2}.
    private static string Group(string latex) => latex.Length == 1
        ? latex
        : $"{{{latex}}}";

    private string RadicalOf(Radical radical)
    {
        var degree = Latex(radical.Degree);
        return degree.Length == 0
            ? $@"\sqrt{{{Latex(radical.Base)}}}"
            : $@"\sqrt[{degree}]{{{Latex(radical.Base)}}}";
    }

    // A sum, product or integral; without a character of its own it is an integral.
    private string NaryOf(Nary nary)
    {
        var character = nary.NaryProperties?.AccentChar?.Val?.Value ?? DefaultIntegral;
        var lower = Latex(nary.SubArgument);
        var upper = Latex(nary.SuperArgument);

        return NaryOperators.GetValueOrDefault(character, character) +
            (lower.Length > 0 ? $"_{{{lower}}}" : "") +
            (upper.Length > 0 ? $"^{{{upper}}}" : "") +
            Latex(nary.Base);
    }

    // Brackets around one or more parts, such as (a+b) or |x|. An empty
    // character means no bracket on that side.
    private string DelimiterOf(Delimiter delimiter)
    {
        var properties = delimiter.DelimiterProperties;
        var opening = properties?.BeginChar?.Val?.Value ?? DefaultOpeningDelimiter;
        var closing = properties?.EndChar?.Val?.Value ?? DefaultClosingDelimiter;
        var separator = properties?.SeparatorChar?.Val?.Value ?? DefaultDelimiterSeparator;

        var parts = string.Join(separator, delimiter.Elements<Base>().Select(Latex));
        return $"{Escape(opening)}{parts}{Escape(closing)}";
    }

    private string FunctionOf(MathFunction function)
    {
        var name = Latex(function.FunctionName);
        var command = NamedFunctions.Contains(name)
            ? $@"\{name}"
            : name;

        return $"{command} {Latex(function.Base)}";
    }

    private string AccentOf(Accent accent)
    {
        var character = accent.AccentProperties?.AccentChar?.Val?.Value ?? DefaultAccent;
        var command = Accents.GetValueOrDefault(character) ?? $@"\overset{{{character}}}";

        return $"{command}{{{Latex(accent.Base)}}}";
    }

    // lim under n→∞ is \lim_{n→∞}; any other base gets its limit set under or over it.
    private static string LimitOf(string baseLatex, string limitLatex, string script, string command) =>
        NamedFunctions.Contains(baseLatex)
            ? $@"\{baseLatex}{script}{{{limitLatex}}}"
            : $"{command}{{{limitLatex}}}{{{baseLatex}}}";

    private static string Rows(string begin, IEnumerable<string> rows, string end) => $@"{begin} {string.Join(@" \\ ", rows)} {end}";
}
