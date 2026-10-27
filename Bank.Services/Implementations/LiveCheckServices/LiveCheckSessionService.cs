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
    private readonly IFileStorageService _fileStorage;
    private readonly IPermissionService _permissions;

    public LiveCheckSessionService(
        ILogger<LiveCheckSessionService> logger, IUnitOfWork unit, AppDbContext dbContext,
        IFileStorageService fileStorage, IPermissionService permissions)
    {
        _logger = logger;
        _unit = unit;
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _permissions = permissions;
    }

    public async Task<(CheckSession Session, Guid? CheckFileId)> CreateSessionAsync(
        Guid userId, string? recorderName, string? district, string? street, string? excelSheetName, CancellationToken cancellationToken = default)
    {
        // اسم المسجل والحي والشارع اختياريين في التشييك — الفاضي بيتخزن null.
        recorderName = string.IsNullOrWhiteSpace(recorderName) ? null : recorderName.Trim();
        district = string.IsNullOrWhiteSpace(district) ? null : district.Trim();
        street = string.IsNullOrWhiteSpace(street) ? null : street.Trim();
        if (recorderName?.Length > 200) throw new ArgumentException("اسم المسجل طويل جدًا.", nameof(recorderName));
        if (district?.Length > 150) throw new ArgumentException("اسم الحي طويل جدًا.", nameof(district));
        if (street?.Length > 150) throw new ArgumentException("اسم الشارع طويل جدًا.", nameof(street));
        var userScope = await ResolveUserScopeAsync(userId, cancellationToken);
        if (userScope is null) throw new UnauthorizedAccessException("المستخدم غير موجود.");

        var activeCheckFileId = await _dbContext.CheckFiles
            .Where(f => f.IsActive)
            .Where(f => userScope.GroupId.HasValue
                ? f.GroupId == userScope.GroupId.Value
                : f.GroupId == null && f.UploadedBy == userId)
            .Select(f => (Guid?)f.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // اسم الجلسة من الشارع، ولو مش موجود من الحي، ولو الاتنين فاضيين "تشييك".
        var displayName = await GenerateUniqueDisplayNameAsync(
      userId, street ?? district ?? "تشييك", cancellationToken);

        var session = new CheckSession
        {
            UserId = userId,
            RecorderName = recorderName,
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

    //public async Task<LiveCheckPlateResultDto> ProcessDetectionAsync(
    //    Guid sessionId, Guid userId, Guid? groupId, Guid? checkFileId, string? district, string? street,
    //    double? latitude, double? longitude, AiPlateDetectedEvent detection, CheckType checkType,
    //    CancellationToken cancellationToken = default)
    //{
    //    var normalized = PlateNormalizer.Normalize(detection.PlateNumber);

    //    long? matchedCheckPlateId = null;

    //    if (checkFileId.HasValue)
    //    {
    //        matchedCheckPlateId = await _dbContext.CheckPlates
    //            .Where(cp => cp.CheckFileId == checkFileId.Value && cp.NormalizedPlate == normalized)
    //            .Select(cp => (long?)cp.Id)
    //            .FirstOrDefaultAsync(cancellationToken);

    //    }

    //    var locationLink = (latitude.HasValue && longitude.HasValue)
    //        ? LocationLinkHelper.ToGoogleMapsLink(latitude.Value, longitude.Value)
    //        : null;

    //    var log = new LiveCheckLog
    //    {
    //        CheckSessionId = sessionId,
    //        UserId = userId,
    //        GroupId = groupId,
    //        RawPlate = detection.PlateNumber,
    //        NormalizedPlate = normalized,
    //        CheckType = checkType,
    //        VehicleType = detection.VehicleType,
    //        District = district,
    //        Street = street,
    //        Location = locationLink,
    //        IsMatched = matchedCheckPlateId.HasValue,
    //        MatchedCheckPlateId = matchedCheckPlateId,
    //        CheckedAt = DateTime.UtcNow
    //    };

    //    await _dbContext.LiveCheckLogs.AddAsync(log, cancellationToken);

    //    await _dbContext.CheckSessions
    //        .Where(s => s.Id == sessionId)
    //        .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.TotalPlates, s => s.TotalPlates + 1), cancellationToken);

    //    await _dbContext.SaveChangesAsync(cancellationToken);

    //    return new LiveCheckPlateResultDto(log.Id, log.NormalizedPlate, log.VehicleType, log.IsMatched, log.Location, log.CheckedAt);
    //}

    public async Task<LiveCheckPlateResultDto> ProcessDetectionAsync(
    Guid sessionId, Guid userId, Guid? groupId, Guid? checkFileId, string? district, string? street,
    double? latitude, double? longitude, AiPlateDetectedEvent detection, CheckType checkType,
    CancellationToken cancellationToken = default)
    {
        var normalized = PlateNormalizer.Normalize(detection.PlateNumber);

        long? matchedCheckPlateId = null;
        string? bankName = null;
        string? vehicleTypeInCheckFile = null;

        if (checkFileId.HasValue)
        {
            var result = await _dbContext.CheckPlates
                .Where(cp => cp.CheckFileId == checkFileId.Value && cp.NormalizedPlate == normalized)
                .Select(cp => new
                {
                    Id = (long?)cp.Id,
                    BankName = cp.BankName,
                    VehicleType = cp.VehicleType
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (result != null)
            {
                matchedCheckPlateId = result.Id;
                bankName = result.BankName;
                vehicleTypeInCheckFile = result.VehicleType;
            }
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
            BankName = bankName,                             // 💡 إضافة اسم البنك
            VehicleTypeInCheckFile = vehicleTypeInCheckFile, // 💡 إضافة نوع السيارة في ملف التشييك
            CheckedAt = DateTime.UtcNow
        };

        await _dbContext.LiveCheckLogs.AddAsync(log, cancellationToken);

        await _dbContext.CheckSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.TotalPlates, s => s.TotalPlates + 1), cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new LiveCheckPlateResultDto(
            log.Id,
            log.NormalizedPlate,
            log.VehicleType,
            log.IsMatched,
            log.Location,
            log.CheckedAt,
            bankName,                // 💡 إرجاع اسم البنك
            vehicleTypeInCheckFile   // 💡 إرجاع نوع السيارة في ملف التشييك
        );
    }
    // الـ AI أحيانًا بيبعت "تصحيح" لآخر لوحة اتسمعت (من غير ما يحدد رقمها القديم —
    // زي لما يسمع صدى/تكرار ويحتاج يظبط آخر قراءة). بناخد آخر LiveCheckLog في نفس
    // الـ Session (بالـ Id، أضمن من CheckedAt لو فيه تساوي بالميلي ثانية) ونعدّله
    // في مكانه، ونعيد الـ Matching لأن رقم اللوحة اتغيّر.
    public async Task<LiveCheckPlateResultDto?> ProcessCorrectionAsync(
    Guid sessionId, Guid? checkFileId, string newPlateNumber, string? vehicleType, CancellationToken cancellationToken = default)
    {
        var lastLog = await _dbContext.LiveCheckLogs
            .Where(l => l.CheckSessionId == sessionId)
            .OrderByDescending(l => l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastLog is null)
        {
            _logger.LogWarning(
                "Plate correction received for CheckSession {SessionId} but no prior detection exists in this session — ignoring",
                sessionId);
            return null;
        }

        var normalized = PlateNormalizer.Normalize(newPlateNumber);

        long? matchedCheckPlateId = null;
        string? bankName = null;
        string? vehicleTypeInCheckFile = null;

        if (checkFileId.HasValue)
        {
            var result = await _dbContext.CheckPlates
                .Where(cp => cp.CheckFileId == checkFileId.Value && cp.NormalizedPlate == normalized)
                .Select(cp => new
                {
                    Id = (long?)cp.Id,
                    BankName = cp.BankName,
                    VehicleType = cp.VehicleType
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (result != null)
            {
                matchedCheckPlateId = result.Id;
                bankName = result.BankName;
                vehicleTypeInCheckFile = result.VehicleType;
            }
        }

        lastLog.RawPlate = newPlateNumber;
        lastLog.NormalizedPlate = normalized;
        if (!string.IsNullOrWhiteSpace(vehicleType)) lastLog.VehicleType = vehicleType;
        lastLog.IsMatched = matchedCheckPlateId.HasValue;
        lastLog.MatchedCheckPlateId = matchedCheckPlateId;
        lastLog.BankName = bankName;                             // 💡 تحديث اسم البنك الخاص باللوحة المصححة
        lastLog.VehicleTypeInCheckFile = vehicleTypeInCheckFile; // 💡 تحديث نوع السيارة الخاص باللوحة المصححة

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Plate correction applied: CheckSession {SessionId}, LiveCheckLog {LogId} corrected to {Plate}",
            sessionId, lastLog.Id, normalized);

        return new LiveCheckPlateResultDto(
            lastLog.Id,
            lastLog.NormalizedPlate,
            lastLog.VehicleType,
            lastLog.IsMatched,
            lastLog.Location,
            lastLog.CheckedAt,
            bankName,               // 💡 إرجاع اسم البنك الجديد
            vehicleTypeInCheckFile  // 💡 إرجاع نوع السيارة في ملف التشييك الجديد
        );
    }
    //public async Task<LiveCheckPlateResultDto?> ProcessCorrectionAsync(
    //    Guid sessionId, Guid? checkFileId, string newPlateNumber, string? vehicleType, CancellationToken cancellationToken = default)
    //{
    //    var lastLog = await _dbContext.LiveCheckLogs
    //        .Where(l => l.CheckSessionId == sessionId)
    //        .OrderByDescending(l => l.Id)
    //        .FirstOrDefaultAsync(cancellationToken);

    //    if (lastLog is null)
    //    {
    //        _logger.LogWarning(
    //            "Plate correction received for CheckSession {SessionId} but no prior detection exists in this session — ignoring",
    //            sessionId);
    //        return null;
    //    }

    //    var normalized = PlateNormalizer.Normalize(newPlateNumber);

    //    long? matchedCheckPlateId = null;
    //    if (checkFileId.HasValue)
    //    {
    //        matchedCheckPlateId = await _dbContext.CheckPlates
    //            .Where(cp => cp.CheckFileId == checkFileId.Value && cp.NormalizedPlate == normalized)
    //            .Select(cp => (long?)cp.Id)
    //            .FirstOrDefaultAsync(cancellationToken);
    //    }

    //    lastLog.RawPlate = newPlateNumber;
    //    lastLog.NormalizedPlate = normalized;
    //    if (!string.IsNullOrWhiteSpace(vehicleType)) lastLog.VehicleType = vehicleType;
    //    lastLog.IsMatched = matchedCheckPlateId.HasValue;
    //    lastLog.MatchedCheckPlateId = matchedCheckPlateId;

    //    await _dbContext.SaveChangesAsync(cancellationToken);

    //    _logger.LogInformation(
    //        "Plate correction applied: CheckSession {SessionId}, LiveCheckLog {LogId} corrected to {Plate}",
    //        sessionId, lastLog.Id, normalized);

    //    return new LiveCheckPlateResultDto(lastLog.Id, lastLog.NormalizedPlate, lastLog.VehicleType, lastLog.IsMatched, lastLog.Location, lastLog.CheckedAt);
    //}

    // الـ "استبدال" بيحدد رقم اللوحة القديم صراحة، فبندوّر على سجل بنفس اللوحة
    // دي **جوه نفس الـ Session بس** (مش أي جلسة تانية لنفس اليوزر) — ده تحقق
    // الأمان المطلوب: مينفعش الـ AI يعدّل سجل جلسة تانية خالص. لو ملقيناش سجل
    // مطابق، بنرفض الطلب ونسجّل تحذير بدل ما نضيف سجل جديد باسم غلط.
    public async Task<LiveCheckPlateResultDto?> ProcessReplacementAsync(
    Guid sessionId, Guid? checkFileId, string oldPlateNumber, string newPlateNumber, string? vehicleType,
    CancellationToken cancellationToken = default)
    {
        var oldNormalized = PlateNormalizer.Normalize(oldPlateNumber);

        var targetLog = await _dbContext.LiveCheckLogs
            .Where(l => l.CheckSessionId == sessionId && l.NormalizedPlate == oldNormalized)
            .OrderByDescending(l => l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (targetLog is null)
        {
            _logger.LogWarning(
                "Plate replacement received for CheckSession {SessionId}: old plate {OldPlate} not found in this session — ignoring",
                sessionId, oldNormalized);
            return null;
        }

        var newNormalized = PlateNormalizer.Normalize(newPlateNumber);

        long? matchedCheckPlateId = null;
        string? bankName = null;
        string? vehicleTypeInCheckFile = null;

        if (checkFileId.HasValue)
        {
            var result = await _dbContext.CheckPlates
                .Where(cp => cp.CheckFileId == checkFileId.Value && cp.NormalizedPlate == newNormalized)
                .Select(cp => new
                {
                    Id = (long?)cp.Id,
                    BankName = cp.BankName,
                    VehicleType = cp.VehicleType
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (result != null)
            {
                matchedCheckPlateId = result.Id;
                bankName = result.BankName;
                vehicleTypeInCheckFile = result.VehicleType;
            }
        }

        targetLog.RawPlate = newPlateNumber;
        targetLog.NormalizedPlate = newNormalized;
        if (!string.IsNullOrWhiteSpace(vehicleType)) targetLog.VehicleType = vehicleType;
        targetLog.IsMatched = matchedCheckPlateId.HasValue;
        targetLog.MatchedCheckPlateId = matchedCheckPlateId;
        targetLog.BankName = bankName;                             // 💡 تحديث اسم البنك للوحة الاستبدال
        targetLog.VehicleTypeInCheckFile = vehicleTypeInCheckFile; // 💡 تحديث نوع السيارة في ملف التشييك للوحة الاستبدال

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Plate replacement applied: CheckSession {SessionId}, LiveCheckLog {LogId} replaced {OldPlate} -> {NewPlate}",
            sessionId, targetLog.Id, oldNormalized, newNormalized);

        return new LiveCheckPlateResultDto(
            targetLog.Id,
            targetLog.NormalizedPlate,
            targetLog.VehicleType,
            targetLog.IsMatched,
            targetLog.Location,
            targetLog.CheckedAt,
            bankName,               // 💡 إرجاع اسم البنك الجديد
            vehicleTypeInCheckFile  // 💡 إرجاع نوع السيارة في ملف التشييك الجديد
        );
    }
    //public async Task<LiveCheckPlateResultDto?> ProcessReplacementAsync(
    //    Guid sessionId, Guid? checkFileId, string oldPlateNumber, string newPlateNumber, string? vehicleType,
    //    CancellationToken cancellationToken = default)
    //{
    //    var oldNormalized = PlateNormalizer.Normalize(oldPlateNumber);

    //    var targetLog = await _dbContext.LiveCheckLogs
    //        .Where(l => l.CheckSessionId == sessionId && l.NormalizedPlate == oldNormalized)
    //        .OrderByDescending(l => l.Id)
    //        .FirstOrDefaultAsync(cancellationToken);

    //    if (targetLog is null)
    //    {
    //        _logger.LogWarning(
    //            "Plate replacement received for CheckSession {SessionId}: old plate {OldPlate} not found in this session — ignoring",
    //            sessionId, oldNormalized);
    //        return null;
    //    }

    //    var newNormalized = PlateNormalizer.Normalize(newPlateNumber);

    //    long? matchedCheckPlateId = null;
    //    if (checkFileId.HasValue)
    //    {
    //        matchedCheckPlateId = await _dbContext.CheckPlates
    //            .Where(cp => cp.CheckFileId == checkFileId.Value && cp.NormalizedPlate == newNormalized)
    //            .Select(cp => (long?)cp.Id)
    //            .FirstOrDefaultAsync(cancellationToken);
    //    }

    //    targetLog.RawPlate = newPlateNumber;
    //    targetLog.NormalizedPlate = newNormalized;
    //    if (!string.IsNullOrWhiteSpace(vehicleType)) targetLog.VehicleType = vehicleType;
    //    targetLog.IsMatched = matchedCheckPlateId.HasValue;
    //    targetLog.MatchedCheckPlateId = matchedCheckPlateId;

    //    await _dbContext.SaveChangesAsync(cancellationToken);

    //    _logger.LogInformation(
    //        "Plate replacement applied: CheckSession {SessionId}, LiveCheckLog {LogId} replaced {OldPlate} -> {NewPlate}",
    //        sessionId, targetLog.Id, oldNormalized, newNormalized);

    //    return new LiveCheckPlateResultDto(targetLog.Id, targetLog.NormalizedPlate, targetLog.VehicleType, targetLog.IsMatched, targetLog.Location, targetLog.CheckedAt);
    //}

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
        var (session, checkFileId) = await CreateSessionAsync(userId, request.RecorderName, request.District, request.Street, request.ExcelSheetName, cancellationToken);

        // ⭐ بنقفلها فورًا كـ session كاملة — زي التشيك اليدوي في التسجيل بالظبط， standalone من غير streaming
        await _dbContext.CheckSessions
            .Where(s => s.Id == session.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Status, SessionStatus.Completed)
                .SetProperty(s => s.EndedAt, DateTime.UtcNow),
                cancellationToken);

        var userScope = await ResolveUserScopeAsync(userId, cancellationToken);

        var detection = new AiPlateDetectedEvent(request.PlateNumber.Trim(), request.VehicleType, 0);
        var plateResult = await ProcessDetectionAsync(
            session.Id, userId, userScope?.GroupId, checkFileId, session.District, session.Street,
            request.Latitude, request.Longitude, detection, CheckType.Manual, cancellationToken);

        _logger.LogInformation(
            "Manual check created: CheckSession {SessionId}, Plate {Plate}, Matched={IsMatched}, by user {UserId}",
            session.Id, plateResult.NormalizedPlate, plateResult.IsMatched, userId);

        return ApiResponse<ManualLiveCheckResultDto>.SuccessResponse(
            new ManualLiveCheckResultDto(session.Id, session.DisplayName, plateResult), statusCode: 201);
    }

    public async Task<ApiResponse<List<CheckSessionSummaryDto>>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var sessions = await _dbContext.CheckSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => new
            {
                s.Id,
                s.DisplayName,
                s.RecorderName,
                s.Status,
                s.TotalPlates,
                s.StartedAt,
                s.EndedAt,
                MatchedCount = s.Logs.Count(l => l.IsMatched)
            })
            .ToListAsync(cancellationToken);

        var items = sessions.Select(s => new CheckSessionSummaryDto(
            s.Id, s.DisplayName, s.RecorderName, s.Status, s.TotalPlates, s.MatchedCount, s.TotalPlates - s.MatchedCount, s.StartedAt, s.EndedAt))
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
                l.CheckSession.RecorderName, l.VehicleType, l.IsMatched))
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
    List<Guid> sessionIds, Guid userId, CancellationToken cancellationToken = default)
    {
        if (sessionIds is null || sessionIds.Count == 0)
        {
            throw new ArgumentException("قائمة الجلسات فارغة.", nameof(sessionIds));
        }

        // 💡 جلب اسم الـ ExcelSheetName لأحدث سيشن تم إنشاؤها من القائمة المحددة
        var lastExcelSheetName = await _dbContext.CheckSessions
            .AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id) && s.UserId == userId)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => s.ExcelSheetName)
            .FirstOrDefaultAsync(cancellationToken);

        // جلب جميع اللوحات المترابطة بالجلسات المحددة
        var rows = await _dbContext.LiveCheckLogs
            .AsNoTracking()
            .Where(l => sessionIds.Contains(l.CheckSessionId) && l.CheckSession.UserId == userId)
            .OrderBy(l => l.CheckedAt)
            .Select(l => new
            {
                l.NormalizedPlate,
                l.Location,
                l.CheckedAt,
                l.District,
                l.Street,
                RecorderName = l.CheckSession.RecorderName,
                l.VehicleType,
                l.IsMatched
            })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("التشييك");
        sheet.RightToLeft = true;

        sheet.Cell(1, 1).Value = "رقم اللوحة";
        sheet.Cell(1, 2).Value = "نوع السيارة";
        sheet.Cell(1, 3).Value = "GPS";
        sheet.Cell(1, 4).Value = "الشارع";
        sheet.Cell(1, 5).Value = "الحي";
        sheet.Cell(1, 6).Value = "تاريخ التسجيل";
        sheet.Cell(1, 7).Value = "اسم المسجّل";

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.NormalizedPlate;
            sheet.Cell(r, 2).Value = row.VehicleType;
            //sheet.Cell(r, 3).Value = row.Location;
            var locCell = sheet.Cell(r, 3);
            if (!string.IsNullOrWhiteSpace(row.Location)
                && Uri.TryCreate(row.Location, UriKind.Absolute, out var locUri))
            {
                locCell.Value = row.Location;                       // اللينك نفسه مكتوب
                locCell.SetHyperlink(new XLHyperlink(locUri));      // يتضغط عليه ويفتح الموقع
                locCell.Style.Font.FontColor = XLColor.Blue;
                locCell.Style.Font.Underline = XLFontUnderlineValues.Single;
            }
            else
            {
                locCell.Value = row.Location;                       // فاضي أو مش لينك صالح: يتكتب زي ما هو
            }
            sheet.Cell(r, 4).Value = row.Street;
            sheet.Cell(r, 5).Value = row.District;
            sheet.Cell(r, 6).Value = row.CheckedAt.ToSaudiLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            sheet.Cell(r, 7).Value = row.RecorderName;
            r++;
        }

        sheet.Columns().AdjustToContents();

        // 💡 الفحص: لو أحدث سيشن فيها ExcelSheetName مش فاضي نستخدمه، غير كده نستخدم الـ Default
        var fileName = !string.IsNullOrWhiteSpace(lastExcelSheetName)
            ? lastExcelSheetName.Trim()
            : $"التشييك-{DateTime.UtcNow.ToSaudiLocalTime():yyyy-MM-dd}";

        if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".xlsx";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), fileName);
    }
    public async Task<(byte[] FileBytes, string FileName)> ExportAsync(
        Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.CheckSessions
      .AsNoTracking()
      .Where(ScopeFilter(userId, sessionId))
      .Select(s => s.ExcelSheetName)
      .FirstOrDefaultAsync(cancellationToken);

        if (session is null) throw new KeyNotFoundException("التشييك غير موجود.");

        var rows = await _dbContext.LiveCheckLogs
            .AsNoTracking()
            .Where(l => l.CheckSessionId == sessionId)
            .OrderBy(l => l.CheckedAt)
            .Select(l => new
            {
                l.NormalizedPlate, l.Location, l.CheckedAt, l.District, l.Street,
                RecorderName = l.CheckSession.RecorderName, l.VehicleType, l.IsMatched
            })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("التشييك");
        sheet.RightToLeft = true;

        sheet.Cell(1, 1).Value = "رقم اللوحة";
        //sheet.Cell(1, 2).Value = "الحالة";
        sheet.Cell(1, 2).Value = "نوع السيارة";
        sheet.Cell(1, 3).Value = "GPS";
        sheet.Cell(1, 4).Value = "الشارع";
        sheet.Cell(1, 5).Value = "الحي";

        sheet.Cell(1, 6).Value = "تاريخ التسجيل";
        sheet.Cell(1, 7).Value = "اسم المسجّل";
       

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.NormalizedPlate;
            //   sheet.Cell(r, 2).Value = row.IsMatched ? "مطابقة" : "غير مطابقة";
            sheet.Cell(r, 2).Value = row.VehicleType;

            // sheet.Cell(r, 3).Value = row.Location;
            var locCell = sheet.Cell(r, 3);
            if (!string.IsNullOrWhiteSpace(row.Location)
                && Uri.TryCreate(row.Location, UriKind.Absolute, out var locUri))
            {
                locCell.Value = row.Location;                       // اللينك نفسه مكتوب
                locCell.SetHyperlink(new XLHyperlink(locUri));      // يتضغط عليه ويفتح الموقع
                locCell.Style.Font.FontColor = XLColor.Blue;
                locCell.Style.Font.Underline = XLFontUnderlineValues.Single;
            }
            else
            {
                locCell.Value = row.Location;                       // فاضي أو مش لينك صالح: يتكتب زي ما هو
            }
            sheet.Cell(r, 4).Value = row.Street;
            sheet.Cell(r, 5).Value = row.District;

            sheet.Cell(r, 6).Value = row.CheckedAt.ToSaudiLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            sheet.Cell(r, 7).Value = row.RecorderName;
            r++;
        }
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return (stream.ToArray(), $"{session ?? $"التشييك-{DateTime.UtcNow.ToSaudiLocalTime():yyyy-MM-dd}"}.xlsx");
    }

    public async Task<ApiResponse<bool>> DeleteAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.CheckSessions
     .FirstOrDefaultAsync(ScopeFilter(userId, sessionId), cancellationToken);

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
        var query = _dbContext.CheckSessions.Where(s => s.UserId == userId);

        var count = await query.CountAsync(cancellationToken);
        if (count == 0) return ApiResponse<int>.SuccessResponse(0, "مفيش جلسات تشييك أصلاً.");

        // Cascade بيشيل LiveCheckLogs تلقائيًا
        await query.ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation("Deleted all {Count} check sessions for user {UserId}", count, userId);
        return ApiResponse<int>.SuccessResponse(count, $"اتمسح {count} جلسة تشييك.");
    }
    // كل لوحات اللي يشوفها اليوزر (نفس نطاق GetAllAsync: جروب أو مستقل)
    private IQueryable<LiveCheckLog> GetScopedLogsQuery(Guid userId)
    {
        return _dbContext.LiveCheckLogs
            .AsNoTracking()
            .Where(l => l.CheckSession.UserId == userId);
    }

    public async Task<ApiResponse<PagedResult<LiveCheckLogRowDto>>> GetAllPlatesAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken = default)
    {

        var query = GetScopedLogsQuery(userId); 
        var totalCount = await query.CountAsync(cancellationToken);

        // الأحدث الأول، وبالـ Id كترتيب ثانوي عشان الـ paging يفضل ثابت
        var items = await query
            .OrderByDescending(l => l.CheckedAt)
            .ThenByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LiveCheckLogRowDto(
                l.Id, l.NormalizedPlate, l.Location, l.CheckedAt, l.District, l.Street,
                l.CheckSession.RecorderName, l.VehicleType, l.IsMatched))
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

    public async Task<(byte[] FileBytes, string FileName)> ExportAllAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var rows = await GetScopedLogsQuery(userId)      // في ExportAllAsync
            .OrderByDescending(l => l.CheckedAt)
                    .ThenByDescending(l => l.Id)
            .Select(l => new
            {
                l.NormalizedPlate,
                l.Location,
                l.CheckedAt,
                l.District,
                l.Street,
                RecorderName = l.CheckSession.RecorderName,
                l.VehicleType,
                l.IsMatched,
                SessionName = l.CheckSession.DisplayName
            })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("التشييك");
        sheet.RightToLeft = true;

        sheet.Cell(1, 1).Value = "رقم اللوحة";
        // sheet.Cell(1, 2).Value = "الحالة";
        sheet.Cell(1, 2).Value = "نوع السيارة";

        sheet.Cell(1, 3).Value = "GPS";
        sheet.Cell(1, 4).Value = "الشارع";
        sheet.Cell(1, 5).Value = "الحي";

        sheet.Cell(1, 6).Value = "تاريخ التسجيل";
        
        sheet.Cell(1, 7).Value = "اسم المسجّل";
      //  sheet.Cell(1, 8).Value = "الجلسة";

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.NormalizedPlate;
            // sheet.Cell(r, 2).Value = row.IsMatched ? "مطابقة" : "غير مطابقة";
            sheet.Cell(r, 2).Value = row.VehicleType;

            //  sheet.Cell(r, 3).Value = row.Location;
            var locCell = sheet.Cell(r, 3);
            if (!string.IsNullOrWhiteSpace(row.Location)
                && Uri.TryCreate(row.Location, UriKind.Absolute, out var locUri))
            {
                locCell.Value = row.Location;                       // اللينك نفسه مكتوب
                locCell.SetHyperlink(new XLHyperlink(locUri));      // يتضغط عليه ويفتح الموقع
                locCell.Style.Font.FontColor = XLColor.Blue;
                locCell.Style.Font.Underline = XLFontUnderlineValues.Single;
            }
            else
            {
                locCell.Value = row.Location;                       // فاضي أو مش لينك صالح: يتكتب زي ما هو
            }
            sheet.Cell(r, 4).Value = row.Street;
            sheet.Cell(r, 5).Value = row.District;

            sheet.Cell(r, 6).Value = row.CheckedAt.ToSaudiLocalTime().ToString("yyyy-MM-dd HH:mm:ss"); ;
            sheet.Cell(r, 7).Value = row.RecorderName;
           // sheet.Cell(r, 8).Value = row.SessionName;
            r++;
        }
        sheet.Columns().AdjustToContents();

        var lastExcelSheetName = await _dbContext.CheckSessions
                .AsNoTracking()
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.StartedAt)
                .Select(s => s.ExcelSheetName)
                .FirstOrDefaultAsync(cancellationToken);

        var baseFileName = !string.IsNullOrWhiteSpace(lastExcelSheetName)
            ? lastExcelSheetName.Trim() 
            : $"كل-التشييك-{DateTime.UtcNow.ToSaudiLocalTime():yyyy-MM-dd}";

        if (!baseFileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            baseFileName += ".xlsx";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return (stream.ToArray(), baseFileName);
    }
    public async Task<ApiResponse<ConvertCheckLogsToDataFileResponseDto>> ConvertToDataFileAsync(
        Guid userId, ConvertCheckLogsToDataFileRequestDto request, CancellationToken cancellationToken = default)
    {
        var hasExisting = request.ExistingDataFileId.HasValue;
        var hasNewName = !string.IsNullOrWhiteSpace(request.NewDataFileName);

        if (hasExisting == hasNewName)
        {
            return ApiResponse<ConvertCheckLogsToDataFileResponseDto>.Fail(
                "اختار إما ملف داتا موجود أو اسم لملف جديد، مش الاتنين ولا من غيرهم.", null, 400);
        }

        var userScope = await ResolveUserScopeAsync(userId, cancellationToken);
        if (userScope is null) return ApiResponse<ConvertCheckLogsToDataFileResponseDto>.Fail("المستخدم غير موجود.");

        var allowed = hasExisting
            ? await _permissions.CanManageDataFileAsync(userId, request.ExistingDataFileId!.Value, cancellationToken)
            : await _permissions.CanUploadDataFilesAsync(userId, cancellationToken);
        if (!allowed)
        {
            return ApiResponse<ConvertCheckLogsToDataFileResponseDto>.Fail("ليس لديك صلاحية على ملف الداتا ده.", null, 403);
        }

        var pendingRows = await _dbContext.LiveCheckLogs
            .Include(l => l.CheckSession)
            .Where(l => l.CheckSession.UserId == userId && l.AppendedToDataFileId == null)
            .OrderBy(l => l.CheckedAt)
            .ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);

        if (pendingRows.Count == 0)
        {
            return ApiResponse<ConvertCheckLogsToDataFileResponseDto>.Fail("مفيش صفوف جديدة في التشييك لسه ما اتحوّلتش.", null, 400);
        }

        Guid dataFileId;

        if (hasExisting)
        {
            // النطاق اتشيك فوق في CanManageDataFileAsync (بيشمل الجروب اللي الأدمن بيديره)،
            // فمنعيدش فلترته بـ userScope.GroupId وإلا الأدمن ميلاقيش ملف جروبه.
            var existingFile = await _dbContext.DataFiles
                .Where(f => f.Id == request.ExistingDataFileId!.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingFile is null)
            {
                return ApiResponse<ConvertCheckLogsToDataFileResponseDto>.Fail("ملف الداتا غير موجود.", null, 404);
            }

            dataFileId = existingFile.Id;

            await _dbContext.DataFiles
                .Where(f => f.Id == dataFileId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(f => f.RowCount, f => f.RowCount + pendingRows.Count), cancellationToken);
        }
        else
        {
            var fileName = request.NewDataFileName!.Trim();
            if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) fileName += ".xlsx";

            var fileBytes = BuildHistoricalWorkbook(pendingRows);
            var subFolder = userScope.GroupId.HasValue ? $"groups/{userScope.GroupId.Value}" : $"users/{userId}";

            await using var memoryStream = new MemoryStream(fileBytes);
            var storagePath = await _fileStorage.SaveAsync(memoryStream, fileName, subFolder, cancellationToken);

            var nextSortOrder = await GetNextDataFileSortOrderAsync(userScope.GroupId, userId, cancellationToken);

            var newDataFile = new DataFile
            {
                GroupId = userScope.GroupId,
                OwnerId = userId,
                FileName = fileName,
                RowCount = pendingRows.Count,
                IsActiveForFiltering = true,
                StoragePath = storagePath,
                ImportStatus = DataFileImportStatus.Completed,
                SortOrder = nextSortOrder
            };

            await _unit.Repository<DataFile>().AddAsync(newDataFile);
            await _unit.CompleteAsync();

            dataFileId = newDataFile.Id;
        }

        var historicalPlates = pendingRows.Select(l => new HistoricalPlate
        {
            DataFileId = dataFileId,
            UserId = l.UserId,
            RawPlate = l.RawPlate,
            NormalizedPlate = l.NormalizedPlate,
            VehicleType = l.VehicleType,
            District = l.District,
            Street = l.Street,
            RecorderName = l.CheckSession.RecorderName,
            Location = l.Location,
            RecordedAt = l.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss")
        });

        await _dbContext.HistoricalPlates.AddRangeAsync(historicalPlates, cancellationToken);

        var appendedAt = DateTime.UtcNow;
        foreach (var row in pendingRows)
        {
            row.AppendedToDataFileId = dataFileId;
            row.AppendedAt = appendedAt;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Converted {Count} live check rows into DataFile {DataFileId} for user {UserId}",
            pendingRows.Count, dataFileId, userId);

        return ApiResponse<ConvertCheckLogsToDataFileResponseDto>.SuccessResponse(
            new ConvertCheckLogsToDataFileResponseDto(dataFileId, pendingRows.Count));
    }

    // نفس أعمدة RecordingArchiveService.BuildHistoricalWorkbook عشان ملف الداتا يتقري زي أي ملف داتا تاني.
    private static byte[] BuildHistoricalWorkbook(List<LiveCheckLog> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("الداتا");
        sheet.RightToLeft = true;

        sheet.Cell(1, 1).Value = "رقم اللوحة";
        sheet.Cell(1, 2).Value = "ملاحظات";
        sheet.Cell(1, 3).Value = "نوع السيارة";
        sheet.Cell(1, 4).Value = "الشارع";
        sheet.Cell(1, 5).Value = "الحي";
        sheet.Cell(1, 6).Value = "GPS";
        sheet.Cell(1, 7).Value = "تاريخ التسجيل";
        sheet.Cell(1, 8).Value = "اسم المسجّل";

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.RawPlate;
            sheet.Cell(r, 3).Value = row.VehicleType;
            sheet.Cell(r, 4).Value = row.Street;
            sheet.Cell(r, 5).Value = row.District;
            // sheet.Cell(r, 6).Value = row.Location;
            var locCell = sheet.Cell(r, 6);
            if (!string.IsNullOrWhiteSpace(row.Location)
                && Uri.TryCreate(row.Location, UriKind.Absolute, out var locUri))
            {
                locCell.Value = row.Location;                       // اللينك نفسه مكتوب
                locCell.SetHyperlink(new XLHyperlink(locUri));      // يتضغط عليه ويفتح الموقع
                locCell.Style.Font.FontColor = XLColor.Blue;
                locCell.Style.Font.Underline = XLFontUnderlineValues.Single;
            }
            else
            {
                locCell.Value = row.Location;                       // فاضي أو مش لينك صالح: يتكتب زي ما هو
            }
            sheet.Cell(r, 7).Value = row.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss");
            sheet.Cell(r, 8).Value = row.CheckSession.RecorderName;
            r++;
        }
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private async Task<int> GetNextDataFileSortOrderAsync(Guid? groupId, Guid userId, CancellationToken cancellationToken)
    {
        var query = _dbContext.DataFiles.AsNoTracking();
        query = groupId.HasValue
            ? query.Where(f => f.GroupId == groupId.Value)
            : query.Where(f => f.GroupId == null && f.OwnerId == userId);

        var maxOrder = await query.Select(f => (int?)f.SortOrder).MaxAsync(cancellationToken);
        return (maxOrder ?? 0) + 1;
    }


    private async Task<string> GenerateUniqueDisplayNameAsync(
        Guid userId, string baseName, CancellationToken cancellationToken)
    {
        var existingNames = new HashSet<string>(
            await _dbContext.CheckSessions.AsNoTracking()
                .Where(s => s.UserId == userId && s.DisplayName.StartsWith(baseName))
                .Select(s => s.DisplayName)
                .ToListAsync(cancellationToken));

        if (!existingNames.Contains(baseName)) return baseName;

        var suffix = 1;
        while (existingNames.Contains($"{baseName} ({suffix})")) suffix++;
        return $"{baseName} ({suffix})";
    }

    private async Task<bool> EnsureSessionInScopeAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.CheckSessions.AsNoTracking()
            .AnyAsync(ScopeFilter(userId, sessionId), cancellationToken);
    }
    private async Task<UserScopeInfo?> ResolveUserScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .WithEffectiveGroup()
            .Where(x => x.User.Id == userId)
            .Select(x => new UserScopeInfo(x.GroupId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static System.Linq.Expressions.Expression<Func<CheckSession, bool>> ScopeFilter(Guid userId, Guid sessionId)
    {
        return s => s.Id == sessionId && s.UserId == userId;
    }

    private record UserScopeInfo(Guid? GroupId);
}
