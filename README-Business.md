# Glucose Monitor — Business Overview

## What Is It?

Glucose Monitor is a **personal Continuous Glucose Monitoring (CGM) dashboard** that turns raw glucose sensor data into actionable health insights. It connects to your **FreeStyle Libre** CGM sensor (via LibreLink Up), correlates glucose readings with your **meal and activity notes** (from Samsung Notes), and uses **AI (GPT)** to analyze how your body responds to different foods and activities.

## Why Does It Exist?

Managing diabetes or pre-diabetes requires understanding how your body responds to what you eat, how you exercise, and how your glucose behaves throughout the day. While the LibreLink app provides basic glucose data, it doesn't:

- **Correlate meals with glucose responses** — You have to mentally connect "I ate pizza at 12:30" with "my glucose spiked to 250 at 14:00."
- **Provide AI-powered analysis** — Understanding glucose patterns requires expertise. AI can act as a knowledgeable health coach available 24/7.
- **Aggregate data into daily summaries** — Seeing the full picture of a day — all meals, all readings, all patterns — is essential for learning.
- **Track trends over time** — Was this week better than last week? Which meals consistently cause problems?
- **Give you full control of your data** — Your health data stays on your own machine, not in a third-party cloud.

Glucose Monitor solves all of these problems in a single, self-hosted dashboard.

## Who Is It For?

- **People with Type 1 or Type 2 diabetes** who use a FreeStyle Libre CGM sensor and want deeper insights into their glucose data.
- **Pre-diabetic individuals** monitoring their glucose to make lifestyle changes.
- **Health-conscious individuals** who wear a CGM for optimization and want to understand their metabolic responses.
- **Anyone** who wants their health data private, on their own hardware, with full control.

## Core Features

### 📊 Real-Time Glucose Dashboard
- **Live glucose value** with trend arrows (rising, stable, falling).
- **Interactive chart** with drag-to-zoom, event markers, and target range visualization (70–180 mg/dL).
- **Statistics**: current reading, average, min/max, time-in-range percentage, total readings.
- **Flexible time ranges**: 6h, 12h, 24h, 48h, 7d, 2w, 30d, 90d, or custom.
- **CSV export** for any time period.

### 🍽️ Meal & Activity Events
- **Automatic correlation**: Samsung Notes from a designated folder (e.g., "Cukier") are automatically matched with glucose data based on timestamps. Each event captures at least 3 hours of post-event glucose data to ensure the full meal response is visible, even if another event occurs sooner. When the gap to the next event is longer than 3 hours, the period extends to cover it.
- **Per-event glucose analysis**: For each meal/activity, the system captures glucose at the time of the event, the spike, min/max/average, peak time, and reading count.
- **Overlapping event awareness**: When multiple events occur within the same 3-hour glucose window (e.g., a meal followed by exercise 30 minutes later), the AI is told about all of them and considers their combined effect on glucose. This prevents misleading analysis — for instance, if exercise blunted a meal spike, the AI will attribute the good recovery to the exercise rather than the food alone. Overlapping events are also shown on the event detail chart as muted reference lines, listed below the chart with classification, time, and glucose data, and are **clickable** to switch the modal to that event's detail view.
- **AI-powered analysis**: Each event is analyzed by GPT, which provides:
  - Baseline assessment
  - Glucose response characterization
  - Spike analysis (mild/moderate/significant)
  - Recovery assessment
  - Overall impact rating
  - Consideration of overlapping events and their influence
  - Practical, actionable tips
- **Traffic-light classification**: Every event is classified as 🟢 **Good** (well-controlled response), 🟡 **Concerning** (moderate impact), or 🔴 **Problematic** (significant spike or poor recovery).
- **Analysis history**: Every AI analysis run is saved with its glucose data snapshot, so you can see how the analysis evolved as more data became available.
- **Re-analysis on demand**: Manually trigger a fresh AI analysis for any event at any time.
- **Auto re-analysis**: When new glucose data arrives that overlaps with an existing event's time window, the system automatically recalculates glucose stats and re-runs AI analysis (with configurable cooldown to avoid excessive API calls).

