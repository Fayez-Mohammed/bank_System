using Bank.DAL.Context;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs.TrainingDTOs;
using Microsoft.EntityFrameworkCore;

namespace Bank.Services.Implementations;

public class TrainingCorrectionsService : ITrainingCorrectionsService
{
    public const int MaxLimit = 1000;

    // Ids are handed out before commit, so a slow transaction can commit a lower id after a higher one
    // was already read. Holding back the newest rows keeps a reader's afterId cursor from skipping it.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(10);

    private readonly AppDbContext _dbContext;

    public TrainingCorrectionsService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PlateCorrectionFeedDto> GetAfterAsync(long afterId, int limit, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
        var settledBefore = DateTime.UtcNow - SettleDelay;

        var rows = await _dbContext.PlateCorrections
            .AsNoTracking()
            .Where(c => c.Id > afterId && c.CreatedAt < settledBefore)
            .OrderBy(c => c.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > limit;
        var items = rows.Take(limit)
            .Select(c => new PlateCorrectionDto(
                c.Id, c.SessionId, c.SessionType.ToString().ToLowerInvariant(), c.PlateId, c.ArchiveRowId, c.IsManualPlate,
                c.Action.ToString().ToLowerInvariant(), c.OldPlate, c.NewPlate, c.OldVehicleType, c.NewVehicleType,
                c.OldNotes, c.NewNotes, c.AudioOffsetMs, c.AudioStartMs, c.AudioEndMs, c.UserId, c.RecorderName, c.CreatedAt))
            .ToList();

        return new PlateCorrectionFeedDto(items, items.Count > 0 ? items[^1].Id : afterId, hasMore);
    }
}
