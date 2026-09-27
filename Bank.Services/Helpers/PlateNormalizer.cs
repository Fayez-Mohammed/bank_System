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

    private static readonly Regex ReferralPlatePattern =
     new(@"^([A-Za-z\u0621-\u064A]+)([0-9]*)$", RegexOptions.Compiled);

 
    private static readonly Regex ReversedReferralPlatePattern =
        new(@"^([0-9]+)([A-Za-z\u0621-\u064A]+)$", RegexOptions.Compiled);

    public static string NormalizeReferralPlate(string rawPlate)
    {
        if (string.IsNullOrWhiteSpace(rawPlate)) return string.Empty;

        var cleaned = rawPlate.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();
        cleaned = ConvertArabicIndicDigitsToWestern(cleaned);

        string letters;
        string digits;

        var match = ReferralPlatePattern.Match(cleaned);
        if (match.Success)
        {
            letters = match.Groups[1].Value;
            digits = match.Groups[2].Value;
        }
        else
        {
            var reversedMatch = ReversedReferralPlatePattern.Match(cleaned);
            if (reversedMatch.Success)
            {
                digits = reversedMatch.Groups[1].Value;
                letters = reversedMatch.Groups[2].Value;
            }
            else
            {
                return cleaned;
            }
        }

        var mappedLetters = new StringBuilder(letters.Length);
        foreach (var ch in letters)
        {
            mappedLetters.Append(EnglishToArabicPlateLetters.TryGetValue(ch, out var arabicChar) ? arabicChar : ch);
        }

        var paddedDigits = digits.Length >= 4 ? digits : digits.PadLeft(4, '0');

        return mappedLetters.ToString() + paddedDigits;
    }
   

    private static string ConvertArabicIndicDigitsToWestern(string input)
    {
        var builder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (ch is >= '\u0660' and <= '\u0669')
            {
                builder.Append((char)('0' + (ch - '\u0660')));
            }
            else
            {
                builder.Append(ch);
            }
        }
        return builder.ToString();
    }
}