### 📅 Daily Summaries
- **Automatic daily aggregation**: All events and glucose readings for each completed day are compiled into a comprehensive summary.
- **Day-level statistics**: average, min, max, standard deviation, time-in-range, time below range, time above range.
- **Time-in-Range visualization**: A colored bar showing the distribution of low/in-range/high readings.
- **AI daily analysis**: GPT generates a comprehensive daily summary covering:
  - Overall glucose control assessment
  - Key metrics commentary
  - **Overnight analysis** (00:00–06:00): glucose stability, nocturnal highs/lows, overnight trend direction
  - **Morning glucose** (06:00–09:00): fasting glucose level, dawn phenomenon detection (compares pre-dawn vs early morning averages), and **pre-diabetes flag** — if fasting average is ≥100 mg/dL, it is flagged as a potential indicator of impaired fasting glucose (100–125 mg/dL = pre-diabetes, ≥126 mg/dL = possible diabetes)
  - Meal/activity impact analysis
  - Patterns and trends (post-meal, afternoon dips, evening trends)
  - Best and worst periods of the day
  - 2–3 actionable improvement suggestions
- **Day classification**: Each day is classified as 🟢 **Good Day**, 🟡 **Concerning**, or 🔴 **Difficult Day**.
- **Manual trigger**: Generate summaries on demand, including for the current (partial) day.
- **Snapshot history**: Each generation run is saved as a snapshot, preserving what data was available at the time and what the AI said — you can compare how the summary evolved throughout the day.

### 📝 Samsung Notes Integration
- **Automatic sync**: Samsung Notes are read directly from the local Samsung Notes SQLite database on your Windows PC (synced via the Samsung Notes Windows app).
- **Folder-based filtering**: Only notes from your designated tracking folder are used for event correlation (default: "Cukier").
- **Full note browsing**: Browse all your Samsung Notes with search and folder filtering.
- **Media preview**: Notes with images can be previewed directly in the dashboard.
- **Binary content extraction**: Automatically extracts text from Samsung Notes' binary format (note.note) when stripped content isn't available.

### 🤖 AI Usage Tracking
- **Cost monitoring**: Track total cost, cost per call, and daily cost trends with automatic pricing estimates for known GPT models.
- **Token usage**: Monitor input/output/total tokens with daily usage charts.
- **Model breakdown**: See usage split by GPT model with visual breakdowns.
- **Call logs**: Detailed log of every API call with status, duration, tokens, cost, and finish reason.
- **Period filtering**: View usage for any time period (7d, 2w, 1m, 3m, 6m, 1y, all time, or custom).
- **Real-time updates**: Usage page automatically refreshes via SignalR when new API calls are made.

### 💬 AI Chat
- **Interactive conversations about your glucose data**: Ask AI free-form questions about your glucose patterns, meal responses, trends, and get personalized insights through a chat interface.
- **Graph-based multi-period selection**: Instead of typing dates, select data periods directly on a zoomable glucose chart by dragging. Each selection becomes a named period that can be referenced in prompts.
  - **Drag-to-select**: Click and drag on the glucose graph to highlight any time range — not restricted to event boundaries.
  - **Mouse wheel zoom**: Scroll to zoom in/out on the graph for precise period selection.
  - **Zoom controls**: "+", "−", and "Reset" buttons for additional zoom control.
  - **Configurable range**: View last 3, 7, 14, 30, or 90 days of glucose data on the selector graph.
  - **Named periods**: Each selected period gets an auto-generated descriptive name (e.g., "Feb 17 Night", "Feb 18 Afternoon") that can be edited inline. Use these names in prompts to reference specific periods.
  - **Color-coded overlays**: Each period is displayed with a unique color on both the selector graph and the thread chart, making it easy to distinguish multiple periods visually.
  - **Event markers**: Events (meals, activities) within the visible range are shown as dots on the graph and listed below it with clickable links.
