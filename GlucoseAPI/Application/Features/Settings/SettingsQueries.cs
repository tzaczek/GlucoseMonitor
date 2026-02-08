using GlucoseAPI.Models;
using GlucoseAPI.Services;
using MediatR;

namespace GlucoseAPI.Application.Features.Settings;

// ── GetLibreSettings ──────────────────────────────────────────

public record GetLibreSettingsQuery : IRequest<LibreSettingsDto>;

public class GetLibreSettingsHandler : IRequestHandler<GetLibreSettingsQuery, LibreSettingsDto>
{
    private readonly SettingsService _settingsService;

    public GetLibreSettingsHandler(SettingsService settingsService) => _settingsService = settingsService;

    public async Task<LibreSettingsDto> Handle(GetLibreSettingsQuery request, CancellationToken ct)
    {
        var settings = await _settingsService.GetLibreSettingsAsync();

        // Mask password for security
        if (!string.IsNullOrEmpty(settings.Password))
            settings.Password = "••••••••";

        return settings;
    }
}

// ── GetAnalysisSettings ───────────────────────────────────────

public record GetAnalysisSettingsQuery : IRequest<AnalysisSettingsDto>;

public class GetAnalysisSettingsHandler : IRequestHandler<GetAnalysisSettingsQuery, AnalysisSettingsDto>
{
    private readonly SettingsService _settingsService;

    public GetAnalysisSettingsHandler(SettingsService settingsService) => _settingsService = settingsService;

    public async Task<AnalysisSettingsDto> Handle(GetAnalysisSettingsQuery request, CancellationToken ct)
    {
        var settings = await _settingsService.GetAnalysisSettingsAsync();

        // Mask API key for security
        if (!string.IsNullOrEmpty(settings.GptApiKey))
            settings.GptApiKey = settings.GptApiKey.Length > 8
                ? settings.GptApiKey[..4] + "••••••••" + settings.GptApiKey[^4..]
                : "••••••••";

        return settings;
    }
}
