using Bank.DAL.Context;
using Bank.Services.Interfaces;
using Microsoft.Extensions.Logging;
using FirebaseAdmin.Messaging;
using Microsoft.EntityFrameworkCore;

public class FcmUserNotifier : IUserNotifier
{
    private readonly AppDbContext _db;
    private readonly ILogger<FcmUserNotifier> _logger;

    public FcmUserNotifier(AppDbContext db, ILogger<FcmUserNotifier> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SendAsync(Guid userId, string title, string body, CancellationToken ct = default)
    {
        var token = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.FcmToken)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(token))
        {
            _logger.LogInformation("No FCM token for user {UserId}; skipping push", userId);
            return;
        }

        var message = new Message
        {
            Token = token,
            Notification = new Notification { Title = title, Body = body },
            Android = new AndroidConfig { Priority = Priority.High }
        };

        try
        {
            await FirebaseMessaging.DefaultInstance.SendAsync(message, ct);
        }
        catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
        {
            // التطبيق اتمسح أو التوكن اتغير: نمسحه ومانعتبرهاش فشل
            await _db.Users
                .Where(u => u.Id == userId && u.FcmToken == token)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.FcmToken, (string?)null)
                    .SetProperty(u => u.FcmTokenUpdatedAt, (DateTime?)null), ct);
        }
        // أي خطأ تاني بيطلع للـ job، فما يحدّثش المرحلة ويحاول في الدورة الجاية
    }
}