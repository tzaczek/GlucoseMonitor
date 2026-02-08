namespace GlucoseAPI.Domain.Services;

/// <summary>
/// Pure domain service for calculating AI API usage costs.
/// No I/O, no dependencies — only pricing logic.
/// </summary>
public static class AiCostCalculator
{
    /// <summary>Known model pricing (USD per 1M tokens).</summary>
    private static readonly Dictionary<string, (double InputPer1M, double OutputPer1M)> ModelPricing =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-5-mini"]             = (0.15,  0.60),
            ["gpt-5-mini-2025-08-07"]  = (0.15,  0.60),
            ["gpt-4o-mini"]            = (0.15,  0.60),
            ["gpt-4o-mini-2024-07-18"] = (0.15,  0.60),
            ["gpt-4o"]                 = (2.50, 10.00),
            ["gpt-4o-2024-08-06"]      = (2.50, 10.00),
            ["gpt-4-turbo"]            = (10.0,  30.0),
            ["gpt-3.5-turbo"]          = (0.50,  1.50),
        };

    /// <summary>
    /// Compute estimated cost in USD for a given model and token counts.
    /// Returns 0 if the model is unknown.
    /// </summary>
    public static double ComputeCost(string model, long inputTokens, long outputTokens)
    {
        if (!ModelPricing.TryGetValue(model, out var pricing))
        {
            // Try prefix match (e.g. "gpt-5-mini-2025-08-07" → "gpt-5-mini")
            var key = ModelPricing.Keys.FirstOrDefault(k =>
                model.StartsWith(k, StringComparison.OrdinalIgnoreCase));
            if (key != null)
                pricing = ModelPricing[key];
            else
                return 0;
        }

        return (inputTokens * pricing.InputPer1M + outputTokens * pricing.OutputPer1M) / 1_000_000.0;
    }

    /// <summary>Returns the known model pricing table for display.</summary>
    public static IReadOnlyDictionary<string, (double InputPer1M, double OutputPer1M)> GetPricingTable()
        => ModelPricing;
}
