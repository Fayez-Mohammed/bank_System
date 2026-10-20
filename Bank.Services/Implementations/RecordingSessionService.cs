using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Repo.Interfaces;
using Bank.Services.HangFireJobs;
using Bank.Services.Helpers;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.RecordingArchiveDTOs;
using Bank.Shared.DTOs.RecordingSessionDTOs;
using Bank.Shared.Enums;
using ClosedXML.Excel;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace Bank.Services.Implementations;

public class RecordingSessionService : IRecordingSessionService
{
    private const long MaxAudioSizeBytes = 50L * 1024 * 1024;
    private static readonly string[] AllowedAudioExtensions = { ".aac", ".m4a", ".mp3", ".wav", ".ogg" };

    private readonly ILogger<RecordingSessionService> _logger;
    private readonly IUnitOfWork _unit;
    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorage;
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IRecordingArchiveService _archiveService;
    private readonly IPermissionService _permissions;

    public RecordingSessionService(
        ILogger<RecordingSessionService> logger, IUnitOfWork unit, AppDbContext dbContext,
        IFileStorageService fileStorage, IBackgroundJobClient backgroundJobClient,
        IRecordingArchiveService archiveService, IPermissionService permissions)
    {
        _permissions = permissions;
        _logger = logger;
        _unit = unit;
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _backgroundJobClient = backgroundJobClient;
        _archiveService = archiveService;
    }
    public async Task<ApiResponse<RecordingSessionStatusDto>> GetStatusAsync(
        Guid sessionId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) return ApiResponse<RecordingSessionStatusDto>.Fail("المستخدم غير موجود.");

        var status = await _dbContext.RecordingSessions
            .AsNoTracking()
            .Where(ScopeFilter(userScope.GroupId, currentUserId, sessionId))
            .Select(s => new RecordingSessionStatusDto(
                s.Id, s.Status, s.TotalPlates, s.RecordedPlates.Count(p => p.IsMatched), s.ProcessingError))
            .FirstOrDefaultAsync(cancellationToken);

