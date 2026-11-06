namespace kisatsingen.Services.KnowledgeFiles.Conversion.Office;

internal sealed record OfficeLimits(long MaxUncompressedBytes, int MaxCharacters, int MaxEstimatedTokens);
