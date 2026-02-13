# Glucose Monitor — Technical Design

## Architecture Overview

Glucose Monitor is a **three-container Docker Compose application** consisting of:

| Container | Technology | Port | Role |
|-----------|-----------|------|------|
| `api` | ASP.NET Core 8.0 (C#) | 8080 | REST API, SignalR hub, background services |
| `web` | React 18 + nginx | 3000 (→80) | Single-page application (SPA) |
| `sqlserver` | SQL Server 2022 | 1433 | Relational database |

The nginx reverse proxy in the `web` container forwards `/api/*` and `/glucosehub` to the `api` container, so the browser only communicates with port 3000.

```
┌──────────────────────────────────────────────────────────────────┐
│                      Docker Compose Stack                        │
│                                                                  │
│  ┌──────────┐    ┌────────────────────────┐    ┌──────────────┐ │
│  │  nginx   │───▶│   ASP.NET Core API     │───▶│  SQL Server  │ │
│  │ (React)  │    │  + Background Services │    │   2022       │ │
│  │  :3000   │◀───│  + SignalR Hub         │    │   :1433      │ │
│  │          │ WS │         :8080          │    │              │ │
│  └──────────┘    └────────────────────────┘    └──────────────┘ │
│       ▲                    ▲      ▲                             │
│       │                    │      │                             │
│   Browser              Volumes    │                             │
│                     /samsung-notes│(read-only)                  │
│                     /backup       │(read-write)                 │
│                                   │                             │
│                        External APIs:                           │
│                        • LibreLink Up (glucose data)            │
│                        • OpenAI GPT (AI analysis)               │
└──────────────────────────────────────────────────────────────────┘
```

## Backend: ASP.NET Core 8.0

### Project Structure

```
GlucoseAPI/
├── Program.cs                    # App startup, DI, database init, middleware
├── GlucoseAPI.csproj             # .NET 8 project, NuGet packages
├── appsettings.json              # Default config (overridden by env vars)
├── Dockerfile                    # Multi-stage build (SDK → runtime) + font deps
│
├── Domain/                       # ── Pure business logic (no I/O, no dependencies) ──
│   └── Services/
│       ├── GlucoseStatsCalculator.cs   # Glucose stats computation + value objects
│       ├── ClassificationParser.cs     # AI classification tag parsing
│       └── TimeZoneConverter.cs        # Timezone resolution + UTC ↔ local conversion
│
├── Application/                  # ── Use cases, CQRS handlers, ports ──
│   ├── Interfaces/
│   │   ├── IGptClient.cs               # GPT API abstraction + GptAnalysisResult record
│   │   ├── INotificationService.cs     # Real-time notification abstraction
│   │   └── IEventLogger.cs             # Central event logging abstraction + EventCategory constants
│   └── Features/                       # MediatR CQRS handlers (one file per use case)
│       ├── Glucose/                    # GetLatestReading, GetHistory, GetStats, GetDates
│       ├── Events/                     # GetEvents, GetEventDetail, GetStatus, Reprocess
│       ├── Comparisons/               # CreateComparison, GetComparisons, GetDetail, Delete
│       ├── PeriodSummaries/           # CreatePeriodSummary, GetPeriodSummaries, GetDetail, Delete
│       ├── EventLogs/                 # GetEventLogs (filtered + paginated)
│       ├── DailySummaries/             # GetSummaries, GetDetail, GetStatus, GetSnapshot, Trigger
│       ├── Notes/                      # GetNotes, GetNote, GetFolders, GetStatus, Media
│       ├── AiUsage/                    # GetLogs, GetSummary, GetPricing
│       ├── Settings/                   # GetLibre, SaveLibre, GetAnalysis, SaveAnalysis, Test
│       ├── Reports/                    # GenerateReport (PDF)
│       └── Sync/                       # TriggerFull, TriggerGlucose, TriggerNotes
│
├── Infrastructure/               # ── External integrations (adapters) ──
│   ├── ExternalApis/
│   │   ├── OpenAiGptClient.cs          # IGptClient implementation via IHttpClientFactory
│   │   └── GptModels.cs                # Shared GPT request/response DTOs
│   ├── Notifications/
│   │   └── SignalRNotificationService.cs # INotificationService via SignalR hub
│   └── Logging/
│       └── EventLogger.cs             # IEventLogger implementation (DB + SignalR push)
│
├── Controllers/                  # REST API endpoints (thin MediatR dispatchers)
│   ├── GlucoseController.cs      # /api/glucose/* — readings, stats, history
│   ├── EventsController.cs       # /api/events/* — meal/activity events
│   ├── ComparisonController.cs  # /api/comparison/* — period comparisons
│   ├── PeriodSummaryController.cs # /api/periodsummary/* — arbitrary period summaries
│   ├── DailySummariesController.cs # /api/dailysummaries/* — daily summaries
│   ├── EventLogController.cs     # /api/eventlog — application event log (filtered + paginated)
│   ├── NotesController.cs        # /api/notes/* — Samsung Notes
│   ├── SettingsController.cs     # /api/settings/* — LibreLink + analysis config
│   ├── AiUsageController.cs      # /api/aiusage/* — GPT usage tracking
│   ├── ReportsController.cs      # /api/reports/* — PDF report generation
│   └── SyncController.cs         # /api/sync/* — manual data sync trigger
│
├── Data/
│   └── GlucoseDbContext.cs       # EF Core DbContext, all DbSets and indexes
│
├── Hubs/
│   └── GlucoseHub.cs             # SignalR hub (connect/disconnect logging)
│
├── Models/                       # Entity models + DTOs
│   ├── GlucoseReading.cs         # CGM reading (value, timestamp, trend)
│   ├── GlucoseEvent.cs           # Meal/activity event + AI analysis + history
│   ├── GlucoseComparison.cs     # Period comparison entity + DTOs
│   ├── PeriodSummary.cs         # Arbitrary period summary entity + DTOs
│   ├── EventLog.cs              # Application event log entity + DTOs
│   ├── DailySummary.cs           # Daily aggregation entity
│   ├── DailySummarySnapshot.cs   # Immutable snapshot per generation run
│   ├── SamsungNote.cs            # Synced Samsung Notes metadata
│   ├── AppSettings.cs            # Key-value settings + DTOs
│   ├── LibreLinkModels.cs        # LibreLink Up API response models
│   └── Dtos.cs                   # GlucoseReadingDto, GlucoseStatsDto
│
└── Services/                     # Application services (orchestration + I/O)
    ├── GlucoseFetchService.cs    # Polls LibreLink Up API for new readings
    ├── SamsungNotesSyncService.cs # Syncs Samsung Notes from local SQLite DB
    ├── GlucoseEventAnalysisService.cs # Correlates notes → events, orchestrates AI
    ├── EventAnalyzer.cs          # Scoped: runs GPT analysis on a single event
    ├── DailySummaryService.cs    # Generates daily summaries with GPT analysis
    ├── DataBackupService.cs      # Periodic JSON/CSV backup to local filesystem
    ├── ComparisonService.cs      # Background: processes period comparisons with AI
    ├── PeriodSummaryService.cs  # Background: processes arbitrary period summaries with AI
    ├── DatabaseBackupService.cs  # Daily SQL Server .bak backup + manual trigger/restore
    ├── ReportService.cs          # Generates PDF reports using QuestPDF + SkiaSharp
    ├── LibreLinkClient.cs        # Unofficial LibreLink Up HTTP client
    ├── SamsungNotesReader.cs     # Reads Samsung Notes SQLite + wdoc files
    └── SettingsService.cs        # DB-backed settings with config fallback

GlucoseAPI.Tests/                 # ── Automated test suite ──
├── GlucoseAPI.Tests.csproj       # xUnit, Moq, FluentAssertions, MVC Testing
├── Domain/                       # Unit tests for pure domain logic
│   ├── GlucoseStatsCalculatorTests.cs
│   ├── ClassificationParserTests.cs
│   └── TimeZoneConverterTests.cs
├── Handlers/                     # Unit tests for MediatR handlers (InMemory DB)
│   ├── GlucoseHandlerTests.cs    # GetLatestReading, GetHistory, GetStats, GetDates
│   ├── EventHandlerTests.cs      # GetEvents, GetEventDetail, GetEventsStatus
│   ├── DailySummaryHandlerTests.cs # GetSummaries, GetDetail, GetStatus, GetSnapshot
│   └── AiUsageHandlerTests.cs    # GetLogs, GetSummary, GetPricing
├── Services/                     # Unit tests for application services (mocked deps)
│   └── EventAnalyzerTests.cs
└── Integration/                  # Integration tests (WebApplicationFactory + InMemory DB)
    └── ApiIntegrationTests.cs
```

### Dependency Injection Setup

```
Program.cs registers:

  MediatR (CQRS):
    - RegisterServicesFromAssembly  — auto-discovers all IRequestHandler<T> in the API assembly
    - Controllers dispatch IRequest<T> via IMediator, keeping them thin

  Domain Services (Scoped):
    - TimeZoneConverter           — timezone resolution + UTC ↔ local conversion
    - AiCostCalculator (static)   — AI cost computation (no DI needed)

  Application Interfaces → Infrastructure Implementations:
    - IGptClient → OpenAiGptClient           (Scoped)
    - INotificationService → SignalRNotificationService (Singleton)
    - IEventLogger → EventLogger             (Singleton — writes to DB + SignalR push)

  Named HttpClients (IHttpClientFactory):
    - "OpenAI"    — BaseAddress: https://api.openai.com/, Accept: application/json
    - "LibreLink" — Custom headers + handler (cookies, gzip/deflate/brotli)

  Application Services (Scoped):
    - GlucoseDbContext (EF Core → SQL Server)
    - LibreLinkClient             — uses IHttpClientFactory("LibreLink")
    - SettingsService
    - SamsungNotesReader
    - EventAnalyzer               — uses IGptClient, INotificationService, TimeZoneConverter
                                     queries overlapping events for multi-event context
    - ReportService               — PDF generation (QuestPDF + SkiaSharp)

  Singleton + HostedService (callable from handlers):
    - GlucoseFetchService        — polls LibreLink every N minutes
      (TriggerSyncAsync for manual sync via TriggerFullSyncHandler)
    - SamsungNotesSyncService    — syncs notes every 10 minutes
      (TriggerSyncAsync for manual sync via TriggerNotesSyncHandler)
    - DailySummaryService        — generates daily summaries every 30 minutes
      (TriggerGenerationAsync for manual trigger via TriggerDailySummaryHandler)

  Singleton + HostedService (callable from controllers/handlers):
    - ComparisonService          — processes period comparisons in background
      (Enqueue from CreateComparison handler, processes queue, calls GPT)
    - PeriodSummaryService       — processes arbitrary period summaries in background
      (Enqueue from CreatePeriodSummary handler, processes queue, calls GPT)
    - DatabaseBackupService      — daily SQL Server .bak backup (retained 7 days)
      (TriggerBackupAsync / RestoreFromBackupAsync for manual backup/restore via Settings)

  Hosted Services (BackgroundService only):
    - GlucoseEventAnalysisService — correlates + analyzes every N minutes
    - DataBackupService          — backs up every 6 hours

  SignalR:
    - GlucoseHub at /glucosehub
```

### Database Schema (SQL Server 2022)

The database is created via `EnsureCreated()` at startup, with additional `ALTER TABLE` statements for schema evolution. No EF Core migrations are used — table creation and column additions are done via raw SQL with `IF NOT EXISTS` guards.

**Tables and relationships:**

```
┌────────────────────┐     ┌───────────────────────┐
│  GlucoseReadings   │     │    SamsungNotes        │
│────────────────────│     │───────────────────────│
│ Id (PK)            │     │ Id (PK)               │
│ Value (mg/dL)      │     │ Uuid (unique)         │
│ Timestamp (UTC)    │     │ Title                 │
│ TrendArrow (1-5)   │     │ TextContent           │
│ IsHigh, IsLow      │     │ ModifiedAt            │
│ PatientId          │     │ FolderName            │
│ CreatedAt          │     │ HasMedia, HasPreview   │
└────────────────────┘     └───────────┬───────────┘
         │                             │
         │  (joined by timestamp)      │ (1:1 via NoteUuid)
         ▼                             ▼
┌──────────────────────────────────────────────┐
│              GlucoseEvents                    │
│──────────────────────────────────────────────│
│ Id (PK)                                      │
│ SamsungNoteId, NoteUuid (unique), NoteTitle   │
│ NoteContent                                  │
│ EventTimestamp (UTC)                          │
│ PeriodStart, PeriodEnd (UTC)                 │
│ ReadingCount, GlucoseAtEvent                 │
│ GlucoseMin, GlucoseMax, GlucoseAvg          │
│ GlucoseSpike, PeakTime                       │
│ AiAnalysis (text), AiClassification (enum)   │
│ IsProcessed, ProcessedAt                     │
└──────────────────┬───────────────────────────┘
                   │
                   │ (1:N)
                   ▼
┌──────────────────────────────────────────────┐
│         EventAnalysisHistory                  │
│──────────────────────────────────────────────│
│ Id (PK)                                      │
│ GlucoseEventId (FK)                          │
│ AiAnalysis, AiClassification                 │
│ AnalyzedAt, Reason                           │
│ PeriodStart, PeriodEnd, ReadingCount         │
│ GlucoseAtEvent, Min, Max, Avg, Spike, Peak   │
└──────────────────────────────────────────────┘

┌──────────────────────────────────────────────┐
│            DailySummaries                     │
│──────────────────────────────────────────────│
│ Id (PK)                                      │
│ Date (unique — one row per calendar day)     │
│ PeriodStartUtc, PeriodEndUtc, TimeZone       │
│ EventCount, EventIds, EventTitles            │
│ ReadingCount                                 │
│ GlucoseMin, Max, Avg, StdDev                │
│ TimeInRange, TimeAboveRange, TimeBelowRange  │
│ AiAnalysis, AiClassification                 │
│ IsProcessed, ProcessedAt                     │
└──────────────────┬───────────────────────────┘
                   │
                   │ (1:N)
                   ▼
┌──────────────────────────────────────────────┐
│       DailySummarySnapshots                   │
│──────────────────────────────────────────────│
│ Id (PK)                                      │
│ DailySummaryId (FK)                          │
│ Date, GeneratedAt, Trigger (auto/manual)     │
│ DataStartUtc, DataEndUtc                     │
│ FirstReadingUtc, LastReadingUtc              │
│ (all same stats as DailySummary)             │
│ AiAnalysis, AiClassification, IsProcessed    │
└──────────────────────────────────────────────┘

┌──────────────────────────────────────────────┐
│          GlucoseComparisons                   │
│──────────────────────────────────────────────│
│ Id (PK)                                      │
│ Name (optional label)                        │
│ PeriodAStart, PeriodAEnd (UTC)               │
│ PeriodALabel                                 │
│ PeriodBStart, PeriodBEnd (UTC)               │
│ PeriodBLabel                                 │
│ TimeZone                                     │
│ PeriodA*: ReadingCount, Min, Max, Avg,       │
│   StdDev, TimeInRange, TimeAbove, TimeBelow, │
│   EventCount, EventTitles                    │
│ PeriodB*: (same set of stats)                │
│ AiAnalysis, AiClassification                 │
│ Status (pending/processing/completed/failed) │
│ ErrorMessage, CreatedAt, CompletedAt         │
└──────────────────────────────────────────────┘

┌──────────────────────────────────────────────┐
│          PeriodSummaries                      │
│──────────────────────────────────────────────│
│ Id (PK)                                      │
│ Name (optional label)                        │
│ PeriodStart, PeriodEnd (UTC)                 │
│ TimeZone                                     │
│ ReadingCount, GlucoseMin, GlucoseMax,        │
│   GlucoseAvg, GlucoseStdDev                 │
│ TimeInRange, TimeAboveRange, TimeBelowRange  │
│ EventCount, EventIds, EventTitles            │
│ AiAnalysis, AiClassification                 │
│ Status (pending/processing/completed/failed) │
│ ErrorMessage, CreatedAt, CompletedAt         │
└──────────────────────────────────────────────┘

┌──────────────────────────────────────────────┐
│            AiUsageLogs                        │
│──────────────────────────────────────────────│
│ Id (PK)                                      │
│ GlucoseEventId (nullable FK)                 │
│ Model, InputTokens, OutputTokens, TotalTokens│
│ Reason, Success, HttpStatusCode              │
│ FinishReason, CalledAt, DurationMs           │
└──────────────────────────────────────────────┘

┌──────────────────────────────────────────────┐
│            EventLogs                          │
│──────────────────────────────────────────────│
│ Id (PK)                                      │
│ Timestamp (UTC)                              │
│ Level (info/warning/error)                   │
│ Category (glucose/notes/events/analysis/     │
│           daily/comparison/summary/backup/   │
│           settings/system/sync)              │
│ Message (≤500 chars)                         │
│ Detail (optional extended description)       │
│ Source (service name)                        │
│ RelatedEntityId, RelatedEntityType           │
│ NumericValue, DurationMs                     │
└──────────────────────────────────────────────┘

┌──────────────────────────────────────────────┐
│            AppSettings                        │
│──────────────────────────────────────────────│
│ Id (PK)                                      │
│ Key (unique), Value, UpdatedAt               │
└──────────────────────────────────────────────┘
```

**Indexes:**
- `GlucoseReadings`: Timestamp, PatientId, (PatientId + Timestamp unique composite)
- `SamsungNotes`: Uuid (unique), ModifiedAt
- `GlucoseEvents`: NoteUuid (unique), EventTimestamp, IsProcessed
- `EventAnalysisHistory`: GlucoseEventId, AnalyzedAt
- `AiUsageLogs`: CalledAt, GlucoseEventId, Model
- `GlucoseComparisons`: Status, CreatedAt
- `PeriodSummaries`: Status, CreatedAt
- `EventLogs`: Timestamp, Level, Category
- `DailySummaries`: Date (unique), IsProcessed
- `DailySummarySnapshots`: DailySummaryId, Date, GeneratedAt

### Background Services Architecture

All background services extend `BackgroundService` (`IHostedService`) and run as long-lived loops with `Task.Delay` between iterations.

#### 1. GlucoseFetchService (every N minutes, default 5)
```
Startup delay: 15 seconds
Loop:
  1. Load LibreLink settings from SettingsService (DB → env fallback)
  2. Configure LibreLinkClient with credentials
  3. Call LibreLink Up Graph API → get current + historical readings
  4. Filter to readings newer than latest in DB
  5. Insert new readings (deduplicated by PatientId + Timestamp)
  6. SignalR → "NewGlucoseData"
  7. Recalculate any GlucoseEvents whose period overlaps with new readings
     → recompute glucose stats, mark as IsProcessed=false for re-analysis
     → SignalR → "EventsUpdated"
```

#### 2. SamsungNotesSyncService (every 10 minutes)
```
Startup delay: 20 seconds
Loop:
  1. Check if Samsung Notes data path exists
  2. Copy SQLite DB to temp dir (avoid lock conflicts with Samsung Notes app)
  3. Read notes from NoteDB table (+ CategoryTreeDB for folder names)
  4. Fallback: schema discovery if NoteDB doesn't exist
  5. For each note: upsert into SamsungNotes table
  6. Extract text from wdoc binary files if StrippedContent is empty
  7. SignalR → "NotesUpdated"
```

#### 3. GlucoseEventAnalysisService (every N minutes, default 15)
```
Startup delay: 30 seconds
Loop:
  1. Load notes from the configured folder (e.g., "Cukier")
  2. Find notes not yet turned into GlucoseEvents
  3. For each new note:
     a. Calculate PeriodStart = previous note's timestamp (or -3h if no previous note)
     b. Calculate PeriodEnd = max(event + 3h, next note's timestamp) or +4h if no next note
        → Always captures at least 3 hours of post-event glucose data (MinimumLookahead),
          ensuring the full post-meal response is visible even when the next event is sooner.
          If the next event is further than 3h away, the period extends to cover the gap.
     c. Update the PREVIOUS event's PeriodEnd = max(prevEvent + 3h, current event timestamp)
        → Previous event also keeps at least 3h of glucose data; periods may overlap.
     d. Recompute previous event's glucose stats → mark for re-analysis
     e. Create new GlucoseEvent with computed stats
  4. Run AI analysis for all unanalyzed events (via EventAnalyzer):
     a. Apply cooldown for re-analyses (configurable, default 30 min)
     b. Query overlapping events (other events whose timestamp falls within the current
        event's [PeriodStart, PeriodEnd] window) so the AI can account for their impact
     c. Call GPT API with glucose data + note content + overlapping event context
     d. Parse [CLASSIFICATION: green/yellow/red] from response
     e. Save to EventAnalysisHistory (immutable history)
     f. Update GlucoseEvent with latest analysis
     g. Log to AiUsageLogs
  5. SignalR → "EventsUpdated", "AiUsageUpdated"
```

#### 4. DailySummaryService (every 30 minutes)
```
Startup delay: 60 seconds
Loop:
  1. Find date range with glucose data (earliest reading → yesterday)
  2. Detect partial past days: any processed summary whose PeriodEndUtc is
     more than 5 minutes before the actual end-of-day boundary was generated
     from incomplete data and is queued for regeneration with full-day data.
  3. Skip days that already have IsProcessed=true summaries (unless partial)
  4. For each unprocessed or partial day:
     a. Convert local midnight boundaries → UTC
     b. Query all readings and events within the day
     c. Compute day-level stats (avg, std dev, time-in-range, etc.)
     d. Compute overnight stats (00:00–06:00): avg, range, std dev, trend direction
     e. Compute morning stats (06:00–09:00): fasting glucose, avg, range,
        dawn phenomenon detection (03:00–04:00 vs 06:00–07:00),
        pre-diabetes flag if fasting avg ≥100 mg/dL
     f. Upsert DailySummary row
     g. Call GPT API with full day data (overnight, morning, hourly profile, events, timeline)
     f. Parse [CLASSIFICATION: green/yellow/red]
     g. Create DailySummarySnapshot (immutable history)
     h. Log to AiUsageLogs
  5. SignalR → "DailySummariesUpdated", "AiUsageUpdated"

Manual trigger (POST /api/dailysummaries/trigger):
  - Same logic but includeToday=true
  - Today's PeriodEnd is capped at now (partial day)
  - Always regenerates today even if already processed
```

#### 5. DataBackupService (every 6 hours)
```
Startup delay: 2 minutes
Loop:
  1. Create timestamped snapshot directory (e.g., 2026-02-08_13-57-35/)
  2. Export glucose_readings.json + glucose_readings.csv
  3. Export glucose_events.json
  4. Export analysis_history.json
  5. Export daily_summaries.json + daily_summaries.csv
  6. Export daily_summary_snapshots.json
  7. Copy all files to latest/ directory
  8. Delete snapshot directories older than 14 days
```

#### 6. ComparisonService (on-demand queue)
```
Startup:
  - Re-queues any comparisons left in "pending" or "processing" state (crash recovery)
Queue processing (event-driven, not polling):
  1. Wait for signal (SemaphoreSlim)
  2. Dequeue comparison ID
  3. Set status = "processing", notify UI via SignalR
  4. Gather glucose readings + events for Period A → compute day stats
  5. Gather glucose readings + events for Period B → compute day stats
  6. Build AI prompt with both periods' stats, events, and sampled timelines
  7. Call GPT API for differential analysis
  8. Parse [CLASSIFICATION: green/yellow/red]
  9. Save results, set status = "completed"
  10. SignalR → "ComparisonsUpdated", "AiUsageUpdated"
  On failure: set status = "failed" with error message

Created via POST /api/comparison:
  - CreateComparisonHandler inserts row with status = "pending"
  - Calls ComparisonService.Enqueue(id)
```

#### 7. PeriodSummaryService (on-demand queue)
```
Startup:
  - Re-queues any period summaries left in "pending" or "processing" state (crash recovery)
Queue processing (event-driven, not polling):
  1. Wait for signal (SemaphoreSlim)
  2. Dequeue period summary ID
  3. Set status = "processing", notify UI via SignalR
  4. Gather glucose readings + events for the requested time range
  5. Compute day-level stats (min, max, avg, stddev, time in range)
  6. Build AI prompt with period stats, events, and sampled glucose timeline
  7. Call GPT API for comprehensive period analysis
  8. Parse [CLASSIFICATION: green/yellow/red]
  9. Save results, set status = "completed"
  10. SignalR → "PeriodSummariesUpdated", "AiUsageUpdated"
  On failure: set status = "failed" with error message

Created via POST /api/periodsummary:
  - CreatePeriodSummaryHandler inserts row with status = "pending"
  - Calls PeriodSummaryService.Enqueue(id)
```

#### 8. DatabaseBackupService (once per day)
```
Startup delay: 3 minutes
On startup:
  - Scans /backup/db/ for existing .bak files to populate status (last backup date, size)
Loop:
  1. Ensure /backup/db/ directory exists, chmod 777 for SQL Server write access
  2. Execute: BACKUP DATABASE [GlucoseDb] TO DISK WITH FORMAT, INIT, COMPRESSION
     → File: /backup/db/GlucoseDb_YYYY-MM-DD_HH-mm-ss.bak
  3. Update in-memory status (last backup time, file name, size)
  4. Delete .bak files older than 7 days

Manual trigger (POST /api/settings/backup):
  - Same logic, callable from Settings page

Manual restore (POST /api/settings/backup/restore):
  1. Validate filename (path traversal prevention)
  2. Connect to master database (cannot restore while connected to GlucoseDb)
  3. ALTER DATABASE [GlucoseDb] SET SINGLE_USER WITH ROLLBACK IMMEDIATE
  4. RESTORE DATABASE [GlucoseDb] FROM DISK WITH REPLACE
  5. ALTER DATABASE [GlucoseDb] SET MULTI_USER
  6. On failure: best-effort recovery to MULTI_USER mode
```

### API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/glucose/latest` | Latest glucose reading |
| GET | `/api/glucose/history?hours=24` | Historical readings for time period |
| GET | `/api/glucose/stats?hours=24` | Aggregated stats for time period |
| GET | `/api/glucose/dates` | All dates that have readings |
| GET | `/api/events` | List all events (summary DTOs) |
| GET | `/api/events/{id}` | Event detail + readings + analysis history |
| GET | `/api/events/status` | Processing status (total/processed/pending) |
| POST | `/api/events/{id}/reprocess` | Trigger immediate AI re-analysis |
| GET | `/api/comparison` | List all period comparisons |
| GET | `/api/comparison/{id}` | Comparison detail + readings + events + AI |
| POST | `/api/comparison` | Create new comparison (queued for background processing) |
| DELETE | `/api/comparison/{id}` | Delete a comparison |
| GET | `/api/periodsummary` | List all period summaries |
| GET | `/api/periodsummary/{id}` | Period summary detail + readings + events + AI |
| POST | `/api/periodsummary` | Create new period summary (queued for background processing) |
| DELETE | `/api/periodsummary/{id}` | Delete a period summary |
| GET | `/api/eventlog` | List event logs (filtered + paginated) |
| GET | `/api/dailysummaries` | List all daily summaries |
| GET | `/api/dailysummaries/{id}` | Daily summary detail + events + readings + snapshots |
| GET | `/api/dailysummaries/status` | Processing status |
| GET | `/api/dailysummaries/snapshots/{id}` | Snapshot detail |
| POST | `/api/dailysummaries/trigger` | Manual trigger for all days |
| GET | `/api/notes` | List Samsung Notes (with folder/search filters) |
| GET | `/api/notes/{id}` | Single note detail |
| GET | `/api/notes/folders` | Unique folder names |
| GET | `/api/notes/status` | Sync status |
| GET | `/api/notes/{id}/preview` | Preview image |
| GET | `/api/notes/{id}/media` | List media files |
| GET | `/api/notes/{id}/media/{fileName}` | Download media file |
| GET | `/api/settings` | LibreLink settings (password masked) |
| PUT | `/api/settings` | Save LibreLink settings |
| GET | `/api/settings/analysis` | Analysis settings (API key masked) |
| PUT | `/api/settings/analysis` | Save analysis settings |
| POST | `/api/settings/test` | Test LibreLink connection |
| GET | `/api/settings/backup` | Database backup status (last backup, file list, sizes) |
| POST | `/api/settings/backup` | Trigger manual database backup |
| POST | `/api/settings/backup/restore` | Restore database from a backup file |
| GET | `/api/aiusage/logs?limit=&from=&to=` | AI usage log entries |
| GET | `/api/aiusage/summary?from=&to=` | Aggregated usage summary |
| GET | `/api/aiusage/pricing` | Known model pricing table |
| GET | `/api/reports/pdf?from=&to=` | Generate PDF report for date range (max 90 days) |
| POST | `/api/sync/trigger` | Manual sync of both glucose data and Samsung Notes |
| POST | `/api/sync/glucose` | Manual sync of glucose data only |
| POST | `/api/sync/notes` | Manual sync of Samsung Notes only |

### SignalR Events

| Event | Payload | Triggered By |
|-------|---------|-------------|
| `NewGlucoseData` | count (int) | GlucoseFetchService after inserting new readings |
| `NotesUpdated` | count (int) | SamsungNotesSyncService after syncing notes |
| `EventsUpdated` | count (int) | EventAnalyzer, GlucoseEventAnalysisService, GlucoseFetchService |
| `DailySummariesUpdated` | count (int) | DailySummaryService after generating a summary |
| `ComparisonsUpdated` | count (int) | ComparisonService after processing a comparison |
| `PeriodSummariesUpdated` | count (int) | PeriodSummaryService after processing a period summary |
| `EventLogsUpdated` | count (int) | EventLogger after writing any application event log entry |
| `AiUsageUpdated` | count (int) | EventAnalyzer, DailySummaryService, ComparisonService, PeriodSummaryService after API calls |

### AI Integration (OpenAI GPT)

- **Model**: `gpt-5-mini` (hardcoded in `EventAnalyzer.cs` and `DailySummaryService.cs`)
- **Max tokens**: 4096 completion tokens
- **API client**: `IGptClient` interface (implemented by `OpenAiGptClient`) handles all HTTP communication with OpenAI via `IHttpClientFactory`. Services like `EventAnalyzer` depend on `IGptClient`, not `HttpClient`.
- **Classification parsing**: The AI is instructed to begin each response with `[CLASSIFICATION: green|yellow|red]`. This is parsed by the domain service `ClassificationParser.Parse()` using a compiled regex, then stripped from the stored analysis text. Classification is stored in a separate `AiClassification` column.
- **Glucose stats**: Computed by the domain service `GlucoseStatsCalculator` — centralizing the logic that was previously duplicated across services.
- **Event analysis prompt**: Includes before/after glucose readings, note content, and glucose statistics. When other events fall within the same glucose observation window (e.g., exercise 30 min after a meal), they are included as an "OTHER EVENTS IN THIS GLUCOSE WINDOW" section listing each overlapping event's title, time offset, content, glucose level, and classification. The system prompt instructs the AI to factor overlapping events into the analysis, attribute glucose patterns to combined effects, and flag when isolating the main event's impact is difficult. Asks for baseline assessment, response analysis, spike analysis, recovery, overall assessment, and a practical tip.
- **Daily summary prompt**: Includes full-day overview, dedicated overnight glucose analysis (00:00–06:00) with trend detection, morning glucose analysis (06:00–09:00) with dawn phenomenon detection and fasting glucose pre-diabetes flag (≥100 mg/dL), hourly glucose profile, event-by-event breakdown, and a sampled glucose timeline. Asks for day overview, key metrics, overnight analysis, morning glucose, meal impacts, patterns, best/worst moments, and actionable insights.
- **Usage logging**: Every API call (success or failure) is logged to `AiUsageLogs` with model name, token counts, HTTP status, finish reason, and duration. Clients are notified via `INotificationService.NotifyAiUsageUpdatedAsync()`.
- **Cost estimation**: `AiUsageController` maintains a static pricing dictionary for known models and computes estimated cost per call and in aggregate.

### Settings Management

Settings are stored in the `AppSettings` table as key-value pairs. The `SettingsService` class provides typed access with fallback to `IConfiguration` (environment variables / `appsettings.json`). Sensitive values (passwords, API keys) are masked in GET responses and preserved when the masked placeholder is sent back in PUT requests.

**Well-known keys:**
- `LibreLink:Email`, `LibreLink:Password`, `LibreLink:PatientId`, `LibreLink:Region`, `LibreLink:Version`, `LibreLink:FetchIntervalMinutes`
- `Analysis:GptApiKey`, `Analysis:NotesFolderName`, `Analysis:IntervalMinutes`, `Analysis:ReanalysisMinIntervalMinutes`
- `Display:TimeZone`

### LibreLink Up Integration

`LibreLinkClient` implements the unofficial LibreLink Up API (based on the reverse-engineered protocol from [nightscout-librelink-up](https://github.com/timoschlueter/nightscout-librelink-up)):

1. **Login**: POST to `/llu/auth/login` with email/password
2. **Region redirect**: If status=2 or redirect=true, re-login at the region-specific URL
3. **Connections**: GET `/llu/connections` → list of patient connections
4. **Graph data**: GET `/llu/connections/{patientId}/graph` → current measurement + historical readings
5. **Auth headers**: Bearer token + SHA-256 hash of user ID as `account-id` header

Supports 10 regions: ae, ap, au, ca, de, eu, eu2, fr, jp, us.

### Samsung Notes Integration

`SamsungNotesReader` reads the Samsung Notes Windows app's local SQLite database:

1. **Database location**: Mounted at `/samsung-notes` in the container (from Windows `%LOCALAPPDATA%\Packages\SAMSUNGELECTRONICSCoLtd.SamsungNotes_*\LocalState`)
2. **Safe reading**: Copies the database to a temp directory to avoid lock conflicts
3. **Schema support**: Primary: `NoteDB` + `CategoryTreeDB`. Fallback: dynamic schema discovery
4. **Text extraction**: Uses `StrippedContent` column; falls back to binary parsing of `wdoc/{uuid}/note.note` files
5. **Media access**: Reads from `wdoc/{uuid}/media/` and thumbnail directories

---

## Frontend: React 18

### Project Structure

```
glucose-ui/
├── Dockerfile             # Multi-stage (node build → nginx serve)
├── nginx.conf             # Reverse proxy config (API + SignalR → backend)
├── package.json           # Dependencies: react, recharts, date-fns, @microsoft/signalr
│
└── src/
    ├── App.js             # Main app: routing, SignalR, state management
    ├── App.css            # Global styles (dark theme, responsive)
    ├── index.js           # React entry point
    │
    └── components/
        ├── CurrentReading.js      # Live glucose value + stats bar
        ├── GlucoseChart.js        # Interactive Recharts line chart + event sidebar
        ├── GlucoseTable.js        # Tabular glucose readings
        ├── EventsPage.js          # Events list with classification badges
        ├── EventDetailModal.js    # Event detail modal (chart, overlapping events, analysis, history)
        ├── DailySummariesPage.js  # Daily summaries list + detail modal
        ├── ComparePage.js         # Period comparison (form, chart overlay, AI analysis)
        ├── PeriodSummaryPage.js   # Arbitrary period summaries (presets, custom, chart, AI analysis)
        ├── EventLogPage.js        # Application event log with filtering, pagination, real-time updates
        ├── NotesPage.js           # Samsung Notes browser
        ├── AiUsagePage.js         # AI usage dashboard (charts, logs, costs)
        ├── ReportsPage.js         # PDF report generation with date range selector
        └── SettingsPage.js        # LibreLink + analysis settings forms
```

### Key Design Decisions

1. **No router**: Navigation is managed via a `page` state variable in `App.js`. Tabs switch between page components: Dashboard, Events, Daily, Summaries, Compare, Event Log, AI Usage, Reports, Settings.
2. **SignalR → Custom Events**: The SignalR connection lives in `App.js`. Events like `NotesUpdated` and `EventsUpdated` are re-dispatched as `window.dispatchEvent(new CustomEvent(...))` so child components can listen independently without prop drilling.
3. **AI Usage versioning**: The `AiUsageUpdated` SignalR event increments an `aiUsageVersion` counter in App.js. The `AiUsagePage` component receives this as a React `key` prop, forcing a complete remount and fresh data fetch — solving the problem of browser-cached API responses.
4. **Cache busting**: AI usage API calls use `{ cache: 'no-store' }` to prevent browser HTTP caching.
5. **CSS-only dark theme**: The UI uses CSS custom properties for a dark theme with green/yellow/red classification colors.
6. **Recharts**: Used for all charts (glucose trends, daily usage, event details).

### Backend Key Design Decisions

1. **MediatR CQRS**: All 8 controllers are thin dispatchers — they parse the HTTP request, construct an `IRequest<T>` message, send it via `IMediator`, and map the result to HTTP. All business logic lives in `IRequestHandler<T>` handlers in `Application/Features/`. This enforces separation of concerns, makes handlers independently testable, and prevents controllers from accumulating business logic.
2. **One file per use case**: Each handler file contains the request record, result record, and handler class together (e.g., `GetLatestReading.cs` has `GetLatestReadingQuery`, `GetLatestReadingHandler`). This keeps related code colocated and avoids "DTO explosion" across multiple files.
3. **IHttpClientFactory**: All outbound HTTP calls (OpenAI, LibreLink Up) use named clients from `IHttpClientFactory`, preventing socket exhaustion.
4. **DDD domain services**: Pure static functions (`GlucoseStatsCalculator`, `AiCostCalculator`, `ClassificationParser`) contain reusable business logic with no I/O or DI dependencies, making them trivially testable and shareable across handlers and services.

### Real-Time Update Flow

```
Backend Service
    │
    ▼ (SignalR SendAsync)
GlucoseHub
    │
    ▼ (WebSocket)
App.js SignalR listener
    │
    ├──▶ NewGlucoseData → fetchData() (refresh dashboard)
    ├──▶ EventsUpdated → window.dispatchEvent('eventsUpdated')
    │       └──▶ EventsPage listens → reloads event list
    ├──▶ NotesUpdated → window.dispatchEvent('notesUpdated')
    │       └──▶ NotesPage listens → reloads notes
    ├──▶ DailySummariesUpdated → window.dispatchEvent('dailySummariesUpdated')
    │       └──▶ DailySummariesPage listens → reloads summaries
    ├──▶ ComparisonsUpdated → window.dispatchEvent('comparisonsUpdated')
    │       └──▶ ComparePage listens → reloads list + refreshes detail
    ├──▶ PeriodSummariesUpdated → window.dispatchEvent('periodSummariesUpdated')
    │       └──▶ PeriodSummaryPage listens → reloads list + refreshes detail
    ├──▶ EventLogsUpdated → window.dispatchEvent('eventLogsUpdated')
    │       └──▶ EventLogPage listens → reloads log entries in real time
    └──▶ AiUsageUpdated → setAiUsageVersion(v => v + 1)
            └──▶ AiUsagePage key={version} → remount → fresh fetch
```

---

## Deployment

### Docker Compose

```yaml
services:
  sqlserver:    # SQL Server 2022 with health check, /backup volume for .bak files
  api:          # ASP.NET Core (depends on sqlserver healthy), /backup volume shared with sqlserver
  web:          # React + nginx (depends on api)
```

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DB_PASSWORD` | `YourStrong!Passw0rd` | SQL Server SA password |
| `LIBRE_EMAIL` | — | LibreLink Up email |
| `LIBRE_PASSWORD` | — | LibreLink Up password |
| `LIBRE_PATIENT_ID` | (auto-detect) | Patient ID |
| `LIBRE_REGION` | `eu` | API region |
| `LIBRE_VERSION` | `4.16.0` | App version header |
| `FETCH_INTERVAL` | `5` | Glucose fetch interval (minutes) |
| `SAMSUNG_NOTES_PATH` | `./samsung-notes-placeholder` | Local Samsung Notes data path |
| `SAMSUNG_NOTES_INTERVAL` | `10` | Notes sync interval (minutes) |
| `BACKUP_PATH` | `./backup` | Local backup output directory |

### Database Initialization

On startup, `Program.cs` runs a retry loop (up to 30 attempts, 3s apart) to:
1. `EnsureCreated()` — creates the database and initial tables from EF Core model
2. Raw SQL `IF NOT EXISTS ... CREATE TABLE` — adds tables that were added after initial deployment
3. Raw SQL `IF NOT EXISTS ... ALTER TABLE ADD` — adds new columns (e.g., `AiClassification`) to existing tables

This approach avoids EF Core migrations entirely, making deployment simpler but requiring careful idempotent SQL for schema evolution.

### Runtime Dependencies (Docker)

The API Dockerfile installs additional system packages in the runtime stage:
- `libfontconfig1` — Font configuration library required by SkiaSharp for PDF rendering
- `fonts-dejavu-core` — DejaVu fonts providing Unicode coverage for PDF text rendering

These are necessary for QuestPDF/SkiaSharp to generate PDF reports with proper font rendering in the Linux container.

### Volumes

| Mount | Container Path | Mode | Purpose |
|-------|---------------|------|---------|
| `sqldata` (named) | `/var/opt/mssql` | RW | SQL Server data persistence |
| `SAMSUNG_NOTES_PATH` | `/samsung-notes` | RO | Samsung Notes local database (API only) |
| `BACKUP_PATH` | `/backup` | RW | Data + DB backup (shared by API and SQL Server) |

---

## NuGet / npm Dependencies

### Backend (NuGet)
- `Microsoft.EntityFrameworkCore.SqlServer` 8.0.11
- `Microsoft.EntityFrameworkCore.Design` 8.0.11
- `Microsoft.EntityFrameworkCore.Tools` 8.0.11
- `Microsoft.Data.Sqlite` 8.0.11 (Samsung Notes reader)
- `Swashbuckle.AspNetCore` 6.5.0 (Swagger, dev only)
- `QuestPDF` 2024.12.2 (PDF report generation)
- `SkiaSharp` 2.88.9 (2D graphics for glucose trend chart rendering)
- `MediatR` 12.4.1 (CQRS mediator pattern — all controller logic routed through handlers)

### Testing (NuGet — GlucoseAPI.Tests)
- `xunit` 2.5.3 (test framework)
- `xunit.runner.visualstudio` 2.5.3 (test runner)
- `Moq` 4.20.70 (mocking library)
- `FluentAssertions` 6.12.0 (fluent assertion syntax)
- `Microsoft.AspNetCore.Mvc.Testing` 8.0.6 (integration test host)
- `Microsoft.EntityFrameworkCore.InMemory` 8.0.6 (in-memory EF provider for tests)

### Frontend (npm)
- `react` ^18.3.1, `react-dom` ^18.3.1
- `react-scripts` 5.0.1 (Create React App)
- `recharts` ^2.13.3 (charting library)
- `date-fns` ^3.6.0 (date formatting)
- `@microsoft/signalr` ^8.0.7 (real-time updates)

---

## Domain-Driven Design (DDD) Architecture

The backend follows a **layered DDD architecture** adapted for a single-project ASP.NET Core application. Rather than splitting into multiple .NET projects (which would add complexity for a personal app), the DDD layers are separated by **namespace/folder conventions** within the same project.

### Why DDD for This Project?

This project started as a straightforward service-oriented ASP.NET Core app, but as features grew (event analysis, daily summaries, PDF reports, AI classification), several problems emerged:

1. **Duplicated business logic** — Glucose stats calculation was copy-pasted between `EventAnalyzer`, `DailySummaryService`, and `ReportService`. A bug fix in one place wouldn't propagate to the others.
2. **Duplicated GPT models** — `GptResponse`, `GptChoice`, `GptMessage`, and `GptUsage` DTOs were defined identically in both `EventAnalyzer.cs` and `DailySummaryService.cs`.
3. **Untestable services** — Services like `EventAnalyzer` created `new HttpClient()` directly and called `IHubContext<GlucoseHub>` directly, making it impossible to unit test without hitting real APIs and SignalR.
4. **Socket exhaustion risk** — `new HttpClient()` in `EventAnalyzer` and `DailySummaryService` creates a new TCP connection for every API call, which can exhaust available sockets under load.

DDD solves these by enforcing clear boundaries between **what the app does** (domain), **what it needs** (application interfaces), and **how it's wired** (infrastructure).

### Layer Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                        Controllers                               │
│  Thin MediatR dispatchers — no business logic                   │
│  Each action: parse request → send IRequest → map response      │
└─────────────────────────────┬────────────────────────────────────┘
                              │ sends IRequest<T> via IMediator
┌─────────────────────────────▼────────────────────────────────────┐
│              Application / Features (MediatR Handlers)           │
│  One query/command + handler per use case (CQRS pattern)        │
│  GetLatestReading, GetEvents, SaveSettings, GenerateReport…     │
│                                                                  │
│  Handlers depend on: DbContext, Domain Services, App Interfaces │
├──────────────────────────────────────────────────────────────────┤
│                    Services (Orchestration Layer)                 │
│  EventAnalyzer, DailySummaryService, GlucoseFetchService, etc.  │
│  Long-running / background work: fetch data, call AI,           │
│  save results, notify clients.                                   │
└──────────┬────────────────────────────┬──────────────────────────┘
           │ uses                       │ depends on (interfaces)
┌──────────▼──────────┐   ┌────────────▼───────────────────────────┐
│   Domain Services   │   │     Application Interfaces (Ports)     │
│   (pure logic)      │   │                                        │
│                     │   │  IGptClient                            │
│ GlucoseStats        │   │  INotificationService                  │
│  Calculator         │   │                                        │
│ AiCostCalculator    │   │  Define WHAT the app needs,            │
│ Classification      │   │  not HOW it's done.                    │
│  Parser             │   │                                        │
│ TimeZone            │   └────────────▲───────────────────────────┘
│  Converter          │                │ implements
│                     │   ┌────────────┴───────────────────────────┐
│ No I/O, no DI deps  │   │     Infrastructure (Adapters)          │
│ Pure functions +     │   │                                        │
│ value objects        │   │  OpenAiGptClient (→ IGptClient)        │
└─────────────────────┘   │    Uses IHttpClientFactory("OpenAI")    │
                          │                                        │
                          │  SignalRNotificationService             │
                          │    (→ INotificationService)             │
                          │    Uses IHubContext<GlucoseHub>         │
                          │                                        │
                          │  LibreLinkClient                       │
                          │    Uses IHttpClientFactory("LibreLink") │
                          └────────────────────────────────────────┘
```

### Domain Layer — `GlucoseAPI/Domain/Services/`

**Design principle**: Domain services contain **pure business logic** with no I/O, no HTTP calls, no database access, and minimal dependencies. They operate on domain entities and return value objects. This makes them trivially testable and reusable.

#### `GlucoseStatsCalculator` (static)
- **Purpose**: Centralizes all glucose statistics computation that was previously duplicated across `EventAnalyzer`, `DailySummaryService`, and `ReportService`.
- **Why static**: It's a pure function — takes readings in, returns stats out. No state, no dependencies, no reason to instantiate.
- **Methods**:
  - `ComputeEventStats(readings, eventTimestamp)` → `GlucoseStats` — Computes glucose at event, min, max, avg, spike, peak time for a set of readings relative to an event.
  - `ComputeDayStats(readings)` → `DayGlucoseStats` — Computes day-level stats: min, max, avg, std dev, time-in-range percentages.
  - `NullableDoubleEquals(a, b, tolerance)` — Utility for comparing nullable doubles with floating-point tolerance.
- **Value objects**: `GlucoseStats` and `DayGlucoseStats` are immutable C# records, ensuring computed results can't be accidentally mutated after calculation.

#### `ClassificationParser` (static)
- **Purpose**: Extracts `[CLASSIFICATION: green/yellow/red]` tags from AI response text. Previously duplicated as `ParseClassification()` in both `EventAnalyzer` and `DailySummaryService`.
- **Why static**: Pure string parsing — no state needed.
- **Methods**:
  - `Parse(rawText)` → `(analysis, classification)` — Strips the classification tag from the beginning of the text and returns both the cleaned analysis and the classification value.
  - `IsValid(classification)` — Validates the classification is one of the three allowed values.

#### `TimeZoneConverter` (scoped, injected)
- **Purpose**: Encapsulates timezone resolution and conversion. Previously, timezone lookup was scattered across services with duplicated `try-catch` blocks and fallback logic.
- **Why not static**: The `Resolve()` method logs a warning when a timezone can't be found, which requires `ILogger`. Static conversion methods (`ToLocal`, `ToUtc`, `GetDayBoundariesUtc`) don't need the logger and are static.
- **Methods**:
  - `Resolve(timeZoneId)` → `TimeZoneInfo` — Resolves IANA timezone IDs (e.g., "Europe/Warsaw") with fallback to UTC.
  - `ToLocal(utc, tz)` / `ToUtc(local, tz)` — Static UTC ↔ local conversion.
  - `GetDayBoundariesUtc(localDate, tz)` — Returns `(startUtc, endUtc)` for midnight boundaries in a given timezone.

### Application Layer — `GlucoseAPI/Application/Interfaces/`

**Design principle**: Application interfaces (also called **ports** in hexagonal architecture) define **what external capabilities the application needs** without specifying how they're implemented. This allows services to be tested with mocks and infrastructure to be swapped without changing business logic.

#### `IGptClient`
- **Purpose**: Abstracts the OpenAI API call. Previously, `EventAnalyzer` and `DailySummaryService` both created `new HttpClient()`, manually built JSON payloads, parsed responses, and defined their own `GptResponse` classes.
- **Why an interface**: Enables mocking in unit tests (no real API calls), eliminates the `new HttpClient()` anti-pattern, and consolidates GPT response models into a single `GptAnalysisResult` record.
- **Contract**: `AnalyzeAsync(apiKey, systemPrompt, userPrompt, model, maxTokens, ct)` → `GptAnalysisResult`
- **`GptAnalysisResult`**: Immutable record containing `Content`, `Model`, `InputTokens`, `OutputTokens`, `TotalTokens`, `FinishReason`, `HttpStatusCode`, `Success`, `DurationMs`, and `ErrorMessage`. Includes a `Failure()` factory for error cases.

#### `INotificationService`
- **Purpose**: Abstracts SignalR notifications. Previously, services injected `IHubContext<GlucoseHub>` directly and called `SendAsync("EventsUpdated", ...)` with string-typed event names.
- **Why an interface**: Decouples services from SignalR (testable with mocks), centralizes event name strings in one place, and would allow switching to a different push mechanism (e.g., WebPush, SSE) without changing services.
- **Contract**: Five methods — `NotifyNewGlucoseDataAsync`, `NotifyEventsUpdatedAsync`, `NotifyDailySummariesUpdatedAsync`, `NotifyAiUsageUpdatedAsync`, `NotifyNotesUpdatedAsync`.

### Infrastructure Layer — `GlucoseAPI/Infrastructure/`

**Design principle**: Infrastructure adapters implement the application interfaces using concrete technology (HTTP clients, SignalR hubs). They are the only layer that knows about external protocols and libraries.

#### `OpenAiGptClient` (implements `IGptClient`)
- **Purpose**: Makes HTTP calls to the OpenAI Chat Completions API.
- **Uses**: `IHttpClientFactory` to get a named `"OpenAI"` `HttpClient` instance per call, avoiding socket exhaustion.
- **Handles**: JSON serialization, Authorization header injection, HTTP error handling, stopwatch timing, response parsing.
- **GPT models**: Request/response DTOs (`GptChatRequest`, `GptChatResponse`, `GptChoice`, `GptMessage`, `GptUsage`) are defined once in `GptModels.cs` — eliminating the previous duplication.

#### `SignalRNotificationService` (implements `INotificationService`)
- **Purpose**: Sends real-time notifications to connected browser clients via SignalR.
- **Uses**: `IHubContext<GlucoseHub>` — the standard ASP.NET Core SignalR hub context.
- **Implementation**: Each interface method maps to a single `SendAsync` call with the appropriate event name.

#### `LibreLinkClient` (refactored, not interface-based)
- **Purpose**: Unofficial LibreLink Up API client for fetching glucose data.
- **Refactored to use**: `IHttpClientFactory` with a named `"LibreLink"` client configured with custom headers (User-Agent, product, Cache-Control) and a handler with gzip/deflate/brotli decompression and cookie support.
- **Why no interface**: LibreLinkClient is only used by `GlucoseFetchService` and has complex stateful behavior (login, token management, region redirect). An interface abstraction would add complexity without significant testability benefit at this stage.

### IHttpClientFactory — Why It Matters

The original code used `new HttpClient()` directly in services:

```csharp
// ❌ Anti-pattern: creates a new TCP connection every call
var client = new HttpClient();
client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
```

This causes **socket exhaustion** — each `HttpClient` instance holds a TCP connection that isn't released until garbage collection. Under load (e.g., analyzing 50+ events), this can exhaust the OS's available sockets.

The refactored code uses `IHttpClientFactory` with named clients:

```csharp
// ✅ Correct: HttpClient from factory, managed connection pooling
var client = _httpClientFactory.CreateClient("OpenAI");
client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
var response = await client.PostAsync("v1/chat/completions", content);
```

Named clients are configured once in `Program.cs` with base addresses, default headers, and custom handlers. The factory manages connection pooling, DNS rotation, and lifecycle — preventing socket exhaustion.

### Testing Strategy

The DDD architecture enables a clear testing pyramid:

#### Unit Tests (`GlucoseAPI.Tests/Domain/`)
- **What**: Test pure domain logic in isolation with no dependencies.
- **How**: Direct method calls on static domain services with known inputs and expected outputs.
- **Framework**: xUnit + FluentAssertions.
- **Examples**:
  - `GlucoseStatsCalculatorTests` — 12 tests covering empty inputs, single/multiple readings, spike calculation, negative spikes, day-level stats, time-in-range percentages, std dev, timestamps.
  - `ClassificationParserTests` — 10+ tests covering valid classifications (green/yellow/red), case insensitivity, missing tags, invalid colors, edge cases (extra whitespace, embedded tags).
  - `TimeZoneConverterTests` — 8 tests covering valid/invalid timezone resolution, null handling, UTC/local conversion, day boundary calculation.

#### Handler Tests (`GlucoseAPI.Tests/Handlers/`)
- **What**: Test MediatR handlers in isolation using EF Core InMemory provider.
- **How**: Each test class creates a fresh InMemory database, instantiates the handler directly, and verifies correct data retrieval, business logic, and DTO mapping.
- **Examples**:
  - `GlucoseHandlerTests` — 6 tests: empty DB, latest reading, history limit, stats calculation (min/max/avg/TIR), distinct dates.
  - `EventHandlerTests` — 6 tests: ordering, limit, analysis counts, content truncation, detail with recalculated stats, status counts.
  - `DailySummaryHandlerTests` — 7 tests: ordering, limit, snapshot counts, detail with events/readings, status counts, snapshot detail.
  - `AiUsageHandlerTests` — 6 tests: empty DB, cost calculation per log, limit, date range, summary totals/breakdown, pricing table.

#### Service Tests (`GlucoseAPI.Tests/Services/`)
- **What**: Test application services with mocked external dependencies.
- **How**: Uses Moq to mock `IGptClient`, `INotificationService`, and `SettingsService`. Uses EF Core InMemory provider for the database.
- **Examples**:
  - `EventAnalyzerTests` — 5 tests verifying:
    - Returns null when API key not configured (GPT never called).
    - Saves analysis + history + usage log when GPT returns success.
    - Logs usage and notifies even when GPT returns empty response.
    - Computes correct glucose stats via domain service.
    - Sends both `EventsUpdated` and `AiUsageUpdated` notifications on success.

#### Integration Tests (`GlucoseAPI.Tests/Integration/`)
- **What**: Test the full HTTP pipeline end-to-end with a real ASP.NET Core host.
- **How**: Uses `WebApplicationFactory<Program>` with EF Core InMemory provider replacing SQL Server. Tests make real HTTP requests to API endpoints.
- **Examples**:
  - `ApiIntegrationTests` — 14 tests covering all major endpoints: glucose history, stats, events, daily summaries, notes, settings, AI usage, sync trigger, and PDF report generation (both valid and invalid inputs).
