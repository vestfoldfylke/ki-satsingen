namespace kisatsingen.Services;

// Database values in everything but name: stored on every usage row and
// grouped by, so one provider has one name. Renaming one splits its history.
internal static class ModelProviders
{
    public const string OpenAI = "OpenAI";
    public const string Mistral = "Mistral AI";
}
