using System.Text.Json;
using System.Text.RegularExpressions;
using GlucoseAPI.Application.Interfaces;
using GlucoseAPI.Data;
using Microsoft.EntityFrameworkCore;
using static GlucoseAPI.Application.Interfaces.EventCategory;

namespace GlucoseAPI.Services;

public class TranslationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventLogger _eventLogger;
    private readonly ILogger<TranslationService> _logger;
    private readonly SemaphoreSlim _signal = new(0);

    public TranslationService(
        IServiceProvider serviceProvider,
        IEventLogger eventLogger,
        ILogger<TranslationService> logger)
    {
        _serviceProvider = serviceProvider;
        _eventLogger = eventLogger;
        _logger = logger;
    }

    public void RequestBackfill()
    {
        _signal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        _logger.LogInformation("TranslationService started.");

        // Auto-backfill on startup
        _signal.Release();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(stoppingToken);

                while (_signal.CurrentCount > 0)
                    await _signal.WaitAsync(stoppingToken);

                await BackfillAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TranslationService.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

    private async Task BackfillAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var gptClient = scope.ServiceProvider.GetRequiredService<IGptClient>();

        var settings = await settingsService.GetAnalysisSettingsAsync();
        if (!settings.IsConfigured || string.IsNullOrWhiteSpace(settings.GptApiKey))
        {
            _logger.LogDebug("GPT not configured, skipping translation backfill.");
            return;
        }

        // Translate events missing English
        var untranslatedEvents = await db.GlucoseEvents
            .Where(e => e.NoteTitleEn == null)
            .OrderBy(e => e.EventTimestamp)
            .Take(50)
            .ToListAsync(ct);

        if (untranslatedEvents.Count > 0)
        {
            _logger.LogInformation("Translating {Count} events to English...", untranslatedEvents.Count);

            int translated = 0;
            foreach (var evt in untranslatedEvents)
            {
                if (ct.IsCancellationRequested) break;

                var (titleEn, contentEn) = await TranslateTextAsync(
                    gptClient, settings.GptApiKey, evt.NoteTitle, evt.NoteContent, ct);

                if (titleEn != null)
                {
                    evt.NoteTitleEn = titleEn;
                    evt.NoteContentEn = contentEn;
                    evt.UpdatedAt = DateTime.UtcNow;
                    translated++;
                }

                await Task.Delay(500, ct);
            }

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Translated {Count}/{Total} events.", translated, untranslatedEvents.Count);

            if (translated > 0)
                await _eventLogger.LogInfoAsync(Analysis,
                    $"Translated {translated} event(s) to English.",
                    source: nameof(TranslationService));

            // If there are more to translate, schedule another run
            var remaining = await db.GlucoseEvents.CountAsync(e => e.NoteTitleEn == null, ct);
            if (remaining > 0)
            {
                _logger.LogInformation("{Remaining} events still need translation. Scheduling continuation.", remaining);
                _signal.Release();
            }
        }

        // Translate food items missing English
        var untranslatedFoods = await db.FoodItems
            .Where(f => f.NameEn == null)
            .OrderBy(f => f.Name)
            .Take(100)
            .ToListAsync(ct);

        if (untranslatedFoods.Count > 0)
        {
            _logger.LogInformation("Translating {Count} food names to English...", untranslatedFoods.Count);

            int translated = 0;
            foreach (var food in untranslatedFoods)
            {
                if (ct.IsCancellationRequested) break;

                var nameEn = await TranslateFoodNameAsync(
                    gptClient, settings.GptApiKey, food.Name, ct);

                if (nameEn != null)
                {
                    food.NameEn = nameEn;
                    food.UpdatedAt = DateTime.UtcNow;
                    translated++;
                }

                await Task.Delay(300, ct);
            }

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Translated {Count}/{Total} food names.", translated, untranslatedFoods.Count);

            if (translated > 0)
                await _eventLogger.LogInfoAsync(Food,
                    $"Translated {translated} food name(s) to English.",
                    source: nameof(TranslationService));
        }
    }

    private async Task<(string? titleEn, string? contentEn)> TranslateTextAsync(
        IGptClient gptClient, string apiKey, string title, string? content, CancellationToken ct)
    {
        try
        {
            var textToTranslate = $"Title: {title}";
            if (!string.IsNullOrWhiteSpace(content))
                textToTranslate += $"\nContent: {content}";

            var systemPrompt = @"You translate Polish meal/food notes to English. Return JSON with ""titleEn"" and ""contentEn"".
Translate naturally — use common English food names. Keep it brief and accurate.
If the text is already in English, return it unchanged.
Return ONLY the JSON object, nothing else.";

            var result = await gptClient.AnalyzeAsync(apiKey, systemPrompt,
                textToTranslate, "gpt-4o-mini", 256, ct);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
                return (null, null);

            var json = result.Content.Trim();
            json = Regex.Replace(json, @"^```(?:json)?\s*", "");
            json = Regex.Replace(json, @"\s*```$", "");

            using var doc = JsonDocument.Parse(json.Trim());
            var root = doc.RootElement;
            var titleEn = root.TryGetProperty("titleEn", out var t) ? t.GetString() : null;
            var contentEn = root.TryGetProperty("contentEn", out var c) ? c.GetString() : null;

            return (titleEn, contentEn);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to translate '{Title}'.", title);
            return (null, null);
        }
    }

    private async Task<string?> TranslateFoodNameAsync(
        IGptClient gptClient, string apiKey, string name, CancellationToken ct)
    {
        try
        {
            var systemPrompt = @"Translate this Polish food/drink name to English. Return ONLY the English name, nothing else.
Use the most common English equivalent. Keep it lowercase and brief.
If it's already English, return it unchanged.";

            var result = await gptClient.AnalyzeAsync(apiKey, systemPrompt,
                name, "gpt-4o-mini", 64, ct);

            if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
                return null;

            var translated = result.Content.Trim().Trim('"');
            return string.IsNullOrWhiteSpace(translated) ? null : translated;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to translate food name '{Name}'.", name);
            return null;
        }
    }
}