        return status is null
            ? ApiResponse<RecordingSessionStatusDto>.Fail("التسجيل غير موجود.", null, 404)
            : ApiResponse<RecordingSessionStatusDto>.SuccessResponse(status);
    }


  
    public async Task<ApiResponse<CreateRecordingSessionResponseDto>> CreateAsync(
        Guid userId, CreateRecordingSessionRequestDto request, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(userId, cancellationToken);
        if (userScope is null) return ApiResponse<CreateRecordingSessionResponseDto>.Fail("المستخدم غير موجود.");

        var activeCheckFileId = await _dbContext.CheckFiles
            .Where(f => f.IsActive)
            .Where(f => userScope.GroupId.HasValue
                ? f.GroupId == userScope.GroupId.Value
                : f.GroupId == null && f.UploadedBy == userId)
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var displayName = await GenerateUniqueDisplayNameAsync(userScope.GroupId, userId, request.Street, cancellationToken);

        var session = new RecordingSession
        {
            UserId = userId,
            GroupId = userScope.GroupId,
            CheckFileId = activeCheckFileId,
            District = request.District,
            Street = request.Street,
            ExcelSheetName = request.ExcelSheetName??$"{request.Street}_{DateTime.UtcNow:yyyyMMdd}",
            DisplayName = displayName,
            TrackingMode = request.TrackingMode,
            TrackingIntervalSeconds = request.TrackingIntervalSeconds,
            RecorderName = request.RecorderName,
            Status = SessionStatus.Recording,
            StartedAt = DateTime.UtcNow
        };

        await _unit.Repository<RecordingSession>().AddAsync(session);
        await _unit.CompleteAsync();

        _logger.LogInformation(
            "RecordingSession {SessionId} ({DisplayName}) started by user {UserId}, snapshot CheckFileId={CheckFileId}",
            session.Id, displayName, userId, activeCheckFileId);

        return ApiResponse<CreateRecordingSessionResponseDto>.SuccessResponse(
            new CreateRecordingSessionResponseDto(session.Id, displayName, session.Status), statusCode: 201);
    }

  
    private async Task<string> GenerateUniqueDisplayNameAsync(
        Guid? groupId, Guid userId, string baseName, CancellationToken cancellationToken)
    {
        var query = _dbContext.RecordingSessions.AsNoTracking();
        query = groupId.HasValue
            ? query.Where(s => s.GroupId == groupId.Value)
            : query.Where(s => s.GroupId == null && s.UserId == userId);

        var existingNames = new HashSet<string>(
            await query.Where(s => s.DisplayName.StartsWith(baseName)).Select(s => s.DisplayName).ToListAsync(cancellationToken));

        if (!existingNames.Contains(baseName)) return baseName;

        var suffix = 1;
        while (existingNames.Contains($"{baseName} ({suffix})")) suffix++;
        return $"{baseName} ({suffix})";
    }

    public async Task<ApiResponse<bool>> AddGpsPointsAsync(
        Guid sessionId, Guid currentUserId, AddGpsPointsRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request.Points.Count == 0) return ApiResponse<bool>.Fail("لا يوجد نقاط GPS مُرسلة.", null, 400);

        var sessionExists = await EnsureSessionInScopeAsync(sessionId, currentUserId, cancellationToken);
        if (!sessionExists) return ApiResponse<bool>.Fail("التسجيل غير موجود.", null, 404);

        var points = request.Points.Select(p => new RecordingGpsPoint
        {
            SessionId = sessionId,
            Location = LocationLinkHelper.ToPoint(p.Latitude, p.Longitude),
            CapturedAt = p.CapturedAt,
            AccuracyMeters = p.AccuracyMeters,
            IsManual = p.IsManual
        });

        await _dbContext.RecordingGpsPoints.AddRangeAsync(points, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<bool>> CompleteAsync(
        Guid sessionId, Guid currentUserId, IFormFile audioFile, CancellationToken cancellationToken = default)
    {
        if (audioFile is null || audioFile.Length == 0) return ApiResponse<bool>.Fail("لازم ترفع الملف الصوتي.", null, 400);
        if (audioFile.Length > MaxAudioSizeBytes) return ApiResponse<bool>.Fail("حجم الملف الصوتي أكبر من الحد المسموح.", null, 400);

        var extension = Path.GetExtension(audioFile.FileName).ToLowerInvariant();
        if (!AllowedAudioExtensions.Contains(extension))
        {
            return ApiResponse<bool>.Fail($"صيغة صوت غير مدعومة. المسموح: {string.Join(", ", AllowedAudioExtensions)}", null, 400);
        }

        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) return ApiResponse<bool>.Fail("المستخدم غير موجود.");

        var session = await _dbContext.RecordingSessions
            .FirstOrDefaultAsync(ScopeFilter(userScope.GroupId, currentUserId, sessionId), cancellationToken);

        if (session is null) return ApiResponse<bool>.Fail("التسجيل غير موجود.", null, 404);

        await using var stream = audioFile.OpenReadStream();
        var storagePath = await _fileStorage.SaveAsync(stream, $"{sessionId}{extension}", "recordings", cancellationToken);

        session.AudioUrl = storagePath;
        session.Status = SessionStatus.Processing;
        await _unit.CompleteAsync();

        _backgroundJobClient.Enqueue<RecordingProcessingJob>(job => job.ProcessAsync(sessionId, CancellationToken.None));

        _logger.LogInformation("Audio uploaded for RecordingSession {SessionId}; queuing AI processing job", sessionId);
        return ApiResponse<bool>.SuccessResponse(true, "الصوت اتقبل وبيتعالج في الخلفية.", 202);
    }

    public async Task<ApiResponse<bool>> RetryAsync(Guid sessionId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) return ApiResponse<bool>.Fail("المستخدم غير موجود.");

        var session = await _dbContext.RecordingSessions
            .FirstOrDefaultAsync(ScopeFilter(userScope.GroupId, currentUserId, sessionId), cancellationToken);

        if (session is null) return ApiResponse<bool>.Fail("التسجيل غير موجود.", null, 404);
        if (session.AudioUrl is null) return ApiResponse<bool>.Fail("مفيش صوت مرفوع أصلاً لإعادة المعالجة.", null, 400);

       
        if (session.Status == SessionStatus.Processing)
        {
            return ApiResponse<bool>.Fail("التسجيل لسه بيتعالج دلوقتي.", null, 409);
        }

        await _dbContext.RecordedPlates.Where(p => p.SessionId == sessionId).ExecuteDeleteAsync(cancellationToken);

        session.Status = SessionStatus.Processing;
        session.ProcessingError = null;
        await _unit.CompleteAsync();

        _backgroundJobClient.Enqueue<RecordingProcessingJob>(job => job.ProcessAsync(sessionId, CancellationToken.None));

        _logger.LogInformation("RecordingSession {SessionId} re-queued for processing by user {UserId}", sessionId, currentUserId);
        return ApiResponse<bool>.SuccessResponse(true, "جاري إعادة المعالجة.");
    }


    public async Task<ApiResponse<bool>> UpdateAsync(
        Guid sessionId, Guid currentUserId, UpdateRecordingSessionRequestDto request, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) return ApiResponse<bool>.Fail("المستخدم غير موجود.");

        var session = await _dbContext.RecordingSessions
            .FirstOrDefaultAsync(ScopeFilter(userScope.GroupId, currentUserId, sessionId), cancellationToken);

        if (session is null) return ApiResponse<bool>.Fail("التسجيل غير موجود.", null, 404);

        if (session.Status == SessionStatus.Processing)
        {
            return ApiResponse<bool>.Fail("مينفعش تعدّل بيانات تسجيل لسه بيتعالج — استنى لحد ما يخلص الأول.", null, 409);
        }

        var districtChanged = request.District is not null && request.District != session.District;
        var streetChanged = request.Street is not null && request.Street != session.Street;

        if (!districtChanged && !streetChanged)
        {
            return ApiResponse<bool>.SuccessResponse(true, "مفيش تغيير.");
        }

        if (request.District is not null) session.District = request.District;
        if (request.Street is not null) session.Street = request.Street;

        await _dbContext.RecordedPlates
            .Where(p => p.SessionId == sessionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(p => p.District, session.District)
                .SetProperty(p => p.Street, session.Street),
                cancellationToken);

        await _unit.CompleteAsync();

        _logger.LogInformation(
            "RecordingSession {SessionId} district/street updated by user {UserId}", sessionId, currentUserId);

        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<List<RecordingSessionSummaryDto>>> GetAllAsync(
        Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) return ApiResponse<List<RecordingSessionSummaryDto>>.Fail("المستخدم غير موجود.");

        var query = _dbContext.RecordingSessions.AsNoTracking();
        query = userScope.GroupId.HasValue
            ? query.Where(s => s.GroupId == userScope.GroupId.Value)
            : query.Where(s => s.GroupId == null && s.UserId == currentUserId);

        var sessions = await query
            .OrderByDescending(s => s.StartedAt)
            .Select(s => new
            {
                s.Id,
                s.DisplayName,
                s.Status,
                s.TotalGpsPoints,
                s.TotalPlates,
                s.StartedAt,
                s.EndedAt,
                s.IsRecord,
                MatchedCount = s.RecordedPlates.Count(p => p.IsMatched)
            })
            .ToListAsync(cancellationToken);

        var items = sessions.Select(s => new RecordingSessionSummaryDto(
            s.Id, s.DisplayName, s.Status,
            s.EndedAt.HasValue ? (int)(s.EndedAt.Value - s.StartedAt).TotalSeconds : null,
            s.TotalGpsPoints, s.TotalPlates, s.MatchedCount, s.TotalPlates - s.MatchedCount,s.IsRecord, s.StartedAt))
            .ToList();

        return ApiResponse<List<RecordingSessionSummaryDto>>.SuccessResponse(items);
    }

    public async Task<ApiResponse<RecordingSessionDetailDto>> GetByIdAsync(
        Guid sessionId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) return ApiResponse<RecordingSessionDetailDto>.Fail("المستخدم غير موجود.");

        var session = await _dbContext.RecordingSessions
            .AsNoTracking()
            .Where(ScopeFilter(userScope.GroupId, currentUserId, sessionId))
            .Select(s => new RecordingSessionDetailDto(
                s.Id, s.DisplayName, s.District, s.Street, s.ExcelSheetName, s.TrackingMode, s.TrackingIntervalSeconds,
                s.Status, s.ProcessingError, s.TotalGpsPoints, s.TotalPlates, s.StartedAt, s.EndedAt, s.IsRecord, s.RecorderName))
            .FirstOrDefaultAsync(cancellationToken);

        return session is null
            ? ApiResponse<RecordingSessionDetailDto>.Fail("التسجيل غير موجود.", null, 404)
            : ApiResponse<RecordingSessionDetailDto>.SuccessResponse(session);
    }

    public Task<ApiResponse<PagedResult<RecordedPlateRowDto>>> GetPlatesAsync(
      Guid sessionId, Guid currentUserId, int page, int pageSize, CancellationToken cancellationToken = default)
      => GetPlatesInternalAsync(sessionId, currentUserId, page, pageSize, matchedOnly: false, cancellationToken);

    public Task<ApiResponse<PagedResult<RecordedPlateRowDto>>> GetMatchedPlatesAsync(
        Guid sessionId, Guid currentUserId, int page, int pageSize, CancellationToken cancellationToken = default)
        => GetPlatesInternalAsync(sessionId, currentUserId, page, pageSize, matchedOnly: true, cancellationToken);

    private async Task<ApiResponse<PagedResult<RecordedPlateRowDto>>> GetPlatesInternalAsync(
        Guid sessionId, Guid currentUserId, int page, int pageSize, bool matchedOnly, CancellationToken cancellationToken)
    {
        var inScope = await EnsureSessionInScopeAsync(sessionId, currentUserId, cancellationToken);
        if (!inScope) return ApiResponse<PagedResult<RecordedPlateRowDto>>.Fail("التسجيل غير موجود.", null, 404);

        var query = _dbContext.RecordedPlates.AsNoTracking().Where(p => p.SessionId == sessionId);
        if (matchedOnly) query = query.Where(p => p.IsMatched);

        var totalCount = await query.CountAsync(cancellationToken);
        var session = await _dbContext.RecordingSessions
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.RecorderName })
            .FirstOrDefaultAsync(cancellationToken);
        var rows = await query
            .OrderBy(p => p.SequenceNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new { p.Id, p.NormalizedPlate, p.Notes, p.VehicleType, p.Street, p.District, p.RecordedAt, p.Location, p.IsMatched, p.BankName, p.VehicleTypeInCheckFile })
            .ToListAsync(cancellationToken);

        var items = rows.Select(p => new RecordedPlateRowDto(
            p.Id, p.NormalizedPlate, p.Notes, p.VehicleType, p.Street, p.District, p.RecordedAt,
            LocationLinkHelper.ToGoogleMapsLink(p.Location), session.RecorderName, p.IsMatched, p.BankName, p.VehicleTypeInCheckFile))
            .ToList();

        var result = new PagedResult<RecordedPlateRowDto>
        {
            Items = items,
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
        return ApiResponse<PagedResult<RecordedPlateRowDto>>.SuccessResponse(result);
    }
    public async Task<(Stream Stream, string ContentType, string FileName)> GetAudioAsync(
        Guid sessionId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) throw new UnauthorizedAccessException("المستخدم غير موجود.");

        var session = await _dbContext.RecordingSessions
            .AsNoTracking()
            .Where(ScopeFilter(userScope.GroupId, currentUserId, sessionId))
            .Select(s => new { s.AudioUrl, s.DisplayName })
            .FirstOrDefaultAsync(cancellationToken);

        if (session?.AudioUrl is null) throw new KeyNotFoundException("مفيش صوت متاح للتسجيل ده.");

        var stream = await _fileStorage.OpenReadAsync(session.AudioUrl, cancellationToken);
        var extension = Path.GetExtension(session.AudioUrl);
        return (stream, "audio/aac", $"{session.DisplayName}{extension}");
    }

    public async Task<(byte[] FileBytes, string FileName)> ExportAsync(
        Guid sessionId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) throw new UnauthorizedAccessException("المستخدم غير موجود.");

        var session = await _dbContext.RecordingSessions
            .AsNoTracking()
            .Where(ScopeFilter(userScope.GroupId, currentUserId, sessionId))
            .Select(s => new { s.DisplayName, s.RecorderName,s.ExcelSheetName })
            .FirstOrDefaultAsync(cancellationToken);

        if (session is null) throw new KeyNotFoundException("التسجيل غير موجود.");

        var rows = await _dbContext.RecordedPlates
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId)
            .OrderBy(p => p.SequenceNumber)
            .Select(p => new { p.NormalizedPlate/*, p.IsMatched*/, p.Notes, p.VehicleType, p.Street, p.District, p.RecordedAt, p.Location })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("التسجيل");
        sheet.RightToLeft = true;

        sheet.Cell(1, 1).Value = "رقم اللوحة";
       // sheet.Cell(1, 2).Value = "الحالة";
        sheet.Cell(1, 2).Value = "ملاحظات";
        sheet.Cell(1, 3).Value = "نوع السيارة";
        sheet.Cell(1, 4).Value = "الشارع";
        sheet.Cell(1, 5).Value = "الحي";
        sheet.Cell(1, 6).Value = "التاريخ";
        sheet.Cell(1, 7).Value = "الموقع";
        sheet.Cell(1, 8).Value = "اسم المسجّل";

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.NormalizedPlate;
           // sheet.Cell(r, 2).Value = row.IsMatched ? "مطابقة" : "غير مطابقة";
            sheet.Cell(r, 2).Value = row.Notes;
            sheet.Cell(r, 3).Value = row.VehicleType;
            sheet.Cell(r, 4).Value = row.Street;
            sheet.Cell(r, 5).Value = row.District;
            sheet.Cell(r, 6).Value = row.RecordedAt;
            sheet.Cell(r, 7).Value = LocationLinkHelper.ToGoogleMapsLink(row.Location);
            sheet.Cell(r, 8).Value = session.RecorderName;
            r++;
        }
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), $"{session.ExcelSheetName}.xlsx");
    }

    public async Task<ApiResponse<AppendToDataFileResultDto>> AppendToDataFileAsync(
        Guid sessionId, Guid currentUserId, AppendToDataFileRequestDto request, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) return ApiResponse<AppendToDataFileResultDto>.Fail("المستخدم غير موجود.");

        var dataFile = await _dbContext.DataFiles
            .Where(f => f.Id == request.DataFileId)
            .FirstOrDefaultAsync(cancellationToken);

        if (dataFile is null) return ApiResponse<AppendToDataFileResultDto>.Fail("ملف الداتا التاريخية غير موجود.", null, 404);

        // إلحاق تسجيل على ملف داتا = append على الملف، فبياخد نفس قاعدة ملف الداتا
        if (!await _permissions.CanManageDataFileAsync(currentUserId, dataFile.Id, cancellationToken))
        {
            return ApiResponse<AppendToDataFileResultDto>.Fail("ليس لديك صلاحية على ملف الداتا ده.", null, StatusCodes.Status403Forbidden);
        }

        var session = await _dbContext.RecordingSessions
            .Where(ScopeFilter(userScope.GroupId, currentUserId, sessionId))
            .FirstOrDefaultAsync(cancellationToken);
        if (session is null) return ApiResponse<AppendToDataFileResultDto>.Fail("التسجيل غير موجود.", null, 404);
        var query = _dbContext.RecordedPlates.AsNoTracking().Where(p => p.SessionId == sessionId);
        query = request.AppendMode switch
        {
            RecordingAppendMode.MatchedOnly => query.Where(p => p.IsMatched),
            RecordingAppendMode.UnmatchedOnly => query.Where(p => !p.IsMatched),
            _ => query
        };

        var plates = await query
            .Select(p => new { p.RawPlate, p.NormalizedPlate, p.VehicleType, p.District, p.Street, p.Location, p.RecordedAt })
            .ToListAsync(cancellationToken);

        if (plates.Count == 0)
        {
            return ApiResponse<AppendToDataFileResultDto>.SuccessResponse(new AppendToDataFileResultDto(0), "مفيش لوحات مطابقة للشرط ده.");
        }

  
        var historicalPlates = plates.Select(p => new HistoricalPlate
        {
            DataFileId = dataFile.Id,
            UserId = currentUserId,
            RawPlate = p.RawPlate,
            NormalizedPlate = p.NormalizedPlate,
            VehicleType = p.VehicleType,
            District = p.District,
            Street = p.Street,
            Location = LocationLinkHelper.ToGoogleMapsLink(p.Location),
            RecordedAt = p.RecordedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            RecorderName=session.RecorderName
           
        });

        await _dbContext.HistoricalPlates.AddRangeAsync(historicalPlates, cancellationToken);

        await _dbContext.DataFiles
            .Where(f => f.Id == dataFile.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(f => f.RowCount, f => f.RowCount + plates.Count), cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Appended {Count} plates from RecordingSession {SessionId} into DataFile {DataFileId} (mode={Mode})",
            plates.Count, sessionId, dataFile.Id, request.AppendMode);

        return ApiResponse<AppendToDataFileResultDto>.SuccessResponse(new AppendToDataFileResultDto(plates.Count));
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid sessionId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) return ApiResponse<bool>.Fail("المستخدم غير موجود.");

        var session = await _dbContext.RecordingSessions
            .FirstOrDefaultAsync(ScopeFilter(userScope.GroupId, currentUserId, sessionId), cancellationToken);

        if (session is null) return ApiResponse<bool>.Fail("التسجيل غير موجود.", null, 404);

        if (session.Status == SessionStatus.Processing)
        {
            return ApiResponse<bool>.Fail("التسجيل لسه بيتعالج — استنى لحد ما يخلص قبل الحذف.", null, 409);
        }

        var audioUrl = session.AudioUrl;

        await _unit.Repository<RecordingSession>().RemoveAsync(session);
        await _unit.CompleteAsync();

        if (audioUrl is not null)
        {
            await _fileStorage.DeleteAsync(audioUrl, cancellationToken);
        }

        _logger.LogInformation("RecordingSession {SessionId} deleted by user {UserId}", sessionId, currentUserId);
        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<int>> DeleteAllAsync(Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) return ApiResponse<int>.Fail("المستخدم غير موجود.");

        var query = _dbContext.RecordingSessions.AsQueryable();
        query = userScope.GroupId.HasValue
            ? query.Where(s => s.GroupId == userScope.GroupId.Value)
            : query.Where(s => s.GroupId == null && s.UserId == currentUserId);

        var sessions = await query.Select(s => new { s.Id, s.AudioUrl }).ToListAsync(cancellationToken);

        if (sessions.Count == 0) return ApiResponse<int>.SuccessResponse(0, "مفيش تسجيلات أصلاً.");

        var ids = sessions.Select(s => s.Id).ToList();

         await _dbContext.RecordingSessions.Where(s => ids.Contains(s.Id)).ExecuteDeleteAsync(cancellationToken);

        foreach (var audioUrl in sessions.Where(s => s.AudioUrl is not null).Select(s => s.AudioUrl!))
        {
            await _fileStorage.DeleteAsync(audioUrl, cancellationToken);
        }

        _logger.LogInformation("Deleted all {Count} recording sessions for user {UserId}", sessions.Count, currentUserId);
        return ApiResponse<int>.SuccessResponse(sessions.Count, $"اتمسح {sessions.Count} تسجيل.");
    }

    private async Task<bool> EnsureSessionInScopeAsync(Guid sessionId, Guid currentUserId, CancellationToken cancellationToken)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) return false;

        return await _dbContext.RecordingSessions.AsNoTracking()
            .AnyAsync(ScopeFilter(userScope.GroupId, currentUserId, sessionId), cancellationToken);
    }

    private async Task<UserScopeInfo?> ResolveUserScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserScopeInfo(u.GroupId, u.FullName))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static System.Linq.Expressions.Expression<Func<RecordingSession, bool>> ScopeFilter(Guid? groupId, Guid currentUserId, Guid sessionId)
    {
        return groupId.HasValue
            ? s => s.Id == sessionId && s.GroupId == groupId.Value
            : s => s.Id == sessionId && s.GroupId == null && s.UserId == currentUserId;
    }

    public async Task<ApiResponse<ManualCheckResultDto>> CreateManualCheckAsync(
    Guid userId, CreateManualCheckRequestDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PlateNumber))
        {
            return ApiResponse<ManualCheckResultDto>.Fail("رقم اللوحة مطلوب.", null, 400);
        }

        var userScope = await ResolveUserScopeAsync(userId, cancellationToken);
        if (userScope is null) return ApiResponse<ManualCheckResultDto>.Fail("المستخدم غير موجود.");

        var activeCheckFileId = await _dbContext.CheckFiles
            .Where(f => f.IsActive)
            .Where(f => userScope.GroupId.HasValue
                ? f.GroupId == userScope.GroupId.Value
                : f.GroupId == null && f.UploadedBy == userId)
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var excelSheetName = string.IsNullOrWhiteSpace(request.ExcelSheetName) ? "تشييك يدوي" : request.ExcelSheetName.Trim();
        var displayName = await GenerateUniqueDisplayNameAsync(userScope.GroupId, userId, request.Street, cancellationToken);
        var now = DateTime.UtcNow;

     
        var session = new RecordingSession
        {
            UserId = userId,
            GroupId = userScope.GroupId,
            CheckFileId = activeCheckFileId,
            District = request.District,
            Street = request.Street,
            ExcelSheetName = excelSheetName,
            DisplayName = displayName,
            TrackingMode = TrackingMode.Manual,
            TotalGpsPoints = 1,
            TotalPlates = 1,
            Status = SessionStatus.Completed,
            RecorderName = request.RecorderName,
            IsRecord =false,
            StartedAt = now,
            EndedAt = now
        };

        await _unit.Repository<RecordingSession>().AddAsync(session);
        await _unit.CompleteAsync();

        var point = LocationLinkHelper.ToPoint(request.Latitude, request.Longitude);

        await _dbContext.RecordingGpsPoints.AddAsync(new RecordingGpsPoint
        {
            SessionId = session.Id,
            Location = point,
            CapturedAt = now,
            IsManual = true
        }, cancellationToken);

        var normalized = PlateNormalizer.Normalize(request.PlateNumber);

        long? matchedCheckPlateId = null;
        string? bankName = null;
        string? vehicleTypeInCheckFile = null;

        if (activeCheckFileId.HasValue)
        {
           var result = await _dbContext.CheckPlates
                .Where(cp => cp.CheckFileId == activeCheckFileId.Value && cp.NormalizedPlate == normalized)
                .Select(cp =>new{ Id = (long?)cp.Id, BankName = cp.BankName, VehicleType = cp.VehicleType })
                .FirstOrDefaultAsync(cancellationToken);
            
            if (result != null)
            {
                matchedCheckPlateId = result.Id;
                bankName = result.BankName;
                vehicleTypeInCheckFile = result.VehicleType;
            }
        }

        var recordedPlate = new RecordedPlate
        {
            SessionId = session.Id,
            UserId = userId,
            SequenceNumber = 1,
            RawPlate = request.PlateNumber.Trim(),
            NormalizedPlate = normalized,
            VehicleType = request.VehicleType,
            District = request.District,
            Street = request.Street,
            Location = point,
            AudioOffsetMs = null,
            IsMatched = matchedCheckPlateId.HasValue,
            MatchedCheckPlateId = matchedCheckPlateId,
            IsManualEntry = true,
            RecordedAt = now,
            BankName = bankName,
            VehicleTypeInCheckFile = vehicleTypeInCheckFile
        };

        await _dbContext.RecordedPlates.AddAsync(recordedPlate, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // التشييك اليدوي كمان بيتسجّل في الأرشيف الدائم، زي أي تفريغ عادي.
        var archiveEntry = new RecordingArchiveEntryInput(
            recordedPlate.RawPlate, recordedPlate.NormalizedPlate, recordedPlate.VehicleType,
            recordedPlate.District, recordedPlate.Street, recordedPlate.Notes,
            LocationLinkHelper.ToGoogleMapsLink(recordedPlate.Location), recordedPlate.RecordedAt, session.RecorderName, recordedPlate.IsMatched);

        await _archiveService.ReplaceSessionEntriesAsync(session.Id, userId, new List<RecordingArchiveEntryInput> { archiveEntry }, cancellationToken);

        _logger.LogInformation(
            "Manual check created: Session {SessionId}, Plate {Plate}, Matched={IsMatched}, by user {UserId}",
            session.Id, normalized, recordedPlate.IsMatched, userId);

        var plateDto = new RecordedPlateRowDto(
            recordedPlate.Id, recordedPlate.NormalizedPlate, recordedPlate.Notes, recordedPlate.VehicleType,
            recordedPlate.Street, recordedPlate.District, recordedPlate.RecordedAt,
            LocationLinkHelper.ToGoogleMapsLink(recordedPlate.Location), session.RecorderName, recordedPlate.IsMatched, bankName, vehicleTypeInCheckFile);

        return ApiResponse<ManualCheckResultDto>.SuccessResponse(
            new ManualCheckResultDto(session.Id, displayName, plateDto), statusCode: 201);
    }

    private record UserScopeInfo(Guid? GroupId, string? FullName);
}
