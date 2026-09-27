using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Repo.Interfaces;
using Bank.Services.HangFireJobs;
using Bank.Services.Helpers;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.LiveCheckDTOs;
using Bank.Shared.Enums;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bank.Services.Implementations;

public class LiveCheckSessionService : ILiveCheckSessionService
{
    private readonly ILogger<LiveCheckSessionService> _logger;
    private readonly IUnitOfWork _unit;
    private readonly AppDbContext _dbContext;

    public LiveCheckSessionService(ILogger<LiveCheckSessionService> logger, IUnitOfWork unit, AppDbContext dbContext)
    {
        _logger = logger;
        _unit = unit;
        _dbContext = dbContext;
    }

    public async Task<(CheckSession Session, Guid? CheckFileId)> CreateSessionAsync(
        Guid userId, string district, string street, string? excelSheetName, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(userId, cancellationToken);
        if (userScope is null) throw new UnauthorizedAccessException("المستخدم غير موجود.");

        var activeCheckFileId = await _dbContext.CheckFiles
            .Where(f => f.IsActive)
            .Where(f => userScope.GroupId.HasValue
                ? f.GroupId == userScope.GroupId.Value
                : f.GroupId == null && f.UploadedBy == userId)
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var displayName = await GenerateUniqueDisplayNameAsync(userScope.GroupId, userId, street, cancellationToken);

        var session = new CheckSession
        {
            UserId = userId,
            GroupId = userScope.GroupId,
            CheckFileId = activeCheckFileId,
            District = district,
            Street = street,
            ExcelSheetName = string.IsNullOrWhiteSpace(excelSheetName) ? displayName : excelSheetName!.Trim(),
            DisplayName = displayName,
            Status = SessionStatus.Recording,
            StartedAt = DateTime.UtcNow
        };

        await _unit.Repository<CheckSession>().AddAsync(session);
        await _unit.CompleteAsync();

        _logger.LogInformation(
            "CheckSession {SessionId} ({DisplayName}) started by user {UserId}, snapshot CheckFileId={CheckFileId}",
            session.Id, displayName, userId, activeCheckFileId);

        return (session, activeCheckFileId);
    }

