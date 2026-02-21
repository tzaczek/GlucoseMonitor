# 🩸 Glucose Monitor

A **self-hosted Continuous Glucose Monitoring (CGM) dashboard** that connects to your FreeStyle Libre sensor, correlates glucose data with meal/activity notes from Samsung Notes, and uses AI (GPT) to provide personalized health insights — all running locally on your machine.

> **⚠️ This is NOT medical software.** See the [Disclaimer](#-disclaimer--legal) section below.

---

## ✨ Features

| Feature | Description |
|---------|-------------|
| 📊 **Real-Time Dashboard** | Live glucose value, trend arrows, interactive chart with zoom, target range visualization (70–180 mg/dL), and flexible time ranges (6h → 90d) |
| 🍽️ **Meal & Activity Events** | Automatic correlation of Samsung Notes with glucose readings — see how each meal affects your glucose |
| 🤖 **AI-Powered Analysis** | GPT analyzes each event (spike severity, recovery, tips) and classifies it as 🟢 Good / 🟡 Concerning / 🔴 Problematic |
| 💬 **AI Chat** | Interactive chat with AI about your glucose data — select multiple periods on a zoomable graph, name them, compare them, and ask follow-up questions with per-message model selection |
| 🍽️ **Food Patterns** | AI extracts food names from meal notes, tracks how each food affects your glucose across all events, and lets you chat with AI about any food's impact |
| 🥗 **Meals Browser** | Browse all meals with glucose impact, click for detail modal with foods and AI chat, select and compare multiple meals side-by-side with AI analysis |
| 🌐 **Bilingual (PL/EN)** | All meal notes and food names are automatically translated between Polish and English using AI, displayed side by side |
| 🛡️ **API Resilience** | Retry policies, circuit breakers, and timeouts on all external API calls (OpenAI, LibreLink) via Polly for robust operation |
| 📅 **Daily Summaries** | Automatic daily aggregation with AI commentary on patterns, trends, and actionable suggestions |
| 📄 **PDF Reports** | Professional reports for your doctor with glucose trends, statistics, time-in-range, and AI highlights |
| 📝 **Samsung Notes Browser** | Browse, search, and preview all your Samsung Notes with media support |
| 📈 **AI Usage Tracking** | Monitor GPT costs, tokens, and call history with visual dashboards |
| 🔄 **Real-Time Updates** | SignalR WebSocket pushes all updates to the browser instantly — no manual refresh |
| 💾 **Automatic Backups** | Periodic export of all data (JSON + CSV) with auto-cleanup of old backups |
| 🔒 **Fully Self-Hosted** | Runs on your machine via Docker — no cloud, no accounts, no telemetry |

---

## 🏗️ Architecture

```
┌──────────────────────────────────────────────────────────────┐
│                    Docker Compose Stack                       │
│                                                              │
│  ┌──────────┐    ┌─────────────────────┐    ┌─────────────┐ │
│  │  nginx   │───▶│  ASP.NET Core API   │───▶│ SQL Server  │ │
│  │ (React)  │    │  + Background Jobs  │    │   2022      │ │
│  │  :3000   │◀───│  + SignalR Hub      │    │   :1433     │ │
│  └──────────┘    └─────────────────────┘    └─────────────┘ │
│                           ▲       ▲                          │
│                       Volumes     External APIs:             │
│                    /samsung-notes  • LibreLink Up (glucose)  │
│                    /backup         • OpenAI GPT (analysis)   │
└──────────────────────────────────────────────────────────────┘
```

| Container | Technology | Port | Role |
|-----------|-----------|------|------|
| `web` | React 18 + nginx | 3000 | Single-page application |
| `api` | ASP.NET Core 8.0 | 8080 | REST API, SignalR, background services |
| `sqlserver` | SQL Server 2022 | 1433 | Relational database |

---

## 🚀 Quick Start

### Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (Windows/Mac/Linux)
- A **FreeStyle Libre** CGM sensor with an active **LibreLink Up** account
- *(Optional)* An **OpenAI API key** for AI analysis
- *(Optional)* **Samsung Notes** Windows app synced on your PC

### 1. Clone the Repository

```bash
git clone https://github.com/tzaczek/GlucoseMonitor.git
cd GlucoseMonitor
```

### 2. Configure Environment

```bash
cp env.example .env
```

Edit `.env` with your credentials:

```env
# LibreLink Up (same credentials as the mobile app)
LIBRE_EMAIL=your-email@example.com
LIBRE_PASSWORD=your-password
LIBRE_REGION=eu

# Database
DB_PASSWORD=PickAStrongPassword123!
```

> **Note:** LibreLink credentials can also be configured later via the Settings page in the UI.

### 3. Samsung Notes (Optional)

If you use Samsung Notes on Windows to log meals, find your data directory:

```powershell
dir "$env:LOCALAPPDATA\Packages\SAMSUNGELECTRONICSCoLtd.SamsungNotes_*\LocalState"
```

Add the path to your `.env`:

```env
SAMSUNG_NOTES_PATH=C:\Users\YourName\AppData\Local\Packages\SAMSUNGELECTRONICSCoLtd.SamsungNotes_wxx...\LocalState
```

### 4. Start

```bash
docker compose up -d
```

Open **http://localhost:3000** in your browser.

### 5. Configure AI Analysis (Optional)

Go to **Settings** in the UI and enter your OpenAI API key to enable AI-powered event analysis and daily summaries.

---

## ⚙️ Configuration

All settings can be configured via environment variables in `.env` or through the Settings page in the UI.

| Variable | Default | Description |
|----------|---------|-------------|
| `LIBRE_EMAIL` | — | LibreLink Up email |
| `LIBRE_PASSWORD` | — | LibreLink Up password |
| `LIBRE_PATIENT_ID` | *(auto-detect)* | Patient ID (leave empty for auto) |
| `LIBRE_REGION` | `eu` | API region (`eu`, `us`, `de`, `fr`, `jp`, `ap`, `au`, `ae`, `ca`, `eu2`) |
| `FETCH_INTERVAL` | `5` | Glucose fetch interval in minutes |
| `DB_PASSWORD` | `YourStrong!Passw0rd` | SQL Server SA password |
| `SAMSUNG_NOTES_PATH` | `./samsung-notes-placeholder` | Path to Samsung Notes LocalState directory |
| `SAMSUNG_NOTES_INTERVAL` | `10` | Samsung Notes sync interval in minutes |
| `BACKUP_PATH` | `./backup` | Local backup output directory |

---

## 📊 How It Works

1. **Wear your sensor** — Continue using your FreeStyle Libre CGM as normal.
2. **Log meals** — Write a quick note in Samsung Notes (in your designated folder, e.g., "Cukier").
3. **Check the dashboard** — The app automatically fetches glucose data every 5 minutes, syncs notes every 10 minutes, creates events, and runs AI analysis.
4. **Learn & improve** — Review which meals cause spikes, read AI analysis for actionable tips, and track your progress in daily summaries.

### Data Flow

```
FreeStyle Libre Sensor → LibreLink App (phone) → LibreLink Up Cloud
                                                        ↓ (API poll every 5 min)
Samsung Notes (phone) → Samsung Notes (Windows sync)    ↓
                    ↓ (local SQLite read every 10 min)  ↓
                    └──────────────► Glucose Monitor Backend
                                          ↓
                                    SQL Server DB
                                          ↓ (SignalR WebSocket)
                                    React Dashboard
```

---

## 🧰 Tech Stack

### Backend
- **ASP.NET Core 8.0** — REST API with MediatR CQRS pattern
- **Entity Framework Core** — SQL Server ORM
- **SignalR** — Real-time WebSocket updates
- **QuestPDF + SkiaSharp** — PDF report generation with glucose charts
- **Microsoft.Data.Sqlite** — Samsung Notes database reader

### Frontend
- **React 18** — Single-page application
- **Recharts** — Interactive glucose charts
- **date-fns** — Date formatting
- **@microsoft/signalr** — Real-time connection

### Infrastructure
- **Docker Compose** — Three-container deployment
- **SQL Server 2022** — Persistent data storage
- **nginx** — Reverse proxy for API and WebSocket routing

---

## 🧪 Testing

```bash
cd GlucoseAPI.Tests
dotnet test
```

The test suite includes:
- **Domain unit tests** — Glucose stats calculation, AI classification parsing, timezone conversion
- **Handler tests** — MediatR CQRS handlers with in-memory database
- **Service tests** — Application services with mocked dependencies
- **Integration tests** — Full HTTP pipeline via `WebApplicationFactory`

---

## 📁 Project Structure

```
Glucose/
├── docker-compose.yml          # Three-container stack definition
├── env.example                 # Environment variable template
│
├── GlucoseAPI/                 # ASP.NET Core backend
│   ├── Domain/                 # Pure business logic (no I/O)
│   ├── Application/            # MediatR CQRS handlers + interfaces
│   │   ├── Features/Chat/      # AI Chat CQRS commands & queries
│   │   ├── Features/Food/      # Food pattern CQRS commands & queries
│   │   └── Features/Meals/    # Meal queries, comparison, stats
│   ├── Infrastructure/         # External API adapters (OpenAI, SignalR)
│   ├── Controllers/            # Thin REST API endpoints (EventsController, FoodController, MealsController, etc.)
│   ├── Services/               # Background services + orchestration
│   │   ├── ChatService.cs      # Background queue processor for AI chat
│   │   ├── FoodPatternService.cs  # AI food extraction + aggregate stats
│   │   └── TranslationService.cs  # Bilingual PL↔EN translation
│   ├── Models/                 # Entity models + DTOs
│   │   ├── ChatModels.cs       # Chat session, message, template, period entities
│   │   └── FoodModels.cs       # Food item, event links, DTOs
│   └── Data/                   # EF Core DbContext
│
├── GlucoseAPI.Tests/           # Automated test suite
│
└── glucose-ui/                 # React frontend
    └── src/components/
        ├── ChatPage.js         # AI Chat with graph-based period selection
        ├── FoodPatternsPage.js # Food patterns with AI chat integration
        ├── MealsPage.js       # Meals browser with detail modal, compare, AI chat
        └── ...                 # Dashboard, Events, Summaries, Reports, Settings
```

---

## ⚖️ Disclaimer & Legal

### Not Medical Software

**This project is NOT a medical device, is NOT FDA/CE approved, and is NOT intended to diagnose, treat, cure, or prevent any disease.** It is a personal hobby project for informational and educational purposes only. Do not make medical decisions based on the data or analysis provided by this software. Always consult your healthcare provider for medical advice.

### Unofficial Integrations

This project uses **unofficial, reverse-engineered API endpoints** and **local file reading** to access data. These integrations are not endorsed, supported, or affiliated with the respective companies:

- **LibreLink Up / LibreView API**: The glucose data fetching is based on the reverse-engineered LibreLink Up protocol from the open-source [nightscout-librelink-up](https://github.com/timoschlueter/nightscout-librelink-up) project (MIT License). This is an **unofficial API** — there is no public API documentation from Abbott. This approach is widely used by the diabetes open-source community (Nightscout, xDrip+, Loop, and many others) and has been tolerated by Abbott, but it is not officially supported and could stop working at any time.

- **Samsung Notes**: The project reads the Samsung Notes Windows app's local SQLite database file (`Storage.sqlite`) directly from your filesystem. It does **not** use any Samsung SDK or API. It simply reads a file that already exists on your computer — the same data you could view by opening the file in any SQLite browser. No Samsung software is modified, redistributed, or reverse-engineered.

### No Affiliation

This project is **not affiliated with, endorsed by, or sponsored by**:
- **Abbott Laboratories** (maker of FreeStyle Libre)
- **Samsung Electronics** (maker of Samsung Notes)
- **OpenAI** (provider of GPT models)
- **Microsoft** (maker of SQL Server)

FreeStyle Libre, LibreLink, and LibreView are trademarks of Abbott. Samsung Notes is a trademark of Samsung Electronics. All other trademarks belong to their respective owners.

### Your Data, Your Responsibility

- This software runs **entirely on your local machine**. No data is sent to any third party except the APIs you explicitly configure (LibreLink Up for glucose data, OpenAI for AI analysis).
- **You are responsible** for securing your deployment, protecting your credentials, and complying with any applicable terms of service for the APIs you use.
- Health data is sensitive. Ensure your Docker host is appropriately secured and that backups are stored safely.
- The AI analysis is generated by a large language model and **may contain errors, hallucinations, or misleading information**. It is not a substitute for professional medical advice.

### Right to Access Your Own Data

This project is built on the principle that **individuals have the right to access and use their own health data**. This right is recognized by:
- **EU GDPR** (Article 20 — Right to Data Portability)
- **US 21st Century Cures Act** (Information Blocking provisions)
- **US HIPAA** (Right of Access)

You are accessing your own glucose data from your own LibreLink account, and your own notes from your own computer.

### Open-Source Community Context

The approach used in this project — accessing LibreLink Up data via unofficial API endpoints — is a well-established practice in the diabetes open-source community. Projects such as [Nightscout](https://nightscout.github.io/), [xDrip+](https://github.com/NightscoutFoundation/xDrip), and [nightscout-librelink-up](https://github.com/timoschlueter/nightscout-librelink-up) have operated publicly for years, serving tens of thousands of diabetes patients worldwide, without legal action from device manufacturers.

The `#WeAreNotWaiting` movement — a grassroots community of diabetes patients building open-source tools to improve their own care — has been widely recognized and even praised by regulatory bodies and device manufacturers for advancing patient outcomes.

---

## 📜 License

This project is licensed under the [MIT License](LICENSE).

---

## 🤝 Contributing

Contributions are welcome! Please open an issue to discuss proposed changes before submitting a pull request.

---

## 🙏 Acknowledgments

- [nightscout-librelink-up](https://github.com/timoschlueter/nightscout-librelink-up) — Reverse-engineered LibreLink Up API protocol (MIT License)
- [Nightscout](https://nightscout.github.io/) — The pioneering open-source CGM dashboard
- The **#WeAreNotWaiting** community — For proving that patients can build better tools for their own care
