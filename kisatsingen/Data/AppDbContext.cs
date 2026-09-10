using kisatsingen.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace kisatsingen.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // Messages and events share one sequence so a chat's transcript has a single
    // exact order across both tables.
    public const string EntrySequenceName = "chat_entry_seq";

    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatEvent> ChatEvents => Set<ChatEvent>();

    // The only place a DDL-capable connection is used — the app's own runtime queries
    // always go through the low-privilege DefaultConnection registered in DI.
    public static AppDbContext CreateForMigrations(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MigrationConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:MigrationConnection is not configured.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var chat = modelBuilder.Entity<Chat>();
        chat.HasKey(c => c.Id);
        chat.Property(c => c.OwnerId).HasMaxLength(128);
        chat.Property(c => c.Title).HasMaxLength(200).IsRequired();
        chat.HasIndex(c => new { c.OwnerId, c.UpdatedAt });

        modelBuilder.HasSequence<long>(EntrySequenceName);

        var message = modelBuilder.Entity<ChatMessage>();
        message.HasKey(m => m.Id);
        message.Property(m => m.Role).HasMaxLength(32).IsRequired();
        message.Property(m => m.Content).IsRequired();
        message.Property(m => m.ResponseId).HasMaxLength(128);
        message.Property(m => m.ModelId).HasMaxLength(128);
        message.Property(m => m.FinishReason).HasMaxLength(64);
        message.HasIndex(m => new { m.ChatId, m.Seq });

        message.HasOne(m => m.Chat)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        var chatEvent = modelBuilder.Entity<ChatEvent>();
        chatEvent.HasKey(e => e.Id);
        chatEvent.Property(e => e.Kind).HasConversion<string>().HasMaxLength(32).IsRequired();
        chatEvent.Property(e => e.Detail).HasMaxLength(500);
        chatEvent.HasIndex(e => new { e.ChatId, e.Seq });

        chatEvent.HasOne(e => e.Chat)
            .WithMany(c => c.Events)
            .HasForeignKey(e => e.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
