namespace Bank.Services.Interfaces;

public interface IUserNotifier
{
    Task SendAsync(Guid userId, string title, string body, CancellationToken ct = default);
}