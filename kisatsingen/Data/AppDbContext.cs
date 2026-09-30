using kisatsingen.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;

namespace kisatsingen.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Orders a chat's turns exactly, whatever the clock says.
    public const string TurnSequenceName = "chat_turn_seq";

    // Set only by CreateForMigrations. EF disposes only data sources it built, and
    // this one cannot be a using there because the returned context queries through
    // it later, so it is disposed with the context.
    private NpgsqlDataSource? _ownedDataSource;

    private const string KnowledgeFileScopeConstraintName = "ck_knowledge_files_single_scope";

    // Named so the save path can tell a duplicate from any other unique violation.
    public const string KnowledgeFileChatSha256IndexName = "IX_KnowledgeFiles_ChatId_Sha256";
    public const string KnowledgeFileAssistantSha256IndexName = "IX_KnowledgeFiles_AssistantId_Sha256";

    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<ChatTurn> ChatTurns => Set<ChatTurn>();
    public DbSet<Assistant> Assistants => Set<Assistant>();
    public DbSet<KnowledgeFile> KnowledgeFiles => Set<KnowledgeFile>();
    public DbSet<TokenUsage> TokenUsages => Set<TokenUsage>();

    // The only place a DDL-capable connection is used — the app's own runtime queries
    // always go through the low-privilege DefaultConnection registered in DI.
    public static AppDbContext CreateForMigrations(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MigrationConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:MigrationConnection is not configured.");

        var dataSourceBuilderForMigration = new NpgsqlDataSourceBuilder(connectionString)
        {
            Name = "ChatDbMigration"
        };

        var dataSourceForMigration = dataSourceBuilderForMigration.Build();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(dataSourceForMigration)
            .Options;

        return new AppDbContext(options) { _ownedDataSource = dataSourceForMigration };
    }

    public override void Dispose()
    {
        base.Dispose();
        _ownedDataSource?.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (_ownedDataSource is not null)
        {
            await _ownedDataSource.DisposeAsync();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var chat = modelBuilder.Entity<Chat>();
        chat.HasKey(c => c.Id);
        chat.Property(c => c.OwnerId).HasMaxLength(128);
        chat.Property(c => c.Title).HasMaxLength(Chat.MaxTitleLength).IsRequired();
        chat.Property(c => c.AssistantNameSnapshot).HasMaxLength(Assistant.MaxNameLength);
        chat.HasIndex(c => new { c.OwnerId, c.UpdatedAt });

        modelBuilder.HasSequence<long>(TurnSequenceName);

        var turn = modelBuilder.Entity<ChatTurn>();
        turn.HasKey(t => t.Id);
        turn.Property(t => t.Prompt).HasColumnType("text").IsRequired();
        turn.Property(t => t.SystemPrompt).HasColumnType("text").IsRequired();
        turn.Property(t => t.ModelKey).HasMaxLength(ChatTurn.MaxModelKeyLength).IsRequired();
        turn.Property(t => t.Status).HasMaxLength(ChatTurn.MaxStatusLength).IsRequired();
        turn.Property(t => t.FailedAt).HasMaxLength(ChatTurn.MaxStatusLength);
        turn.Property(t => t.ServedModelId).HasMaxLength(ChatTurn.MaxProviderIdLength);
        turn.Property(t => t.ResponseId).HasMaxLength(ChatTurn.MaxProviderIdLength);
        turn.Property(t => t.FinishReason).HasMaxLength(ChatTurn.MaxFinishReasonLength);

        // text, not jsonb: jsonb rejects a \u0000 anywhere in the document, and
        // model output and tool results are text we do not control. A turn that
        // cannot be saved over one stray byte is not worth the querying.
        turn.Property(t => t.AnswerJson).HasColumnType("text").IsRequired();
        turn.Property(t => t.AttachmentsJson).HasColumnType("text");

        turn.HasIndex(t => new { t.ChatId, t.Seq });
        ConfigureSeq(turn.Property(t => t.Seq));

        turn.HasOne<Chat>()
            .WithMany(c => c.Turns)
            .HasForeignKey(t => t.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        var assistant = modelBuilder.Entity<Assistant>();
        assistant.HasKey(a => a.Id);
        assistant.Property(a => a.OwnerId).HasMaxLength(128).IsRequired();
        assistant.Property(a => a.Name).HasMaxLength(Assistant.MaxNameLength).IsRequired();
        assistant.Property(a => a.Description).HasMaxLength(Assistant.MaxDescriptionLength);
        assistant.Property(a => a.Instructions).HasColumnType("text").IsRequired();
        assistant.HasIndex(a => new { a.OwnerId, a.UpdatedAt });

        // SetNull, not Cascade: deleting an assistant must not delete the
        // conversations people had with it.
        chat.HasOne<Assistant>()
            .WithMany()
            .HasForeignKey(c => c.AssistantId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        var knowledgeFile = modelBuilder.Entity<KnowledgeFile>();
        knowledgeFile.HasKey(f => f.Id);
        knowledgeFile.Property(f => f.OwnerId).HasMaxLength(128).IsRequired();
        knowledgeFile.Property(f => f.FileName).HasMaxLength(KnowledgeFile.MaxFileNameLength).IsRequired();
        knowledgeFile.Property(f => f.ContentType).HasMaxLength(KnowledgeFile.MaxContentTypeLength).IsRequired();
        knowledgeFile.Property(f => f.Sha256).HasMaxLength(KnowledgeFile.Sha256HexLength).IsRequired();
        knowledgeFile.Property(f => f.Summary).HasColumnType("text");
        knowledgeFile.Property(f => f.Markdown).HasColumnType("text").IsRequired();
        knowledgeFile.Property(f => f.ContentOrigin).HasMaxLength(KnowledgeFile.MaxContentOriginLength).IsRequired();

        // Partial, so each only covers rows in its own scope. They also serve
        // every lookup by scope, which is why there is no plain index on
        // ChatId or AssistantId. OwnerId has none either: every query narrows
        // by id or scope first.
        knowledgeFile.HasIndex(f => new { f.ChatId, f.Sha256 })
            .IsUnique()
            .HasFilter("\"ChatId\" IS NOT NULL")
            .HasDatabaseName(KnowledgeFileChatSha256IndexName);
        knowledgeFile.HasIndex(f => new { f.AssistantId, f.Sha256 })
            .IsUnique()
            .HasFilter("\"AssistantId\" IS NOT NULL")
            .HasDatabaseName(KnowledgeFileAssistantSha256IndexName);

        knowledgeFile.ToTable(t => t.HasCheckConstraint(
            KnowledgeFileScopeConstraintName,
            """num_nonnulls("AssistantId", "ChatId") = 1"""));

        // Cascade must be spelled out on both: EF defaults an optional foreign
        // key to SetNull, which would null the only scope a row has and break
        // the check constraint. The delete paths that never load an entity —
        // DeleteChatAsync, DeleteAssistantAsync — depend entirely on this DDL.
        knowledgeFile.HasOne<Assistant>()
            .WithMany()
            .HasForeignKey(f => f.AssistantId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        knowledgeFile.HasOne<Chat>()
            .WithMany()
            .HasForeignKey(f => f.ChatId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        var tokenUsage = modelBuilder.Entity<TokenUsage>();
        tokenUsage.HasKey(u => u.Id);
        tokenUsage.Property(u => u.OwnerId).HasMaxLength(TokenUsage.MaxOwnerIdLength).IsRequired();
        tokenUsage.Property(u => u.Provider).HasMaxLength(TokenUsage.MaxProviderLength).IsRequired();
        tokenUsage.Property(u => u.ModelId).HasMaxLength(TokenUsage.MaxModelIdLength).IsRequired();
        tokenUsage.Property(u => u.Status).HasConversion<string>().HasMaxLength(TokenUsage.MaxStatusLength).IsRequired();
        tokenUsage.HasIndex(u => new { u.OwnerId, u.Timestamp });
    }

    // The Ignore behaviours keep Seq out of every INSERT and UPDATE, so only the
    // sequence default ever assigns it, whatever code path saves. AfterSaveBehavior
    // matters as much: without it, an Update() of an entity built in code writes
    // Seq = 0 and sorts the turn ahead of the whole chat.
    private static void ConfigureSeq(PropertyBuilder<long> seq)
    {
        seq.HasDefaultValueSql($"nextval('{TurnSequenceName}')").ValueGeneratedOnAdd();
        seq.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        seq.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}
