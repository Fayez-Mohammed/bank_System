namespace Bank.Services.Implementations.CertificateServices;

/// <summary>
/// تنسيق داخل العملية بين المزامنة (المنتج) والـ worker (المستهلك):
///  • Signal(): المزامنة بتصحّي الـ worker أول ما صفحة فيها شهادات جديدة اتسجّلت (من غير ما تستنى الـ polling).
///  • Circuit breaker: لو Drive رفض (توكن/quota)، الـ worker بيوقف مؤقتاً بدل ما يفشّل آلاف الشهادات ورا بعض.
/// </summary>
public interface ICertificateWorkGate
{
    void Signal();
    Task WaitForWorkAsync(TimeSpan timeout, CancellationToken ct);
    void Trip(TimeSpan duration, string reason);
    DateTime? TrippedUntil { get; }
}

public sealed class CertificateWorkGate : ICertificateWorkGate
{
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private long _trippedUntilTicks;

    public void Signal()
    {
        // مش محتاجين تراكم إشارات: واحدة تكفي لتصحية حلقة واحدة، والباقي بيعمل polling.
        if (_signal.CurrentCount < 64) _signal.Release();
    }

    public async Task WaitForWorkAsync(TimeSpan timeout, CancellationToken ct)
    {
        try { await _signal.WaitAsync(timeout, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public void Trip(TimeSpan duration, string reason)
    {
        var until = DateTime.UtcNow.Add(duration);
        Interlocked.Exchange(ref _trippedUntilTicks, until.Ticks);
    }

    public DateTime? TrippedUntil
    {
        get
        {
            var ticks = Interlocked.Read(ref _trippedUntilTicks);
            if (ticks == 0) return null;
            var until = new DateTime(ticks, DateTimeKind.Utc);
            return until > DateTime.UtcNow ? until : null;
        }
    }
}
