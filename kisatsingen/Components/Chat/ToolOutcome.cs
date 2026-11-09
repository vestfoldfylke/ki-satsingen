namespace kisatsingen.Components.Chat;

// Refused is an answer the tool gave on purpose, such as a file that is gone,
// which tells the model why. Failed is an exception, whose result says only
// that the tool failed.
internal enum ToolOutcome
{
    Running,
    Succeeded,
    Refused,
    Failed,
    Interrupted
}
