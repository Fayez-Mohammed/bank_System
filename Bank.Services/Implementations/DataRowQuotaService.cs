using Bank.DAL.Context;
using Bank.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Bank.Services.Implementations;

public class DataRowQuotaService : IDataRowQuotaService
{
    private readonly AppDbContext _db;

    public DataRowQuotaService(AppDbContext db) => _db = db;

    public async Task<long?> GetLimitAsync(Guid? groupId, Guid ownerId, CancellationToken ct = default)
    {
        return groupId.HasValue
            ? await _db.Groups.AsNoTracking().Where(g => g.Id == groupId.Value)
                .Select(g => g.MaxDataFileRows).FirstOrDefaultAsync(ct)
            : await _db.Users.AsNoTracking().Where(u => u.Id == ownerId)
                .Select(u => u.MaxDataFileRows).FirstOrDefaultAsync(ct);
    }

    public async Task<DataRowQuota> GetForScopeAsync(
        Guid? groupId, Guid ownerId, Guid? excludeFileId = null, CancellationToken ct = default)
    {
        var limit = await GetLimitAsync(groupId, ownerId, ct);

        var files = _db.DataFiles.AsNoTracking().Where(f =>
            groupId.HasValue ? f.GroupId == groupId.Value : f.GroupId == null && f.OwnerId == ownerId);
        if (excludeFileId.HasValue) files = files.Where(f => f.Id != excludeFileId.Value);

        var used = await files.SumAsync(f => (long?)f.RowCount, ct) ?? 0;
        return new DataRowQuota(limit, used);
    }
}
