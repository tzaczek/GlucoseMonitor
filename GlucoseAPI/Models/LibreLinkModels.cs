using System.Text.Json.Serialization;

namespace GlucoseAPI.Models;

// ── Login ───────────────────────────────────────────────

public class LoginRequest
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("data")]
    public LoginData? Data { get; set; }

    // Redirect fields may appear at root level in some API versions
    [JsonPropertyName("redirect")]
    public bool? Redirect { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }
}

public class LoginData
{
    [JsonPropertyName("user")]
    public LibreUser? User { get; set; }

    [JsonPropertyName("authTicket")]
    public AuthTicket? AuthTicket { get; set; }

    /// <summary>
    /// If the API requires a region redirect, this will contain the regional API URL.
    /// </summary>
    [JsonPropertyName("redirect")]
    public bool Redirect { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }
}

public class LibreUser
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}

public class AuthTicket
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("expires")]
    public long Expires { get; set; }

    [JsonPropertyName("duration")]
    public long Duration { get; set; }
}

// ── Connections ─────────────────────────────────────────

public class ConnectionsResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("data")]
    public List<Connection>? Data { get; set; }
}

public class Connection
{
    [JsonPropertyName("patientId")]
    public string? PatientId { get; set; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("glucoseMeasurement")]
    public GlucoseMeasurement? GlucoseMeasurement { get; set; }
}

public class GlucoseMeasurement
{
    [JsonPropertyName("Value")]
    public double Value { get; set; }

    [JsonPropertyName("TrendArrow")]
    public int TrendArrow { get; set; }

    [JsonPropertyName("Timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("FactoryTimestamp")]
    public string? FactoryTimestamp { get; set; }

    [JsonPropertyName("isHigh")]
    public bool IsHigh { get; set; }

    [JsonPropertyName("isLow")]
    public bool IsLow { get; set; }
}

// ── Graph / History ─────────────────────────────────────

public class GraphResponse
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("data")]
    public GraphData? Data { get; set; }
}

public class GraphData
{
    [JsonPropertyName("connection")]
    public Connection? Connection { get; set; }

    [JsonPropertyName("graphData")]
    public List<GlucoseMeasurement>? Measurements { get; set; }

    [JsonPropertyName("activeSensors")]
    public List<object>? ActiveSensors { get; set; }
}
