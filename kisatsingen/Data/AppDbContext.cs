using kisatsingen.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;

namespace kisatsingen.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Messages and events share one sequence so a chat's transcript has a single
    // exact order across both tables.
    public const string EntrySequenceName = "chat_entry_seq";

    // Set only by CreateForMigrations. EF disposes a data source only when it
    // built one itself, so the one handed to it below would otherwise outlive
    // every caller — and it cannot simply be wrapped in a using here, because the
    // returned context queries through it long after this method returns.
    // Disposing it with the context is what gives it the right lifetime.
    private NpgsqlDataSource? _ownedDataSource;

    // Enforces that a knowledge file is scoped to exactly one owner — an
    // assistant or a chat, never both, never neither.
    private const string KnowledgeFileScopeConstraintName = "ck_knowledge_files_single_scope";

    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatEvent> ChatEvents => Set<ChatEvent>();
    public DbSet<Assistant> Assistants => Set<Assistant>();
    public DbSet<KnowledgeFile> KnowledgeFiles => Set<KnowledgeFile>();
    public DbSet<KnowledgeFileChunk> KnowledgeFileChunks => Set<KnowledgeFileChunk>();

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
        chat.HasIndex(c => new { c.OwnerId, c.UpdatedAt });

        modelBuilder.HasSequence<long>(EntrySequenceName);

        var message = modelBuilder.Entity<ChatMessage>();
        message.HasKey(m => m.Id);
        message.Property(m => m.Role).HasMaxLength(32).IsRequired();
        message.Property(m => m.Content).IsRequired();
        message.Property(m => m.ResponseId).HasMaxLength(128);
        message.Property(m => m.ModelId).HasMaxLength(128);
        message.Property(m => m.FinishReason).HasMaxLength(64);
        message.Property(m => m.ContentsSchemaVersion).HasMaxLength(64);

        // Deliberately text and not jsonb. jsonb normalises key order, and
        // System.Text.Json requires the "$type" discriminator to come first when
        // deserialising a polymorphic AIContent — so a jsonb round trip silently
        // turns every tool call back into plain text. Verified by
        // a_tool_call_survives_the_round_trip_through_storage, which fails on
        // jsonb. This column stores bytes a strict deserialiser has to read back
        // exactly; querying into it is not a use case.
        message.Property(m => m.ContentsJson).HasColumnType("text");
        message.HasIndex(m => new { m.ChatId, m.Seq });
        ConfigureSeq(message.Property(m => m.Seq));

        // No navigation back to the chat: nothing walks from a message to its
        // parent, and the collection side is what GetChatAsync includes.
        message.HasOne<Chat>()
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        var chatEvent = modelBuilder.Entity<ChatEvent>();
        chatEvent.HasKey(e => e.Id);
        chatEvent.Property(e => e.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        chatEvent.Property(e => e.Detail).HasMaxLength(500);
        chatEvent.HasIndex(e => new { e.ChatId, e.Seq });
        ConfigureSeq(chatEvent.Property(e => e.Seq));

        chatEvent.HasOne<Chat>()
            .WithMany(c => c.Events)
            .HasForeignKey(e => e.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        var assistant = modelBuilder.Entity<Assistant>();
        assistant.HasKey(a => a.Id);
        assistant.Property(a => a.OwnerId).HasMaxLength(128).IsRequired();
        assistant.Property(a => a.Name).HasMaxLength(Assistant.MaxNameLength).IsRequired();
        assistant.Property(a => a.Description).HasMaxLength(Assistant.MaxDescriptionLength);
        assistant.Property(a => a.Instructions).HasColumnType("text").IsRequired();
        assistant.HasIndex(a => new { a.OwnerId, a.UpdatedAt });

        // SetNull, not Cascade: deleting an assistant must not delete the
        // conversations people had with it. The chat keeps its transcript and
        // loses only the link — and, separately, the assistant's files, which
        // cascade from the assistant below.
        //
        // Configured with no navigation on either side: nothing traverses from a
        // chat to its assistant or back, so the relationship is the foreign key
        // and this configuration. Declaring navigations anyway would add loading
        // paths that only exist to be misused.
        chat.HasOne<Assistant>()
            .WithMany()
            .HasForeignKey(c => c.AssistantId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        var knowledgeFile = modelBuilder.Entity<KnowledgeFile>();
        knowledgeFile.HasKey(f => f.Id);
        knowledgeFile.Property(f => f.OwnerId).HasMaxLength(128).IsRequired();
        knowledgeFile.Property(f => f.FileName).HasMaxLength(260).IsRequired();
        knowledgeFile.Property(f => f.ContentType).HasMaxLength(128).IsRequired();
        knowledgeFile.Property(f => f.Sha256).HasMaxLength(64).IsRequired();
        knowledgeFile.Property(f => f.Language).HasMaxLength(32);
        knowledgeFile.Property(f => f.Summary).HasColumnType("text").IsRequired();
        knowledgeFile.Property(f => f.TableOfContents).HasColumnType("text");
        knowledgeFile.HasIndex(f => f.AssistantId);
        knowledgeFile.HasIndex(f => f.ChatId);
        knowledgeFile.HasIndex(f => f.OwnerId);

        knowledgeFile.ToTable(t => t.HasCheckConstraint(
            KnowledgeFileScopeConstraintName,
            """num_nonnulls("AssistantId", "ChatId") = 1"""));

        // Both scope relationships are optional but cascade, and the cascade has
        // to be spelled out: EF defaults an optional foreign key to SetNull, which
        // here would null the only scope a row has and break the check constraint.
        // That matters most for the delete paths that never load an entity —
        // ChatRepository.DeleteChatAsync uses ExecuteDeleteAsync, which bypasses
        // the change tracker entirely and relies on what the DDL says.
        //
        // Two cascade paths reach this table, but never the same row: the check
        // constraint guarantees exactly one of the two foreign keys is non-null,
        // so a file is only ever reachable from one parent.
        //
        // Only the assistant side carries a navigation, and only the one
        // direction that a query uses: AssistantRepository.GetAssistantAsync
        // includes an assistant's files. Nothing loads a file's parent, and
        // nothing loads a chat's files — ListFilesForChatAsync projects instead — so
        // those three navigations are not declared.
        knowledgeFile.HasOne<Assistant>()
            .WithMany(a => a.KnowledgeFiles)
            .HasForeignKey(f => f.AssistantId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        knowledgeFile.HasOne<Chat>()
            .WithMany()
            .HasForeignKey(f => f.ChatId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        var chunk = modelBuilder.Entity<KnowledgeFileChunk>();
        chunk.HasKey(c => c.Id);
        chunk.Property(c => c.Heading).HasMaxLength(500);
        chunk.Property(c => c.Content).HasColumnType("text").IsRequired();

        // Unique, not just an index: two chunks claiming the same position would
        // make the document's order ambiguous. It does not enforce density —
        // nothing here objects to 0, 1, 3. That comes from the repository
        // assigning Sequence from list order, and is the reason chunks have no
        // second write path.
        chunk.HasIndex(c => new { c.KnowledgeFileId, c.Sequence }).IsUnique();

        chunk.HasOne(c => c.KnowledgeFile)
            .WithMany(f => f.Chunks)
            .HasForeignKey(c => c.KnowledgeFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    // The two Ignore behaviours are the point of this: EF omits Seq from every
    // INSERT and every UPDATE regardless of what the entity holds, so the
    // sequence default is the only thing that can ever produce a value. No
    // application code path — not a future one that skips ChatRepository — can
    // assign an ordering number.
    //
    // AfterSaveBehavior matters as much as BeforeSaveBehavior: without it an
    // Update() on an entity built in code writes Seq = 0 over a real row and
    // sorts it ahead of the whole transcript.
    private static void ConfigureSeq(PropertyBuilder<long> seq)
    {
        seq.HasDefaultValueSql($"nextval('{EntrySequenceName}')").ValueGeneratedOnAdd();
        seq.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        seq.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }
}
