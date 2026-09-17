namespace kisatsingen.Services.Chat;

public sealed record ChatModelOptions(string ModelId, IReadOnlyList<string> AvailableModels);
