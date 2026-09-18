using System.Text;
using System.Text.RegularExpressions;

namespace Bank.Services.HangFireJobs;


public static class PlateNormalizer
{
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

            sb.Append(mappedChar);
        }

        return sb.ToString().Trim();
    }

    //public static string Normalize(string rawPlate)
    //{
    //    if (string.IsNullOrWhiteSpace(rawPlate)) return string.Empty;
    //    return rawPlate.Replace(" ", "").Trim().ToUpperInvariant();
    //}


    public static string NormalizeReferralPlate(string rawPlate)
    {
        if (string.IsNullOrWhiteSpace(rawPlate)) return string.Empty;

        var cleaned = rawPlate.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();
        cleaned = ConvertArabicIndicDigitsToWestern(cleaned);

        var match = ReferralPlatePattern.Match(cleaned);
        if (!match.Success)
        {
       
            return cleaned;
        }

        var letters = match.Groups[1].Value;
        var digits = match.Groups[2].Value;

       
        var paddedDigits = digits.Length >= 4 ? digits : digits.PadLeft(4, '0');

        return letters + paddedDigits;
    }

    private static readonly Regex ReferralPlatePattern =
        new(@"^([A-Za-z\u0621-\u064A]+)([0-9]*)$", RegexOptions.Compiled);

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
