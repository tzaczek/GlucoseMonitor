using GlucoseAPI.Models;
using GlucoseAPI.Services;
using MediatR;

namespace GlucoseAPI.Application.Features.Settings;

// ── SaveLibreSettings ─────────────────────────────────────────

public record SaveLibreSettingsCommand(LibreSettingsDto Dto)
    : IRequest<SaveSettingsResult>;

public record SaveSettingsResult(bool Success, string Message);

public class SaveLibreSettingsHandler : IRequestHandler<SaveLibreSettingsCommand, SaveSettingsResult>
{
    private readonly SettingsService _settingsService;
    private readonly ILogger<SaveLibreSettingsHandler> _logger;

    public SaveLibreSettingsHandler(SettingsService settingsService, ILogger<SaveLibreSettingsHandler> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<SaveSettingsResult> Handle(SaveLibreSettingsCommand request, CancellationToken ct)
    {
        var dto = request.Dto;

        if (string.IsNullOrWhiteSpace(dto.Email))
            return new SaveSettingsResult(false, "Email is required.");

        // If password is the mask placeholder, keep the existing password
        if (dto.Password == "••••••••")
        {
            var existing = await _settingsService.GetLibreSettingsAsync();
            dto.Password = existing.Password;
        }

        if (string.IsNullOrWhiteSpace(dto.Password))
            return new SaveSettingsResult(false, "Password is required.");

        if (dto.FetchIntervalMinutes < 1) dto.FetchIntervalMinutes = 1;
        if (dto.FetchIntervalMinutes > 60) dto.FetchIntervalMinutes = 60;

        await _settingsService.SaveLibreSettingsAsync(dto);
        _logger.LogInformation("LibreLink settings saved. Email: {Email}, Region: {Region}", dto.Email, dto.Region);

        return new SaveSettingsResult(true, "Settings saved successfully.");
    }
}

// ── SaveAnalysisSettings ──────────────────────────────────────

public record SaveAnalysisSettingsCommand(AnalysisSettingsDto Dto)
    : IRequest<SaveSettingsResult>;

public class SaveAnalysisSettingsHandler : IRequestHandler<SaveAnalysisSettingsCommand, SaveSettingsResult>
{
    private readonly SettingsService _settingsService;
    private readonly ILogger<SaveAnalysisSettingsHandler> _logger;

    public SaveAnalysisSettingsHandler(SettingsService settingsService, ILogger<SaveAnalysisSettingsHandler> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<SaveSettingsResult> Handle(SaveAnalysisSettingsCommand request, CancellationToken ct)
    {
        var dto = request.Dto;

        if (string.IsNullOrWhiteSpace(dto.NotesFolderName))
            dto.NotesFolderName = "Cukier";

        // If API key is the mask placeholder or empty, keep the existing key
        if (string.IsNullOrEmpty(dto.GptApiKey) || dto.GptApiKey.Contains("••••••••"))
        {
            var existing = await _settingsService.GetAnalysisSettingsAsync();
            dto.GptApiKey = existing.GptApiKey;
        }

        if (dto.AnalysisIntervalMinutes < 1) dto.AnalysisIntervalMinutes = 1;
        if (dto.AnalysisIntervalMinutes > 120) dto.AnalysisIntervalMinutes = 120;

        await _settingsService.SaveAnalysisSettingsAsync(dto);
        _logger.LogInformation("Analysis settings saved. Folder: {Folder}, Model: {Model}", dto.NotesFolderName, dto.GptModelName);

        return new SaveSettingsResult(true, "Analysis settings saved successfully.");
    }
}

// ── TestLibreLinkConnection ───────────────────────────────────

public record TestLibreLinkConnectionCommand(LibreSettingsDto Dto)
    : IRequest<ConnectionTestResult>;

public class TestLibreLinkConnectionHandler
    : IRequestHandler<TestLibreLinkConnectionCommand, ConnectionTestResult>
{
    private readonly SettingsService _settingsService;
    private readonly LibreLinkClient _libreLinkClient;
    private readonly ILogger<TestLibreLinkConnectionHandler> _logger;

    public TestLibreLinkConnectionHandler(
        SettingsService settingsService,
        LibreLinkClient libreLinkClient,
        ILogger<TestLibreLinkConnectionHandler> logger)
    {
        _settingsService = settingsService;
        _libreLinkClient = libreLinkClient;
        _logger = logger;
    }

    public async Task<ConnectionTestResult> Handle(
        TestLibreLinkConnectionCommand request, CancellationToken ct)
    {
        var dto = request.Dto;

        // If password is masked, use the stored one
        if (dto.Password == "••••••••")
        {
            var existing = await _settingsService.GetLibreSettingsAsync();
            dto.Password = existing.Password;
        }

        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
        {
            return new ConnectionTestResult
            {
                Success = false,
                Message = "Email and password are required."
            };
        }

        try
        {
            _libreLinkClient.Configure(dto.Email, dto.Password, dto.PatientId, dto.Region, dto.Version);
            await _libreLinkClient.LoginAsync();

            var connections = await _libreLinkClient.GetConnectionsAsync();
            var patients = connections.Select(c => new PatientInfo
            {
                PatientId = c.PatientId,
                FirstName = c.FirstName,
                LastName = c.LastName
            }).ToList();

            return new ConnectionTestResult
            {
                Success = true,
                Message = $"Connected successfully! Found {patients.Count} patient(s).",
                Patients = patients
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection test failed.");
            return new ConnectionTestResult
            {
                Success = false,
                Message = $"Connection failed: {ex.Message}"
            };
        }
    }
}
