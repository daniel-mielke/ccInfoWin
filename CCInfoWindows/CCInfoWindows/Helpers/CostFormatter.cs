using System.Globalization;

namespace CCInfoWindows.Helpers;

/// <summary>
/// Formats USD cost values for display, including tilde prefix for estimated costs.
/// </summary>
public static class CostFormatter
{
    /// <summary>
    /// Returns a formatted cost string using a period as decimal separator.
    /// Estimated costs are prefixed with "~" (e.g. "~$4.23").
    /// Exact costs use no prefix (e.g. "$4.23").
    /// </summary>
    public static string FormatCost(decimal amount, bool isEstimated)
    {
        var formatted = $"${amount.ToString("F2", CultureInfo.InvariantCulture)}";
        return isEstimated ? "~" + formatted : formatted;
    }
}