- **Prompt templates**: Create reusable prompt templates for common analysis tasks (e.g., "Overnight Analysis", "Meal Comparison", "Weekly Summary"). Templates support placeholders like `{glucose_data}`, `{events}`, and `{period_label}` that are automatically filled with real data.
- **Prompt preview & editing**: When using a template, the fully interpolated prompt is shown as a preview before sending. You can edit it freely or reset to the original template.
- **Per-message model selection**: Choose which GPT model to use for each message — set a default for the session and override it per follow-up.
- **Follow-up conversations**: Continue chatting with the AI about the same data — ask clarifying questions, request deeper analysis, or explore different angles. The AI retains full conversation context.
- **Thread glucose chart**: Each chat thread displays a zoomable glucose chart with all selected periods highlighted and color-coded. Events within each period are shown as labeled markers with clickable links to event details.
- **Event links in responses**: AI responses that reference specific event IDs (e.g., "Event #42") are automatically turned into clickable links that open the event detail modal.
- **Real-time responses**: AI responses are streamed to the browser via SignalR — no need to refresh or wait on a loading screen.
- **Session management**: All conversations are saved persistently. Browse, resume, or permanently delete individual sessions or all sessions at once.
- **Background processing**: Chat messages are processed asynchronously. The queue-based architecture handles multiple simultaneous conversations and recovers from crashes.

### 🍽️ Food Pattern Recognition
- **AI-powered food extraction**: When meal events are processed, AI (GPT-4o-mini) automatically extracts individual food and drink names from your note text — handling Polish and English.
- **Personal food database**: Each unique food builds a profile over time, tracking how many times you've eaten it, average glucose spike, best/worst spike, classification breakdown (good/concerning/problematic), and date range.
- **Food detail with full event history**: Click any food to see every event where you ate it — with full note content, glucose metrics (min/avg/max, spike, recovery time, reading count), and the original AI analysis for each event.
- **AI food analysis chat**: Click "Analyze with AI" on any food to start a chat session pre-loaded with all the food's data — aggregate stats and every individual event's glucose readings. Ask the AI about patterns, best/worst timing, whether to keep eating that food, or what alternatives to try.
- **Quick analysis prompts**: Pre-built prompt buttons for common questions — "Best timing?", "Best vs worst", "Should I eat this?", "Alternatives?" — each starting a focused AI conversation.
- **Search and sort**: Find foods by name (in Polish or English), sort by occurrence count, average spike, or last seen date.
- **Food management**: Rename foods, merge duplicates, or delete food items and their links.
- **Real-time updates**: When new events are analyzed, food patterns are automatically updated and pushed to the UI via SignalR.

### 🥗 Meals Browser & Comparison
- **Full meal list**: Browse every recorded meal with date, classification, glucose spike, food tags, and note preview — sorted by date, spike, glucose level, or name with search and classification filtering.
- **Detail modal**: Click any meal to open a rich modal showing full note content (bilingual), glucose statistics (at event, spike, min/max/avg, peak time, readings, period), linked food items with per-food spike data, and the AI analysis.
- **Multi-select comparison**: Select any number of meals with checkboxes, then compare them in a side-by-side grid showing classification, spike, glucose metrics, and food tags for each.
- **AI analysis & chat**: Start an AI chat from any meal detail or comparison — the AI receives full glucose data and food context and answers questions about the meal's impact, food recommendations, and patterns.
- **Quick prompts**: Pre-built analysis buttons for common questions — "What caused the spike?", "Safe to eat?", "Healthier alternatives" for single meals; "Best meal?", "Common patterns", "Recommendations" for comparisons.
- **Drill-down to event detail**: Jump from any meal to the full event detail modal with interactive glucose chart and analysis history.
- **Summary stats**: Dashboard cards showing total meals, analyzed count, good/concerning/problematic breakdown, and average spike.

