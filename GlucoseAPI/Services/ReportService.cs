using GlucoseAPI.Data;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SkiaSharp;

namespace GlucoseAPI.Services;

/// <summary>
/// Generates PDF medical reports for healthcare providers.
/// </summary>
public class ReportService
{
    private readonly GlucoseDbContext _db;
    private readonly SettingsService _settings;
    private readonly ILogger<ReportService> _logger;

    // Colors matching the app theme
    private static readonly string ColorPrimary = "#0ea5e9";
    private static readonly string ColorGreen = "#22c55e";
    private static readonly string ColorYellow = "#eab308";
    private static readonly string ColorRed = "#ef4444";
    private static readonly string ColorGrayDark = "#1e293b";
    private static readonly string ColorGrayMedium = "#475569";
    private static readonly string ColorGrayLight = "#94a3b8";
    private static readonly string ColorBorderLight = "#e2e8f0";

    public ReportService(GlucoseDbContext db, SettingsService settings, ILogger<ReportService> logger)
    {
        _db = db;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Generate a PDF report for the given date range.</summary>
    public async Task<byte[]> GenerateReportAsync(DateTime fromDate, DateTime toDate, CancellationToken ct)
    {
        var tz = await _settings.GetAsync(SettingKeys.DisplayTimeZone, "Europe/Warsaw");
        var tzInfo = TimeZoneInfo.FindSystemTimeZoneById(tz);

        // Convert local dates to UTC boundaries
        var fromLocal = fromDate.Date;
        var toLocal = toDate.Date.AddDays(1); // end of the last day
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(fromLocal, tzInfo);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(toLocal, tzInfo);

        // Fetch data
        var readings = await _db.GlucoseReadings
            .Where(r => r.Timestamp >= fromUtc && r.Timestamp < toUtc)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        var events = await _db.GlucoseEvents
            .Where(e => e.EventTimestamp >= fromUtc && e.EventTimestamp < toUtc)
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(ct);

        var dailySummaries = await _db.DailySummaries
            .Where(d => d.Date >= fromLocal && d.Date <= toDate.Date)
            .OrderBy(d => d.Date)
            .ToListAsync(ct);

        // Compute aggregate stats
        var totalReadings = readings.Count;
        double? avgGlucose = totalReadings > 0 ? Math.Round(readings.Average(r => r.Value), 1) : null;
        double? minGlucose = totalReadings > 0 ? readings.Min(r => r.Value) : null;
        double? maxGlucose = totalReadings > 0 ? readings.Max(r => r.Value) : null;
        double? stdDev = null;
        if (totalReadings > 1 && avgGlucose.HasValue)
        {
            var variance = readings.Average(r => Math.Pow(r.Value - avgGlucose.Value, 2));
            stdDev = Math.Round(Math.Sqrt(variance), 1);
        }

        var inRangeCount = readings.Count(r => r.Value >= 70 && r.Value <= 180);
        var belowRangeCount = readings.Count(r => r.Value < 70);
        var aboveRangeCount = readings.Count(r => r.Value > 180);
        double? timeInRange = totalReadings > 0 ? Math.Round((double)inRangeCount / totalReadings * 100, 1) : null;
        double? timeBelowRange = totalReadings > 0 ? Math.Round((double)belowRangeCount / totalReadings * 100, 1) : null;
        double? timeAboveRange = totalReadings > 0 ? Math.Round((double)aboveRangeCount / totalReadings * 100, 1) : null;

        // Estimated A1C = (average glucose + 46.7) / 28.7
        double? estimatedA1C = avgGlucose.HasValue ? Math.Round((avgGlucose.Value + 46.7) / 28.7, 1) : null;

        // GMI (Glucose Management Indicator) = 3.31 + 0.02392 × mean glucose (mg/dL)
        double? gmi = avgGlucose.HasValue ? Math.Round(3.31 + 0.02392 * avgGlucose.Value, 1) : null;

        // Coefficient of Variation
        double? cv = (stdDev.HasValue && avgGlucose.HasValue && avgGlucose.Value > 0)
            ? Math.Round(stdDev.Value / avgGlucose.Value * 100, 1)
            : null;

        var totalDays = (toDate.Date - fromDate.Date).Days + 1;

        // Generate PDF
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(40);
                page.MarginBottom(40);
                page.MarginHorizontal(45);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(ColorGrayDark));

                page.Header().Element(c => ComposeHeader(c, fromLocal, toLocal.AddDays(-1), tz, totalDays));

                page.Content().Element(c => ComposeContent(c,
                    readings, events, dailySummaries, tzInfo,
                    totalReadings, avgGlucose, minGlucose, maxGlucose, stdDev,
                    timeInRange, timeBelowRange, timeAboveRange,
                    estimatedA1C, gmi, cv, totalDays));

                page.Footer().Element(ComposeFooter);
            });
        });

        return document.GeneratePdf();
    }

    private void ComposeHeader(IContainer container, DateTime fromDate, DateTime toDate, string timezone, int totalDays)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(inner =>
                {
                    inner.Item().Text("Glucose Monitoring Report")
                        .FontSize(18).Bold().FontColor(ColorPrimary);
                    inner.Item().Text("Continuous Glucose Monitor Data Summary")
                        .FontSize(9).FontColor(ColorGrayLight);
                });

                row.ConstantItem(140).AlignRight().Column(inner =>
                {
                    inner.Item().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}")
                        .FontSize(7).FontColor(ColorGrayLight);
                    inner.Item().Text($"Timezone: {timezone}")
                        .FontSize(7).FontColor(ColorGrayLight);
                });
            });

            col.Item().PaddingTop(8).LineHorizontal(2).LineColor(ColorPrimary);

            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("Period: ").FontColor(ColorGrayMedium).FontSize(9);
                    text.Span($"{fromDate:yyyy-MM-dd}").Bold().FontSize(9);
                    text.Span(" — ").FontColor(ColorGrayMedium).FontSize(9);
                    text.Span($"{toDate:yyyy-MM-dd}").Bold().FontSize(9);
                    text.Span($"  ({totalDays} day{(totalDays != 1 ? "s" : "")})").FontColor(ColorGrayLight).FontSize(8);
                });
            });

            col.Item().PaddingBottom(10);
        });
    }

    private void ComposeContent(IContainer container,
        List<GlucoseReading> readings, List<GlucoseEvent> events, List<DailySummary> dailySummaries,
        TimeZoneInfo tzInfo,
        int totalReadings, double? avgGlucose, double? minGlucose, double? maxGlucose, double? stdDev,
        double? timeInRange, double? timeBelowRange, double? timeAboveRange,
        double? estimatedA1C, double? gmi, double? cv, int totalDays)
    {
        container.Column(col =>
        {
            // ── Summary Statistics ──────────────────────────
            col.Item().Element(c => ComposeSummaryStats(c,
                totalReadings, avgGlucose, minGlucose, maxGlucose, stdDev,
                timeInRange, timeBelowRange, timeAboveRange,
                estimatedA1C, gmi, cv, totalDays, events.Count));

            // ── Time in Range Bar ───────────────────────────
            if (timeInRange.HasValue)
            {
                col.Item().PaddingTop(12).Element(c => ComposeTimeInRangeBar(c,
                    timeInRange.Value, timeBelowRange ?? 0, timeAboveRange ?? 0));
            }

            // ── Glucose Trend Chart ────────────────────────────
            if (readings.Count > 1)
            {
                col.Item().PaddingTop(16).Element(c => ComposeGlucoseChart(c, readings, events, tzInfo));
            }

            // ── Daily Breakdown ─────────────────────────────
            if (dailySummaries.Any())
            {
                col.Item().PaddingTop(16).Element(c => ComposeDailyTable(c, dailySummaries, tzInfo));
            }

            // ── Events Detail ───────────────────────────────
            if (events.Any())
            {
                col.Item().PaddingTop(16).Element(c => ComposeEventsTable(c, events, tzInfo));
            }

            // ── Glucose Distribution ────────────────────────
            if (readings.Any())
            {
                col.Item().PaddingTop(16).Element(c => ComposeGlucoseDistribution(c, readings));
            }

            // ── AI Insights Summary ─────────────────────────
            if (dailySummaries.Any(d => !string.IsNullOrEmpty(d.AiAnalysis)))
            {
                col.Item().PaddingTop(16).Element(c => ComposeAiInsights(c, dailySummaries, events));
            }

            // ── Disclaimer ──────────────────────────────────
            col.Item().PaddingTop(20).Element(ComposeDisclaimer);
        });
    }

    private void ComposeSummaryStats(IContainer container,
        int totalReadings, double? avgGlucose, double? minGlucose, double? maxGlucose, double? stdDev,
        double? timeInRange, double? timeBelowRange, double? timeAboveRange,
        double? estimatedA1C, double? gmi, double? cv, int totalDays, int eventCount)
    {
        container.Column(col =>
        {
            col.Item().Text("Summary Statistics").FontSize(13).Bold().FontColor(ColorPrimary);
            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(ColorBorderLight);

            col.Item().PaddingTop(8).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                // Row 1: Core metrics
                table.Cell().Element(StatCell).Column(inner =>
                {
                    inner.Item().Text("Average Glucose").FontSize(7).FontColor(ColorGrayLight);
                    inner.Item().Text($"{FormatValue(avgGlucose)} mg/dL").FontSize(14).Bold();
                });

                table.Cell().Element(StatCell).Column(inner =>
                {
                    inner.Item().Text("Estimated A1C").FontSize(7).FontColor(ColorGrayLight);
                    inner.Item().Text($"{FormatValue(estimatedA1C)}%").FontSize(14).Bold().FontColor(ColorPrimary);
                });

                table.Cell().Element(StatCell).Column(inner =>
                {
                    inner.Item().Text("Time in Range").FontSize(7).FontColor(ColorGrayLight);
                    inner.Item().Text($"{FormatValue(timeInRange)}%").FontSize(14).Bold()
                        .FontColor(GetTirColor(timeInRange));
                });

                table.Cell().Element(StatCell).Column(inner =>
                {
                    inner.Item().Text("Glucose Variability (CV)").FontSize(7).FontColor(ColorGrayLight);
                    inner.Item().Text($"{FormatValue(cv)}%").FontSize(14).Bold()
                        .FontColor(cv.HasValue && cv.Value > 36 ? ColorRed : ColorGreen);
                });

                // Row 2: Additional metrics
                table.Cell().Element(StatCell).Column(inner =>
                {
                    inner.Item().Text("Min Glucose").FontSize(7).FontColor(ColorGrayLight);
                    inner.Item().Text($"{FormatValue(minGlucose)} mg/dL").FontSize(11).SemiBold();
                });

                table.Cell().Element(StatCell).Column(inner =>
                {
                    inner.Item().Text("Max Glucose").FontSize(7).FontColor(ColorGrayLight);
                    inner.Item().Text($"{FormatValue(maxGlucose)} mg/dL").FontSize(11).SemiBold();
                });

                table.Cell().Element(StatCell).Column(inner =>
                {
                    inner.Item().Text("Std Deviation").FontSize(7).FontColor(ColorGrayLight);
                    inner.Item().Text($"{FormatValue(stdDev)} mg/dL").FontSize(11).SemiBold();
                });

                table.Cell().Element(StatCell).Column(inner =>
                {
                    inner.Item().Text("GMI").FontSize(7).FontColor(ColorGrayLight);
                    inner.Item().Text($"{FormatValue(gmi)}%").FontSize(11).SemiBold();
                });

                // Row 3: Counts
                table.Cell().Element(StatCell).Column(inner =>
                {
                    inner.Item().Text("Total Readings").FontSize(7).FontColor(ColorGrayLight);
                    inner.Item().Text($"{totalReadings:N0}").FontSize(11).SemiBold();
                });

                table.Cell().Element(StatCell).Column(inner =>
                {
                    inner.Item().Text("Days Covered").FontSize(7).FontColor(ColorGrayLight);
                    inner.Item().Text($"{totalDays}").FontSize(11).SemiBold();
                });

                table.Cell().Element(StatCell).Column(inner =>
                {
                    inner.Item().Text("Events Logged").FontSize(7).FontColor(ColorGrayLight);
                    inner.Item().Text($"{eventCount}").FontSize(11).SemiBold();
                });

                table.Cell().Element(StatCell).Column(inner =>
                {
                    inner.Item().Text("Below / Above Range").FontSize(7).FontColor(ColorGrayLight);
                    inner.Item().Text(text =>
                    {
                        text.Span($"{FormatValue(timeBelowRange)}%").FontColor(ColorRed).FontSize(11).SemiBold();
                        text.Span(" / ").FontColor(ColorGrayLight).FontSize(9);
                        text.Span($"{FormatValue(timeAboveRange)}%").FontColor(ColorYellow).FontSize(11).SemiBold();
                    });
                });
            });
        });
    }

    private void ComposeTimeInRangeBar(IContainer container, double inRange, double belowRange, double aboveRange)
    {
        container.Column(col =>
        {
            col.Item().Text("Time in Range Distribution").FontSize(10).Bold().FontColor(ColorGrayMedium);
            col.Item().PaddingTop(6).Row(row =>
            {
                if (belowRange > 0)
                    row.RelativeItem((float)Math.Max(belowRange, 2))
                        .Height(16).Background(ColorRed);

                if (inRange > 0)
                    row.RelativeItem((float)Math.Max(inRange, 2))
                        .Height(16).Background(ColorGreen);

                if (aboveRange > 0)
                    row.RelativeItem((float)Math.Max(aboveRange, 2))
                        .Height(16).Background(ColorYellow);
            });
            col.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("■ ").FontColor(ColorRed).FontSize(8);
                    text.Span($"Below (<70): {belowRange}%  ").FontSize(7).FontColor(ColorGrayMedium);
                    text.Span("■ ").FontColor(ColorGreen).FontSize(8);
                    text.Span($"In Range (70-180): {inRange}%  ").FontSize(7).FontColor(ColorGrayMedium);
                    text.Span("■ ").FontColor(ColorYellow).FontSize(8);
                    text.Span($"Above (>180): {aboveRange}%").FontSize(7).FontColor(ColorGrayMedium);
                });
            });
        });
    }

    private void ComposeDailyTable(IContainer container, List<DailySummary> dailySummaries, TimeZoneInfo tzInfo)
    {
        container.Column(col =>
        {
            col.Item().Text("Daily Breakdown").FontSize(13).Bold().FontColor(ColorPrimary);
            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(ColorBorderLight);

            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(70);  // Date
                    columns.ConstantColumn(22);  // Classification
                    columns.RelativeColumn();    // Events
                    columns.ConstantColumn(45);  // Avg
                    columns.ConstantColumn(45);  // Min
                    columns.ConstantColumn(45);  // Max
                    columns.ConstantColumn(45);  // TIR
                    columns.ConstantColumn(35);  // Readings
                });

                // Header
                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Date");
                    header.Cell().Element(HeaderCell).Text("");
                    header.Cell().Element(HeaderCell).Text("Events");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Avg");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Min");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Max");
                    header.Cell().Element(HeaderCell).AlignRight().Text("TIR");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Rdgs");
                });

                // Rows
                foreach (var day in dailySummaries)
                {
                    var isAlt = dailySummaries.IndexOf(day) % 2 == 1;

                    table.Cell().Element(c => DataCell(c, isAlt))
                        .Text($"{day.Date:ddd MM/dd}").FontSize(8).SemiBold();

                    table.Cell().Element(c => DataCell(c, isAlt)).AlignCenter()
                        .Text(GetClassificationIcon(day.AiClassification)).FontSize(8);

                    table.Cell().Element(c => DataCell(c, isAlt))
                        .Text(TruncateText(day.EventTitles, 40)).FontSize(7).FontColor(ColorGrayMedium);

                    table.Cell().Element(c => DataCell(c, isAlt)).AlignRight()
                        .Text($"{FormatValue(day.GlucoseAvg)}").FontSize(8);

                    table.Cell().Element(c => DataCell(c, isAlt)).AlignRight()
                        .Text($"{FormatValue(day.GlucoseMin)}").FontSize(8)
                        .FontColor(day.GlucoseMin.HasValue && day.GlucoseMin < 70 ? ColorRed : ColorGrayDark);

                    table.Cell().Element(c => DataCell(c, isAlt)).AlignRight()
                        .Text($"{FormatValue(day.GlucoseMax)}").FontSize(8)
                        .FontColor(day.GlucoseMax.HasValue && day.GlucoseMax > 180 ? ColorYellow : ColorGrayDark);

                    table.Cell().Element(c => DataCell(c, isAlt)).AlignRight()
                        .Text($"{FormatValue(day.TimeInRange)}%").FontSize(8)
                        .FontColor(GetTirColor(day.TimeInRange));

                    table.Cell().Element(c => DataCell(c, isAlt)).AlignRight()
                        .Text($"{day.ReadingCount}").FontSize(8).FontColor(ColorGrayMedium);
                }
            });
        });
    }

    private void ComposeEventsTable(IContainer container, List<GlucoseEvent> events, TimeZoneInfo tzInfo)
    {
        container.Column(col =>
        {
            col.Item().Text("Meal & Activity Events").FontSize(13).Bold().FontColor(ColorPrimary);
            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(ColorBorderLight);

            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(80);  // DateTime
                    columns.ConstantColumn(22);  // Classification
                    columns.RelativeColumn();    // Content summary
                    columns.ConstantColumn(50);  // At Event
                    columns.ConstantColumn(50);  // Spike
                    columns.ConstantColumn(50);  // Min-Max
                    columns.ConstantColumn(35);  // Readings
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Date/Time");
                    header.Cell().Element(HeaderCell).Text("");
                    header.Cell().Element(HeaderCell).Text("Description");
                    header.Cell().Element(HeaderCell).AlignRight().Text("@ Event");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Spike");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Range");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Rdgs");
                });

                foreach (var evt in events)
                {
                    var isAlt = events.IndexOf(evt) % 2 == 1;
                    var localTime = TimeZoneInfo.ConvertTimeFromUtc(evt.EventTimestamp, tzInfo);

                    table.Cell().Element(c => DataCell(c, isAlt))
                        .Text($"{localTime:MM/dd HH:mm}").FontSize(7).SemiBold();

                    table.Cell().Element(c => DataCell(c, isAlt)).AlignCenter()
                        .Text(GetClassificationIcon(evt.AiClassification)).FontSize(8);

                    // Show content summary instead of title
                    var contentSummary = SummarizeContent(evt.NoteContent, evt.NoteTitle, 60);
                    table.Cell().Element(c => DataCell(c, isAlt))
                        .Text(contentSummary).FontSize(7).FontColor(ColorGrayMedium);

                    table.Cell().Element(c => DataCell(c, isAlt)).AlignRight()
                        .Text($"{FormatValue(evt.GlucoseAtEvent)}").FontSize(8);

                    table.Cell().Element(c => DataCell(c, isAlt)).AlignRight()
                        .Text(evt.GlucoseSpike.HasValue ? $"+{Math.Round(evt.GlucoseSpike.Value)}" : "–").FontSize(8)
                        .FontColor(GetSpikeColor(evt.GlucoseSpike));

                    table.Cell().Element(c => DataCell(c, isAlt)).AlignRight()
                        .Text($"{FormatValue(evt.GlucoseMin)}-{FormatValue(evt.GlucoseMax)}").FontSize(7)
                        .FontColor(ColorGrayMedium);

                    table.Cell().Element(c => DataCell(c, isAlt)).AlignRight()
                        .Text($"{evt.ReadingCount}").FontSize(8).FontColor(ColorGrayMedium);
                }
            });

            // Legend
            col.Item().PaddingTop(6).Text(text =>
            {
                text.Span("Classification: ").FontSize(7).FontColor(ColorGrayLight);
                text.Span("● Good  ").FontSize(7).FontColor(ColorGreen);
                text.Span("● Concerning  ").FontSize(7).FontColor(ColorYellow);
                text.Span("● Problematic  ").FontSize(7).FontColor(ColorRed);
                text.Span("  |  Spike: ").FontSize(7).FontColor(ColorGrayLight);
                text.Span("<30 mild  ").FontSize(7).FontColor(ColorGreen);
                text.Span("30-60 moderate  ").FontSize(7).FontColor(ColorYellow);
                text.Span(">60 significant").FontSize(7).FontColor(ColorRed);
            });
        });
    }

    private void ComposeGlucoseChart(IContainer container,
        List<GlucoseReading> readings, List<GlucoseEvent> events, TimeZoneInfo tzInfo)
    {
        // Render chart as a PNG image using SkiaSharp, then embed in PDF
        // Using 3× resolution for crisp rendering when scaled down in PDF
        const int imgW = 1515;
        const int imgH = 600;
        const float marginLeft = 120f;
        const float marginRight = 30f;
        const float marginTop = 30f;
        const float marginBottom = 90f;
        const float plotW = imgW - marginLeft - marginRight;
        const float plotH = imgH - marginTop - marginBottom;

        // Y-axis range
        var minVal = Math.Max(40, readings.Min(r => r.Value) - 10);
        var maxVal = Math.Min(400, readings.Max(r => r.Value) + 10);
        if (maxVal - minVal < 50) maxVal = minVal + 50;

        // X-axis range (timestamps)
        var tMin = readings.First().Timestamp.Ticks;
        var tMax = readings.Last().Timestamp.Ticks;
        if (tMax <= tMin) tMax = tMin + TimeSpan.TicksPerHour;

        float XOf(DateTime dt) => marginLeft + (float)((dt.Ticks - tMin) / (double)(tMax - tMin)) * plotW;
        float YOf(double val) => marginTop + (float)((maxVal - val) / (maxVal - minVal)) * plotH;

        byte[] chartImageBytes;
        using (var surface = SKSurface.Create(new SKImageInfo(imgW, imgH, SKColorType.Rgba8888, SKAlphaType.Premul)))
        {
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.White);

            // ── Background ──
            using var bgPaint = new SKPaint { Color = SKColor.Parse("#f8fafc"), Style = SKPaintStyle.Fill };
            canvas.DrawRect(marginLeft, marginTop, plotW, plotH, bgPaint);

            // ── Target range band (70-180) ──
            var rangeLow = Math.Max(70, minVal);
            var rangeHigh = Math.Min(180, maxVal);
            if (rangeHigh > rangeLow)
            {
                var yTop = YOf(rangeHigh);
                var yBot = YOf(rangeLow);
                using var rangePaint = new SKPaint
                {
                    Color = SKColor.Parse("#22c55e").WithAlpha(30),
                    Style = SKPaintStyle.Fill
                };
                canvas.DrawRect(marginLeft, yTop, plotW, yBot - yTop, rangePaint);
            }

            // ── Grid lines & Y-axis labels ──
            var yStep = (maxVal - minVal) <= 100 ? 20 : (maxVal - minVal) <= 200 ? 40 : 50;
            using var gridPaint = new SKPaint
            {
                Color = SKColor.Parse("#e2e8f0"),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                PathEffect = SKPathEffect.CreateDash(new[] { 8f, 8f }, 0)
            };
            using var axisLabelPaint = new SKPaint
            {
                Color = SKColor.Parse("#64748b"),
                TextSize = 21f,
                IsAntialias = true,
                TextAlign = SKTextAlign.Right
            };

            for (var v = (int)(Math.Ceiling(minVal / yStep) * yStep); v <= maxVal; v += yStep)
            {
                var y = YOf(v);
                canvas.DrawLine(marginLeft, y, marginLeft + plotW, y, gridPaint);
                canvas.DrawText($"{v}", marginLeft - 10, y + 7, axisLabelPaint);
            }

            // ── Target range boundary lines (70, 180) ──
            using var targetLinePaint = new SKPaint
            {
                Color = SKColor.Parse("#22c55e").WithAlpha(100),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2.4f,
                PathEffect = SKPathEffect.CreateDash(new[] { 12f, 6f }, 0)
            };
            if (70 >= minVal && 70 <= maxVal)
                canvas.DrawLine(marginLeft, YOf(70), marginLeft + plotW, YOf(70), targetLinePaint);
            if (180 >= minVal && 180 <= maxVal)
                canvas.DrawLine(marginLeft, YOf(180), marginLeft + plotW, YOf(180), targetLinePaint);

            // ── Target range labels ──
            using var targetLabelPaint = new SKPaint
            {
                Color = SKColor.Parse("#22c55e").WithAlpha(180),
                TextSize = 18f,
                IsAntialias = true
            };
            if (70 >= minVal && 70 <= maxVal)
                canvas.DrawText("70", marginLeft + 6, YOf(70) - 6, targetLabelPaint);
            if (180 >= minVal && 180 <= maxVal)
                canvas.DrawText("180", marginLeft + 6, YOf(180) - 6, targetLabelPaint);

            // ── X-axis date labels ──
            var totalSpan = TimeSpan.FromTicks(tMax - tMin);
            var labelInterval = totalSpan.TotalDays <= 1 ? TimeSpan.FromHours(3)
                : totalSpan.TotalDays <= 3 ? TimeSpan.FromHours(12)
                : totalSpan.TotalDays <= 7 ? TimeSpan.FromDays(1)
                : totalSpan.TotalDays <= 30 ? TimeSpan.FromDays(2)
                : TimeSpan.FromDays(7);

            var dateFormat = totalSpan.TotalDays <= 2 ? "HH:mm" : "MM/dd";

            using var xLabelPaint = new SKPaint
            {
                Color = SKColor.Parse("#64748b"),
                TextSize = 19.5f,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center
            };
            using var xTickPaint = new SKPaint
            {
                Color = SKColor.Parse("#cbd5e1"),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f
            };

            var firstLocal = TimeZoneInfo.ConvertTimeFromUtc(readings.First().Timestamp, tzInfo);
            var labelStart = totalSpan.TotalDays <= 2
                ? new DateTime(firstLocal.Year, firstLocal.Month, firstLocal.Day, (firstLocal.Hour / 3) * 3, 0, 0)
                : firstLocal.Date;
            var labelStartUtc = TimeZoneInfo.ConvertTimeToUtc(labelStart, tzInfo);

            for (var t = labelStartUtc; t.Ticks <= tMax; t = t.Add(labelInterval))
            {
                if (t.Ticks < tMin) continue;
                var x = XOf(t);
                canvas.DrawLine(x, marginTop + plotH, x, marginTop + plotH + 12, xTickPaint);
                var localT = TimeZoneInfo.ConvertTimeFromUtc(t, tzInfo);
                canvas.DrawText(localT.ToString(dateFormat), x, marginTop + plotH + 36, xLabelPaint);
            }

            // ── Glucose trend line ──
            using var linePaint = new SKPaint
            {
                Color = SKColor.Parse("#0ea5e9"),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3.6f,
                IsAntialias = true,
                StrokeJoin = SKStrokeJoin.Round
            };

            var plotReadings = readings;
            if (readings.Count > 2000)
            {
                var step = readings.Count / 2000;
                plotReadings = readings.Where((_, i) => i % step == 0).ToList();
            }

            using var path = new SKPath();
            path.MoveTo(XOf(plotReadings[0].Timestamp), YOf(plotReadings[0].Value));
            for (int i = 1; i < plotReadings.Count; i++)
            {
                var r = plotReadings[i];
                var gap = (r.Timestamp - plotReadings[i - 1].Timestamp).TotalMinutes;
                if (gap > 30)
                    path.MoveTo(XOf(r.Timestamp), YOf(r.Value));
                else
                    path.LineTo(XOf(r.Timestamp), YOf(r.Value));
            }
            canvas.DrawPath(path, linePaint);

            // ── High/Low coloring ──
            using var highPaint = new SKPaint
            {
                Color = SKColor.Parse("#ef4444").WithAlpha(160),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 4.8f,
                IsAntialias = true,
                StrokeJoin = SKStrokeJoin.Round
            };
            using var lowPaint = new SKPaint
            {
                Color = SKColor.Parse("#eab308").WithAlpha(200),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 4.8f,
                IsAntialias = true,
                StrokeJoin = SKStrokeJoin.Round
            };

            for (int i = 1; i < plotReadings.Count; i++)
            {
                var gap = (plotReadings[i].Timestamp - plotReadings[i - 1].Timestamp).TotalMinutes;
                if (gap > 30) continue;
                var v0 = plotReadings[i - 1].Value;
                var v1 = plotReadings[i].Value;
                if (v0 > 180 || v1 > 180)
                {
                    canvas.DrawLine(XOf(plotReadings[i - 1].Timestamp), YOf(v0),
                        XOf(plotReadings[i].Timestamp), YOf(v1), highPaint);
                }
                else if (v0 < 70 || v1 < 70)
                {
                    canvas.DrawLine(XOf(plotReadings[i - 1].Timestamp), YOf(v0),
                        XOf(plotReadings[i].Timestamp), YOf(v1), lowPaint);
                }
            }

            // ── Event markers ──
            using var eventPaint = new SKPaint
            {
                Color = SKColor.Parse("#8b5cf6"),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            using var eventStrokePaint = new SKPaint
            {
                Color = SKColor.Parse("#8b5cf6").WithAlpha(100),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                IsAntialias = true,
                PathEffect = SKPathEffect.CreateDash(new[] { 6f, 6f }, 0)
            };

            foreach (var evt in events)
            {
                if (evt.EventTimestamp.Ticks < tMin || evt.EventTimestamp.Ticks > tMax) continue;
                var ex = XOf(evt.EventTimestamp);
                canvas.DrawLine(ex, marginTop, ex, marginTop + plotH, eventStrokePaint);

                var ey = marginTop + 12;
                using var diamond = new SKPath();
                diamond.MoveTo(ex, ey - 9);
                diamond.LineTo(ex + 9, ey);
                diamond.LineTo(ex, ey + 9);
                diamond.LineTo(ex - 9, ey);
                diamond.Close();
                canvas.DrawPath(diamond, eventPaint);
            }

            // ── Plot border ──
            using var borderPaint = new SKPaint
            {
                Color = SKColor.Parse("#cbd5e1"),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3f
            };
            canvas.DrawRect(marginLeft, marginTop, plotW, plotH, borderPaint);

            // ── Legend at bottom ──
            var legendY = imgH - 24f;
            var legendX = marginLeft;

            using var legendLinePaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 6f, IsAntialias = true };
            using var legendTextPaint = new SKPaint { TextSize = 18f, IsAntialias = true, Color = SKColor.Parse("#475569") };
            using var legendDotPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };

            // Glucose
            legendLinePaint.Color = SKColor.Parse("#0ea5e9");
            canvas.DrawLine(legendX, legendY, legendX + 30, legendY, legendLinePaint);
            canvas.DrawText("Glucose", legendX + 38, legendY + 6, legendTextPaint);
            legendX += 140;

            // Above Range
            legendLinePaint.Color = SKColor.Parse("#ef4444");
            canvas.DrawLine(legendX, legendY, legendX + 30, legendY, legendLinePaint);
            canvas.DrawText("Above Range (>180)", legendX + 38, legendY + 6, legendTextPaint);
            legendX += 250;

            // Below Range
            legendLinePaint.Color = SKColor.Parse("#eab308");
            canvas.DrawLine(legendX, legendY, legendX + 30, legendY, legendLinePaint);
            canvas.DrawText("Below Range (<70)", legendX + 38, legendY + 6, legendTextPaint);
            legendX += 240;

            // Target Zone
            using var legendRangePaint = new SKPaint { Color = SKColor.Parse("#22c55e").WithAlpha(50), Style = SKPaintStyle.Fill };
            canvas.DrawRect(legendX, legendY - 9, 18, 18, legendRangePaint);
            canvas.DrawText("Target Zone (70-180)", legendX + 26, legendY + 6, legendTextPaint);
            legendX += 260;

            // Events
            legendDotPaint.Color = SKColor.Parse("#8b5cf6");
            canvas.DrawCircle(legendX + 6, legendY, 6, legendDotPaint);
            canvas.DrawText("Events", legendX + 18, legendY + 6, legendTextPaint);

            // Flush and encode
            canvas.Flush();
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            chartImageBytes = data.ToArray();
        }

        container.Column(col =>
        {
            col.Item().Text("Glucose Trend").FontSize(13).Bold().FontColor(ColorPrimary);
            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(ColorBorderLight);
            col.Item().PaddingTop(6).Image(chartImageBytes);
        });
    }

    private void ComposeGlucoseDistribution(IContainer container, List<GlucoseReading> readings)
    {
        // Create distribution buckets
        var buckets = new (string Label, int Min, int Max, string Color)[]
        {
            ("< 54", 0, 54, ColorRed),
            ("54-69", 54, 70, "#fb923c"),
            ("70-100", 70, 101, ColorGreen),
            ("100-140", 101, 141, ColorGreen),
            ("140-180", 141, 181, ColorYellow),
            ("180-250", 181, 251, "#fb923c"),
            ("> 250", 251, 999, ColorRed)
        };

        var total = readings.Count;
        var distribution = buckets.Select(b => new
        {
            b.Label,
            b.Color,
            Count = readings.Count(r => r.Value >= b.Min && r.Value < b.Max),
            Pct = total > 0 ? Math.Round((double)readings.Count(r => r.Value >= b.Min && r.Value < b.Max) / total * 100, 1) : 0
        }).ToList();

        container.Column(col =>
        {
            col.Item().Text("Glucose Distribution").FontSize(13).Bold().FontColor(ColorPrimary);
            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(ColorBorderLight);

            col.Item().PaddingTop(6).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(60);  // Range
                    columns.RelativeColumn();     // Bar
                    columns.ConstantColumn(45);  // Count
                    columns.ConstantColumn(40);  // Pct
                });

                foreach (var bucket in distribution)
                {
                    table.Cell().PaddingVertical(3).AlignRight().PaddingRight(8)
                        .Text(bucket.Label).FontSize(8).FontColor(ColorGrayMedium);

                    table.Cell().PaddingVertical(3).AlignLeft().PaddingRight(8).Column(c =>
                    {
                        var barWidth = (float)Math.Max(bucket.Pct * 2.5, 1); // Scale for visual
                        c.Item().Width(barWidth).Height(12).Background(bucket.Color);
                    });

                    table.Cell().PaddingVertical(3).AlignRight()
                        .Text($"{bucket.Count}").FontSize(8).FontColor(ColorGrayMedium);

                    table.Cell().PaddingVertical(3).AlignRight()
                        .Text($"{bucket.Pct}%").FontSize(8).SemiBold();
                }
            });
        });
    }

    private void ComposeAiInsights(IContainer container, List<DailySummary> dailySummaries, List<GlucoseEvent> events)
    {
        container.Column(col =>
        {
            col.Item().Text("AI Analysis Highlights").FontSize(13).Bold().FontColor(ColorPrimary);
            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(ColorBorderLight);

            // Show daily summary AI insights (limit to avoid overly long reports)
            var summariesWithAnalysis = dailySummaries
                .Where(d => !string.IsNullOrEmpty(d.AiAnalysis))
                .OrderByDescending(d => d.Date)
                .Take(5)
                .ToList();

            foreach (var summary in summariesWithAnalysis)
            {
                col.Item().PaddingTop(8).Column(inner =>
                {
                    inner.Item().Row(row =>
                    {
                        row.AutoItem().Text($"{summary.Date:ddd, MMM dd}")
                            .FontSize(9).Bold().FontColor(ColorGrayDark);

                        if (!string.IsNullOrEmpty(summary.AiClassification))
                        {
                            row.AutoItem().PaddingLeft(8)
                                .Text(GetClassificationLabel(summary.AiClassification))
                                .FontSize(7).Bold()
                                .FontColor(GetClassificationColor(summary.AiClassification));
                        }
                    });

                    // Strip markdown formatting for cleaner PDF display
                    var cleanAnalysis = StripMarkdown(summary.AiAnalysis ?? "");
                    if (cleanAnalysis.Length > 600)
                        cleanAnalysis = cleanAnalysis[..597] + "...";

                    inner.Item().PaddingTop(3).PaddingLeft(8)
                        .Text(cleanAnalysis).FontSize(7.5f).FontColor(ColorGrayMedium).LineHeight(1.4f);
                });
            }

            if (summariesWithAnalysis.Count < dailySummaries.Count(d => !string.IsNullOrEmpty(d.AiAnalysis)))
            {
                col.Item().PaddingTop(6).Text($"... and {dailySummaries.Count(d => !string.IsNullOrEmpty(d.AiAnalysis)) - summariesWithAnalysis.Count} more day(s) with AI analysis.")
                    .FontSize(7).FontColor(ColorGrayLight).Italic();
            }
        });
    }

    private void ComposeDisclaimer(IContainer container)
    {
        container.Background("#f8fafc").BorderColor(ColorBorderLight).Border(1)
            .Padding(12).Column(col =>
            {
                col.Item().Text("Important Disclaimer").FontSize(8).Bold().FontColor(ColorGrayMedium);
                col.Item().PaddingTop(4).Text(
                    "This report is generated automatically from continuous glucose monitoring data and AI-powered analysis. " +
                    "It is intended as a supplementary tool for discussions with your healthcare provider. " +
                    "It should NOT be used as a sole basis for medical decisions. " +
                    "AI analysis may contain inaccuracies. Always consult with a qualified healthcare professional " +
                    "for medical advice, diagnosis, or treatment.")
                    .FontSize(7).FontColor(ColorGrayLight).LineHeight(1.5f);
            });
    }

    private void ComposeFooter(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Text("Glucose Monitor — CGM Data Report")
                .FontSize(7).FontColor(ColorGrayLight);

            row.RelativeItem().AlignRight().Text(text =>
            {
                text.Span("Page ").FontSize(7).FontColor(ColorGrayLight);
                text.CurrentPageNumber().FontSize(7).FontColor(ColorGrayMedium);
                text.Span(" of ").FontSize(7).FontColor(ColorGrayLight);
                text.TotalPages().FontSize(7).FontColor(ColorGrayMedium);
            });
        });
    }

    // ── Helper methods ───────────────────────────────────────

    private static IContainer StatCell(IContainer container)
        => container.Padding(6).Background("#f8fafc").Border(1).BorderColor(ColorBorderLight);

    private static IContainer HeaderCell(IContainer container)
        => container.PaddingVertical(6).PaddingHorizontal(4)
            .BorderBottom(2).BorderColor(ColorPrimary)
            .DefaultTextStyle(x => x.FontSize(7).Bold().FontColor(ColorGrayMedium));

    private static IContainer DataCell(IContainer container, bool isAlt)
        => container.PaddingVertical(5).PaddingHorizontal(4)
            .BorderBottom(1).BorderColor(ColorBorderLight)
            .Background(isAlt ? "#f8fafc" : "#ffffff");

    private static string FormatValue(double? value) => value.HasValue ? $"{value.Value:F0}" : "–";

    private static string GetTirColor(double? tir) => tir switch
    {
        >= 70 => ColorGreen,
        >= 50 => ColorYellow,
        _ => ColorRed
    };

    private static string GetSpikeColor(double? spike) => spike switch
    {
        null => ColorGrayLight,
        <= 30 => ColorGreen,
        <= 60 => ColorYellow,
        _ => ColorRed
    };

    private static string GetClassificationIcon(string? classification) => classification switch
    {
        "green" => "●",
        "yellow" => "●",
        "red" => "●",
        _ => ""
    };

    private static string GetClassificationLabel(string? classification) => classification switch
    {
        "green" => "Good Day",
        "yellow" => "Concerning",
        "red" => "Difficult Day",
        _ => ""
    };

    private static string GetClassificationColor(string? classification) => classification switch
    {
        "green" => ColorGreen,
        "yellow" => ColorYellow,
        "red" => ColorRed,
        _ => ColorGrayMedium
    };

    private static string TruncateText(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "–";
        return text.Length > maxLen ? text[..(maxLen - 1)] + "…" : text;
    }

    /// <summary>
    /// Creates a concise summary from the note content for the PDF report.
    /// Falls back to the title if content is empty.
    /// Collapses whitespace and truncates to maxLen.
    /// </summary>
    private static string SummarizeContent(string? content, string? title, int maxLen)
    {
        var text = content;
        if (string.IsNullOrWhiteSpace(text))
            text = title;
        if (string.IsNullOrWhiteSpace(text))
            return "–";

        // Collapse newlines and multiple spaces into single spaces
        text = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");

        return text.Length > maxLen ? text[..(maxLen - 1)] + "…" : text;
    }

    private static string StripMarkdown(string text)
    {
        // Remove common markdown formatting
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "$1"); // bold
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*(.+?)\*", "$1");     // italic
        text = System.Text.RegularExpressions.Regex.Replace(text, @"#{1,6}\s*", "");        // headers
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\s*[-*]\s+", "• ", System.Text.RegularExpressions.RegexOptions.Multiline); // list items
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[CLASSIFICATION:\s*\w+\]\s*", ""); // classification tags
        return text.Trim();
    }
}
