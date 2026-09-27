public static class ReferralColumnSuggestor
{
    private static readonly string[] ColorAliases =
    {
        "لون المركبة الأساسي", "لون المركبة", "اللون", "لون", "color"
    };

    private static readonly string[] VehicleTypeAliases =
    {
        "نوع السيارة", "نوع المركبة", "التصنيف", "vehicle type", "vehicletype", "type"
    };

    private static readonly string[] ModelAliases =
    {
        "طراز المركبة", "موديل السيارة", "الطراز", "الموديل", "model"
    };

    public static (string? ColorHeader, string? VehicleTypeHeader, string? ModelHeader) SuggestDefaults(
        IReadOnlyList<string?> headers)
    {
        return (
            FindByAliases(headers, ColorAliases),
            FindByAliases(headers, VehicleTypeAliases),
            FindByAliases(headers, ModelAliases));
    }

    private static string? FindByAliases(IReadOnlyList<string?> headers, string[] aliases)
    {
        foreach (var alias in aliases)
        {
            var match = headers.FirstOrDefault(h =>
                !string.IsNullOrWhiteSpace(h) && string.Equals(h.Trim(), alias, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return null;
    }
}