### 🌐 Bilingual Support (Polish & English)
- **Automatic translation**: All meal/activity note titles and content are automatically translated to English using AI (GPT-4o-mini) when events are created.
- **Food name translation**: Food items are extracted and stored in both the original language (Polish) and English (e.g., "ser" → "cheese", "soczewica" → "lentils").
- **Side-by-side display**: The UI shows both Polish and English text throughout — event titles, note content, food names — so you can work in either language.
- **Searchable in both languages**: Search for foods by their Polish or English name.
- **Backfill on startup**: A background service automatically translates any existing events and food items that don't have English translations yet, processing in batches.
- **Manual backfill**: Trigger translation of all untranslated data via API endpoint.

### 🛡️ API Resilience
- **Retry policies**: Transient failures on external API calls (OpenAI GPT, LibreLink Up) are automatically retried with exponential backoff.
- **Circuit breakers**: If an API is consistently failing, the circuit breaker opens to prevent wasting resources, automatically recovering when the service is healthy again.
- **Timeouts**: All external API calls have configurable timeouts to prevent hanging requests from blocking background services.
- **Polly integration**: Built on Microsoft.Extensions.Http.Resilience (Polly) for production-grade resilience patterns.

### 📊 Period Comparison
- **Compare any two time periods**: Select two glucose monitoring periods — hours, days, weeks, or custom date ranges — and see how your glucose control differed.
- **Quick presets**: One-click comparisons for common scenarios: last 6h/12h/24h/48h/7d/14d/30d vs the previous equivalent period.
- **Custom periods**: Choose any start and end date/time for each period, with optional labels (e.g., "Weekday" vs "Weekend").
- **Overlay chart**: Both periods are displayed on the same graph with distinct colors (blue for Period A, orange for Period B), normalized by time offset from each period's start so they can be visually compared regardless of actual dates.
- **Side-by-side statistics**: Average glucose, min/max, standard deviation, time in range, time above/below range, and event counts — shown in a comparison table with delta indicators (green for improvement, red for deterioration).
- **Event comparison**: Events from both periods are listed side by side, showing what meals and activities occurred in each period and their glucose impact.
- **AI differential analysis**: GPT analyzes the differences between the two periods and provides:
  - Overview of which period was better controlled
  - Key metrics comparison with highlights of the most significant differences
  - Event analysis — what foods/activities differed and how they affected glucose
  - Pattern differences — overnight, post-meal, morning fasting, etc.
  - Root cause analysis — what likely caused the differences
  - Actionable insights based on what worked well
- **Background processing**: Comparisons are processed asynchronously. The UI updates in real time via SignalR when the analysis is complete.
- **Persistent history**: All comparisons are saved in the database and can be reviewed or deleted at any time.
- **Traffic-light classification**: Each comparison is classified as 🟢 **Improvement/Good**, 🟡 **Mixed**, or 🔴 **Deterioration/Poor**.

### 📋 Period Summaries
- **Summarize any time period**: Generate a comprehensive AI-powered summary for any arbitrary time range — from a few hours to several weeks or months.
- **Quick presets**: One-click summaries for common periods: last 6h, 12h, 24h, 48h, 3 days, 7 days, 14 days, or 30 days.
- **Custom periods**: Choose any start and end date/time, with an optional label (e.g., "Weekend trip", "Fasting experiment", "Holiday week").
- **Glucose chart**: Interactive glucose trend chart for the selected period with event markers, target range visualization, and drag-to-zoom.
- **Statistics dashboard**: Comprehensive stats — average glucose, min/max, standard deviation, time in range, time above/below range, reading count, event count.
- **Event listing**: All events within the period are shown with their timestamp, glucose at event, spike, and AI classification.
- **AI period analysis**: GPT generates a comprehensive analysis covering:
  - Overall glucose control assessment
  - Key metrics review against healthy targets
  - Glucose pattern identification (overnight, post-meal, fasting, time-of-day trends)
  - Per-event impact analysis — which meals/activities helped or hurt
  - Night and morning glucose analysis with dawn phenomenon detection
  - 3–5 specific, actionable recommendations
