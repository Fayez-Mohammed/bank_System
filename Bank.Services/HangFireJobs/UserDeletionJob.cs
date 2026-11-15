using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Services.Interfaces;
using Bank.Shared.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Bank.Services.HangFireJobs;

[Queue("bulk-import")]
public class UserDeletionJob
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IFileStorageService _storage;
    private readonly ILogger<UserDeletionJob> _logger;

    public UserDeletionJob(AppDbContext db, IConfiguration configuration,
        IFileStorageService storage, ILogger<UserDeletionJob> logger)
    {
        _db = db;
        _configuration = configuration;
        _storage = storage;
        _logger = logger;
    }

    // كل الخطوات idempotent، فلو الـ job اتعاد يكمل من حيث وقف
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
    public async Task RunAsync(Guid userId, Guid? transferredToAdminId, CancellationToken ct)
    {
        // لو اليوزر اتمسح قبل كده مفيش حاجة نعملها
        if (!await _db.Users.AnyAsync(u => u.Id == userId, ct)) return;

        // الحذف الجماعي بالـ cascade ممكن يعدّي الـ 30 ثانية الافتراضية
        _db.Database.SetCommandTimeout(TimeSpan.FromMinutes(30));

        if (await _db.DataFiles.AnyAsync(f => f.OwnerId == userId && f.ImportStatus == DataFileImportStatus.Processing, ct))
            throw new InvalidOperationException("فيه استيراد شغال على ملفات اليوزر، هيتعاد بعد شوية.");

        _logger.LogInformation("User deletion started for {UserId}", userId);

        var connectionString = _configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Could not resolve the database connection string.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // 1) ملفات الداتا اللي لسه باسمه (من غير جروب: كلها. في جروب: الشخصية القديمة بس لأن بتاعة الجروب اتنقلت)
        var dataFiles = await _db.DataFiles.AsNoTracking()
            .Where(f => f.OwnerId == userId)
            .Select(f => new { f.Id, f.StoragePath })
            .ToListAsync(ct);

        foreach (var file in dataFiles)
        {
            // نفس منطق حذف ملف داتا: match_results ثم الصفوف على دفعات
            await DataFileImportJob.DeleteHistoricalPlatesAsync(connection, file.Id, ct);
            await _db.DataFiles.Where(f => f.Id == file.Id).ExecuteDeleteAsync(ct);
            await _storage.DeleteAsync(file.StoragePath, ct);
        }
        _logger.LogInformation("User {UserId}: deleted {Count} data file(s)", userId, dataFiles.Count);

        // 2) التسجيلات: ملفات الصوت من الديسك، ثم الجلسات (اللوحات ونقاط الـ GPS بالـ cascade)
        var audioPaths = await _db.RecordingSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.AudioUrl != null)
            .Select(s => s.AudioUrl!)
            .ToListAsync(ct);
        foreach (var path in audioPaths) await _storage.DeleteAsync(path, ct);

        await _db.RecordingSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        await _db.RecordedPlates.Where(p => p.UserId == userId).ExecuteDeleteAsync(ct);
        await _db.RecordingArchivePlates.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);

        // 3) التشييك: الجلسات (اللوحات بالـ cascade) وسجلات اللايف والتشييك اليدوي
        await _db.CheckSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        await _db.LiveCheckLogs.Where(l => l.UserId == userId).ExecuteDeleteAsync(ct);
        await _db.Set<ManualCheck>().Where(m => m.UserId == userId).ExecuteDeleteAsync(ct);

        // 4) ملفات التشيك اللي لسه باسمه (check_plates بالـ cascade)
        await _db.CheckFiles.Where(f => f.UploadedBy == userId).ExecuteDeleteAsync(ct);

        // 5) الإحالات (referral_plates وmatch_results بالـ cascade) والخرائط (مفيش FK، فيدوي)
        await _db.ReferralJobs.Where(j => j.UserId == userId).ExecuteDeleteAsync(ct);
        await _db.Set<MapResult>().Where(m => m.UserId == userId && m.GroupId == null).ExecuteDeleteAsync(ct);
        await _db.Set<MapUploadedFile>().Where(m => m.UploadedBy == userId && m.GroupId == null).ExecuteDeleteAsync(ct);

        // 6) historical_plates.user_id: مفيش index عليه، فنصفّيه بأمر واحد بدل ما الـ FK يلف على الجدول جوه الـ DELETE.
        //    الصفوف اللي في الملفات المنقولة (أو اللي اليوزر ضافها في ملفات غيره) بتروح للأدمن، أو null لو مفيش.
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandTimeout = 1800;
            cmd.CommandText = "UPDATE historical_plates SET user_id = @newOwner WHERE user_id = @userId";
            cmd.Parameters.Add(new NpgsqlParameter("userId", NpgsqlDbType.Uuid) { Value = userId });
            cmd.Parameters.Add(new NpgsqlParameter("newOwner", NpgsqlDbType.Uuid)
            {
                Value = (object?)transferredToAdminId ?? DBNull.Value
            });
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 7) اليوزر نفسه (permissions وrefresh tokens وجداول Identity بالـ cascade، وaudit_logs بـ SetNull)
        await _db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync(ct);

        // 8) فولدر التخزين الشخصي (ملفات الجروب في groups/... وماتتمسحش)
        await _storage.DeleteFolderAsync($"users/{userId}", ct);

        _logger.LogInformation("User deletion completed for {UserId}", userId);
    }
}
