using GlucoseAPI.Data;
using GlucoseAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace GlucoseAPI.Services;

/// <summary>
/// Service for reading/writing app settings from the database.
/// Falls back to IConfiguration (env vars / appsettings.json) when no DB value exists.
/// </summary>
public class SettingsService
{
    private readonly GlucoseDbContext _db;
    private readonly IConfiguration _config;

    public SettingsService(GlucoseDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<string> GetAsync(string key, string defaultValue = "")
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting != null && !string.IsNullOrEmpty(setting.Value))
            return setting.Value;

        // Fallback to config (env vars / appsettings.json)
        // Keys like "LibreLink:Email" map to config["LibreLink:Email"]
        var configValue = _config[key];
        return !string.IsNullOrEmpty(configValue) ? configValue : defaultValue;
    }

    public async Task SetAsync(string key, string value)
    {
        var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting != null)
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTime.UtcNow
            });
        }
        await _db.SaveChangesAsync();
    }

    public async Task<LibreSettingsDto> GetLibreSettingsAsync()
    {
        var email = await GetAsync(SettingKeys.LibreEmail);
        var password = await GetAsync(SettingKeys.LibrePassword);

        return new LibreSettingsDto
        {
            Email = email,
            Password = password,
            PatientId = await GetAsync(SettingKeys.LibrePatientId),
            Region = await GetAsync(SettingKeys.LibreRegion, "eu"),
            Version = await GetAsync(SettingKeys.LibreVersion, "4.12.0"),
            FetchIntervalMinutes = int.TryParse(await GetAsync(SettingKeys.FetchIntervalMinutes, "5"), out var mins) ? mins : 5,
            IsConfigured = !string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(password)
        };
    }

    public async Task SaveLibreSettingsAsync(LibreSettingsDto dto)
    {
        await SetAsync(SettingKeys.LibreEmail, dto.Email);
        await SetAsync(SettingKeys.LibrePassword, dto.Password);
        await SetAsync(SettingKeys.LibrePatientId, dto.PatientId);
        await SetAsync(SettingKeys.LibreRegion, dto.Region);
        await SetAsync(SettingKeys.LibreVersion, dto.Version);
        await SetAsync(SettingKeys.FetchIntervalMinutes, dto.FetchIntervalMinutes.ToString());
    }

    public virtual async Task<AnalysisSettingsDto> GetAnalysisSettingsAsync()
    {
        var apiKey = await GetAsync(SettingKeys.GptApiKey);

        return new AnalysisSettingsDto
        {
            GptApiKey = apiKey,
            NotesFolderName = await GetAsync(SettingKeys.AnalysisFolderName, "Cukier"),
            AnalysisIntervalMinutes = int.TryParse(
                await GetAsync(SettingKeys.AnalysisIntervalMinutes, "15"), out var mins) ? mins : 15,
            ReanalysisMinIntervalMinutes = int.TryParse(
                await GetAsync(SettingKeys.ReanalysisMinIntervalMinutes, "30"), out var reanalysisMins) ? reanalysisMins : 30,
            TimeZone = await GetAsync(SettingKeys.DisplayTimeZone, "Europe/Warsaw"),
            GptModelName = await GetAsync(SettingKeys.GptModelName, "gpt-4o-mini"),
            IsConfigured = !string.IsNullOrEmpty(apiKey)
        };
    }

    public async Task SaveAnalysisSettingsAsync(AnalysisSettingsDto dto)
    {
        await SetAsync(SettingKeys.GptApiKey, dto.GptApiKey);
        await SetAsync(SettingKeys.AnalysisFolderName, dto.NotesFolderName);
        await SetAsync(SettingKeys.AnalysisIntervalMinutes, dto.AnalysisIntervalMinutes.ToString());
        await SetAsync(SettingKeys.ReanalysisMinIntervalMinutes, dto.ReanalysisMinIntervalMinutes.ToString());
        await SetAsync(SettingKeys.DisplayTimeZone, dto.TimeZone);
        await SetAsync(SettingKeys.GptModelName, dto.GptModelName);
    }
}
