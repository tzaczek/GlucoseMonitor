using System.Text.RegularExpressions;

namespace GlucoseAPI.Domain.Services;

/// <summary>
/// Pure domain service for parsing AI classification tags from analysis text.
/// </summary>
public static class ClassificationParser
{
    private static readonly Regex ClassificationRegex = new(
        @"^\s*\[CLASSIFICATION:\s*(green|yellow|red)\]\s*\n?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Extracts a [CLASSIFICATION: green/yellow/red] tag from the beginning of an AI response.
    /// Returns the cleaned analysis text and the classification value.
    /// </summary>
    public static (string analysis, string? classification) Parse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return (rawText ?? "", null);

        var match = ClassificationRegex.Match(rawText);

        if (match.Success)
        {
            var classification = match.Groups[1].Value.ToLowerInvariant();
            var cleanedText = rawText[match.Length..].TrimStart();
            return (cleanedText, classification);
        }

        return (rawText, null);
    }

    /// <summary>
    /// Validates that a classification value is one of the allowed values.
    /// </summary>
    public static bool IsValid(string? classification)
    {
        return classification is "green" or "yellow" or "red";
    }
}
