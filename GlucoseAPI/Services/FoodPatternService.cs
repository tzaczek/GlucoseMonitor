using System.Text.Json;
using System.Text.RegularExpressions;
using GlucoseAPI.Application.Interfaces;
using GlucoseAPI.Data;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;
using static GlucoseAPI.Application.Interfaces.EventCategory;

namespace GlucoseAPI.Services;

public class FoodPatternService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventLogger _eventLogger;
    private readonly INotificationService _notifications;
    private readonly ILogger<FoodPatternService> _logger;
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _lock = new();
    private bool _fullScanRequested;

    public FoodPatternService(
        IServiceProvider serviceProvider,
        IEventLogger eventLogger,
        INotificationService notifications,
        ILogger<FoodPatternService> logger)
    {
        _serviceProvider = serviceProvider;
        _eventLogger = eventLogger;
        _notifications = notifications;
        _logger = logger;
    }

    public void RequestFullScan()
    {
        lock (_lock) _fullScanRequested = true;
        _signal.Release();
    }

    public void EnqueueEventProcessing()
    {
        _signal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(90), stoppingToken);
        _logger.LogInformation("FoodPatternService started.");

        // Initial scan of unprocessed events
        _signal.Release();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(stoppingToken);

                bool fullScan;
                lock (_lock) { fullScan = _fullScanRequested; _fullScanRequested = false; }

                while (_signal.CurrentCount > 0)
                    await _signal.WaitAsync(stoppingToken);

                await ProcessAsync(fullScan, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in FoodPatternService.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(bool fullScan, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var gptClient = scope.ServiceProvider.GetRequiredService<IGptClient>();

        var settings = await settingsService.GetAnalysisSettingsAsync();
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(settings.GptApiKey))
        {
            _logger.LogDebug("GPT not configured, skipping food extraction.");
            return;
        }

        if (fullScan)
        {
            await FullScanAsync(db, gptClient, settings.GptApiKey, settings.GptModelName, ct);
        }
        else
        {
            await ProcessNewEventsAsync(db, gptClient, settings.GptApiKey, settings.GptModelName, ct);
        }
    }

    private async Task ProcessNewEventsAsync(GlucoseDbContext db, IGptClient gptClient,
        string apiKey, string modelName, CancellationToken ct)
    {
        var linkedEventIds = await db.FoodEventLinks
            .Select(l => l.GlucoseEventId)
            .Distinct()
            .ToListAsync(ct);

        var unprocessedEvents = await db.GlucoseEvents
            .Where(e => e.IsProcessed && !linkedEventIds.Contains(e.Id))
            .OrderBy(e => e.EventTimestamp)
            .Take(50)
            .ToListAsync(ct);

        if (unprocessedEvents.Count == 0) return;

        _logger.LogInformation("Processing {Count} events for food extraction.", unprocessedEvents.Count);

        int extracted = 0;
        foreach (var evt in unprocessedEvents)
        {
            var foods = await ExtractFoodsAsync(gptClient, apiKey, modelName, evt, ct);
            if (foods.Count > 0)
            {
                await LinkFoodsToEventAsync(db, evt, foods, ct);
                extracted += foods.Count;
            }
        }

        if (extracted > 0)
        {
            await RecalculateAggregatesAsync(db, ct);
            await _notifications.NotifyFoodPatternsUpdatedAsync(extracted, ct);
            await _eventLogger.LogInfoAsync(Analysis,
                $"Extracted {extracted} food item(s) from {unprocessedEvents.Count} event(s).",
                source: nameof(FoodPatternService));
        }
    }

    private async Task FullScanAsync(GlucoseDbContext db, IGptClient gptClient,
        string apiKey, string modelName, CancellationToken ct)
    {
        _logger.LogInformation("Starting full food pattern scan...");

        var allEvents = await db.GlucoseEvents
            .Where(e => e.IsProcessed)
            .OrderBy(e => e.EventTimestamp)
            .ToListAsync(ct);

        int extracted = 0;
        foreach (var evt in allEvents)
        {
            var existingLinks = await db.FoodEventLinks
                .AnyAsync(l => l.GlucoseEventId == evt.Id, ct);

            if (existingLinks) continue;

            var foods = await ExtractFoodsAsync(gptClient, apiKey, modelName, evt, ct);
            if (foods.Count > 0)
            {
                await LinkFoodsToEventAsync(db, evt, foods, ct);
                extracted += foods.Count;
            }
        }

        await RecalculateAggregatesAsync(db, ct);

        _logger.LogInformation("Full food scan complete. Extracted {Count} food links from {EventCount} events.",
            extracted, allEvents.Count);

        await _notifications.NotifyFoodPatternsUpdatedAsync(extracted, ct);
        await _eventLogger.LogInfoAsync(Analysis,
            $"Full food pattern scan complete. Found {extracted} food item(s) across {allEvents.Count} event(s).",
            source: nameof(FoodPatternService));
    }

    public class ExtractedFood
    {
        public string Name { get; set; } = string.Empty;
        public string? NameEn { get; set; }
    }

    private async Task<List<ExtractedFood>> ExtractFoodsAsync(IGptClient gptClient,
        string apiKey, string modelName, GlucoseEvent evt, CancellationToken ct)
    {
        var noteText = $"{evt.NoteTitle}\n{evt.NoteContent ?? ""}".Trim();
        if (string.IsNullOrWhiteSpace(noteText)) return new();

        var systemPrompt = @"You extract food and drink names from meal/activity notes.
The notes may be in Polish or English.
Return a JSON array of objects, each with ""name"" (original language, lowercase) and ""nameEn"" (English translation, lowercase).
Use simple, normalized names (lowercase, singular form, common name).
Examples: [{""name"":""kawa z mlekiem"",""nameEn"":""coffee with milk""},{""name"":""pierogi"",""nameEn"":""dumplings""}]
If the note describes an activity (exercise, walk) rather than food, return an empty array.
If you cannot identify specific foods, return an empty array.
Return ONLY the JSON array, nothing else.";

        var userPrompt = $"Extract food names from this note:\n\n{noteText}";

        try
        {
            var result = await gptClient.AnalyzeAsync(apiKey, systemPrompt, userPrompt,
                "gpt-4o-mini", 512, ct);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
                return new();

            var content = result.Content.Trim();
            content = Regex.Replace(content, @"^```(?:json)?\s*", "");
            content = Regex.Replace(content, @"\s*```$", "");
            content = content.Trim();

            var foods = JsonSerializer.Deserialize<List<ExtractedFood>>(content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return foods?
                .Where(f => !string.IsNullOrWhiteSpace(f.Name))
                .Select(f => new ExtractedFood
                {
                    Name = f.Name.Trim(),
                    NameEn = string.IsNullOrWhiteSpace(f.NameEn) ? null : f.NameEn.Trim()
                })
                .DistinctBy(f => f.Name.ToLowerInvariant())
                .ToList() ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract foods from event #{Id}.", evt.Id);
            return new();
        }
    }

    private static async Task LinkFoodsToEventAsync(GlucoseDbContext db,
        GlucoseEvent evt, List<ExtractedFood> foods, CancellationToken ct)
    {
        foreach (var food in foods)
        {
            var normalized = food.Name.ToLowerInvariant().Trim();

            var foodItem = await db.FoodItems
                .FirstOrDefaultAsync(f => f.NormalizedName == normalized, ct);

            if (foodItem == null)
            {
                foodItem = new FoodItem
                {
                    Name = food.Name,
                    NameEn = food.NameEn,
                    NormalizedName = normalized,
                    OccurrenceCount = 0,
                    FirstSeen = evt.EventTimestamp,
                    LastSeen = evt.EventTimestamp
                };
                db.FoodItems.Add(foodItem);
                await db.SaveChangesAsync(ct);
            }
            else if (foodItem.NameEn == null && food.NameEn != null)
            {
                foodItem.NameEn = food.NameEn;
            }

            var existingLink = await db.FoodEventLinks
                .AnyAsync(l => l.FoodItemId == foodItem.Id && l.GlucoseEventId == evt.Id, ct);

            if (!existingLink)
            {
                double? recoveryMinutes = null;
                if (evt.GlucoseSpike.HasValue && evt.GlucoseSpike > 0 && evt.PeakTime.HasValue)
                    recoveryMinutes = (evt.PeriodEnd - evt.PeakTime.Value).TotalMinutes;

                db.FoodEventLinks.Add(new FoodEventLink
                {
                    FoodItemId = foodItem.Id,
                    GlucoseEventId = evt.Id,
                    Spike = evt.GlucoseSpike,
                    GlucoseAtEvent = evt.GlucoseAtEvent,
                    AiClassification = evt.AiClassification,
                    RecoveryMinutes = recoveryMinutes
                });
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private static async Task RecalculateAggregatesAsync(GlucoseDbContext db, CancellationToken ct)
    {
        var foodItems = await db.FoodItems.ToListAsync(ct);

        foreach (var food in foodItems)
        {
            var links = await db.FoodEventLinks
                .Where(l => l.FoodItemId == food.Id)
                .Include(l => l.GlucoseEvent)
                .ToListAsync(ct);

            if (links.Count == 0) continue;

            food.OccurrenceCount = links.Count;

            var spikes = links.Where(l => l.Spike.HasValue).Select(l => l.Spike!.Value).ToList();
            food.AvgSpike = spikes.Count > 0 ? spikes.Average() : null;
            food.WorstSpike = spikes.Count > 0 ? spikes.Max() : null;
            food.BestSpike = spikes.Count > 0 ? spikes.Min() : null;

            var glucoseAtEvents = links.Where(l => l.GlucoseAtEvent.HasValue).Select(l => l.GlucoseAtEvent!.Value).ToList();
            food.AvgGlucoseAtEvent = glucoseAtEvents.Count > 0 ? glucoseAtEvents.Average() : null;

            var maxVals = links.Where(l => l.GlucoseEvent?.GlucoseMax != null).Select(l => l.GlucoseEvent!.GlucoseMax!.Value).ToList();
            food.AvgGlucoseMax = maxVals.Count > 0 ? maxVals.Average() : null;

            var minVals = links.Where(l => l.GlucoseEvent?.GlucoseMin != null).Select(l => l.GlucoseEvent!.GlucoseMin!.Value).ToList();
            food.AvgGlucoseMin = minVals.Count > 0 ? minVals.Average() : null;

            var recoveries = links.Where(l => l.RecoveryMinutes.HasValue).Select(l => l.RecoveryMinutes!.Value).ToList();
            food.AvgRecoveryMinutes = recoveries.Count > 0 ? recoveries.Average() : null;

            food.GreenCount = links.Count(l => l.AiClassification == "green");
            food.YellowCount = links.Count(l => l.AiClassification == "yellow");
            food.RedCount = links.Count(l => l.AiClassification == "red");

            var timestamps = links.Where(l => l.GlucoseEvent != null).Select(l => l.GlucoseEvent!.EventTimestamp).ToList();
            if (timestamps.Count > 0)
            {
                food.FirstSeen = timestamps.Min();
                food.LastSeen = timestamps.Max();
            }

            food.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