    public async Task<LiveCheckPlateResultDto> ProcessDetectionAsync(
        Guid sessionId, Guid userId, Guid? groupId, Guid? checkFileId, string district, string street,
        double? latitude, double? longitude, AiPlateDetectedEvent detection, CheckType checkType,
        CancellationToken cancellationToken = default)
    {
        var normalized = PlateNormalizer.Normalize(detection.PlateNumber);

        long? matchedCheckPlateId = null;
        if (checkFileId.HasValue)
        {
            matchedCheckPlateId = await _dbContext.CheckPlates
                .Where(cp => cp.CheckFileId == checkFileId.Value && cp.NormalizedPlate == normalized)
                .Select(cp => (long?)cp.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var locationLink = (latitude.HasValue && longitude.HasValue)
            ? LocationLinkHelper.ToGoogleMapsLink(latitude.Value, longitude.Value)
            : null;

        var log = new LiveCheckLog
        {
            CheckSessionId = sessionId,
            UserId = userId,
            GroupId = groupId,
            RawPlate = detection.PlateNumber,
            NormalizedPlate = normalized,
            CheckType = checkType,
            VehicleType = detection.VehicleType,
            District = district,
            Street = street,
            Location = locationLink,
            IsMatched = matchedCheckPlateId.HasValue,
            MatchedCheckPlateId = matchedCheckPlateId,
            CheckedAt = DateTime.UtcNow
        };

        await _dbContext.LiveCheckLogs.AddAsync(log, cancellationToken);

        await _dbContext.CheckSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.TotalPlates, s => s.TotalPlates + 1), cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new LiveCheckPlateResultDto(log.Id, log.NormalizedPlate, log.VehicleType, log.IsMatched, log.Location, log.CheckedAt);
    }

    public async Task CompleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        await _dbContext.CheckSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Status, SessionStatus.Completed)
                .SetProperty(s => s.EndedAt, DateTime.UtcNow),
                cancellationToken);
    }

    public async Task MarkSessionFailedAsync(Guid sessionId, string error, CancellationToken cancellationToken = default)
    {
        await _dbContext.CheckSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Status, SessionStatus.Failed)
                .SetProperty(s => s.ProcessingError, error)
                .SetProperty(s => s.EndedAt, DateTime.UtcNow),
                cancellationToken);
    }

    public async Task<ApiResponse<ManualLiveCheckResultDto>> CreateManualCheckAsync(
        Guid userId, CreateManualLiveCheckRequestDto request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PlateNumber))
        {
            return ApiResponse<ManualLiveCheckResultDto>.Fail("رقم اللوحة مطلوب.", null, 400);
        }

        var (session, checkFileId) = await CreateSessionAsync(userId, request.District, request.Street, request.ExcelSheetName, cancellationToken);

        // ⭐ بنقفلها فورًا كـ session كاملة — زي التشيك اليدوي في التسجيل بالظبط، standalone من غير streaming
        await _dbContext.CheckSessions
            .Where(s => s.Id == session.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Status, SessionStatus.Completed)
                .SetProperty(s => s.EndedAt, DateTime.UtcNow),
                cancellationToken);

        var userScope = await ResolveUserScopeAsync(userId, cancellationToken);

        var detection = new AiPlateDetectedEvent(request.PlateNumber.Trim(), request.VehicleType, 0);
        var plateResult = await ProcessDetectionAsync(
            session.Id, userId, userScope?.GroupId, checkFileId, request.District, request.Street,
            request.Latitude, request.Longitude, detection, CheckType.Manual, cancellationToken);

        _logger.LogInformation(
            "Manual check created: CheckSession {SessionId}, Plate {Plate}, Matched={IsMatched}, by user {UserId}",
            session.Id, plateResult.NormalizedPlate, plateResult.IsMatched, userId);

        return ApiResponse<ManualLiveCheckResultDto>.SuccessResponse(
            new ManualLiveCheckResultDto(session.Id, session.DisplayName, plateResult), statusCode: 201);
    }

    public async Task<ApiResponse<List<CheckSessionSummaryDto>>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(userId, cancellationToken);
        if (userScope is null) return ApiResponse<List<CheckSessionSummaryDto>>.Fail("المستخدم غير موجود.");

        var query = _dbContext.CheckSessions.AsNoTracking();
        query = userScope.GroupId.HasValue
            ? query.Where(s => s.GroupId == userScope.GroupId.Value)
            : query.Where(s => s.GroupId == null && s.UserId == userId);

        var sessions = await query
            .OrderByDescending(s => s.StartedAt)
            .Select(s => new
            {
                s.Id,
                s.DisplayName,
                s.Status,
                s.TotalPlates,
                s.StartedAt,
                s.EndedAt,
                MatchedCount = s.Logs.Count(l => l.IsMatched)
            })
            .ToListAsync(cancellationToken);

        var items = sessions.Select(s => new CheckSessionSummaryDto(
            s.Id, s.DisplayName, s.Status, s.TotalPlates, s.MatchedCount, s.TotalPlates - s.MatchedCount, s.StartedAt, s.EndedAt))
            .ToList();

        return ApiResponse<List<CheckSessionSummaryDto>>.SuccessResponse(items);
    }

    public async Task<ApiResponse<PagedResult<LiveCheckLogRowDto>>> GetPlatesAsync(
        Guid sessionId, Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var inScope = await EnsureSessionInScopeAsync(sessionId, userId, cancellationToken);
        if (!inScope) return ApiResponse<PagedResult<LiveCheckLogRowDto>>.Fail("التشييك غير موجود.", null, 404);

        var query = _dbContext.LiveCheckLogs.AsNoTracking().Where(l => l.CheckSessionId == sessionId);
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(l => l.CheckedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LiveCheckLogRowDto(
                l.Id, l.NormalizedPlate, l.Location, l.CheckedAt, l.District, l.Street,
                l.User.FullName, l.VehicleType, l.IsMatched))
            .ToListAsync(cancellationToken);
        var result = new PagedResult<LiveCheckLogRowDto>
        {
            Items = items,
            PageSize = pageSize,
            PageNumber = page,
            TotalCount = totalCount
        };
        return ApiResponse<PagedResult<LiveCheckLogRowDto>>.SuccessResponse(result);
    }

    public async Task<(byte[] FileBytes, string FileName)> ExportAsync(
        Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(userId, cancellationToken);
        if (userScope is null) throw new UnauthorizedAccessException("المستخدم غير موجود.");

        var session = await _dbContext.CheckSessions
            .AsNoTracking()
            .Where(ScopeFilter(userScope.GroupId, userId, sessionId))
            .Select(s => s.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);

        if (session is null) throw new KeyNotFoundException("التشييك غير موجود.");

        var rows = await _dbContext.LiveCheckLogs
            .AsNoTracking()
            .Where(l => l.CheckSessionId == sessionId)
            .OrderBy(l => l.CheckedAt)
            .Select(l => new
            {
                l.NormalizedPlate, l.Location, l.CheckedAt, l.District, l.Street,
                RecorderName = l.User.FullName, l.VehicleType, l.IsMatched
            })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("التشييك");
        sheet.RightToLeft = true;

        sheet.Cell(1, 1).Value = "رقم اللوحة";
        sheet.Cell(1, 2).Value = "الحالة";
        sheet.Cell(1, 3).Value = "الموقع";
        sheet.Cell(1, 4).Value = "تاريخ التسجيل";
        sheet.Cell(1, 5).Value = "الحي";
        sheet.Cell(1, 6).Value = "الشارع";
        sheet.Cell(1, 7).Value = "اسم المسجل";
        sheet.Cell(1, 8).Value = "نوع العربية";

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.NormalizedPlate;
            sheet.Cell(r, 2).Value = row.IsMatched ? "مطابقة" : "غير مطابقة";
            sheet.Cell(r, 3).Value = row.Location;
            sheet.Cell(r, 4).Value = row.CheckedAt;
            sheet.Cell(r, 5).Value = row.District;
            sheet.Cell(r, 6).Value = row.Street;
            sheet.Cell(r, 7).Value = row.RecorderName;
            sheet.Cell(r, 8).Value = row.VehicleType;
            r++;
        }
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), $"{session}.xlsx");
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(userId, cancellationToken);
        if (userScope is null) return ApiResponse<bool>.Fail("المستخدم غير موجود.");

        var session = await _dbContext.CheckSessions
            .FirstOrDefaultAsync(ScopeFilter(userScope.GroupId, userId, sessionId), cancellationToken);

        if (session is null) return ApiResponse<bool>.Fail("التشييك غير موجود.", null, 404);

        if (session.Status == SessionStatus.Recording)
        {
            return ApiResponse<bool>.Fail("فيه جلسة تشييك شغالة دلوقتي — أوقفها الأول قبل الحذف.", null, 409);
        }

       await _unit.Repository<CheckSession>().RemoveAsync(session);
        await _unit.CompleteAsync();

        _logger.LogInformation("CheckSession {SessionId} deleted by user {UserId}", sessionId, userId);
        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<int>> DeleteAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(userId, cancellationToken);
        if (userScope is null) return ApiResponse<int>.Fail("المستخدم غير موجود.");

        var query = _dbContext.CheckSessions.AsQueryable();
        query = userScope.GroupId.HasValue
            ? query.Where(s => s.GroupId == userScope.GroupId.Value)
            : query.Where(s => s.GroupId == null && s.UserId == userId);

        var count = await query.CountAsync(cancellationToken);
        if (count == 0) return ApiResponse<int>.SuccessResponse(0, "مفيش جلسات تشييك أصلاً.");

        // Cascade بيشيل LiveCheckLogs تلقائيًا
        await query.ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation("Deleted all {Count} check sessions for user {UserId}", count, userId);
        return ApiResponse<int>.SuccessResponse(count, $"اتمسح {count} جلسة تشييك.");
    }

    private async Task<string> GenerateUniqueDisplayNameAsync(
        Guid? groupId, Guid userId, string baseName, CancellationToken cancellationToken)
    {
        var query = _dbContext.CheckSessions.AsNoTracking();
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

    private async Task<bool> EnsureSessionInScopeAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken)
    {
        var userScope = await ResolveUserScopeAsync(userId, cancellationToken);
        if (userScope is null) return false;

        return await _dbContext.CheckSessions.AsNoTracking()
            .AnyAsync(ScopeFilter(userScope.GroupId, userId, sessionId), cancellationToken);
    }

    private async Task<UserScopeInfo?> ResolveUserScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserScopeInfo(u.GroupId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static System.Linq.Expressions.Expression<Func<CheckSession, bool>> ScopeFilter(Guid? groupId, Guid userId, Guid sessionId)
    {
        return groupId.HasValue
            ? s => s.Id == sessionId && s.GroupId == groupId.Value
            : s => s.Id == sessionId && s.GroupId == null && s.UserId == userId;
    }

    private record UserScopeInfo(Guid? GroupId);
}
