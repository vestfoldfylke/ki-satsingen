using kisatsingen.Services;

namespace kisatsingen.AIFunctions.FileTools;

// Defaults in code, overridable per environment under this section name.
public sealed class FileToolOptions
{
    public const string SectionName = "KnowledgeFileTools";

    // Capped by tokens rather than lines: one line can hold a whole paragraph,
    // so a line cap would not bound what a read costs.
    public int MaxReadTokens { get; init; } = 8_000;

    // A safety net for single lines longer than any real paragraph. Also what
    // guarantees a read always returns at least one line within the cap.
    public int MaxLineCharacters { get; init; } = 2_000;

    public int DefaultOutlineDepth { get; init; } = 2;

    // A generated file can have thousands of headings; without a cap one
    // outline could fill the context the way an uncapped read would.
    public int MaxOutlineEntries { get; init; } = 200;

    public void Validate()
    {
        if (MaxReadTokens <= 0 || MaxLineCharacters <= 0 || DefaultOutlineDepth <= 0 || MaxOutlineEntries <= 0)
        {
            throw new InvalidOperationException(
                $"Every '{SectionName}' setting must be positive. Check the '{SectionName}' section in configuration.");
        }

        if (DefaultOutlineDepth > GetOutlineTool.MaxDepth)
        {
            throw new InvalidOperationException(
                $"'{SectionName}:DefaultOutlineDepth' ({DefaultOutlineDepth}) is deeper than Markdown's {GetOutlineTool.MaxDepth} heading levels. Set it between 1 and {GetOutlineTool.MaxDepth}.");
        }

        if (TokenEstimate.FromCharacters(MaxLineCharacters) >= MaxReadTokens)
        {
            throw new InvalidOperationException(
                $"'{SectionName}:MaxLineCharacters' ({MaxLineCharacters}) must fit well within '{SectionName}:MaxReadTokens' ({MaxReadTokens}), or a single line could fill a whole read. Lower the first or raise the second.");
        }
    }
}
