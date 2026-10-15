using kisatsingen.Services;

namespace kisatsingen.AIFunctions.FileTools;

// Not read from configuration: these shape what the model sees through the
// tools, and the tools are tested with them. Tests override them.
public sealed class FileToolOptions
{
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
                $"Every {nameof(FileToolOptions)} value must be positive. Fix the defaults in {nameof(FileToolOptions)}.");
        }

        if (DefaultOutlineDepth > GetOutlineTool.MaxDepth)
        {
            throw new InvalidOperationException(
                $"{nameof(DefaultOutlineDepth)} ({DefaultOutlineDepth}) is deeper than Markdown's {GetOutlineTool.MaxDepth} heading levels. Set it between 1 and {GetOutlineTool.MaxDepth}.");
        }

        if (TokenEstimate.FromCharacters(MaxLineCharacters) >= MaxReadTokens)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxLineCharacters)} ({MaxLineCharacters}) must fit well within {nameof(MaxReadTokens)} ({MaxReadTokens}), or a single line could fill a whole read. Lower the first or raise the second.");
        }
    }
}
