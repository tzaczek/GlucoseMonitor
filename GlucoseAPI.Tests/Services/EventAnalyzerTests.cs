using FluentAssertions;
using GlucoseAPI.Application.Interfaces;
using GlucoseAPI.Data;
using GlucoseAPI.Domain.Services;
using GlucoseAPI.Models;
using GlucoseAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GlucoseAPI.Tests.Services;

/// <summary>
/// Unit tests for <see cref="EventAnalyzer"/> using mocked dependencies.
/// The InMemory EF provider is used for the database so no SQL Server is needed.
/// </summary>
public class EventAnalyzerTests : IDisposable
{
    private readonly GlucoseDbContext _db;
    private readonly Mock<IGptClient> _gptClientMock;
    private readonly Mock<INotificationService> _notificationsMock;
    private readonly Mock<SettingsService> _settingsMock;
    private readonly EventAnalyzer _analyzer;

    public EventAnalyzerTests()
    {
        // In-memory database per test run
        var options = new DbContextOptionsBuilder<GlucoseDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new GlucoseDbContext(options);

        _gptClientMock = new Mock<IGptClient>();
        _notificationsMock = new Mock<INotificationService>();

        // SettingsService requires DbContext + IConfiguration, but we mock its return value
        var configMock = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        _settingsMock = new Mock<SettingsService>(_db, configMock.Object);

        var tzConverter = new TimeZoneConverter(NullLogger<TimeZoneConverter>.Instance);
        var eventLoggerMock = new Mock<IEventLogger>();

        _analyzer = new EventAnalyzer(
            _db,
            _settingsMock.Object,
            _gptClientMock.Object,
            _notificationsMock.Object,
            tzConverter,
            eventLoggerMock.Object,
            NullLogger<EventAnalyzer>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task AnalyzeEventAsync_WhenApiKeyNotConfigured_ReturnsNull()
    {
        // Arrange
        _settingsMock.Setup(s => s.GetAnalysisSettingsAsync())
            .ReturnsAsync(new AnalysisSettingsDto
            {
                IsConfigured = false,
                GptApiKey = null
            });

        var evt = CreateTestEvent();

        // Act
        var result = await _analyzer.AnalyzeEventAsync(evt, "test");

        // Assert
        result.Should().BeNull();
        _gptClientMock.Verify(g => g.AnalyzeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AnalyzeEventAsync_WhenGptReturnsSuccess_SavesAnalysisAndHistory()
    {
        // Arrange
        SetupConfiguredSettings();

        var evt = CreateTestEvent();
        _db.GlucoseEvents.Add(evt);
        SeedGlucoseReadings(evt);
        await _db.SaveChangesAsync();

        _gptClientMock.Setup(g => g.AnalyzeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GptAnalysisResult(
                Content: "[CLASSIFICATION: green]\nGreat glucose response!",
                Model: "gpt-5-mini",
                InputTokens: 100,
                OutputTokens: 50,
                TotalTokens: 150,
                FinishReason: "stop",
                HttpStatusCode: 200,
                Success: true,
                DurationMs: 1500));

        // Act
        var result = await _analyzer.AnalyzeEventAsync(evt, "test analysis");

        // Assert
        result.Should().NotBeNull();
        result.Should().Be("Great glucose response!");

        evt.AiAnalysis.Should().Be("Great glucose response!");
        evt.AiClassification.Should().Be("green");
        evt.IsProcessed.Should().BeTrue();

        var history = await _db.EventAnalysisHistory.ToListAsync();
        history.Should().HaveCount(1);
        history[0].AiClassification.Should().Be("green");
        history[0].AiAnalysis.Should().Be("Great glucose response!");

        var usageLogs = await _db.AiUsageLogs.ToListAsync();
        usageLogs.Should().HaveCount(1);
        usageLogs[0].InputTokens.Should().Be(100);
        usageLogs[0].Success.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeEventAsync_WhenGptReturnsEmpty_LogsUsageAndReturnsNull()
    {
        // Arrange
        SetupConfiguredSettings();

        var evt = CreateTestEvent();
        _db.GlucoseEvents.Add(evt);
        await _db.SaveChangesAsync();

        _gptClientMock.Setup(g => g.AnalyzeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GptAnalysisResult(
                Content: "",
                Model: "gpt-5-mini",
                InputTokens: 100,
                OutputTokens: 0,
                TotalTokens: 100,
                FinishReason: "stop",
                HttpStatusCode: 200,
                Success: true,
                DurationMs: 500));

        // Act
        var result = await _analyzer.AnalyzeEventAsync(evt, "test");

        // Assert
        result.Should().BeNull();

        // AI usage should still be logged even on empty response
        var usageLogs = await _db.AiUsageLogs.ToListAsync();
        usageLogs.Should().HaveCount(1);

        // UI should be notified about AI usage update
        _notificationsMock.Verify(n => n.NotifyAiUsageUpdatedAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnalyzeEventAsync_CalculatesGlucoseStats_UsingDomainService()
    {
        // Arrange
        SetupConfiguredSettings();

        var eventTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var evt = new GlucoseEvent
        {
            NoteTitle = "Lunch",
            NoteContent = "Ate a sandwich",
            NoteUuid = "test-uuid-stats",
            EventTimestamp = eventTime,
            PeriodStart = eventTime.AddHours(-2),
            PeriodEnd = eventTime.AddHours(3),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.GlucoseEvents.Add(evt);

        // Seed specific readings to verify stats
        _db.GlucoseReadings.AddRange(
            CreateReading(100, eventTime.AddMinutes(-30)),
            CreateReading(110, eventTime),
            CreateReading(150, eventTime.AddMinutes(45)),
            CreateReading(130, eventTime.AddMinutes(90))
        );
        await _db.SaveChangesAsync();

        _gptClientMock.Setup(g => g.AnalyzeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GptAnalysisResult(
                Content: "[CLASSIFICATION: yellow]\nModerate spike.",
                Model: "gpt-5-mini", InputTokens: 80, OutputTokens: 40, TotalTokens: 120,
                FinishReason: "stop", HttpStatusCode: 200, Success: true, DurationMs: 1000));

        // Act
        await _analyzer.AnalyzeEventAsync(evt, "test");

        // Assert — verify domain service was used to compute stats
        evt.GlucoseAtEvent.Should().Be(110);  // closest to event time
        evt.GlucoseMin.Should().Be(100);
        evt.GlucoseMax.Should().Be(150);
        evt.ReadingCount.Should().Be(4);
        evt.GlucoseSpike.Should().Be(40);     // 150 (peak after) - 110 (at event)
    }

    [Fact]
    public async Task AnalyzeEventAsync_NotifiesClients_OnSuccess()
    {
        // Arrange
        SetupConfiguredSettings();

        var evt = CreateTestEvent();
        _db.GlucoseEvents.Add(evt);
        SeedGlucoseReadings(evt);
        await _db.SaveChangesAsync();

        _gptClientMock.Setup(g => g.AnalyzeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GptAnalysisResult(
                Content: "[CLASSIFICATION: green]\nGood response.",
                Model: "gpt-5-mini", InputTokens: 80, OutputTokens: 40, TotalTokens: 120,
                FinishReason: "stop", HttpStatusCode: 200, Success: true, DurationMs: 1000));

        // Act
        await _analyzer.AnalyzeEventAsync(evt, "test");

        // Assert
        _notificationsMock.Verify(n => n.NotifyEventsUpdatedAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _notificationsMock.Verify(n => n.NotifyAiUsageUpdatedAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Helpers ──────────────────────────────────────────────

    private void SetupConfiguredSettings()
    {
        _settingsMock.Setup(s => s.GetAnalysisSettingsAsync())
            .ReturnsAsync(new AnalysisSettingsDto
            {
                IsConfigured = true,
                GptApiKey = "test-api-key",
                TimeZone = "UTC",
                NotesFolderName = "Cukier",
                AnalysisIntervalMinutes = 15,
                ReanalysisMinIntervalMinutes = 30
            });
    }

    private static GlucoseEvent CreateTestEvent()
    {
        var eventTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        return new GlucoseEvent
        {
            NoteTitle = "Test Event",
            NoteContent = "Test content",
            NoteUuid = $"test-uuid-{Guid.NewGuid():N}",
            EventTimestamp = eventTime,
            PeriodStart = eventTime.AddHours(-2),
            PeriodEnd = eventTime.AddHours(3),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private void SeedGlucoseReadings(GlucoseEvent evt)
    {
        for (int i = -6; i <= 12; i++)
        {
            _db.GlucoseReadings.Add(CreateReading(
                100 + (i > 0 ? i * 3 : 0),
                evt.EventTimestamp.AddMinutes(i * 15)));
        }
    }

    private static GlucoseReading CreateReading(double value, DateTime timestamp) => new()
    {
        Value = value,
        Timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc),
        CreatedAt = DateTime.UtcNow
    };
}
