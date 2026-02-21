using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Data;

public class GlucoseDbContext : DbContext
{
    public GlucoseDbContext(DbContextOptions<GlucoseDbContext> options) : base(options) { }

    public DbSet<GlucoseReading> GlucoseReadings => Set<GlucoseReading>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<SamsungNote> SamsungNotes => Set<SamsungNote>();
    public DbSet<GlucoseEvent> GlucoseEvents => Set<GlucoseEvent>();
    public DbSet<EventAnalysisHistory> EventAnalysisHistory => Set<EventAnalysisHistory>();
    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();
    public DbSet<DailySummary> DailySummaries => Set<DailySummary>();
    public DbSet<DailySummarySnapshot> DailySummarySnapshots => Set<DailySummarySnapshot>();
    public DbSet<GlucoseComparison> GlucoseComparisons => Set<GlucoseComparison>();
    public DbSet<PeriodSummary> PeriodSummaries => Set<PeriodSummary>();
    public DbSet<EventLog> EventLogs => Set<EventLog>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatPromptTemplate> ChatPromptTemplates => Set<ChatPromptTemplate>();
    public DbSet<FoodItem> FoodItems => Set<FoodItem>();
    public DbSet<FoodEventLink> FoodEventLinks => Set<FoodEventLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GlucoseReading>(entity =>
        {
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => new { e.PatientId, e.Timestamp }).IsUnique();
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasIndex(e => e.Key).IsUnique();
        });

        modelBuilder.Entity<SamsungNote>(entity =>
        {
            entity.HasIndex(e => e.Uuid).IsUnique();
            entity.HasIndex(e => e.ModifiedAt);
        });

        modelBuilder.Entity<GlucoseEvent>(entity =>
        {
            entity.HasIndex(e => e.NoteUuid).IsUnique();
            entity.HasIndex(e => e.EventTimestamp);
            entity.HasIndex(e => e.IsProcessed);
        });

        modelBuilder.Entity<EventAnalysisHistory>(entity =>
        {
            entity.HasIndex(e => e.GlucoseEventId);
            entity.HasIndex(e => e.AnalyzedAt);
        });

        modelBuilder.Entity<AiUsageLog>(entity =>
        {
            entity.HasIndex(e => e.CalledAt);
            entity.HasIndex(e => e.GlucoseEventId);
            entity.HasIndex(e => e.Model);
        });

        modelBuilder.Entity<DailySummary>(entity =>
        {
            entity.HasIndex(e => e.Date).IsUnique();
            entity.HasIndex(e => e.IsProcessed);
        });

        modelBuilder.Entity<DailySummarySnapshot>(entity =>
        {
            entity.HasIndex(e => e.DailySummaryId);
            entity.HasIndex(e => e.Date);
            entity.HasIndex(e => e.GeneratedAt);
        });

        modelBuilder.Entity<GlucoseComparison>(entity =>
        {
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<PeriodSummary>(entity =>
        {
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<EventLog>(entity =>
        {
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.Level);
            entity.HasIndex(e => e.Category);
        });

        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.UpdatedAt);
            entity.HasMany(e => e.Messages)
                  .WithOne(m => m.Session)
                  .HasForeignKey(m => m.ChatSessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasIndex(e => e.ChatSessionId);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<ChatPromptTemplate>(entity =>
        {
            entity.HasIndex(e => e.Category);
        });

        modelBuilder.Entity<FoodItem>(entity =>
        {
            entity.HasIndex(e => e.NormalizedName);
            entity.HasIndex(e => e.OccurrenceCount);
        });

        modelBuilder.Entity<FoodEventLink>(entity =>
        {
            entity.HasIndex(e => e.FoodItemId);
            entity.HasIndex(e => e.GlucoseEventId);
            entity.HasIndex(e => new { e.FoodItemId, e.GlucoseEventId }).IsUnique();
        });
    }
}
