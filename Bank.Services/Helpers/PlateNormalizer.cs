using System.Text;
using System.Text.RegularExpressions;

namespace Bank.Services.HangFireJobs;

public static class PlateNormalizer
{
    
    private static readonly Dictionary<char, char> EnglishToArabicPlateLetters = new()
    {
        ['A'] = 'ا',
        ['B'] = 'ب',
        ['Z'] = 'م',
        ['R'] = 'ر',
        ['D'] = 'د',
        ['V'] = 'ي',
        ['J'] = 'ح',
        ['E'] = 'ع',
        ['K'] = 'ك',
        ['G'] = 'ق',
        ['X'] = 'ص',
        ['U'] = 'و',
        ['L'] = 'ل',
        ['T'] = 'ط',
        ['S'] = 'س',
        ['N'] = 'ن',
        ['H'] = 'ه'
    };

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var normalized = input.Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            if (char.IsWhiteSpace(ch) || ch == '\u0640' || (ch >= '\u064B' && ch <= '\u065F'))
            {
                continue;
            }

            var mappedChar = ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ة' => 'ه',
                'ى' => 'ي',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => char.ToUpperInvariant(ch)
            };

            if (EnglishToArabicPlateLetters.TryGetValue(mappedChar, out var arabicEquivalent))
            {
                mappedChar = arabicEquivalent;
            }

            sb.Append(mappedChar);
        }

        return sb.ToString().Trim();
    }

    private static readonly Regex LettersThenDigitsPattern =
        new(@"^([A-Z\u0621-\u064A]+)([0-9]*)$", RegexOptions.Compiled);

    private static readonly Regex DigitsThenLettersPattern =
        new(@"^([0-9]+)([A-Z\u0621-\u064A]+)$", RegexOptions.Compiled);

    public static string NormalizeReferralPlate(string rawPlate)
    {
        if (string.IsNullOrWhiteSpace(rawPlate)) return string.Empty;

        var cleaned = CleanReferralInput(rawPlate);

        string letterPart, digitPart;

        var match = LettersThenDigitsPattern.Match(cleaned);
        if (match.Success)
        {
            letterPart = match.Groups[1].Value;
            digitPart = match.Groups[2].Value;
        }
        else
        {
            var reversedMatch = DigitsThenLettersPattern.Match(cleaned);
            if (!reversedMatch.Success) return cleaned;

            digitPart = reversedMatch.Groups[1].Value;
            letterPart = reversedMatch.Groups[2].Value;
        }

        // نفصل الحروف العربية عن اللاتينية (ممكن الاتنين موجودين في نفس الخانة)
        var arabic = new StringBuilder();
        var latin = new StringBuilder();
        foreach (var ch in letterPart)
            (ch is >= 'A' and <= 'Z' ? latin : arabic).Append(ch);

        string letters;
        if (arabic.Length > 0)
        {
            // العربي موجود (لوحده أو جنب الإنجليزي): هو الأصل، بنستخدمه زي ما هو.
            letters = arabic.ToString();
        }
        else
        {
            // إنجليزي بس: نحوّله لعربي ونعكس الترتيب
            var mapped = new StringBuilder(latin.Length);
            for (var i = latin.Length - 1; i >= 0; i--)
            {
                mapped.Append(EnglishToArabicPlateLetters.TryGetValue(latin[i], out var arabicChar)
                    ? arabicChar
                    : latin[i]);
            }
            letters = mapped.ToString();
        }

        var paddedDigits = digitPart.Length >= 4 ? digitPart : digitPart.PadLeft(4, '0');
        return letters + paddedDigits;
    }

    // بيشيل المسافات (بكل أنواعها)، الشرطة، التطويل، التشكيل، وعلامات اتجاه الكتابة
    // اللي بتيجي مع النسخ واللصق، ويوحّد الأرقام والحروف العربية زي Normalize بالظبط.
    private static string CleanReferralInput(string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(normalized.Length);

        foreach (var raw in normalized)
        {
            if (char.IsWhiteSpace(raw) || raw == '-' || raw == '\u0640' ||
                (raw >= '\u064B' && raw <= '\u065F') || IsBidiControl(raw))
            {
                continue;
            }

            var ch = ToWesternDigit(raw);
            ch = ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ة' => 'ه',
                'ى' => 'ي',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => char.ToUpperInvariant(ch)
            };

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static bool IsBidiControl(char ch) =>
        ch is '\u200B' or '\u200E' or '\u200F'
            or (>= '\u202A' and <= '\u202E')
            or (>= '\u2066' and <= '\u2069');

    private static char ToWesternDigit(char ch) => ch switch
    {
        >= '\u0660' and <= '\u0669' => (char)('0' + (ch - '\u0660')),   // ٠١٢٣...
        >= '\u06F0' and <= '\u06F9' => (char)('0' + (ch - '\u06F0')),   // ۰۱۲۳...
        _ => ch
    };

}