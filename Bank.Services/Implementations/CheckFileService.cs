using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Repo.Interfaces;
using Bank.Services.HangFireJobs;
using Bank.Services.Helpers;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.CheckFileDTos;
using Bank.Shared.Enums;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using ExcelDataReader;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace Bank.Services.Implementations;

public class CheckFileService : ICheckFileService
{
    private const long MaxUploadSizeBytes = 50L * 1024 * 1024;
    private static readonly string[] AllowedExtensions = { ".xlsx", ".xlsm", ".xltx", ".xltm", ".xlsb", ".xls", ".csv" };

    private const int PlateColumnIndex = 0;
    private const int VehicleTypeColumnIndex = 1;
    private const int BankNameColumnIndex = 2;
    private const int ChassisNumberColumnIndex = 3;

    private readonly ILogger<CheckFileService> _logger;
    private readonly IUnitOfWork _unit;
    private readonly AppDbContext _dbContext;
    private readonly ICheckPlateCacheService _cache;
    private readonly IConfiguration _configuration;
    private readonly IPermissionService _permissions;

    private const string NoCheckFilePermissionMessage = "ليس لديك صلاحية إضافة أو حذف ملفات التشييك.";

    public CheckFileService(
        ILogger<CheckFileService> logger, IUnitOfWork unit, AppDbContext dbContext, ICheckPlateCacheService cache
        , IConfiguration configuration, IPermissionService permissions)
    {
        _logger = logger;
        _unit = unit;
        _dbContext = dbContext;
        _cache = cache;
        _configuration = configuration;
        _permissions = permissions;
    }
    public async Task<(byte[] FileBytes, string FileName)> ExportAsync(
    Guid checkFileId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope is null) throw new UnauthorizedAccessException("المستخدم غير موجود.");

        var checkFile = await _dbContext.CheckFiles
            .AsNoTracking()
            .Where(ScopeFilter(userScope.GroupId, currentUserId, checkFileId))
            .Select(f => new { f.FileName })
            .FirstOrDefaultAsync(cancellationToken);

        if (checkFile is null) throw new KeyNotFoundException("الملف غير موجود.");

        var rows = await _dbContext.CheckPlates
            .AsNoTracking()
            .Where(p => p.CheckFileId == checkFileId)
            .OrderBy(p => p.Id)
            .Select(p => new { p.RawPlate, p.VehicleType, p.BankName, p.ChassisNumber })
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("ملف التشييك");
        sheet.RightToLeft = true;

        // نفس ترتيب أعمدة الرفع (لوحة، نوع، بنك، شاصي)
        sheet.Cell(1, 1).Value = "اللوحه";
        sheet.Cell(1, 2).Value = "النوع";
        sheet.Cell(1, 3).Value = "البنك";
        sheet.Cell(1, 4).Value = "الشاص";

