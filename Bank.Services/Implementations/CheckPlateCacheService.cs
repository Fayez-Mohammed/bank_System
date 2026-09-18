using Bank.DAL.Context;
using Bank.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Bank.Services.Implementations;

public class CheckPlateCacheService : ICheckPlateCacheService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);
    private readonly IMemoryCache _cache;
    private readonly AppDbContext _dbContext;

    public CheckPlateCacheService(IMemoryCache cache, AppDbContext dbContext)
    {
        _cache = cache;
        _dbContext = dbContext;
    }

    public async Task<bool> IsPlateInActiveCheckFileAsync(
        Guid? groupId, Guid userId, string normalizedPlate, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(groupId, userId);

        var plateSet = await _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;

            var query = _dbContext.CheckPlates
                .AsNoTracking()
                .Where(cp => cp.CheckFile.IsActive);

            query = groupId.HasValue
                ? query.Where(cp => cp.CheckFile.GroupId == groupId.Value)
                : query.Where(cp => cp.CheckFile.GroupId == null && cp.CheckFile.UploadedBy == userId);

            var plates = await query.Select(cp => cp.NormalizedPlate).ToListAsync(cancellationToken);
            return new HashSet<string>(plates, StringComparer.OrdinalIgnoreCase);
        });

        return plateSet?.Contains(normalizedPlate) ?? false;
    }

    public void InvalidateActiveCheckFileCache(Guid? groupId, Guid userId)
    {
        _cache.Remove(BuildCacheKey(groupId, userId));
    }

    private static string BuildCacheKey(Guid? groupId, Guid userId) =>
        groupId.HasValue ? $"active-check-plates:group:{groupId.Value}" : $"active-check-plates:user:{userId}";
}
