namespace Bank.Services.Helpers;

public static class TextUtil
{
    public static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length <= max ? s : s[..max];
    }

    public static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>عرض اللوحة (ر ع ط 4428) بيتحسب وقت القراءة، ومش متخزن.</summary>
public static class PlateText
{
    public static string? Display(string? letters, string? digits)
    {
        var l = letters ?? "";
        var d = digits ?? "";
        if (l.Length == 0 && d.Length == 0) return null;
        if (l.Length == 0) return d;
        var spaced = string.Join(" ", l.ToCharArray());
        return d.Length == 0 ? spaced : spaced + " " + d;
    }
}
