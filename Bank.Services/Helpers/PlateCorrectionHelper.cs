namespace Bank.Services.Helpers;

internal static class PlateCorrectionHelper
{
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static bool Same(string? a, string? b) => Clean(a) == Clean(b);
}
