using Bank.DAL.Context;
using Bank.Services.Interfaces;
using Bank.Shared.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bank.Services.HangFireJobs;

public class CertificateNotificationJob
{
    private readonly AppDbContext _db;
    private readonly IUserNotifier _notifier;
    private readonly ILogger<CertificateNotificationJob> _logger;

    public CertificateNotificationJob(AppDbContext db, IUserNotifier notifier, ILogger<CertificateNotificationJob> logger)
    {
        _db = db;
        _notifier = notifier;
        _logger = logger;
    }

    // محاولة تانية بس: الإرسال مش idempotent، والتكرار معناه إشعار مزدوج
    [AutomaticRetry(Attempts = 1)]
    public async Task SendNewCertificatesNotificationAsync(int newCertificatesCount, int? batchNo, CancellationToken ct)
    {
        if (newCertificatesCount <= 0) return;

        // اللي عنده token بس: بيوفّر query لكل يوزر ومابيملاش اللوج
        var userIds = await _db.Users
            .Where(u => u.IsActive && u.Role != UserRole.SuperAdmin && u.FcmToken != null)
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (userIds.Count == 0) return;

        var title = "شهادات جديدة";
        var message = FormatMessage(newCertificatesCount, batchNo);

        _logger.LogInformation(
            "Sending new certificate notification (batch {Batch}, {Count} certs) to {UserCount} users",
            batchNo, newCertificatesCount, userIds.Count);

        // بالتتابع: الـ notifier بيستخدم DbContext واحد مش thread-safe، فماتعملش Parallel هنا
        foreach (var userId in userIds)
        {
            try
            {
                await _notifier.SendAsync(userId, title, message, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send new certificate notification to user {UserId}", userId);
            }
        }
    }

    private static string FormatMessage(int count, int? batchNo)
    {
        var countPhrase = count switch
        {
            1 => "شهادة واحدة",
            2 => "شهادتان",
            >= 3 and <= 10 => $"{count} شهادات",
            _ => $"{count} شهادة"
        };

        return batchNo.HasValue
            ? $"نزلت دفعة جديدة (رقم {batchNo}) فيها {countPhrase}."
            : $"نزلت دفعة جديدة فيها {countPhrase}.";
    }
}