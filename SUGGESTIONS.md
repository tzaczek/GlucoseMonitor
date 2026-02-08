# Glucose Monitor — Improvement Suggestions

## 🔴 Must-Fix / Technical Debt

### 1. No Authentication
The app has zero authentication. Anyone on the network can access all health data, change settings, and trigger API calls. Add at least a simple password/PIN gate or basic auth middleware.

### 2. Hardcoded GPT Model
The GPT model (`gpt-4o-mini`) is hardcoded in `EventAnalyzer.cs` and `DailySummaryService.cs`. Make it a configurable setting so users can switch models (e.g., `gpt-4o`, `gpt-4-turbo`) without code changes.

### 3. ~~Duplicated GPT Response Classes~~ ✅ FIXED
~~`GptResponse`, `GptChoice`, `GptMessage`, and `GptUsage` classes are duplicated in both `EventAnalyzer.cs` and `DailySummaryService.cs`. Extract them into a shared model file.~~
*Fixed: GPT request/response DTOs consolidated into `Infrastructure/ExternalApis/GptModels.cs`. The `IGptClient` interface and `GptAnalysisResult` record in `Application/Interfaces/IGptClient.cs` provide a single contract for all GPT interactions.*

### 4. ~~`new HttpClient()` Anti-Pattern~~ ✅ FIXED
~~Both `EventAnalyzer.cs` and `DailySummaryService.cs` create `new HttpClient()` instances directly. This can cause socket exhaustion. Use `IHttpClientFactory` instead.~~
*Fixed: All HTTP clients now use `IHttpClientFactory` with named clients ("OpenAI", "LibreLink") configured in `Program.cs`. The `OpenAiGptClient` infrastructure adapter and refactored `LibreLinkClient` both obtain pooled `HttpClient` instances from the factory.*

### 5. No Retry/Resilience for External APIs
Calls to the GPT API and LibreLink Up API have no retry logic, circuit breaker, or exponential backoff. A single transient failure causes the entire operation to fail silently. Consider adding Polly or the built-in .NET resilience library.

---

## 🟡 High-Value Features

### 6. Weekly & Monthly Reports
Generate weekly and monthly summary reports that aggregate daily summaries, identify trends across days, and show progress over time. "This week your time-in-range improved by 8% compared to last week."

### 7. Food Pattern Recognition
Track how specific foods affect glucose across multiple events. "Pizza has caused a spike >80 mg/dL in 4 out of 5 times." Build a personal food database with average glucose impact per food.

### 8. Notifications & Alerts
Push browser notifications for:
- Glucose going out of range (high/low alerts)
- Daily summary ready
- Unusual patterns detected (e.g., overnight highs)
- Sensor connection lost

### 9. Goal Tracking & Streaks
Let users set goals (e.g., "Time in range > 70%", "No spikes > 200 mg/dL") and track streaks. "You've had 5 consecutive green days!" Gamification increases engagement and motivation.

### 10. Period Comparison View
Compare two time periods side-by-side: "This week vs. last week", "Before diet change vs. after." Show overlaid glucose charts and comparative statistics.

### 11. ~~PDF Reports for Doctors~~ ✅ IMPLEMENTED
~~Generate a clean, printable PDF report for a selected time period that a user can share with their healthcare provider. Include key metrics, charts, daily summaries, and AI insights.~~
*Implemented: QuestPDF-based PDF reports with summary statistics, glucose trend chart (SkiaSharp), time-in-range bar, daily breakdown table, events table with content summaries, glucose distribution histogram, and AI analysis highlights. Available via Reports tab with date range presets and custom selection.*

### 12. ~~Estimated A1C Calculation~~ ✅ IMPLEMENTED (in reports)
~~Calculate an estimated HbA1c from the average glucose readings using the standard formula: `eA1C = (average glucose + 46.7) / 28.7`. Display it prominently on the dashboard with trend over time.~~
*Partially implemented: Estimated A1C and GMI are calculated and displayed in the PDF reports. Dashboard trend display is not yet implemented.*

---

## 🟢 Nice-to-Have Enhancements

### 13. Progressive Web App (PWA) / Mobile Support
Add a PWA manifest and service worker so the app can be installed on mobile devices and used as a home screen app. The current UI is desktop-focused.

### 14. Light Theme / Theme Toggle
The app only has a dark theme. Some users prefer light mode, especially in bright environments. Add a theme toggle in settings.

### 15. Event Tags & Categories
Allow users to tag events with categories like "breakfast", "lunch", "dinner", "snack", "exercise", "stress", "medication". Enable filtering and analysis by category.

### 16. Carb / Macro Tracking
Add optional fields for estimated carbs, protein, and fat for each event. This enables more precise AI analysis: "Your 80g carb meal caused a 120 mg/dL spike vs. your 30g carb meal which only caused 40 mg/dL."

### 17. Exercise-Specific Analysis
Differentiate between food events and exercise events. Analyze how different types of exercise (walking, running, weightlifting) affect glucose differently.

### 18. Multi-User Support
Support multiple users/profiles, each with their own LibreLink credentials, settings, and data. Useful for families where multiple members use CGMs.

### 19. Dashboard Customization
Let users choose which widgets/cards appear on the dashboard and in what order. Some users may want to see the chart first, others the latest reading, others the daily summary.

### 20. In-App Meal Logging
Allow logging meals directly in the app instead of requiring Samsung Notes. This removes the Samsung Notes dependency and makes the app more portable. Keep Samsung Notes as an optional integration.

### 21. Calendar View for Daily Summaries
Add a calendar view (month grid) where each day is color-coded by its AI classification (green/yellow/red). Clicking a day opens the daily summary. Provides an instant visual overview of glucose control over time.

### 22. Glucose Prediction
Use historical patterns to predict glucose trends for the next 1-2 hours based on current trajectory and recent meals. "Based on your current trend and the meal you logged 30 minutes ago, your glucose may peak around 185 mg/dL in ~45 minutes."

### 23. Dexcom / Other CGM Support
Currently only FreeStyle Libre is supported. Adding Dexcom (via Dexcom Share API) or Nightscout integration would broaden the user base significantly.

### 24. Backup Restore
The app creates backups but has no restore mechanism. Add a "Restore from backup" feature in Settings that can import JSON backup files back into the database.

### 25. Docker Health Checks
Add `HEALTHCHECK` instructions to both Dockerfiles so Docker can automatically detect and restart unhealthy containers. Monitor database connectivity, LibreLink API reachability, and disk space for backups.
