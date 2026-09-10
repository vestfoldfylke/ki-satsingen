using kisatsingen.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql;

namespace kisatsingen.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

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

        return new AppDbContext(options);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var chat = modelBuilder.Entity<Chat>();
        chat.HasKey(c => c.Id);
        chat.Property(c => c.OwnerId).HasMaxLength(128);
        chat.Property(c => c.Title).HasMaxLength(200).IsRequired();
        chat.HasIndex(c => new { c.OwnerId, c.UpdatedAt });

        var message = modelBuilder.Entity<ChatMessage>();
        message.HasKey(m => m.Id);
        message.Property(m => m.Role).HasMaxLength(32).IsRequired();
        message.Property(m => m.Content).IsRequired();
        message.Property(m => m.ResponseId).HasMaxLength(128);
        message.Property(m => m.ModelId).HasMaxLength(128);
        message.Property(m => m.FinishReason).HasMaxLength(64);
        message.HasIndex(m => new { m.ChatId, m.CreatedAt });

        message.HasOne(m => m.Chat)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        if (Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
        {
            return;
        }

        var dtoToTicks = new ValueConverter<DateTimeOffset, long>(
            v => v.UtcTicks,
            v => new DateTimeOffset(v, TimeSpan.Zero));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(dtoToTicks);
                }
            }
        }
    }
}
