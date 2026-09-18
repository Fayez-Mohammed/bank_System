using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Services.HangFireJobs;
using Bank.Services.Interfaces;
using Bank.Shared.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace Bank.Services.HangFireJobs;

[Queue("recording-processing")]
public class RecordingProcessingJob
{
    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorage;
    private readonly IAiTranscriptionService _aiService;
    private readonly ILogger<RecordingProcessingJob> _logger;

    public RecordingProcessingJob(
        AppDbContext dbContext, IFileStorageService fileStorage, IAiTranscriptionService aiService, ILogger<RecordingProcessingJob> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _aiService = aiService;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ProcessAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Recording processing job started for Session {SessionId}", sessionId);

        var session = await _dbContext.RecordingSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.StartedAt, s.CheckFileId, s.AudioUrl, s.UserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (session is null || session.AudioUrl is null)
        {
            _logger.LogWarning("RecordingSession {SessionId} not found or has no audio — aborting", sessionId);
            return;
        }

        try
        {
            await using var audioStream = await _fileStorage.OpenReadAsync(session.AudioUrl, cancellationToken);
            var aiResult = await _aiService.TranscribeAsync(audioStream, "recording.aac", "audio/aac", cancellationToken);

            var gpsPoints = await _dbContext.RecordingGpsPoints
                .AsNoTracking()
                .Where(g => g.SessionId == sessionId)
                .OrderBy(g => g.CapturedAt)
                .Select(g => new GpsPointDto(
        g.Location,
        g.CapturedAt))/*(g => new { g.Location, g.CapturedAt })*/
                .ToListAsync(cancellationToken);

            var checkPlatesLookup = session.CheckFileId is null
                ? new Dictionary<string, long>()
                : await _dbContext.CheckPlates
                    .AsNoTracking()
                    .Where(cp => cp.CheckFileId == session.CheckFileId.Value)
                    .ToDictionaryAsync(cp => cp.NormalizedPlate, cp => cp.Id, cancellationToken);

            var recordedPlates = new List<RecordedPlate>();
            var sequence = 0;

            foreach (var car in aiResult.Cars)
            {
                sequence++;
                if (string.IsNullOrWhiteSpace(car.PlateNumber)) continue;

                var normalized = PlateNormalizer.Normalize(car.PlateNumber);

                
                var effectiveOffsetMs = car.TimestampStartMs.HasValue && car.TimestampEndMs.HasValue
                    ? (car.TimestampStartMs.Value + car.TimestampEndMs.Value) / 2
                    : car.TimestampMs;

                var recordedAt = session.StartedAt.AddMilliseconds(effectiveOffsetMs);
                var nearestLocation = FindNearestGpsPoint(gpsPoints, recordedAt);

                var isMatched = checkPlatesLookup.TryGetValue(normalized, out var matchedCheckPlateId);

                recordedPlates.Add(new RecordedPlate
                {
                    SessionId = sessionId,
                    UserId = session.UserId,
                    SequenceNumber = sequence,
                    RawPlate = car.PlateNumber,
                    NormalizedPlate = normalized,
                    VehicleType = car.VehicleType,
                    Notes = car.Notes,
                    Location = nearestLocation,
                    AudioOffsetMs = effectiveOffsetMs,
                    IsMatched = isMatched,
                    MatchedCheckPlateId = isMatched ? matchedCheckPlateId : null,
                    IsManualEntry = false,
                    RecordedAt = recordedAt
                });
            }

            await _dbContext.RecordedPlates.AddRangeAsync(recordedPlates, cancellationToken);

            await _dbContext.RecordingSessions
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.TotalPlates, recordedPlates.Count)
                    .SetProperty(s => s.TotalGpsPoints, gpsPoints.Count)
                    .SetProperty(s => s.Status, SessionStatus.Completed)
                    .SetProperty(s => s.EndedAt, DateTime.UtcNow)
                    .SetProperty(s => s.ProcessingError, (string?)null),
                    cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Recording processing completed for Session {SessionId}: {PlateCount} plates, {MatchedCount} matched",
                sessionId, recordedPlates.Count, recordedPlates.Count(p => p.IsMatched));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recording processing failed for Session {SessionId}", sessionId);

            await _dbContext.RecordingSessions
                .Where(s => s.Id == sessionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.Status, SessionStatus.Failed)
                    .SetProperty(s => s.ProcessingError, ex.Message),
                    cancellationToken);

            throw;
        }
    }

    
    private static NetTopologySuite.Geometries.Point? FindNearestGpsPoint(
        List<GpsPointDto> orderedGpsPoints, DateTime targetTime)
    {
        if (orderedGpsPoints.Count == 0) return null;

        NetTopologySuite.Geometries.Point? nearest = null;
        var smallestDiff = TimeSpan.MaxValue;

        foreach (var point in orderedGpsPoints)
        {
            TimeSpan diff = ((DateTime)point.CapturedAt - targetTime).Duration();
            if (diff < smallestDiff)
            {
                smallestDiff = diff;
                nearest = (NetTopologySuite.Geometries.Point)point.Location;
            }
        }

        return nearest;
    }
    private sealed record GpsPointDto(
    Point? Location,
    DateTime CapturedAt);
}