- **Background processing**: Summaries are processed asynchronously. The UI updates in real time via SignalR when the analysis is complete.
- **Persistent history**: All period summaries are saved in the database and can be reviewed or deleted at any time.
- **Traffic-light classification**: Each period is classified as 🟢 **Good Control**, 🟡 **Moderate Control**, or 🔴 **Poor Control**.

### 📄 PDF Reports for Doctors
- **Professional PDF reports** that can be generated for any date range (up to 90 days) and shared with healthcare providers.
- **Glucose Trend Chart**: A full-period glucose line graph rendered with SkiaSharp, showing the glucose trend line, target range shading (70–180 mg/dL), high/low coloring, and event markers.
- **Summary Statistics**: Average glucose, estimated A1C, GMI, coefficient of variation, time-in-range, min/max, and standard deviation.
- **Time in Range Bar**: Visual bar chart showing the percentage of time spent below, in, and above the target range.
- **Daily Breakdown Table**: Day-by-day statistics with average glucose, min/max, time-in-range, reading count, and AI classification.
- **Meal & Activity Events Table**: Each event with a content summary, glucose at event, spike, range, reading count, and AI classification.
- **Glucose Distribution Histogram**: Time spent in each glucose bracket (< 54, 54–69, 70–100, 100–140, 140–180, 180–250, > 250).
- **AI Analysis Highlights**: Top AI-generated daily insights with classification labels.
- **Disclaimer**: Automatically included note that the report is for supplementary use and not a substitute for professional medical advice.

### 🔄 Manual Sync
- **Sync button** in the header bar: Instantly triggers a glucose data fetch from LibreLink Up and a Samsung Notes sync without waiting for the next automatic poll.
- **Visual feedback**: Animated spinner during sync, with a brief success/error message that auto-dismisses.
- **Always accessible**: The button is in the app header, available from any page.
- **Per-source control**: API also supports syncing glucose data or notes independently.

### 💾 Automatic Data Backup
- **Periodic JSON/CSV backups** of all data: glucose readings (JSON + CSV), events, analysis history, daily summaries, daily summary snapshots, and AI usage logs.
- **Timestamped snapshots**: Each backup run creates a uniquely named folder with a full data export.
- **"Latest" symlink**: Always have a `latest/` folder pointing to the most recent backup.
- **Auto-cleanup**: JSON/CSV backups older than 14 days are automatically removed.
- **Local storage**: Backups are saved to a mounted Docker volume on your machine.

### 🗄️ Database Backup & Restore
- **Daily SQL Server backup**: A full compressed `.bak` database backup is created automatically once per day, stored in `/backup/db/`.
- **Manual backup**: Trigger a backup at any time from the Settings page.
- **Backup status**: The Settings page shows the last backup time, file name, size, number of stored backups, and any errors.
- **Stored backup list**: View all stored backup files with size and creation date.
- **One-click restore**: Restore the database from any stored backup file directly from the Settings page, with a confirmation dialog warning that all current data will be replaced.
- **Auto-cleanup**: Database backups older than 7 days are automatically removed.

### 📜 Event Log
- **Centralized application event log**: Every significant operation across all background services is recorded — glucose data fetches, Samsung Notes syncs, event creation, AI analyses, daily summaries, comparisons, period summaries, database backups, and system events.
- **Three severity levels**: **Info** (normal operations), **Warning** (unexpected situations like data gaps), **Error** (failures).
- **Categorized entries**: Events are grouped into categories: glucose, notes, events, analysis, daily, comparison, summary, backup, settings, system, sync.
- **Data gap detection**: When glucose readings have a gap > 30 minutes (e.g., due to container downtime), a warning is automatically logged with details about the missing period.
- **Rich context**: Each log entry includes the timestamp, source service, an optional numeric value (e.g., count of inserted readings), and optional detailed information (e.g., error stack trace, affected entity IDs).
- **Real-time updates**: New event log entries are pushed to the browser via SignalR as they happen.
- **Filtering & search**: Filter logs by time range (last hour, 6h, 24h, 7d, 30d, all, or custom range), severity level, category, and free-text search across messages.
- **Pagination**: Browse through large volumes of events with paginated results.
- **Expandable details**: Click any log entry with details to expand and see full technical context.

