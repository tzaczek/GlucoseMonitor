using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GlucoseAPI.Data;
using GlucoseAPI.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GlucoseAPI.Tests.Integration;

/// <summary>
/// Integration tests that spin up the real ASP.NET Core pipeline (with in-memory DB)
/// and make HTTP requests against the API endpoints.
/// Verifies that controllers, services, and the database work together correctly.
/// 
/// NOTE: These tests require the .NET 8 ASP.NET Core runtime to be installed on the host.
/// They will run correctly in Docker containers with .NET 8 or in CI pipelines.
/// Use: dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public class ApiIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public ApiIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    // ────────────────────────────────────────────────────────────
    // Glucose Endpoints
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReadings_ReturnsOk_WithEmptyDatabase()
    {
        var response = await _client.GetAsync("/api/glucose/readings?hours=24");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetReadings_ReturnsData_WhenReadingsExist()
    {
        // Arrange: seed some data
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();
        db.GlucoseReadings.Add(new GlucoseReading
        {
            Value = 120,
            Timestamp = DateTime.UtcNow.AddMinutes(-30),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/glucose/readings?hours=24");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("120");
    }

    [Fact]
    public async Task GetStats_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/glucose/stats?hours=24");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ────────────────────────────────────────────────────────────
    // Events Endpoints
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetEvents_ReturnsOk_WithEmptyDatabase()
    {
        var response = await _client.GetAsync("/api/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetEvent_Returns404_ForNonExistentId()
    {
        var response = await _client.GetAsync("/api/events/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetEvents_ReturnsSeededEvent()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();
        db.GlucoseEvents.Add(new GlucoseEvent
        {
            NoteTitle = "Integration Test Event",
            NoteContent = "Test content for integration test",
            NoteUuid = $"integration-test-{Guid.NewGuid():N}",
            EventTimestamp = DateTime.UtcNow.AddHours(-1),
            PeriodStart = DateTime.UtcNow.AddHours(-3),
            PeriodEnd = DateTime.UtcNow,
            IsProcessed = true,
            AiAnalysis = "Test analysis",
            AiClassification = "green",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/events");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Integration Test Event");
        content.Should().Contain("green");
    }

    // ────────────────────────────────────────────────────────────
    // Daily Summaries Endpoints
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDailySummaries_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/dailysummaries");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetDailySummary_Returns404_ForNonExistentId()
    {
        var response = await _client.GetAsync("/api/dailysummaries/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ────────────────────────────────────────────────────────────
    // AI Usage Endpoints
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAiUsageSummary_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/aiusage/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAiUsageLogs_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/aiusage/logs?count=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ────────────────────────────────────────────────────────────
    // Settings Endpoints
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSettings_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/settings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SaveSettings_ReturnsOk()
    {
        var settings = new
        {
            libreLinkEmail = "test@example.com",
            libreLinkPassword = "test-password",
            fetchIntervalMinutes = 5,
            displayTimeZone = "UTC"
        };

        var response = await _client.PostAsJsonAsync("/api/settings", settings);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ────────────────────────────────────────────────────────────
    // Reports Endpoints
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetReport_ReturnsBadRequest_WhenDatesMissing()
    {
        var response = await _client.GetAsync("/api/reports/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetReport_ReturnsBadRequest_WhenRangeTooLarge()
    {
        var from = DateTime.UtcNow.AddDays(-100).ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var response = await _client.GetAsync($"/api/reports/pdf?from={from}&to={to}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ────────────────────────────────────────────────────────────
    // Notes Endpoints
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetNotes_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/notes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ────────────────────────────────────────────────────────────
    // End-to-End: Event with Readings Flow
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EventWithReadings_EndToEnd_ReturnsCompleteData()
    {
        // Arrange: seed event with readings
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GlucoseDbContext>();

        var eventTime = DateTime.UtcNow.AddHours(-2);
        var evt = new GlucoseEvent
        {
            NoteTitle = "E2E Test Lunch",
            NoteContent = "Pasta with sauce",
            NoteUuid = $"e2e-{Guid.NewGuid():N}",
            EventTimestamp = eventTime,
            PeriodStart = eventTime.AddHours(-1),
            PeriodEnd = eventTime.AddHours(3),
            ReadingCount = 5,
            GlucoseAtEvent = 105,
            GlucoseMin = 95,
            GlucoseMax = 165,
            GlucoseAvg = 130,
            GlucoseSpike = 60,
            AiAnalysis = "Significant glucose spike after pasta.",
            AiClassification = "red",
            IsProcessed = true,
            ProcessedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.GlucoseEvents.Add(evt);

        for (int i = 0; i < 5; i++)
        {
            db.GlucoseReadings.Add(new GlucoseReading
            {
                Value = 95 + i * 15,
                Timestamp = eventTime.AddMinutes(i * 15),
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();

        // Act: get events list
        var listResponse = await _client.GetAsync("/api/events");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listContent = await listResponse.Content.ReadAsStringAsync();
        listContent.Should().Contain("E2E Test Lunch");
        listContent.Should().Contain("red");

        // Act: get event detail
        var detailResponse = await _client.GetAsync($"/api/events/{evt.Id}");
        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailContent = await detailResponse.Content.ReadAsStringAsync();
        detailContent.Should().Contain("Significant glucose spike");
        detailContent.Should().Contain("red");
    }
}

/// <summary>
/// Custom WebApplicationFactory that replaces SQL Server with InMemory database
/// and disables background services for faster, isolated testing.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove the existing GlucoseDbContext registration (SQL Server)
            var descriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<GlucoseDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Remove all hosted service registrations (background services)
            // so they don't run during tests
            var hostedServices = services.Where(d =>
                d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)).ToList();
            foreach (var svc in hostedServices)
                services.Remove(svc);

            // Add InMemory database
            services.AddDbContext<GlucoseDbContext>(options =>
            {
                options.UseInMemoryDatabase("TestDb_" + Guid.NewGuid().ToString("N"));
            });
        });
    }
}
