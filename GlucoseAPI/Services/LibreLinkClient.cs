using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GlucoseAPI.Models;

namespace GlucoseAPI.Services;

/// <summary>
/// Unofficial LibreLink Up API client.
/// Based on the reverse-engineered API from https://github.com/timoschlueter/nightscout-librelink-up
/// 
/// Uses IHttpClientFactory to avoid the "new HttpClient()" anti-pattern.
/// The factory manages the underlying HttpMessageHandler pool for proper DNS rotation
/// and socket reuse.
/// </summary>
public class LibreLinkClient
{
    private readonly ILogger<LibreLinkClient> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>Name of the named HttpClient registered in DI.</summary>
    public const string HttpClientName = "LibreLink";

    private string _email = string.Empty;
    private string _password = string.Empty;
    private string? _patientId;
    private string _lluVersion = "4.16.0";

    private string? _token;
    private string? _userId;
    private string _baseUrl = "https://api-eu.libreview.io";

    private const string UserAgent =
        "Mozilla/5.0 (iPhone; CPU OS 17_4.1 like Mac OS X) AppleWebKit/536.26 " +
        "(KHTML, like Gecko) Version/17.4.1 Mobile/10A5355d Safari/8536.25";

    private static readonly Dictionary<string, string> RegionUrls = new()
    {
        ["ae"] = "https://api-ae.libreview.io",
        ["ap"] = "https://api-ap.libreview.io",
        ["au"] = "https://api-au.libreview.io",
        ["ca"] = "https://api-ca.libreview.io",
        ["de"] = "https://api-de.libreview.io",
        ["eu"] = "https://api-eu.libreview.io",
        ["eu2"] = "https://api-eu2.libreview.io",
        ["fr"] = "https://api-fr.libreview.io",
        ["jp"] = "https://api-jp.libreview.io",
        ["us"] = "https://api-us.libreview.io",
    };

    public bool IsConfigured => !string.IsNullOrEmpty(_email) && !string.IsNullOrEmpty(_password);

    public LibreLinkClient(ILogger<LibreLinkClient> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Configure the client with credentials (from DB settings or config).</summary>
    public void Configure(string email, string password, string? patientId = null,
                          string region = "eu", string version = "4.16.0")
    {
        _email = email;
        _password = password;
        _patientId = patientId;
        _lluVersion = version;
        _baseUrl = RegionUrls.GetValueOrDefault(region, "https://api-eu.libreview.io");
        _token = null; // force re-login
        _userId = null;
    }

    /// <summary>Create common headers used on every request.</summary>
    private Dictionary<string, string> GetBaseHeaders()
    {
        return new Dictionary<string, string>
        {
            ["User-Agent"] = UserAgent,
            ["Content-Type"] = "application/json;charset=UTF-8",
            ["product"] = "llu.ios",
            ["version"] = _lluVersion,
            ["Cache-Control"] = "no-cache",
            ["Pragma"] = "no-cache",
            ["Connection"] = "keep-alive",
        };
    }

    /// <summary>Create headers for authenticated requests (adds Authorization + account-id).</summary>
    private Dictionary<string, string> GetAuthHeaders()
    {
        var headers = GetBaseHeaders();

        if (!string.IsNullOrEmpty(_token))
        {
            headers["Authorization"] = $"Bearer {_token}";
        }

        if (!string.IsNullOrEmpty(_userId))
        {
            // SHA-256 hash of the user id — required by the API
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(_userId));
            headers["account-id"] = Convert.ToHexString(hash).ToLowerInvariant();
        }

        return headers;
    }

    /// <summary>Apply headers to an HttpRequestMessage.</summary>
    private static void ApplyHeaders(HttpRequestMessage request, Dictionary<string, string> headers)
    {
        foreach (var (key, value) in headers)
        {
            // Content-Type is a content header, skip it on request headers
            if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                continue;
            request.Headers.TryAddWithoutValidation(key, value);
        }
    }

    /// <summary>Create an HttpClient from the factory.</summary>
    private HttpClient CreateClient() => _httpClientFactory.CreateClient(HttpClientName);

    /// <summary>Send a POST request with all required headers.</summary>
    private async Task<HttpResponseMessage> SendPostAsync(string url, object body)
    {
        var json = JsonSerializer.Serialize(body);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        ApplyHeaders(request, GetBaseHeaders());

        using var client = CreateClient();
        return await client.SendAsync(request);
    }

    /// <summary>Send an authenticated GET request with all required headers.</summary>
    private async Task<HttpResponseMessage> SendAuthGetAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(request, GetAuthHeaders());

