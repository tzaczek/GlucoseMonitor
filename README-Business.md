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
- **Automatic correlation**: Samsung Notes from a designated folder (e.g., "Cukier") are automatically matched with glucose data based on timestamps.
- **Per-event glucose analysis**: For each meal/activity, the system captures glucose at the time of the event, the spike, min/max/average, peak time, and reading count.
- **AI-powered analysis**: Each event is analyzed by GPT, which provides:
  - Baseline assessment
  - Glucose response characterization
  - Spike analysis (mild/moderate/significant)
  - Recovery assessment
  - Overall impact rating
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
  - Meal/activity impact analysis
  - Patterns and trends (overnight, dawn phenomenon, post-meal)
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
- **Periodic backups** of all data: glucose readings (JSON + CSV), events, analysis history, daily summaries, daily summary snapshots, and AI usage logs.
- **Timestamped snapshots**: Each backup run creates a uniquely named folder with a full data export.
- **"Latest" symlink**: Always have a `latest/` folder pointing to the most recent backup.
- **Auto-cleanup**: Backups older than 14 days are automatically removed.
- **Local storage**: Backups are saved to a mounted Docker volume on your machine.

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
