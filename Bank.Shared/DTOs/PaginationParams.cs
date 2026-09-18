public class PaginationParams
{
    private const int MaxPageSize = 100;
    private int _pageSize = 20;

    public int PageNumber { get; set; } = 1;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value <= 0 ? 20 : Math.Min(value, MaxPageSize);
    }

    public string? SearchTerm { get; set; }
}