using Bank.Services.Interfaces;
using Microsoft.Extensions.Logging;

public class LogOnlyUserNotifier : IUserNotifier
{
    private readonly ILogger<LogOnlyUserNotifier> _logger;
    public LogOnlyUserNotifier(ILogger<LogOnlyUserNotifier> logger) => _logger = logger;

    public Task SendAsync(Guid userId, string title, string body, CancellationToken ct = default)
    {
        _logger.LogInformation("Notification to {UserId}: {Title} - {Body}", userId, title, body);
        return Task.CompletedTask;
    }
}