        using var client = CreateClient();
        return await client.SendAsync(request);
    }

    /// <summary>Login to LibreLink Up and obtain an auth token.</summary>
    public async Task LoginAsync()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("LibreLink credentials are not configured. Please set them in Settings.");

        _logger.LogInformation("Logging in to LibreLink Up at {BaseUrl}...", _baseUrl);

        var loginBody = new LoginRequest { Email = _email, Password = _password };

        var response = await SendPostAsync($"{_baseUrl}/llu/auth/login", loginBody);
        var body = await response.Content.ReadAsStringAsync();

        _logger.LogDebug("Login response HTTP {StatusCode}, body length: {Len}", response.StatusCode, body.Length);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Login failed HTTP {StatusCode} - Body: {Body}", response.StatusCode,
                body.Length > 500 ? body[..500] : body);
            throw new Exception($"Login failed with status {response.StatusCode}");
        }

        var loginResponse = JsonSerializer.Deserialize<LoginResponse>(body);

        // Redirect info can appear at root level or nested under data
        bool needsRedirect = loginResponse?.Status == 2
                          || (loginResponse?.Data?.Redirect == true)
                          || (loginResponse?.Redirect == true);

        var redirectRegion = loginResponse?.Data?.Region
                          ?? loginResponse?.Region;

        _logger.LogInformation("Login API status: {Status}, needsRedirect: {Redirect}, region: {Region}",
            loginResponse?.Status, needsRedirect, redirectRegion ?? "(none)");

        if (needsRedirect)
        {
            _logger.LogDebug("Redirect response body: {Body}", body);
        }

        if (needsRedirect && !string.IsNullOrEmpty(redirectRegion))
        {
            _logger.LogInformation("Region redirect required → {Region}", redirectRegion);

            if (RegionUrls.TryGetValue(redirectRegion, out var regionUrl))
            {
                _baseUrl = regionUrl;
            }
            else
            {
                _baseUrl = $"https://api-{redirectRegion}.libreview.io";
            }

            _logger.LogInformation("Re-logging in at {BaseUrl}...", _baseUrl);

            response = await SendPostAsync($"{_baseUrl}/llu/auth/login", loginBody);
            body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Re-login failed HTTP {StatusCode} - Body: {Body}", response.StatusCode,
                    body.Length > 500 ? body[..500] : body);
                throw new Exception($"Re-login failed with status {response.StatusCode}");
            }

            loginResponse = JsonSerializer.Deserialize<LoginResponse>(body);
            _logger.LogInformation("Re-login API status: {Status}", loginResponse?.Status);
        }

        _token = loginResponse?.Data?.AuthTicket?.Token
            ?? throw new Exception("Failed to obtain auth token from login response");

        // Store user ID for account-id header
        _userId = loginResponse?.Data?.User?.Id;

        _logger.LogInformation("Successfully logged in to LibreLink Up. Base URL: {BaseUrl}, UserId: {UserId}",
            _baseUrl, _userId ?? "(none)");
    }

    /// <summary>Get the list of patient connections.</summary>
    public async Task<List<Connection>> GetConnectionsAsync()
    {
        await EnsureLoggedIn();

        _logger.LogInformation("Fetching connections from {Url}...", $"{_baseUrl}/llu/connections");

        var response = await SendAuthGetAsync($"{_baseUrl}/llu/connections");
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to fetch connections: HTTP {StatusCode} - Body: {Body}",
                response.StatusCode, body.Length > 500 ? body[..500] : body);
            throw new Exception($"Failed to fetch connections: {response.StatusCode}");
        }

        var connectionsResponse = JsonSerializer.Deserialize<ConnectionsResponse>(body);
        return connectionsResponse?.Data ?? new List<Connection>();
    }

    /// <summary>
    /// Fetch the graph (historical) data for a patient.
    /// Returns the current measurement + historical graph data points.
    /// </summary>
    public async Task<GraphData?> GetGraphDataAsync(string? patientId = null)
    {
        await EnsureLoggedIn();

        var pid = patientId ?? _patientId;

        // If no patient ID is configured, get the first connection
        if (string.IsNullOrEmpty(pid))
        {
            var connections = await GetConnectionsAsync();
            pid = connections.FirstOrDefault()?.PatientId;

            if (string.IsNullOrEmpty(pid))
            {
                _logger.LogWarning("No patient connections found.");
                return null;
            }

            _logger.LogInformation("Using first connection patient ID: {PatientId}", pid);
        }

        var response = await SendAuthGetAsync($"{_baseUrl}/llu/connections/{pid}/graph");
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Failed to fetch graph data: {StatusCode}", response.StatusCode);
            throw new Exception($"Failed to fetch graph data: {response.StatusCode}");
        }

        var graphResponse = JsonSerializer.Deserialize<GraphResponse>(body);
        return graphResponse?.Data;
    }

    /// <summary>
    /// Parse glucose measurements into GlucoseReading entities.
    /// </summary>
    public List<GlucoseReading> ParseReadings(GraphData graphData, string? patientId)
    {
        var readings = new List<GlucoseReading>();

        // Add graph data (historical readings)
        if (graphData.Measurements != null)
        {
            foreach (var m in graphData.Measurements)
            {
                var reading = MapMeasurement(m, patientId);
                if (reading != null) readings.Add(reading);
            }
        }

        // Add the current measurement from the connection
        if (graphData.Connection?.GlucoseMeasurement != null)
        {
            var current = MapMeasurement(graphData.Connection.GlucoseMeasurement, patientId);
            if (current != null) readings.Add(current);
        }

        return readings;
    }

    private GlucoseReading? MapMeasurement(GlucoseMeasurement m, string? patientId)
    {
        DateTime timestamp;
        if (!string.IsNullOrEmpty(m.FactoryTimestamp))
        {
            if (!DateTime.TryParse(m.FactoryTimestamp, out timestamp))
            {
                _logger.LogWarning("Could not parse FactoryTimestamp: {Ts}", m.FactoryTimestamp);
                return null;
            }
        }
        else if (!string.IsNullOrEmpty(m.Timestamp))
        {
            if (!DateTime.TryParse(m.Timestamp, out timestamp))
            {
                _logger.LogWarning("Could not parse Timestamp: {Ts}", m.Timestamp);
                return null;
            }
        }
        else
        {
            timestamp = DateTime.UtcNow;
        }

        // LibreLink timestamps are UTC — mark them so they serialize with "Z"
        timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);

        return new GlucoseReading
        {
            Value = m.Value,
            Timestamp = timestamp,
            TrendArrow = m.TrendArrow,
            FactoryTimestamp = m.FactoryTimestamp,
            IsHigh = m.IsHigh,
            IsLow = m.IsLow,
            PatientId = patientId,
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task EnsureLoggedIn()
    {
        if (string.IsNullOrEmpty(_token))
        {
            await LoginAsync();
        }
    }
}
