using System.Text.RegularExpressions;
using Bank.Services.Interfaces;

namespace Bank.Services.Implementations;

public class PlateColumnDetector : IPlateColumnDetector
{
    private static readonly string[] HeaderKeywords =
    {
        "لوحة", "اللوحة", "رقم اللوحة", "رقم المركبة", "plate", "license"
    };

    private static readonly Regex PlatePattern =
        new(@"^[A-Za-z\u0621-\u064A]{2,4}[0-9\u0660-\u0669]{3,5}$", RegexOptions.Compiled);

    private const double MinMatchRatio = 0.8; 

    public int? DetectPlateColumn(IReadOnlyList<string?> headerRow, IEnumerable<IReadOnlyList<string?>> sampleDataRows)
    {
        for (var i = 0; i < headerRow.Count; i++)
        {
            var header = headerRow[i]?.Trim() ?? string.Empty;
            if (header.Length > 0 && HeaderKeywords.Any(k => header.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                return i;
            }
        }

        var samples = sampleDataRows as IReadOnlyList<IReadOnlyList<string?>> ?? sampleDataRows.ToList();
        if (samples.Count == 0) return null;

        var columnCount = samples.Max(r => r.Count);
        var bestColumn = -1;
        var bestRatio = 0.0;

        for (var col = 0; col < columnCount; col++)
        {
            var nonEmpty = 0;
            var matches = 0;

            foreach (var row in samples)
            {
                if (col >= row.Count) continue;
                var value = row[col]?.Trim();
                if (string.IsNullOrWhiteSpace(value)) continue;

                nonEmpty++;
                if (PlatePattern.IsMatch(value)) matches++;
            }

            if (nonEmpty == 0) continue;

            var ratio = (double)matches / nonEmpty;
            if (ratio > bestRatio)
            {
                bestRatio = ratio;
                bestColumn = col;
            }
        }

        return bestRatio >= MinMatchRatio ? bestColumn : null;
    }
}