        // تنسيق الهيدر (الصف الأول)
        var headerRow = sheet.Row(1);
        headerRow.Style.Font.Bold = true;
        headerRow.Style.Font.FontSize = 14;
        headerRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRow.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        // نص صريح عشان الشاصي واللوحات الرقمية ما يتحولوش لأرقام
        sheet.Column(1).Style.NumberFormat.Format = "@";
        sheet.Column(4).Style.NumberFormat.Format = "@";

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.RawPlate;
            sheet.Cell(r, 2).Value = row.VehicleType;
            sheet.Cell(r, 3).Value = row.BankName;
            sheet.Cell(r, 4).Value = row.ChassisNumber;
            r++;
        }

        var totalRows = r - 1;

        if (totalRows >= 2)
        {
            // 💡 تطبيق الألوان المحددة من الصورة على الخلايا المطلوبة (من الصف الثاني وحتى نهاية البيانات)
            sheet.Range(2, 1, totalRows, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#00B050"); // الأخضر
            sheet.Range(2, 2, totalRows, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#5B9BD5"); // الأزرق
            sheet.Range(2, 3, totalRows, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4B084"); // البرتقالي
            sheet.Range(2, 4, totalRows, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFC000"); // الأصفر/الذهبي

            // تنسيق الخط والسنترة لكل خلايا البيانات
            var dataRange = sheet.Range(2, 1, totalRows, 4);
            dataRange.Style.Font.FontColor = XLColor.Black;
            dataRange.Style.Font.FontSize = 13;
            dataRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            dataRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // إضافة حدود خفيفة (Borders) مطابقة لمظهر الإكسيل في الصورة
            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.OutsideBorderColor = XLColor.Black;
            dataRange.Style.Border.InsideBorderColor = XLColor.Black;
        }

        sheet.Column(1).Width = 20; 
        sheet.Column(2).Width = 22; 
        sheet.Column(3).Width = 22; 
        sheet.Column(4).Width = 32; 

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var baseName = Path.GetFileNameWithoutExtension(checkFile.FileName);
        return (stream.ToArray(), $"{baseName}.xlsx");
    }
    //public async Task<(byte[] FileBytes, string FileName)> ExportAsync(
    //Guid checkFileId, Guid currentUserId, CancellationToken cancellationToken = default)
    //{
    //    var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
    //    if (userScope is null) throw new UnauthorizedAccessException("المستخدم غير موجود.");

    //    var checkFile = await _dbContext.CheckFiles
    //        .AsNoTracking()
    //        .Where(ScopeFilter(userScope.GroupId, currentUserId, checkFileId))
    //        .Select(f => new { f.FileName })
    //        .FirstOrDefaultAsync(cancellationToken);

    //    if (checkFile is null) throw new KeyNotFoundException("الملف غير موجود.");

    //    var rows = await _dbContext.CheckPlates
    //        .AsNoTracking()
    //        .Where(p => p.CheckFileId == checkFileId)
    //        .OrderBy(p => p.Id)
    //        .Select(p => new { p.RawPlate, p.VehicleType, p.BankName, p.ChassisNumber })
    //        .ToListAsync(cancellationToken);

    //    using var workbook = new XLWorkbook();
    //    var sheet = workbook.Worksheets.Add("ملف التشييك");
    //    sheet.RightToLeft = true;

    //    // نفس ترتيب أعمدة الرفع (لوحة، نوع، بنك، شاصي) عشان الملف المصدّر يترفع تاني زي ما هو
    //    sheet.Cell(1, 1).Value = "اللوحه";
    //    sheet.Cell(1, 2).Value = "النوع";
    //    sheet.Cell(1, 3).Value = "البنك";
    //    sheet.Cell(1, 4).Value = "الشاص";
    //    sheet.Row(1).Style.Font.Bold = true;

    //    // نص صريح عشان الشاصي واللوحات الرقمية ما يتحولوش لأرقام
    //    sheet.Column(1).Style.NumberFormat.Format = "@";
    //    sheet.Column(4).Style.NumberFormat.Format = "@";

    //    var r = 2;
    //    foreach (var row in rows)
    //    {
    //        sheet.Cell(r, 1).Value = row.RawPlate;
    //        sheet.Cell(r, 2).Value = row.VehicleType;
    //        sheet.Cell(r, 3).Value = row.BankName;
    //        sheet.Cell(r, 4).Value = row.ChassisNumber;
    //        r++;
    //    }
    //    sheet.Columns().AdjustToContents();

    //    using var stream = new MemoryStream();
    //    workbook.SaveAs(stream);

    //    var baseName = Path.GetFileNameWithoutExtension(checkFile.FileName);
    //    return (stream.ToArray(), $"{baseName}.xlsx");
    //}
    public async Task<ApiResponse<List<CheckFileSummaryDto>>> GetCheckFilesAsync(
        Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null) return ApiResponse<List<CheckFileSummaryDto>>.Fail("المستخدم غير موجود.");

        var query = _dbContext.CheckFiles.AsNoTracking();
        query = userScope.GroupId.HasValue
            ? query.Where(f => f.GroupId == userScope.GroupId.Value)
            : query.Where(f => f.GroupId == null && f.UploadedBy == currentUserId);

        var files = await query
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new CheckFileSummaryDto(f.Id, f.FileName, f.TotalPlates, f.IsActive, f.Uploader.FullName, f.CreatedAt))
            .ToListAsync(cancellationToken);

        return ApiResponse<List<CheckFileSummaryDto>>.SuccessResponse(files);
    }

    public async Task<ApiResponse<CheckFileSummaryDto>> UploadAsync(
        Guid uploaderId, CreateCheckFileRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanManageCheckFilesAsync(uploaderId, cancellationToken))
        {
            return ApiResponse<CheckFileSummaryDto>.Fail(NoCheckFilePermissionMessage, null, 403);
        }

        var timer = System.Diagnostics.Stopwatch.StartNew();
        var validationError = ValidateUploadedFile(request.File);
        if (validationError is not null)
        {
            _logger.LogWarning("Rejected check file upload: {Reason}", validationError);
            return ApiResponse<CheckFileSummaryDto>.Fail(validationError, null, 400);
        }

        var userScope = await ResolveUserScopeAsync(uploaderId, cancellationToken);
        if (userScope == null) return ApiResponse<CheckFileSummaryDto>.Fail("المستخدم غير موجود.");

        var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        List<List<string?>> allRows;
        await using (var stream = request.File.OpenReadStream())
        {
            allRows = ReadAllRowsAsStrings(stream, extension).ToList();
        }

        if (allRows.Count < 2)
        {
            return ApiResponse<CheckFileSummaryDto>.Fail("الملف فارغ أو فيه صف العناوين بس.", null, 400);
        }

        var dataRows = allRows.Skip(1).ToList();

        await DeactivateExistingActiveFileAsync(userScope.GroupId, uploaderId, cancellationToken);

        var checkFile = new CheckFile
        {
            GroupId = userScope.GroupId,
            UploadedBy = uploaderId,
            FileName = request.File.FileName,
            TotalPlates = 0,
            IsActive = true
        };

        await _unit.Repository<CheckFile>().AddAsync(checkFile);
        await _unit.CompleteAsync();

        var insertedCount = await BulkInsertCheckPlatesAsync(_dbContext, checkFile.Id, dataRows, cancellationToken);

        checkFile.TotalPlates = insertedCount;
        await _unit.CompleteAsync();

        _cache.InvalidateActiveCheckFileCache(userScope.GroupId, uploaderId);

        _logger.LogInformation(
            "CheckFile {CheckFileId} uploaded by user {UserId} ({Count} plates across multiple banks)",
            checkFile.Id, uploaderId, insertedCount);
        timer.Stop();
        _logger.LogInformation("CheckFile upload processing time: {ElapsedMilliseconds} ms", timer.ElapsedMilliseconds);

        var dto = new CheckFileSummaryDto(checkFile.Id, checkFile.FileName, insertedCount, true, "أنت", checkFile.CreatedAt);
        return ApiResponse<CheckFileSummaryDto>.SuccessResponse(dto, "اتحمّل ملف التشيك بنجاح.", 201);
    }

    public async Task<ApiResponse<bool>> ToggleActiveAsync(
        Guid checkFileId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null) return ApiResponse<bool>.Fail("المستخدم غير موجود.");

        var checkFile = await _dbContext.CheckFiles
            .FirstOrDefaultAsync(ScopeFilter(userScope.GroupId, currentUserId, checkFileId), cancellationToken);

        if (checkFile is null) return ApiResponse<bool>.Fail("الملف غير موجود.");

        if (!checkFile.IsActive)
        {
            await DeactivateExistingActiveFileAsync(userScope.GroupId, currentUserId, cancellationToken);
        }

        checkFile.IsActive = !checkFile.IsActive;
        await _unit.CompleteAsync();

        _cache.InvalidateActiveCheckFileCache(userScope.GroupId, currentUserId);

        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<bool>> DeleteAsync(
        Guid checkFileId, Guid currentUserId, CancellationToken cancellationToken = default)
    {
        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null) return ApiResponse<bool>.Fail("المستخدم غير موجود.");

        if (!await _permissions.CanManageCheckFilesAsync(currentUserId, cancellationToken))
        {
            return ApiResponse<bool>.Fail(NoCheckFilePermissionMessage, null, 403);
        }

        // التحقق من نطاق الملف: ملفات الجروب، أو ملفات المستخدم الشخصية لو مش في جروب
        var checkFile = await _dbContext.CheckFiles
            .FirstOrDefaultAsync(ScopeFilter(userScope.GroupId, currentUserId, checkFileId), cancellationToken);

        if (checkFile is null) return ApiResponse<bool>.Fail("الملف غير موجود.");

       await _unit.Repository<CheckFile>().RemoveAsync(checkFile);
        await _unit.CompleteAsync();

        if (checkFile.IsActive)
        {
            _cache.InvalidateActiveCheckFileCache(userScope.GroupId, currentUserId);
        }

        _logger.LogInformation("CheckFile {CheckFileId} deleted by user {UserId}", checkFileId, currentUserId);
        return ApiResponse<bool>.SuccessResponse(true);
    }

    public async Task<ApiResponse<ReplaceBankPlatesResultDto>> ReplaceBankPlatesAsync(
    Guid currentUserId, ReplaceBankPlatesRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanManageCheckFilesAsync(currentUserId, cancellationToken))
            return ApiResponse<ReplaceBankPlatesResultDto>.Fail(NoCheckFilePermissionMessage, null, 403);

        var bankName = request.BankName?.Trim();
        if (string.IsNullOrWhiteSpace(bankName))
            return ApiResponse<ReplaceBankPlatesResultDto>.Fail("اسم البنك مطلوب.", null, 400);
        if (bankName.Length > 150)
            return ApiResponse<ReplaceBankPlatesResultDto>.Fail("اسم البنك طويل جدًا.", null, 400);

        var validationError = ValidateUploadedFile(request.File);
        if (validationError is not null)
            return ApiResponse<ReplaceBankPlatesResultDto>.Fail(validationError, null, 400);

        var userScope = await ResolveUserScopeAsync(currentUserId, cancellationToken);
        if (userScope == null) return ApiResponse<ReplaceBankPlatesResultDto>.Fail("المستخدم غير موجود.");

        var activeCheckFile = await _dbContext.CheckFiles
            .Where(f => f.IsActive)
            .Where(f => userScope.GroupId.HasValue
                ? f.GroupId == userScope.GroupId.Value
                : f.GroupId == null && f.UploadedBy == currentUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (activeCheckFile is null)
            return ApiResponse<ReplaceBankPlatesResultDto>.Fail("مفيش ملف تشيك نشط حاليًا.", null, 400);

        var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
        List<List<string?>> allRows;
        await using (var stream = request.File.OpenReadStream())
        {
            allRows = ReadAllRowsAsStrings(stream, extension).ToList();   // الشيت الأول بس
        }

        if (allRows.Count < 2)
            return ApiResponse<ReplaceBankPlatesResultDto>.Fail("الملف فارغ أو فيه صف العناوين بس.", null, 400);

        var header = allRows[0];
        var dataRows = allRows.Skip(1).ToList();

        var plateCol = new PlateColumnDetector().DetectPlateColumn(header, dataRows.Take(200).ToList());
        if (plateCol is null)
            return ApiResponse<ReplaceBankPlatesResultDto>.Fail("مقدرتش ألاقي عمود اللوحة في الملف.", null, 400);

        var used = new HashSet<int> { plateCol.Value };
        var chassisCol = DetectChassisColumn(header, dataRows, used);
        if (chassisCol.HasValue) used.Add(chassisCol.Value);
        var vehicleTypeCol = DetectVehicleTypeColumn(header, dataRows, used);

        var bankLower = bankName.ToLowerInvariant();

        // لوحات البنوك التانية في نفس الملف (هتفضل) — عشان نمنع التكرار معاها
        var otherBanksPlates = await _dbContext.CheckPlates
            .AsNoTracking()
            .Where(p => p.CheckFileId == activeCheckFile.Id && p.BankName.ToLower() != bankLower)
            .Select(p => p.NormalizedPlate)
            .ToListAsync(cancellationToken);

        var seen = new HashSet<string>(otherBanksPlates, StringComparer.OrdinalIgnoreCase);
        var newPlates = new List<CheckPlate>();
        var skipped = 0;

        foreach (var row in dataRows)
        {
            var rawPlate = GetCell(row, plateCol.Value);
            if (string.IsNullOrWhiteSpace(rawPlate)) continue;

            var normalized = PlateNormalizer.Normalize(rawPlate);
            if (string.IsNullOrWhiteSpace(normalized)) continue;

            if (!seen.Add(normalized)) { skipped++; continue; }

            newPlates.Add(new CheckPlate
            {
                CheckFileId = activeCheckFile.Id,
                RawPlate = rawPlate,
                NormalizedPlate = normalized,
                BankName = bankName,
                VehicleType = GetCellOrNull(row, vehicleTypeCol),
                ChassisNumber = GetCellOrNull(row, chassisCol)
            });
        }

        // حماية: مانمسحش القديم لو الجديد فاضي
        if (newPlates.Count == 0)
            return ApiResponse<ReplaceBankPlatesResultDto>.Fail("الملف مفيهوش لوحات صالحة — ماتمسحش أي حاجة.", null, 400);

        int deleted;
        await using (var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            deleted = await _dbContext.CheckPlates
                .Where(p => p.CheckFileId == activeCheckFile.Id && p.BankName.ToLower() == bankLower)
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.CheckPlates.AddRangeAsync(newPlates, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var total = await _dbContext.CheckPlates.CountAsync(p => p.CheckFileId == activeCheckFile.Id, cancellationToken);
            await _dbContext.CheckFiles
                .Where(f => f.Id == activeCheckFile.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.TotalPlates, total), cancellationToken);

            await tx.CommitAsync(cancellationToken);
        }

        _cache.InvalidateActiveCheckFileCache(userScope.GroupId, currentUserId);

        _logger.LogInformation(
            "Bank {BankName} plates replaced in CheckFile {CheckFileId}: deleted {Deleted}, inserted {Inserted}, skipped {Skipped}",
            bankName, activeCheckFile.Id, deleted, newPlates.Count, skipped);

        return ApiResponse<ReplaceBankPlatesResultDto>.SuccessResponse(
            new ReplaceBankPlatesResultDto(
                bankName, deleted, newPlates.Count, skipped,
                HeaderName(header, plateCol), HeaderName(header, vehicleTypeCol), HeaderName(header, chassisCol)),
            $"اتعمل reset للبنك: اتمسح {deleted} واتضاف {newPlates.Count}.");
    }
    /////////private methods
    private static string? GetCellOrNull(List<string?> row, int? index)
    {
        if (!index.HasValue) return null;
        var v = GetCell(row, index.Value);
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    private static string? HeaderName(List<string?> header, int? index) =>
        index.HasValue && index.Value < header.Count ? header[index.Value] : null;

    // تطبيع للمقارنة: شيل المسافات والشرطات والتشكيل، وحّد الألف والياء والتاء المربوطة، وكبّر الإنجليزي
    private static string NormalizeText(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Trim().Normalize(NormalizationForm.FormC))
        {
            if (char.IsWhiteSpace(ch) || ch == '-' || ch == '_' || ch == '\u0640' || (ch >= '\u064B' && ch <= '\u065F')) continue;
            sb.Append(ch switch
            {
                'أ' or 'إ' or 'آ' => 'ا',
                'ة' => 'ه',
                'ى' => 'ي',
                _ => char.ToUpperInvariant(ch)
            });
        }
        return sb.ToString();
    }

    private static int? FindHeaderColumn(List<string?> header, string[] keywords, HashSet<int> excluded)
    {
        var normalizedKeywords = keywords.Select(NormalizeText).ToArray();
        for (var i = 0; i < header.Count; i++)
        {
            if (excluded.Contains(i)) continue;
            var h = NormalizeText(header[i]);
            if (h.Length > 0 && normalizedKeywords.Any(k => h.Contains(k))) return i;
        }
        return null;
    }

    private static int ColumnCount(List<string?> header, List<List<string?>> rows) =>
        Math.Max(header.Count, rows.Count == 0 ? 0 : rows.Max(r => r.Count));

    private static int? IndexOfMax(int[] scores)
    {
        var best = -1;
        var max = 0;
        for (var i = 0; i < scores.Length; i++)
            if (scores[i] > max) { max = scores[i]; best = i; }
        return best >= 0 ? best : null;
    }

    // ===== الشاصي =====
    private static readonly Regex ChassisPattern = new(@"^[A-Za-z0-9]{10,20}$", RegexOptions.Compiled);

    private static int? DetectChassisColumn(List<string?> header, List<List<string?>> rows, HashSet<int> excluded)
    {
        var byHeader = FindHeaderColumn(header, new[] { "شاص", "شاسي", "هيكل", "chassis", "chasis", "vin" }, excluded);
        if (byHeader.HasValue) return byHeader;

        var bestCol = -1;
        var bestRatio = 0.0;
        for (var col = 0; col < ColumnCount(header, rows); col++)
        {
            if (excluded.Contains(col)) continue;
            int nonEmpty = 0, matches = 0;
            foreach (var row in rows)
            {
                if (col >= row.Count) continue;
                var v = row[col]?.Replace(" ", "").Trim();
                if (string.IsNullOrWhiteSpace(v)) continue;
                nonEmpty++;
                if (ChassisPattern.IsMatch(v) && v.Any(char.IsDigit)) matches++;
            }
            if (nonEmpty == 0) continue;
            var ratio = (double)matches / nonEmpty;
            if (ratio > bestRatio) { bestRatio = ratio; bestCol = col; }
        }
        return bestRatio >= 0.6 ? bestCol : null;
    }

    // ===== الطراز (النوع) =====
    private static readonly string[] VehicleTypeReferenceRaw =
    {
    "YARIS","كامري","اكسنت","سوناتا","يارس","كورولا","ELANTRA","COROLLA","النترا","باترول واجن",
    "كيكس واجن","ACCENT","بكب","بالينو","CAMRY","صني","TAURUS","ديز اير","X-TRAIL","بيجاس",
    "GRAND I10","PEGAS","TIGUAN","اس 450","تورس","SONATA","ايدو بلس","اكس تريل","LAND CRUISER","برادو",
    "PRADO","رانجلر","520 اي سيدان","TERRITORY","تورس سيدان","URBANCRUISER","يوكون","جيب فورتشنر","K5","6سيدان",
    "MAGNITE","T-ROC","TERAMONT","السفن","TUCSON","RAV 4","شاسيه","هاي ايس","شاحنه","فان",
    "جي اكس ار","كابريس","هايلكس","بكب غمارتين","سيراتيو","ريو سيدان","امبالا","أف جي","أوبتيما","انوفا واجن",
    "افالون","شارجر","كي5","ر اس تريلة","فان بضاعه","ثلاجة","مينى فان","شاحنة بثلاجة","بكب غماره","شاحنة بصندوق",
    "ام جي 5","DZIRE","430-LS-AT","LIANA-1.6-AT","MAXIMA","التيما","جرووف","اكورد","520 اي","تاهو",
    "اكس 6","سيراتو","تيرامونت","PATROL","C","توسان","أل أكس 570","صندوق"
};

    private static readonly HashSet<string> VehicleTypeReference =
        new(VehicleTypeReferenceRaw.Select(NormalizeText));

    // للمطابقة الجزئية بنستخدم الكلمات الطويلة بس (٤ حروف+) عشان "C" و"فان" ماتطابقش غلط
    private static readonly string[] VehicleTypeReferenceLong =
        VehicleTypeReference.Where(x => x.Length >= 4).ToArray();

    private static int? DetectVehicleTypeColumn(List<string?> header, List<List<string?>> rows, HashSet<int> excluded)
    {
        // 1) الهيدر: طراز / تراز
        var byHeader = FindHeaderColumn(header, new[] { "طراز", "تراز" }, excluded);
        if (byHeader.HasValue) return byHeader;

        // 2) مش في الهيدر → ندور في الصفوف على القيم المرجعية
        var columnCount = ColumnCount(header, rows);
        var exactScores = new int[columnCount];
        var containsScores = new int[columnCount];

        foreach (var row in rows)
        {
            for (var c = 0; c < row.Count; c++)
            {
                if (excluded.Contains(c)) continue;
                var v = NormalizeText(row[c]);
                if (v.Length == 0) continue;

                if (VehicleTypeReference.Contains(v)) exactScores[c]++;
                else if (VehicleTypeReferenceLong.Any(k => v.Contains(k))) containsScores[c]++;
            }
        }

        // العمود اللي فيه أكتر مطابقة تامة بيكسب، ولو مفيش خالص نجرب المطابقة الجزئية
        return IndexOfMax(exactScores) ?? IndexOfMax(containsScores);
    }

    //
    private async Task DeactivateExistingActiveFileAsync(Guid? groupId, Guid uploaderId, CancellationToken cancellationToken)
    {
        var query = _dbContext.CheckFiles.Where(f => f.IsActive);
        query = groupId.HasValue
            ? query.Where(f => f.GroupId == groupId.Value)
            : query.Where(f => f.GroupId == null && f.UploadedBy == uploaderId);

        await query.ExecuteUpdateAsync(setters => setters.SetProperty(f => f.IsActive, false), cancellationToken);
    }

    private async Task<int> BulkInsertCheckPlatesAsync(
        AppDbContext dbContext, Guid checkFileId, List<List<string?>> dataRows, CancellationToken cancellationToken)
    {
        var table = new DataTable();
        table.Columns.Add("check_file_id", typeof(Guid));
        table.Columns.Add("raw_plate", typeof(string));
        table.Columns.Add("normalized_plate", typeof(string));
        table.Columns.Add("bank_name", typeof(string));
        table.Columns.Add("vehicle_type", typeof(string));
        table.Columns.Add("chassis_number", typeof(string));

        var seen = new HashSet<string>();

        // Query DB for already-existing normalized plates for this check file
        var existingPlates = new HashSet<string>(await dbContext.CheckPlates
            .Where(p => p.CheckFileId == checkFileId)
            .Select(p => p.NormalizedPlate)
            .ToListAsync(cancellationToken));

        foreach (var row in dataRows)
        {
            if (PlateColumnIndex >= row.Count) continue;
            var rawPlate = row[PlateColumnIndex]?.Trim();
            if (string.IsNullOrWhiteSpace(rawPlate)) continue;

            var normalized = PlateNormalizer.Normalize(rawPlate);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized)) continue;

            // Skip if this normalized plate already exists in DB for this check file
            if (existingPlates.Contains(normalized)) continue;

            var dr = table.NewRow();
            dr["check_file_id"] = checkFileId;
            dr["raw_plate"] = rawPlate;
            dr["normalized_plate"] = normalized;
            dr["bank_name"] = GetCell(row, BankNameColumnIndex) ?? "غير محدد";
            dr["vehicle_type"] = (object?)GetCell(row, VehicleTypeColumnIndex) ?? DBNull.Value;
            dr["chassis_number"] = (object?)GetCell(row, ChassisNumberColumnIndex) ?? DBNull.Value;
            table.Rows.Add(dr);
        }

        if (table.Rows.Count == 0) return 0;

        var connectionString = _configuration.GetConnectionString("DefaultConnection")
     ?? throw new InvalidOperationException("Could not resolve the database connection string.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var importer = await connection.BeginBinaryImportAsync(
            "COPY check_plates (check_file_id, raw_plate, normalized_plate, bank_name, vehicle_type, chassis_number) FROM STDIN (FORMAT BINARY)", cancellationToken);
        importer.Timeout = TimeSpan.FromSeconds(120);

        foreach (DataRow row in table.Rows)
        {
            await importer.StartRowAsync(cancellationToken);
            await importer.WriteAsync((Guid)row["check_file_id"], NpgsqlDbType.Uuid, cancellationToken);
            foreach (var column in new[] { "raw_plate", "normalized_plate", "bank_name", "vehicle_type", "chassis_number" })
            {
                if (row[column] is string value)
                    await importer.WriteAsync(value, NpgsqlDbType.Varchar, cancellationToken);
                else
                    await importer.WriteNullAsync(cancellationToken);
            }
        }

        await importer.CompleteAsync(cancellationToken);
        return table.Rows.Count;
    }

    private static string? GetCell(List<string?> row, int index) => index < row.Count ? row[index]?.Trim() : null;

    private async Task<UserScopeInfo?> ResolveUserScopeAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .WithEffectiveGroup()
            .Where(x => x.User.Id == userId)
            .Select(x => new UserScopeInfo(x.GroupId, x.User.Role))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static System.Linq.Expressions.Expression<Func<CheckFile, bool>> ScopeFilter(Guid? groupId, Guid currentUserId, Guid checkFileId)
    {
        return groupId.HasValue
            ? f => f.Id == checkFileId && f.GroupId == groupId.Value
            : f => f.Id == checkFileId && f.GroupId == null && f.UploadedBy == currentUserId;
    }

    private static string? ValidateUploadedFile(Microsoft.AspNetCore.Http.IFormFile? file)
    {
        if (file is null || file.Length == 0) return "لازم ترفع ملف.";
        if (file.Length > MaxUploadSizeBytes) return $"حجم الملف أكبر من الحد المسموح ({MaxUploadSizeBytes / 1024 / 1024} MB).";

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            return $"صيغة الملف غير مدعومة. المسموح: {string.Join(", ", AllowedExtensions)}";
        }

        return null;
    }

    /// <summary>مستخدمة كمان من ReferralJobService (نفس منطق القراءة، أعمدة مختلفة).</summary>
    internal static IEnumerable<List<string?>> ReadAllRowsAsStrings(Stream stream, string extension)
    {
        if (extension == ".csv")
        {
            using var textReader = new StreamReader(stream);
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = false,
                MissingFieldFound = null,
                BadDataFound = null
            };
            using var csv = new CsvReader(textReader, config);

            while (csv.Read())
            {
                var record = csv.Parser.Record;
                yield return record?.Select(v => (string?)v).ToList() ?? new List<string?>();
            }

            yield break;
        }

        using var reader = ExcelReaderFactory.CreateReader(stream);
        while (reader.Read())
        {
            var row = new List<string?>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row.Add(reader.GetValue(i)?.ToString()?.Trim());
            }
            yield return row;
        }
    }

    private record UserScopeInfo(Guid? GroupId, UserRole Role);
}
