using Bank.DAL.Context;
using Bank.Services.Interfaces;
using Bank.Shared.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bank.Services.HangFireJobs;

public class SubscriptionExpiryJob
{
    private static readonly TimeSpan[] Thresholds =
    {
        TimeSpan.FromDays(4), TimeSpan.FromDays(3), TimeSpan.FromDays(2),
        TimeSpan.FromDays(1), TimeSpan.FromHours(2)
    };

    private static readonly string[] Messages =
    {
        "اشتراكك هينتهي خلال 4 أيام. تواصل مع الأدمن للتجديد.",
        "اشتراكك هينتهي خلال 3 أيام. تواصل مع الأدمن للتجديد.",
        "اشتراكك هينتهي خلال يومين. تواصل مع الأدمن للتجديد.",
        "اشتراكك هينتهي خلال يوم واحد. تواصل مع الأدمن للتجديد.",
        "اشتراكك هينتهي خلال ساعتين. تواصل مع الأدمن للتجديد."
    };

    private readonly AppDbContext _db;
    private readonly IUserNotifier _notifier;
    private readonly ILogger<SubscriptionExpiryJob> _logger;

    public SubscriptionExpiryJob(AppDbContext db, IUserNotifier notifier, ILogger<SubscriptionExpiryJob> logger)
    {
        _db = db;
        _notifier = notifier;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var horizon = now + Thresholds[0];

        var candidates = await _db.Users
            .Where(u => u.IsActive && u.Role != UserRole.SuperAdmin
                     && u.SubscriptionExpiresAt != null
                     && u.SubscriptionExpiresAt > now
                     && u.SubscriptionExpiresAt <= horizon
                     && u.ExpiryReminderStage < Thresholds.Length)
            .Select(u => new { u.Id, ExpiresAt = u.SubscriptionExpiresAt!.Value, u.ExpiryReminderStage })
            .ToListAsync(ct);

        foreach (var u in candidates)
        {
            // عدد العتبات اللي الوقت المتبقي نزل تحتها
            var remaining = u.ExpiresAt - now;
            var targetStage = Thresholds.Count(t => remaining <= t);

            // لو الـ job كان واقف وعدّى أكتر من عتبة، بنبعت إشعار واحد بس مش كلهم ورا بعض
            if (targetStage <= u.ExpiryReminderStage) continue;

            try
            {
                await _notifier.SendAsync(u.Id, "تنبيه انتهاء الاشتراك", Messages[targetStage - 1], ct);

                await _db.Users
                    .Where(x => x.Id == u.Id && x.ExpiryReminderStage < targetStage)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiryReminderStage, targetStage), ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send expiry reminder (stage {Stage}) to user {UserId}", targetStage, u.Id);
            }
        }
    }
}