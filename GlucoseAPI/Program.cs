using GlucoseAPI.Application.Interfaces;
using GlucoseAPI.Data;
using GlucoseAPI.Domain.Services;
using GlucoseAPI.Hubs;
using GlucoseAPI.Infrastructure.ExternalApis;
using GlucoseAPI.Infrastructure.Logging;
using GlucoseAPI.Infrastructure.Notifications;
using GlucoseAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using QuestPDF.Infrastructure;

// QuestPDF Community License (free for revenue < $1M)
QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// ── Database ────────────────────────────────────────────
builder.Services.AddDbContext<GlucoseDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── HttpClientFactory (fixes "new HttpClient()" anti-pattern) ────
builder.Services.AddHttpClient(LibreLinkClient.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = System.Net.DecompressionMethods.GZip
                           | System.Net.DecompressionMethods.Deflate
                           | System.Net.DecompressionMethods.Brotli,
})
.AddResilienceHandler("LibreLink-resilience", builder =>
{
    builder.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(2),
        BackoffType = DelayBackoffType.Exponential,
        ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Result?.StatusCode is
                System.Net.HttpStatusCode.RequestTimeout or
                System.Net.HttpStatusCode.TooManyRequests or
                System.Net.HttpStatusCode.ServiceUnavailable or
                System.Net.HttpStatusCode.GatewayTimeout
            || args.Outcome.Exception is HttpRequestException or TaskCanceledException)
    });
    builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        SamplingDuration = TimeSpan.FromSeconds(60),
        FailureRatio = 0.8,
        MinimumThroughput = 5,
        BreakDuration = TimeSpan.FromSeconds(30)
    });
    builder.AddTimeout(TimeSpan.FromSeconds(25));
});

builder.Services.AddHttpClient("OpenAI", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/");
    client.Timeout = TimeSpan.FromSeconds(120);
    client.DefaultRequestHeaders.Add("User-Agent", "GlucoseAPI/1.0");
})
.AddResilienceHandler("OpenAI-resilience", builder =>
{
    builder.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = 2,
        Delay = TimeSpan.FromSeconds(3),
        BackoffType = DelayBackoffType.Exponential,
        ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Result?.StatusCode is
                System.Net.HttpStatusCode.TooManyRequests or
                System.Net.HttpStatusCode.ServiceUnavailable or
                System.Net.HttpStatusCode.GatewayTimeout or
                System.Net.HttpStatusCode.InternalServerError
            || args.Outcome.Exception is HttpRequestException or TaskCanceledException)
    });
    builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        SamplingDuration = TimeSpan.FromSeconds(120),
        FailureRatio = 0.9,
        MinimumThroughput = 3,
        BreakDuration = TimeSpan.FromSeconds(60)
    });
    builder.AddTimeout(TimeSpan.FromSeconds(110));
});

// ── Domain Services ────────────────────────────────────
builder.Services.AddScoped<TimeZoneConverter>();

// ── Application / Infrastructure Interfaces ────────────
builder.Services.AddScoped<IGptClient, OpenAiGptClient>();
builder.Services.AddSingleton<INotificationService, SignalRNotificationService>();
builder.Services.AddSingleton<IEventLogger, GlucoseAPI.Infrastructure.Logging.EventLogger>();

// ── Application Services ───────────────────────────────
builder.Services.AddScoped<LibreLinkClient>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<SamsungNotesReader>();
builder.Services.AddScoped<EventAnalyzer>();
builder.Services.AddScoped<ReportService>();

// ── Background / Hosted Services ───────────────────────
builder.Services.AddSingleton<GlucoseFetchService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GlucoseFetchService>());
builder.Services.AddSingleton<SamsungNotesSyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SamsungNotesSyncService>());
builder.Services.AddSingleton<GlucoseEventAnalysisService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GlucoseEventAnalysisService>());
builder.Services.AddHostedService<DataBackupService>();
builder.Services.AddSingleton<DatabaseBackupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DatabaseBackupService>());
builder.Services.AddSingleton<ComparisonService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ComparisonService>());
builder.Services.AddSingleton<DailySummaryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DailySummaryService>());
builder.Services.AddSingleton<PeriodSummaryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PeriodSummaryService>());
builder.Services.AddSingleton<ChatService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ChatService>());
builder.Services.AddSingleton<FoodPatternService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<FoodPatternService>());
builder.Services.AddSingleton<TranslationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TranslationService>());

// ── MediatR (CQRS) ──────────────────────────────────────
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// ── Controllers & Swagger ───────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── SignalR ─────────────────────────────────────────────
builder.Services.AddSignalR();