### ⚡ Real-Time Updates (SignalR)
- **WebSocket connection**: All data updates — new glucose readings, new events, analysis completions, daily summaries, AI usage — are pushed to the browser in real time.
- **Automatic reconnection**: If the WebSocket connection drops, it automatically reconnects with exponential backoff.
- **No manual refresh needed**: The dashboard stays current without polling.

### 🧪 Tested & Well-Architected
- **Domain-Driven Design**: Core business logic (glucose statistics, AI classification parsing, timezone conversion) is isolated in pure domain services — no I/O, no external dependencies, fully testable.
- **Automated test suite**: Unit tests for domain logic, service tests with mocked dependencies, and integration tests that spin up the full API pipeline.
- **Clean separation of concerns**: External API communication (OpenAI, LibreLink) is abstracted behind interfaces, enabling easy testing and future extensibility.

### 🔒 Privacy & Self-Hosting
- **Runs entirely on your machine** via Docker Compose — no cloud dependency (except the GPT API for AI analysis).
- **Your data stays yours**: All glucose readings, notes, and analyses are stored in a local SQL Server database and backed up locally.
- **No accounts, no tracking, no telemetry**.

## How It Works (User Perspective)

1. **Set up**: Install Docker, run `docker compose up`, and enter your LibreLink Up credentials and GPT API key in Settings.
2. **Wear your sensor**: Continue using your FreeStyle Libre sensor as normal.
3. **Log meals**: When you eat or do an activity, jot a quick note in Samsung Notes (in your tracking folder).
4. **Check the dashboard**: The app automatically fetches glucose data every 5 minutes, syncs your notes every 10 minutes, creates events, and runs AI analysis.
5. **Learn**: Review your events to see which meals caused spikes. Read AI analysis for actionable insights. Check daily summaries for the big picture.
6. **Improve**: Use the insights to adjust your diet, timing, portions, and activities for better glucose control.

## Data Flow

```
FreeStyle Libre Sensor
        ↓
   LibreLink App (phone)
        ↓
   LibreLink Up Cloud
        ↓ (API polling every 5 min)
   ┌─────────────────────────────────┐
   │     Glucose Monitor Backend     │
   │  ┌───────────────────────────┐  │
   │  │   GlucoseFetchService     │──── Fetches glucose readings
   │  │   SamsungNotesSyncService  │──── Syncs notes from local SQLite DB
   │  │   GlucoseEventAnalysis     │──── Correlates notes → events, calls GPT
   │  │   DailySummaryService      │──── Aggregates days, calls GPT
   │  │   DataBackupService        │──── Periodic JSON/CSV export
   │  │   ComparisonService       │──── Period comparison analysis (AI)
   │  │   PeriodSummaryService   │──── Arbitrary period summaries (AI)
   │  │   ChatService             │──── Interactive AI chat with multi-period context
   │  │   FoodPatternService      │──── AI food extraction + pattern tracking
   │  │   TranslationService      │──── Bilingual PL↔EN translation
   │  │   DatabaseBackupService   │──── Daily SQL Server .bak backup/restore
   │  └───────────────────────────┘  │
   │            │                    │
   │     SQL Server Database         │
   └────────────┬────────────────────┘
                │ SignalR (WebSocket)
                ↓
   React Dashboard (real-time updates)
        ↑
   Samsung Notes (local Windows sync)
```
