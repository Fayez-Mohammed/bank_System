using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Bank.Services.Implementations.CertificateServices;

/// <summary>
/// Postgres session-level advisory lock: مزامنة واحدة بس في نفس الوقت حتى لو فيه أكتر من instance للتطبيق،
/// وحتى لو Hangfire أعاد جدولة job طويلة (invisibility timeout). بيتحرر تلقائياً لو الـ connection وقعت.
/// </summary>
public sealed class CertificateSyncLock : IAsyncDisposable
{
    private const long LockKey = 7_342_001_001;   // ثابت لميزة الشهادات

    private readonly NpgsqlConnection _connection;

    private CertificateSyncLock(NpgsqlConnection connection) => _connection = connection;

    public static async Task<CertificateSyncLock?> TryAcquireAsync(IConfiguration configuration, CancellationToken ct)
    {
        var cs = configuration.GetConnectionString("DefaultConnection")
                 ?? throw new InvalidOperationException("Could not resolve the database connection string.");
        var builder = new NpgsqlConnectionStringBuilder(cs) { Pooling = false, KeepAlive = 30 };

        var conn = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@k)", conn);
            cmd.Parameters.AddWithValue("k", LockKey);
            var acquired = (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
            if (acquired) return new CertificateSyncLock(conn);

            await conn.DisposeAsync();
            return null;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@k)", _connection);
            cmd.Parameters.AddWithValue("k", LockKey);
            await cmd.ExecuteScalarAsync();
        }
        catch { /* الـ connection بتتقفل بعد كده وده بيحرر الـ lock بردو */ }
        await _connection.DisposeAsync();
    }
}
