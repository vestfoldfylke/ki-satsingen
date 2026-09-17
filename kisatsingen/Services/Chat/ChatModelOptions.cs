namespace kisatsingen.Services.Chat;

public sealed record ChatModelOptions(ModelOption Current, IReadOnlyList<ModelOption> AvailableModels);

