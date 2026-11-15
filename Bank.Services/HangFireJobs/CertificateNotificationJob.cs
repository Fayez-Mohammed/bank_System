using Bank.DAL.Context;
using Bank.Shared.Enums;
using Bank.Services.Interfaces;
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

    public async Task SendNewCertificatesNotificationAsync(int newCertificatesCount, CancellationToken ct)
    {
        if (newCertificatesCount <= 0) return;

        // جلب جميع المستخدمين النشطين (غير السوبر أدمن)
        var userIds = await _db.Users
            .Where(u => u.IsActive && u.Role != UserRole.SuperAdmin)
            .Select(u => u.Id)
            .ToListAsync(ct);

        if (userIds.Count == 0) return;

        var title = "شهادات جديدة";
        var message = FormatNotificationMessage(newCertificatesCount);

        _logger.LogInformation("Sending new certificate notification ({Count} certs) to {UserCount} users",
            newCertificatesCount, userIds.Count);

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

    private static string FormatNotificationMessage(int count) => count switch
    {
        1 => "تم إدراج شهادة جديدة واحدة في النظام.",
        2 => "تم إدراج شهادتين جديدتين في النظام.",
        >= 3 and <= 10 => $"تم إدراج {count} شهادات جديدة في النظام.",
        _ => $"تم إدراج {count} شهادة جديدة في النظام."
    };
}