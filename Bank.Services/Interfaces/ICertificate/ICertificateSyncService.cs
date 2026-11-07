namespace Bank.Services.Interfaces.ICertificate;

public interface ICertificateSyncService
{
    /// <summary>
    /// دورة مزامنة واحدة: لو الـ Baseline ماخلصش بيكمّله (مستأنف من آخر صفحة)، وإلا Scan للجديد + Changes API.
    /// بتتجاهل نفسها لو فيه مزامنة شغالة (advisory lock).
    /// </summary>
    Task RunAsync(CancellationToken ct);

    /// <summary>قايمة كاملة مسطّحة (طلبات قليلة) لتصحيح الأسماء/المجلدات واكتشاف المحذوف. مش بتعمل استخراج لأي ملف.</summary>
    Task ReconcileAsync(CancellationToken ct);
}
