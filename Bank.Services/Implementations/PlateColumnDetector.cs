using Bank.Services.Interfaces;
using System.Text.RegularExpressions;

public class PlateColumnDetector : IPlateColumnDetector
{
    private static readonly string[] ArabicPlateExplicitKeywords =
    {
        "لوحة عربي", "اللوحة عربي", "اللوحة العربي", "اللوحة بالعربي",
        "رقم اللوحة عربي", "plate number in arabic", "plate in arabic", "arabic plate"
    };

    private static readonly string[] HeaderKeywords =
    {
        "لوحة", "اللوحة", "رقم اللوحة", "رقم المركبة", "plate", "license"
    };

    // يطابق عربي بس (حروف+أرقام أو أرقام+حروف) بعد إزالة المسافات والشرطات
    private static readonly Regex ArabicPlatePattern =
        new(@"^(?:[\u0621-\u064A]{2,4}[0-9\u0660-\u0669]{1,5}|[0-9\u0660-\u0669]{1,5}[\u0621-\u064A]{2,4})$",
            RegexOptions.Compiled);

    private static readonly Regex GeneralPlatePattern =
        new(@"^(?:[A-Za-z\u0621-\u064A]{2,4}[0-9\u0660-\u0669]{1,5}|[0-9\u0660-\u0669]{1,5}[A-Za-z\u0621-\u064A]{2,4})$",
            RegexOptions.Compiled);

    private const double MinMatchRatio = 0.8;

    public int? DetectPlateColumn(IReadOnlyList<string?> headerRow, IEnumerable<IReadOnlyList<string?>> sampleDataRows)
    {
        var samples = sampleDataRows as IReadOnlyList<IReadOnlyList<string?>> ?? sampleDataRows.ToList();

        // 1) أولوية قصوى: عنوان صريح بيقول "لوحة عربي"
        var explicitArabicMatch = FindColumnByKeywords(headerRow, ArabicPlateExplicitKeywords);
        if (explicitArabicMatch.HasValue) return explicitArabicMatch;

        // 2) لو فيه عمود بياناته الفعلية لوحات عربية (حتى لو الهيدر اسمه "عربي" بس) -> يكسب على عمود "Plate no" الإنجليزي
        var arabicByContent = FindBestColumnByPattern(samples, ArabicPlatePattern);
        if (arabicByContent.HasValue) return arabicByContent;

        // 3) بعد كده العناوين العامة زي "لوحة" أو "Plate no"
        var generalMatch = FindColumnByKeywords(headerRow, HeaderKeywords);
        if (generalMatch.HasValue) return generalMatch;

        // 4) Fallback: أي عمود بياناته لوحات (عربي أو إنجليزي)
        return FindBestColumnByPattern(samples, GeneralPlatePattern);
    }

    private static int? FindBestColumnByPattern(IReadOnlyList<IReadOnlyList<string?>> samples, Regex pattern)
    {
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
                var cleaned = StripSeparators(row[col]);
                if (string.IsNullOrEmpty(cleaned)) continue;

                nonEmpty++;
                if (pattern.IsMatch(cleaned)) matches++;
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

    private static string StripSeparators(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return new string(value.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '\u0640').ToArray());
    }

    private static int? FindColumnByKeywords(IReadOnlyList<string?> headerRow, string[] keywords)
    {
        for (var i = 0; i < headerRow.Count; i++)
        {
            var header = headerRow[i]?.Trim() ?? string.Empty;
            if (header.Length > 0 && keywords.Any(k => header.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return i;
        }
        return null;
    }
}
//using System.Text.RegularExpressions;
//using Bank.Services.Interfaces;

//namespace Bank.Services.Implementations;

//public class PlateColumnDetector : IPlateColumnDetector
//{

//    private static readonly string[] ArabicPlateExplicitKeywords =
//    {
//        "لوحة عربي", "اللوحة عربي", "اللوحة العربي", "اللوحة بالعربي",
//        "رقم اللوحة عربي", "plate number in arabic", "plate in arabic", "arabic plate"
//    };

//    private static readonly string[] HeaderKeywords =
//    {
//        "لوحة", "اللوحة", "رقم اللوحة", "رقم المركبة", "plate", "license"
//    };

//    private static readonly Regex PlatePattern =
//        new(@"^[A-Za-z\u0621-\u064A]{2,4}[0-9\u0660-\u0669]{3,5}$", RegexOptions.Compiled);

//    private const double MinMatchRatio = 0.8;

//    public int? DetectPlateColumn(IReadOnlyList<string?> headerRow, IEnumerable<IReadOnlyList<string?>> sampleDataRows)
//    {
//        // أولوية قصوى: عمود بيقول صراحة إنه اللوحة "عربي" — لو موجود، بيكسب حتى
//        // لو عمود تاني فيه كلمة "لوحة" عادية وجاي قبله في ترتيب الأعمدة.
//        var explicitArabicMatch = FindColumnByKeywords(headerRow, ArabicPlateExplicitKeywords);
//        if (explicitArabicMatch.HasValue) return explicitArabicMatch;

//        var generalMatch = FindColumnByKeywords(headerRow, HeaderKeywords);
//        if (generalMatch.HasValue) return generalMatch;

//        var samples = sampleDataRows as IReadOnlyList<IReadOnlyList<string?>> ?? sampleDataRows.ToList();
//        if (samples.Count == 0) return null;

//        var columnCount = samples.Max(r => r.Count);
//        var bestColumn = -1;
//        var bestRatio = 0.0;

//        for (var col = 0; col < columnCount; col++)
//        {
//            var nonEmpty = 0;
//            var matches = 0;

//            foreach (var row in samples)
//            {
//                if (col >= row.Count) continue;
//                var value = row[col]?.Trim();
//                if (string.IsNullOrWhiteSpace(value)) continue;

//                nonEmpty++;
//                if (PlatePattern.IsMatch(value)) matches++;
//            }

//            if (nonEmpty == 0) continue;

//            var ratio = (double)matches / nonEmpty;
//            if (ratio > bestRatio)
//            {
//                bestRatio = ratio;
//                bestColumn = col;
//            }
//        }

//        return bestRatio >= MinMatchRatio ? bestColumn : null;
//    }

//    private static int? FindColumnByKeywords(IReadOnlyList<string?> headerRow, string[] keywords)
//    {
//        for (var i = 0; i < headerRow.Count; i++)
//        {
//            var header = headerRow[i]?.Trim() ?? string.Empty;
//            if (header.Length > 0 && keywords.Any(k => header.Contains(k, StringComparison.OrdinalIgnoreCase)))
//            {
//                return i;
//            }
//        }
//        return null;
//    }
//}