// ── CORS (allow React dev server + SignalR) ─────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// ── Auto-create database ────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    for (int i = 0; i < 30; i++)
    {
        try
        {
            logger.LogInformation("Attempting to ensure database is created...");
            db.Database.EnsureCreated();
            logger.LogInformation("Database is ready.");

            // Create SamsungNotes table if it doesn't exist (EnsureCreated won't add new tables to existing DB)
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'SamsungNotes') AND type = 'U')
                    BEGIN
                        CREATE TABLE SamsungNotes (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Uuid NVARCHAR(200) NOT NULL,
                            Title NVARCHAR(MAX) NOT NULL,
                            TextContent NVARCHAR(MAX) NULL,
                            ModifiedAt DATETIME2 NOT NULL,
                            IsDeleted BIT NOT NULL DEFAULT 0,
                            FolderName NVARCHAR(500) NULL,
                            HasMedia BIT NOT NULL DEFAULT 0,
                            HasPreview BIT NOT NULL DEFAULT 0,
                            CreatedAt DATETIME2 NOT NULL,
                            UpdatedAt DATETIME2 NOT NULL
                        );
                        CREATE UNIQUE INDEX IX_SamsungNotes_Uuid ON SamsungNotes (Uuid);
                        CREATE INDEX IX_SamsungNotes_ModifiedAt ON SamsungNotes (ModifiedAt);
                        PRINT 'Created SamsungNotes table.';
                    END");
                logger.LogInformation("SamsungNotes table check complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not verify SamsungNotes table: {Message}", tableEx.Message);
            }

            // Create GlucoseEvents table if it doesn't exist
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'GlucoseEvents') AND type = 'U')
                    BEGIN
                        CREATE TABLE GlucoseEvents (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            SamsungNoteId INT NOT NULL,
                            NoteUuid NVARCHAR(200) NOT NULL,
                            NoteTitle NVARCHAR(500) NOT NULL,
                            NoteContent NVARCHAR(MAX) NULL,
                            EventTimestamp DATETIME2 NOT NULL,
                            PeriodStart DATETIME2 NOT NULL,
                            PeriodEnd DATETIME2 NOT NULL,
                            ReadingCount INT NOT NULL DEFAULT 0,
                            GlucoseAtEvent FLOAT NULL,
                            GlucoseMin FLOAT NULL,
                            GlucoseMax FLOAT NULL,
                            GlucoseAvg FLOAT NULL,
                            GlucoseSpike FLOAT NULL,
                            PeakTime DATETIME2 NULL,
                            AiAnalysis NVARCHAR(MAX) NULL,
                            IsProcessed BIT NOT NULL DEFAULT 0,
                            ProcessedAt DATETIME2 NULL,
                            CreatedAt DATETIME2 NOT NULL,
                            UpdatedAt DATETIME2 NOT NULL
                        );
                        CREATE UNIQUE INDEX IX_GlucoseEvents_NoteUuid ON GlucoseEvents (NoteUuid);
                        CREATE INDEX IX_GlucoseEvents_EventTimestamp ON GlucoseEvents (EventTimestamp);
                        CREATE INDEX IX_GlucoseEvents_IsProcessed ON GlucoseEvents (IsProcessed);
                        PRINT 'Created GlucoseEvents table.';
                    END");
                logger.LogInformation("GlucoseEvents table check complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not verify GlucoseEvents table: {Message}", tableEx.Message);
            }

            // Create EventAnalysisHistory table if it doesn't exist
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'EventAnalysisHistory') AND type = 'U')
                    BEGIN
                        CREATE TABLE EventAnalysisHistory (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            GlucoseEventId INT NOT NULL,
                            AiAnalysis NVARCHAR(MAX) NULL,
                            AnalyzedAt DATETIME2 NOT NULL,
                            PeriodStart DATETIME2 NOT NULL,
                            PeriodEnd DATETIME2 NOT NULL,
                            ReadingCount INT NOT NULL DEFAULT 0,
                            Reason NVARCHAR(500) NULL,
                            GlucoseAtEvent FLOAT NULL,
                            GlucoseMin FLOAT NULL,
                            GlucoseMax FLOAT NULL,
                            GlucoseAvg FLOAT NULL,
                            GlucoseSpike FLOAT NULL,
                            PeakTime DATETIME2 NULL
                        );
                        CREATE INDEX IX_EventAnalysisHistory_GlucoseEventId ON EventAnalysisHistory (GlucoseEventId);
                        CREATE INDEX IX_EventAnalysisHistory_AnalyzedAt ON EventAnalysisHistory (AnalyzedAt);
                        PRINT 'Created EventAnalysisHistory table.';
                    END

                    -- Add glucose stats columns to existing EventAnalysisHistory table if missing
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'EventAnalysisHistory') AND name = 'GlucoseAtEvent')
                    BEGIN
                        ALTER TABLE EventAnalysisHistory ADD GlucoseAtEvent FLOAT NULL;
                        ALTER TABLE EventAnalysisHistory ADD GlucoseMin FLOAT NULL;
                        ALTER TABLE EventAnalysisHistory ADD GlucoseMax FLOAT NULL;
                        ALTER TABLE EventAnalysisHistory ADD GlucoseAvg FLOAT NULL;
                        ALTER TABLE EventAnalysisHistory ADD GlucoseSpike FLOAT NULL;
                        ALTER TABLE EventAnalysisHistory ADD PeakTime DATETIME2 NULL;
                        PRINT 'Added glucose stats columns to EventAnalysisHistory.';
                    END");
                logger.LogInformation("EventAnalysisHistory table check complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not verify EventAnalysisHistory table: {Message}", tableEx.Message);
            }

            // Create DailySummaries table if it doesn't exist
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'DailySummaries') AND type = 'U')
                    BEGIN
                        CREATE TABLE DailySummaries (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Date DATETIME2 NOT NULL,
                            PeriodStartUtc DATETIME2 NOT NULL,
                            PeriodEndUtc DATETIME2 NOT NULL,
                            TimeZone NVARCHAR(100) NOT NULL DEFAULT '',
                            EventCount INT NOT NULL DEFAULT 0,
                            EventIds NVARCHAR(MAX) NULL,
                            EventTitles NVARCHAR(MAX) NULL,
                            ReadingCount INT NOT NULL DEFAULT 0,
                            GlucoseMin FLOAT NULL,
                            GlucoseMax FLOAT NULL,
                            GlucoseAvg FLOAT NULL,
                            GlucoseStdDev FLOAT NULL,
                            TimeInRange FLOAT NULL,
                            TimeAboveRange FLOAT NULL,
                            TimeBelowRange FLOAT NULL,
                            AiAnalysis NVARCHAR(MAX) NULL,
                            IsProcessed BIT NOT NULL DEFAULT 0,
                            ProcessedAt DATETIME2 NULL,
                            CreatedAt DATETIME2 NOT NULL,
                            UpdatedAt DATETIME2 NOT NULL
                        );
                        CREATE UNIQUE INDEX IX_DailySummaries_Date ON DailySummaries (Date);
                        CREATE INDEX IX_DailySummaries_IsProcessed ON DailySummaries (IsProcessed);
                        PRINT 'Created DailySummaries table.';
                    END");
                logger.LogInformation("DailySummaries table check complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not verify DailySummaries table: {Message}", tableEx.Message);
            }

            // Create DailySummarySnapshots table if it doesn't exist
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'DailySummarySnapshots') AND type = 'U')
                    BEGIN
                        CREATE TABLE DailySummarySnapshots (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            DailySummaryId INT NOT NULL,
                            Date DATETIME2 NOT NULL,
                            GeneratedAt DATETIME2 NOT NULL,
                            [Trigger] NVARCHAR(20) NOT NULL DEFAULT 'auto',
                            DataStartUtc DATETIME2 NOT NULL,
                            DataEndUtc DATETIME2 NOT NULL,
                            FirstReadingUtc DATETIME2 NULL,
                            LastReadingUtc DATETIME2 NULL,
                            TimeZone NVARCHAR(100) NOT NULL DEFAULT '',
                            EventCount INT NOT NULL DEFAULT 0,
                            EventIds NVARCHAR(MAX) NULL,
                            EventTitles NVARCHAR(MAX) NULL,
                            ReadingCount INT NOT NULL DEFAULT 0,
                            GlucoseMin FLOAT NULL,
                            GlucoseMax FLOAT NULL,
                            GlucoseAvg FLOAT NULL,
                            GlucoseStdDev FLOAT NULL,
                            TimeInRange FLOAT NULL,
                            TimeAboveRange FLOAT NULL,
                            TimeBelowRange FLOAT NULL,
                            AiAnalysis NVARCHAR(MAX) NULL,
                            IsProcessed BIT NOT NULL DEFAULT 0
                        );
                        CREATE INDEX IX_DailySummarySnapshots_DailySummaryId ON DailySummarySnapshots (DailySummaryId);
                        CREATE INDEX IX_DailySummarySnapshots_Date ON DailySummarySnapshots (Date);
                        CREATE INDEX IX_DailySummarySnapshots_GeneratedAt ON DailySummarySnapshots (GeneratedAt);
                        PRINT 'Created DailySummarySnapshots table.';
                    END");
                logger.LogInformation("DailySummarySnapshots table check complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not verify DailySummarySnapshots table: {Message}", tableEx.Message);
            }

            // Add AiClassification columns to existing tables if missing
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'GlucoseEvents') AND name = 'AiClassification')
                    BEGIN
                        ALTER TABLE GlucoseEvents ADD AiClassification NVARCHAR(10) NULL;
                        PRINT 'Added AiClassification column to GlucoseEvents.';
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'EventAnalysisHistory') AND name = 'AiClassification')
                    BEGIN
                        ALTER TABLE EventAnalysisHistory ADD AiClassification NVARCHAR(10) NULL;
                        PRINT 'Added AiClassification column to EventAnalysisHistory.';
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'DailySummaries') AND name = 'AiClassification')
                    BEGIN
                        ALTER TABLE DailySummaries ADD AiClassification NVARCHAR(10) NULL;
                        PRINT 'Added AiClassification column to DailySummaries.';
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'DailySummarySnapshots') AND name = 'AiClassification')
                    BEGIN
                        ALTER TABLE DailySummarySnapshots ADD AiClassification NVARCHAR(10) NULL;
                        PRINT 'Added AiClassification column to DailySummarySnapshots.';
                    END");
                logger.LogInformation("AiClassification column check complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not verify AiClassification columns: {Message}", tableEx.Message);
            }

            // Create GlucoseComparisons table if it doesn't exist
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'GlucoseComparisons') AND type = 'U')
                    BEGIN
                        CREATE TABLE GlucoseComparisons (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Name NVARCHAR(500) NULL,
                            PeriodAStart DATETIME2 NOT NULL,
                            PeriodAEnd DATETIME2 NOT NULL,
                            PeriodALabel NVARCHAR(500) NULL,
                            PeriodBStart DATETIME2 NOT NULL,
                            PeriodBEnd DATETIME2 NOT NULL,
                            PeriodBLabel NVARCHAR(500) NULL,
                            TimeZone NVARCHAR(100) NOT NULL DEFAULT '',
                            PeriodAReadingCount INT NOT NULL DEFAULT 0,
                            PeriodAGlucoseMin FLOAT NULL,
                            PeriodAGlucoseMax FLOAT NULL,
                            PeriodAGlucoseAvg FLOAT NULL,
                            PeriodAGlucoseStdDev FLOAT NULL,
                            PeriodATimeInRange FLOAT NULL,
                            PeriodATimeAboveRange FLOAT NULL,
                            PeriodATimeBelowRange FLOAT NULL,
                            PeriodAEventCount INT NOT NULL DEFAULT 0,
                            PeriodAEventTitles NVARCHAR(MAX) NULL,
                            PeriodBReadingCount INT NOT NULL DEFAULT 0,
                            PeriodBGlucoseMin FLOAT NULL,
                            PeriodBGlucoseMax FLOAT NULL,
                            PeriodBGlucoseAvg FLOAT NULL,
                            PeriodBGlucoseStdDev FLOAT NULL,
                            PeriodBTimeInRange FLOAT NULL,
                            PeriodBTimeAboveRange FLOAT NULL,
                            PeriodBTimeBelowRange FLOAT NULL,
                            PeriodBEventCount INT NOT NULL DEFAULT 0,
                            PeriodBEventTitles NVARCHAR(MAX) NULL,
                            AiAnalysis NVARCHAR(MAX) NULL,
                            AiClassification NVARCHAR(10) NULL,
                            Status NVARCHAR(20) NOT NULL DEFAULT 'pending',
                            ErrorMessage NVARCHAR(MAX) NULL,
                            CreatedAt DATETIME2 NOT NULL,
                            CompletedAt DATETIME2 NULL
                        );
                        CREATE INDEX IX_GlucoseComparisons_Status ON GlucoseComparisons (Status);
                        CREATE INDEX IX_GlucoseComparisons_CreatedAt ON GlucoseComparisons (CreatedAt);
                        PRINT 'Created GlucoseComparisons table.';
                    END");
                logger.LogInformation("GlucoseComparisons table check complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not verify GlucoseComparisons table: {Message}", tableEx.Message);
            }

            // Create PeriodSummaries table if it doesn't exist
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'PeriodSummaries') AND type = 'U')
                    BEGIN
                        CREATE TABLE PeriodSummaries (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Name NVARCHAR(500) NULL,
                            PeriodStart DATETIME2 NOT NULL,
                            PeriodEnd DATETIME2 NOT NULL,
                            TimeZone NVARCHAR(100) NOT NULL DEFAULT '',
                            ReadingCount INT NOT NULL DEFAULT 0,
                            GlucoseMin FLOAT NULL,
                            GlucoseMax FLOAT NULL,
                            GlucoseAvg FLOAT NULL,
                            GlucoseStdDev FLOAT NULL,
                            TimeInRange FLOAT NULL,
                            TimeAboveRange FLOAT NULL,
                            TimeBelowRange FLOAT NULL,
                            EventCount INT NOT NULL DEFAULT 0,
                            EventIds NVARCHAR(MAX) NULL,
                            EventTitles NVARCHAR(MAX) NULL,
                            AiAnalysis NVARCHAR(MAX) NULL,
                            AiClassification NVARCHAR(10) NULL,
                            Status NVARCHAR(20) NOT NULL DEFAULT 'pending',
                            ErrorMessage NVARCHAR(MAX) NULL,
                            CreatedAt DATETIME2 NOT NULL,
                            CompletedAt DATETIME2 NULL
                        );
                        CREATE INDEX IX_PeriodSummaries_Status ON PeriodSummaries (Status);
                        CREATE INDEX IX_PeriodSummaries_CreatedAt ON PeriodSummaries (CreatedAt);
                        PRINT 'Created PeriodSummaries table.';
                    END");
                logger.LogInformation("PeriodSummaries table check complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not verify PeriodSummaries table: {Message}", tableEx.Message);
            }

            // Create EventLogs table if it doesn't exist
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'EventLogs') AND type = 'U')
                    BEGIN
                        CREATE TABLE EventLogs (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Timestamp DATETIME2 NOT NULL,
                            Level NVARCHAR(10) NOT NULL DEFAULT 'info',
                            Category NVARCHAR(30) NOT NULL DEFAULT 'system',
                            Message NVARCHAR(500) NOT NULL DEFAULT '',
                            Detail NVARCHAR(MAX) NULL,
                            Source NVARCHAR(100) NULL,
                            RelatedEntityId INT NULL,
                            RelatedEntityType NVARCHAR(50) NULL,
                            NumericValue INT NULL,
                            DurationMs INT NULL
                        );
                        CREATE INDEX IX_EventLogs_Timestamp ON EventLogs (Timestamp);
                        CREATE INDEX IX_EventLogs_Level ON EventLogs (Level);
                        CREATE INDEX IX_EventLogs_Category ON EventLogs (Category);
                        PRINT 'Created EventLogs table.';
                    END");
                logger.LogInformation("EventLogs table check complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not verify EventLogs table: {Message}", tableEx.Message);
            }

            // Create AiUsageLogs table if it doesn't exist
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'AiUsageLogs') AND type = 'U')
                    BEGIN
                        CREATE TABLE AiUsageLogs (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            GlucoseEventId INT NULL,
                            Model NVARCHAR(100) NOT NULL DEFAULT '',
                            InputTokens INT NOT NULL DEFAULT 0,
                            OutputTokens INT NOT NULL DEFAULT 0,
                            TotalTokens INT NOT NULL DEFAULT 0,
                            Reason NVARCHAR(500) NULL,
                            Success BIT NOT NULL DEFAULT 0,
                            HttpStatusCode INT NULL,
                            FinishReason NVARCHAR(50) NULL,
                            CalledAt DATETIME2 NOT NULL,
                            DurationMs INT NULL
                        );
                        CREATE INDEX IX_AiUsageLogs_CalledAt ON AiUsageLogs (CalledAt);
                        CREATE INDEX IX_AiUsageLogs_GlucoseEventId ON AiUsageLogs (GlucoseEventId);
                        CREATE INDEX IX_AiUsageLogs_Model ON AiUsageLogs (Model);
                        PRINT 'Created AiUsageLogs table.';
                    END");
                logger.LogInformation("AiUsageLogs table check complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not verify AiUsageLogs table: {Message}", tableEx.Message);
            }

            // Add AiModel column to all AI-analyzed tables
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'GlucoseEvents') AND name = 'AiModel')
                    BEGIN
                        ALTER TABLE GlucoseEvents ADD AiModel NVARCHAR(50) NULL;
                        PRINT 'Added AiModel column to GlucoseEvents.';
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'EventAnalysisHistory') AND name = 'AiModel')
                    BEGIN
                        ALTER TABLE EventAnalysisHistory ADD AiModel NVARCHAR(50) NULL;
                        PRINT 'Added AiModel column to EventAnalysisHistory.';
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'DailySummaries') AND name = 'AiModel')
                    BEGIN
                        ALTER TABLE DailySummaries ADD AiModel NVARCHAR(50) NULL;
                        PRINT 'Added AiModel column to DailySummaries.';
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'DailySummarySnapshots') AND name = 'AiModel')
                    BEGIN
                        ALTER TABLE DailySummarySnapshots ADD AiModel NVARCHAR(50) NULL;
                        PRINT 'Added AiModel column to DailySummarySnapshots.';
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'GlucoseComparisons') AND name = 'AiModel')
                    BEGIN
                        ALTER TABLE GlucoseComparisons ADD AiModel NVARCHAR(50) NULL;
                        PRINT 'Added AiModel column to GlucoseComparisons.';
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'PeriodSummaries') AND name = 'AiModel')
                    BEGIN
                        ALTER TABLE PeriodSummaries ADD AiModel NVARCHAR(50) NULL;
                        PRINT 'Added AiModel column to PeriodSummaries.';
                    END");
                logger.LogInformation("AiModel column check complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not add AiModel columns: {Message}", tableEx.Message);
            }

            // Create ChatSessions table if it doesn't exist
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'ChatSessions') AND type = 'U')
                    BEGIN
                        CREATE TABLE ChatSessions (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Title NVARCHAR(200) NOT NULL DEFAULT '',
                            PeriodStart DATETIME2 NULL,
                            PeriodEnd DATETIME2 NULL,
                            TemplateName NVARCHAR(100) NULL,
                            Status NVARCHAR(20) NOT NULL DEFAULT 'active',
                            CreatedAt DATETIME2 NOT NULL,
                            UpdatedAt DATETIME2 NOT NULL
                        );
                        CREATE INDEX IX_ChatSessions_Status ON ChatSessions (Status);
                        CREATE INDEX IX_ChatSessions_UpdatedAt ON ChatSessions (UpdatedAt);
                        PRINT 'Created ChatSessions table.';
                    END");

                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'ChatMessages') AND type = 'U')
                    BEGIN
                        CREATE TABLE ChatMessages (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            ChatSessionId INT NOT NULL,
                            Role NVARCHAR(20) NOT NULL DEFAULT 'user',
                            Content NVARCHAR(MAX) NOT NULL DEFAULT '',
                            AiModel NVARCHAR(50) NULL,
                            InputTokens INT NULL,
                            OutputTokens INT NULL,
                            CostUsd FLOAT NULL,
                            DurationMs INT NULL,
                            ReferencedEventIds NVARCHAR(500) NULL,
                            Status NVARCHAR(20) NOT NULL DEFAULT 'completed',
                            ErrorMessage NVARCHAR(MAX) NULL,
                            CreatedAt DATETIME2 NOT NULL,
                            CONSTRAINT FK_ChatMessages_ChatSessions FOREIGN KEY (ChatSessionId)
                                REFERENCES ChatSessions(Id) ON DELETE CASCADE
                        );
                        CREATE INDEX IX_ChatMessages_ChatSessionId ON ChatMessages (ChatSessionId);
                        CREATE INDEX IX_ChatMessages_CreatedAt ON ChatMessages (CreatedAt);
                        PRINT 'Created ChatMessages table.';
                    END");

                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'ChatPromptTemplates') AND type = 'U')
                    BEGIN
                        CREATE TABLE ChatPromptTemplates (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Name NVARCHAR(100) NOT NULL DEFAULT '',
                            Category NVARCHAR(50) NOT NULL DEFAULT 'custom',
                            SystemPrompt NVARCHAR(MAX) NOT NULL DEFAULT '',
                            UserPromptTemplate NVARCHAR(MAX) NOT NULL DEFAULT '',
                            IsBuiltIn BIT NOT NULL DEFAULT 0,
                            SortOrder INT NOT NULL DEFAULT 0,
                            CreatedAt DATETIME2 NOT NULL,
                            UpdatedAt DATETIME2 NOT NULL
                        );
                        CREATE INDEX IX_ChatPromptTemplates_Category ON ChatPromptTemplates (Category);
                        PRINT 'Created ChatPromptTemplates table.';
                    END");

                // Add PeriodDescription column if missing
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ChatSessions') AND name = 'PeriodDescription')
                    BEGIN
                        ALTER TABLE ChatSessions ADD PeriodDescription NVARCHAR(500) NULL;
                        PRINT 'Added PeriodDescription column to ChatSessions.';
                    END");

                // Add PeriodsJson column if missing
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ChatSessions') AND name = 'PeriodsJson')
                    BEGIN
                        ALTER TABLE ChatSessions ADD PeriodsJson NVARCHAR(MAX) NULL;
                        PRINT 'Added PeriodsJson column to ChatSessions.';
                    END");

                logger.LogInformation("Chat tables check complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not verify Chat tables: {Message}", tableEx.Message);
            }

            // Create FoodItems and FoodEventLinks tables if they don't exist
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'FoodItems') AND type = 'U')
                    BEGIN
                        CREATE TABLE FoodItems (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            Name NVARCHAR(200) NOT NULL DEFAULT '',
                            NormalizedName NVARCHAR(200) NOT NULL DEFAULT '',
                            Category NVARCHAR(50) NULL,
                            OccurrenceCount INT NOT NULL DEFAULT 0,
                            AvgSpike FLOAT NULL,
                            AvgGlucoseAtEvent FLOAT NULL,
                            AvgGlucoseMax FLOAT NULL,
                            AvgGlucoseMin FLOAT NULL,
                            AvgRecoveryMinutes FLOAT NULL,
                            WorstSpike FLOAT NULL,
                            BestSpike FLOAT NULL,
                            GreenCount INT NOT NULL DEFAULT 0,
                            YellowCount INT NOT NULL DEFAULT 0,
                            RedCount INT NOT NULL DEFAULT 0,
                            FirstSeen DATETIME2 NOT NULL,
                            LastSeen DATETIME2 NOT NULL,
                            CreatedAt DATETIME2 NOT NULL,
                            UpdatedAt DATETIME2 NOT NULL
                        );
                        CREATE INDEX IX_FoodItems_NormalizedName ON FoodItems (NormalizedName);
                        CREATE INDEX IX_FoodItems_OccurrenceCount ON FoodItems (OccurrenceCount);
                        PRINT 'Created FoodItems table.';
                    END

                    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'FoodEventLinks') AND type = 'U')
                    BEGIN
                        CREATE TABLE FoodEventLinks (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            FoodItemId INT NOT NULL,
                            GlucoseEventId INT NOT NULL,
                            Spike FLOAT NULL,
                            GlucoseAtEvent FLOAT NULL,
                            AiClassification NVARCHAR(10) NULL,
                            RecoveryMinutes FLOAT NULL,
                            CreatedAt DATETIME2 NOT NULL,
                            CONSTRAINT FK_FoodEventLinks_FoodItems FOREIGN KEY (FoodItemId) REFERENCES FoodItems(Id) ON DELETE CASCADE,
                            CONSTRAINT FK_FoodEventLinks_GlucoseEvents FOREIGN KEY (GlucoseEventId) REFERENCES GlucoseEvents(Id) ON DELETE CASCADE
                        );
                        CREATE INDEX IX_FoodEventLinks_FoodItemId ON FoodEventLinks (FoodItemId);
                        CREATE INDEX IX_FoodEventLinks_GlucoseEventId ON FoodEventLinks (GlucoseEventId);
                        CREATE UNIQUE INDEX IX_FoodEventLinks_FoodItem_Event ON FoodEventLinks (FoodItemId, GlucoseEventId);
                        PRINT 'Created FoodEventLinks table.';
                    END");
                logger.LogInformation("FoodItems/FoodEventLinks table check complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not verify Food tables: {Message}", tableEx.Message);
            }

            // Add bilingual columns to GlucoseEvents and FoodItems
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('GlucoseEvents') AND name = 'NoteTitleEn')
                    BEGIN
                        ALTER TABLE GlucoseEvents ADD NoteTitleEn NVARCHAR(500) NULL;
                        ALTER TABLE GlucoseEvents ADD NoteContentEn NVARCHAR(MAX) NULL;
                        PRINT 'Added English translation columns to GlucoseEvents.';
                    END

                    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('FoodItems') AND name = 'NameEn')
                    BEGIN
                        ALTER TABLE FoodItems ADD NameEn NVARCHAR(200) NULL;
                        PRINT 'Added NameEn column to FoodItems.';
                    END");
                logger.LogInformation("Bilingual column migration check complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not add bilingual columns: {Message}", tableEx.Message);
            }

            // Seed built-in chat prompt templates
            try
            {
                db.Database.ExecuteSqlRaw(@"
                    IF NOT EXISTS (SELECT 1 FROM ChatPromptTemplates WHERE IsBuiltIn = 1 AND Name = 'Period Summary')
                    BEGIN
                        INSERT INTO ChatPromptTemplates (Name, Category, SystemPrompt, UserPromptTemplate, IsBuiltIn, SortOrder, CreatedAt, UpdatedAt)
                        VALUES (
                            'Period Summary',
                            'summary',
                            'You are a diabetes management assistant generating a comprehensive summary of a glucose monitoring period.
Analyze the glucose data and events provided. Include: overall control assessment, key metrics, patterns, meal/activity impacts, overnight and morning analysis, and actionable insights.
Use mg/dL units. Format with markdown. Be friendly and supportive. When mentioning events, use event #ID format for clickable links.
All timestamps are in the user''s local time.

{{glucose_data}}',
                            'Please provide a comprehensive summary analysis for this period: {{period_label}}

Focus on overall glucose control, patterns, and actionable recommendations.',
                            1, 1, GETUTCDATE(), GETUTCDATE()
                        );
                        PRINT 'Seeded Period Summary template.';
                    END

                    IF NOT EXISTS (SELECT 1 FROM ChatPromptTemplates WHERE IsBuiltIn = 1 AND Name = 'Period Comparison')
                    BEGIN
                        INSERT INTO ChatPromptTemplates (Name, Category, SystemPrompt, UserPromptTemplate, IsBuiltIn, SortOrder, CreatedAt, UpdatedAt)
                        VALUES (
                            'Period Comparison',
                            'comparison',
                            'You are a diabetes management assistant comparing glucose patterns. Analyze the glucose data and events provided. Compare key metrics, patterns, event impacts, and identify what caused differences. Provide actionable insights.
Use mg/dL units. Format with markdown. Be friendly and supportive. When mentioning events, use event #ID format for clickable links.
All timestamps are in the user''s local time.

{{glucose_data}}',
                            'Compare the glucose patterns in this period: {{period_label}}

What trends do you see? What days or time periods show the best and worst control? What patterns stand out?',
                            1, 2, GETUTCDATE(), GETUTCDATE()
                        );
                        PRINT 'Seeded Period Comparison template.';
                    END

                    IF NOT EXISTS (SELECT 1 FROM ChatPromptTemplates WHERE IsBuiltIn = 1 AND Name = 'Event Deep Dive')
                    BEGIN
                        INSERT INTO ChatPromptTemplates (Name, Category, SystemPrompt, UserPromptTemplate, IsBuiltIn, SortOrder, CreatedAt, UpdatedAt)
                        VALUES (
                            'Event Deep Dive',
                            'custom',
                            'You are a diabetes management assistant specializing in analyzing specific meals, foods, and activities and their glucose impact. Analyze the events and glucose data provided in detail.
Use mg/dL units. Format with markdown. Be friendly and supportive. When mentioning events, use event #ID format for clickable links.
All timestamps are in the user''s local time.

{{glucose_data}}',
                            'Please analyze the events (meals/activities) in this period in detail: {{period_label}}

Which events had the biggest glucose impact? Are there any recurring patterns with specific foods or activities?',
                            1, 3, GETUTCDATE(), GETUTCDATE()
                        );
                        PRINT 'Seeded Event Deep Dive template.';
                    END

                    IF NOT EXISTS (SELECT 1 FROM ChatPromptTemplates WHERE IsBuiltIn = 1 AND Name = 'General Question')
                    BEGIN
                        INSERT INTO ChatPromptTemplates (Name, Category, SystemPrompt, UserPromptTemplate, IsBuiltIn, SortOrder, CreatedAt, UpdatedAt)
                        VALUES (
                            'General Question',
                            'custom',
                            'You are a knowledgeable diabetes management assistant. The user is tracking their glucose with a CGM. Help them understand their data and answer questions.
Use mg/dL units. Format with markdown. Be friendly. When mentioning events, use event #ID format for clickable links. All timestamps are in the user''s local time.

{{glucose_data}}',
                            '{{user_message}}',
                            1, 4, GETUTCDATE(), GETUTCDATE()
                        );
                        PRINT 'Seeded General Question template.';
                    END");

                logger.LogInformation("Chat template seeding complete.");
            }
            catch (Exception tableEx)
            {
                logger.LogWarning("Could not seed chat templates: {Message}", tableEx.Message);
            }

            break;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Database not ready yet ({Attempt}/30): {Message}", i + 1, ex.Message);
            Thread.Sleep(3000);
        }
    }
}

// ── Middleware ───────────────────────────────────────────
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHub<GlucoseHub>("/glucosehub");

// Log application startup
try
{
    var eventLogger = app.Services.GetRequiredService<IEventLogger>();
    _ = eventLogger.LogInfoAsync(
        GlucoseAPI.Application.Interfaces.EventCategory.System,
        "Application started.",
        source: "Program");
}
catch { /* best effort */ }

app.Run();

// ── Make Program class accessible for integration tests ──
public partial class Program { }
