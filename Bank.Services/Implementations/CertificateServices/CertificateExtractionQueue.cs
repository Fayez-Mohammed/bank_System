using Bank.DAL.Context;
using Bank.Services.HangFireJobs;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bank.Services.Implementations.CertificateServices;

/// <summary>
/// طابور الاستخراج = جدول certificates نفسه (Status = Pending)، مش Job في Hangfire لكل شهادة:
///  - 3000 شهادة جديدة = 3000 صف Pending، وعدد ثابت من "drain" jobs (= عدد الـ workers) بيسحبوا منهم واحدة واحدة.
///  - السحب ذرّي (UPDATE ... FOR UPDATE SKIP LOCKED): مستحيل شهادتين workers ياخدوا نفس الشهادة.
///  - لو الـ process مات وهو شغال: الشهادة بتفضل Processing لحد ما الـ lease يخلص، والـ pump بيرجّعها Pending.
///  - Hangfire storage مابيتملاش بآلاف الـ jobs، وأي job اتمسح/ضاع مابيضيّعش شهادة (الحالة في الـ DB).
/// </summary>
public sealed class CertificateExtractionQueue
{
    public const string QueueName = "certificates";

    private static int _activeDrains;

    private readonly AppDbContext _db;
    private readonly IBackgroundJobClient _jobs;
    private readonly JobStorage _storage;
    private readonly ILogger<CertificateExtractionQueue> _logger;

    public CertificateExtractionQueue(
        AppDbContext db, IBackgroundJobClient jobs, JobStorage storage, IConfiguration configuration,
        ILogger<CertificateExtractionQueue> logger)
    {
        _db = db;
        _jobs = jobs;
        _storage = storage;
        _logger = logger;
        Workers = ReadInt(configuration, "Certificates:ExtractionWorkers", 6, 1, 64);
        MaxAttempts = ReadInt(configuration, "Certificates:MaxExtractionAttempts", 3, 1, 20);
        Lease = TimeSpan.FromMinutes(ReadInt(configuration, "Certificates:ExtractionLeaseMinutes", 15, 2, 240));
    }

    /// <summary>= WorkerCount بتاع Hangfire server الاستخراج (نفس الإعداد).</summary>
    public int Workers { get; }
    public int MaxAttempts { get; }
    public TimeSpan Lease { get; }

    public static int ActiveDrains => Volatile.Read(ref _activeDrains);
    public static void DrainStarted() => Interlocked.Increment(ref _activeDrains);
    public static void DrainFinished() => Interlocked.Decrement(ref _activeDrains);

    public static int ReadInt(IConfiguration c, string key, int fallback, int min, int max) =>
        int.TryParse(c[key], out var v) ? Math.Clamp(v, min, max) : fallback;

    /// <summary>
    /// يتأكد إن في drain jobs كفاية شغالة/مستنية للشهادات الجاهزة. آمن تناديه كتير:
    /// أقصى عدد بيتحسب من الشغال + اللي في الطابور، والزيادة (لو حصلت) بتخلص فوراً لما ماتلاقيش شغل.
    /// </summary>
    public async Task<int> KickAsync(CancellationToken ct = default)
    {
        var ready = await CountReadyAsync(Workers, ct);
        if (ready == 0) return 0;

        long enqueued;
        try
        {
            enqueued = _storage.GetMonitoringApi().EnqueuedCount(QueueName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read Hangfire queue length; assuming empty");
            enqueued = 0;
        }

        var wanted = Math.Min(Workers, ready) - ActiveDrains - (int)Math.Min(enqueued, int.MaxValue);
        for (var i = 0; i < wanted; i++)
            _jobs.Enqueue<CertificateExtractionJob>(j => j.DrainAsync(CancellationToken.None));

        if (wanted > 0)
            _logger.LogInformation(
                "Certificate extraction: started {Count} drain job(s) (ready>={Ready}, active={Active}, queued={Queued})",
                wanted, ready, ActiveDrains, enqueued);
        return Math.Max(wanted, 0);
    }

    /// <summary>عدد الشهادات الجاهزة للسحب (لحد limit بس، عشان الـ query تفضل رخيصة).</summary>
    public async Task<int> CountReadyAsync(int limit, CancellationToken ct = default) =>
        await _db.Database.SqlQueryRaw<int>(
                """
                SELECT count(*)::int AS "Value" FROM (
                    SELECT 1 FROM certificates
                    WHERE status = 'Pending' AND is_removed_from_drive = false
                      AND (next_attempt_at IS NULL OR next_attempt_at <= now())
                    LIMIT {0}) t
                """, limit)
            .SingleAsync(ct);

    /// <summary>بيسحب أقدم شهادة جاهزة ويعلّمها Processing (ذرّي). null = مفيش شغل.</summary>
    public async Task<Guid?> ClaimNextAsync(CancellationToken ct)
    {
        // ADO مباشر: UPDATE ... RETURNING لازم يتنفذ زي ما هو (EF ممكن يلفّه في subquery)
        await _db.Database.OpenConnectionAsync(ct);
        await using var cmd = _db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = """
            UPDATE certificates SET
                status = 'Processing',
                processing_started_at = now(),
                extraction_attempts = extraction_attempts + 1,
                next_attempt_at = NULL
            WHERE id = (
                SELECT id FROM certificates
                WHERE status = 'Pending' AND is_removed_from_drive = false
                  AND (next_attempt_at IS NULL OR next_attempt_at <= now())
                ORDER BY discovered_at, id
                LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING id
            """;
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is Guid id ? id : null;
    }

    /// <summary>سحب شهادة معيّنة (للـ jobs القديمة ExtractAsync(id)). بيرجع false لو مش Pending/Failed أو حد ماسكها.</summary>
    public async Task<bool> ClaimByIdAsync(Guid id, CancellationToken ct)
    {
        var n = await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE certificates SET
                status = 'Processing',
                processing_started_at = now(),
                extraction_attempts = extraction_attempts + 1,
                next_attempt_at = NULL
            WHERE id = {0} AND status IN ('Pending', 'Failed') AND is_removed_from_drive = false
            """, new object[] { id }, ct);
        return n == 1;
    }

    /// <summary>
    /// Processing أقدم من الـ lease = الـ worker مات (restart / crash / OOM). بترجع Pending،
    /// أو Failed لو خلّصت عدد المحاولات (عشان ملف بيوقّع الـ process مايفضلش يوقّعه للأبد).
    /// </summary>
    public async Task<int> RecoverExpiredLeasesAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - Lease;
        var n = await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE certificates SET
                status = CASE WHEN extraction_attempts >= {1} THEN 'Failed' ELSE 'Pending' END,
                status_note = CASE WHEN extraction_attempts >= {1}
                                   THEN 'المعالجة وقفت في النص أكتر من مرة (restart / timeout). اعمل Retry يدوي.'
                                   ELSE status_note END,
                processing_started_at = NULL
            WHERE status = 'Processing' AND (processing_started_at IS NULL OR processing_started_at < {0})
            """, new object[] { cutoff, MaxAttempts }, ct);
        if (n > 0)
            _logger.LogWarning("Certificate extraction: recovered {Count} certificate(s) with an expired lease", n);
        return n;
    }
}
