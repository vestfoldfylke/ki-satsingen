using kisatsingen.Services.Attachments;
using kisatsingen.Services.KnowledgeFiles.Conversion;

namespace kisatsingen.Services.KnowledgeFiles.Pipeline;

internal static class KnowledgeFilePipelineServiceCollectionExtensions
{
    public static IServiceCollection AddKnowledgeFilePipeline(
        this IServiceCollection services,
        IConfiguration configuration,
        int maxKnowledgeFileTokens)
    {
        // Defaults live in KnowledgeFilePipelineOptions; the section overrides them.
        var pipelineOptions = configuration.GetSection(KnowledgeFilePipelineOptions.SectionName).Get<KnowledgeFilePipelineOptions>()
            ?? new KnowledgeFilePipelineOptions();
        pipelineOptions.Validate();

        services.AddSingleton(pipelineOptions);

        // One converter per content type. Moving a type to a remote converter means
        // swapping its registration here for one that calls that service.
        services.AddSingleton<IDocumentConverter, TextDocumentConverter>();
        services.AddSingleton<IDocumentConverter, ImagePlaceholderConverter>();
        services.AddSingleton<IDocumentConverter, PdfDocumentConverter>();
        services.AddSingleton<IDocumentConverter, DocxDocumentConverter>();
        services.AddSingleton<DocumentConverterRegistry>();
        services.AddSingleton<IDocumentSummarizer, OpeningExcerptSummarizer>();
        services.AddSingleton<KnowledgeFilePipelineMetrics>();
        services.AddSingleton(sp => new KnowledgeFilePipeline(
            sp.GetRequiredService<DocumentConverterRegistry>(),
            sp.GetRequiredService<IDocumentSummarizer>(),
            sp.GetRequiredService<AttachmentStorage>(),
            pipelineOptions,
            sp.GetRequiredService<KnowledgeFilePipelineMetrics>(),
            maxKnowledgeFileTokens,
            sp.GetRequiredService<ILogger<KnowledgeFilePipeline>>()));

        return services;
    }
}
