namespace Bank.Services.Interfaces;

/// <summary>
/// حصة صفوف ملفات الداتا لنطاق واحد (جروب أو يوزر مستقل).
/// Limit = null يعني بلا حد. Used = مجموع RowCount لكل ملفات النطاق.
/// </summary>
public record DataRowQuota(long? Limit, long Used)
{
    public long? Remaining => Limit.HasValue ? Math.Max(0, Limit.Value - Used) : null;
}

public interface IDataRowQuotaService
{
    Task<long?> GetLimitAsync(Guid? groupId, Guid ownerId, CancellationToken ct = default);

    // excludeFileId: لو ملف هيتستبدل، صفوفه القديمة مش بتتحسب
    Task<DataRowQuota> GetForScopeAsync(Guid? groupId, Guid ownerId, Guid? excludeFileId = null, CancellationToken ct = default);
}

public static class DataRowQuotaMessages
{
    public static string Full(DataRowQuota q) =>
        $"وصلت للحد الأقصى لعدد صفوف ملفات الداتا ({q.Limit:N0} صف). امسح ملفات أو اطلب زيادة الحد.";

    public static string Exceeded(DataRowQuota q, long adding) =>
        $"العملية دي هتضيف {adding:N0} صف وده بيعدّي الحد المسموح. الحد {q.Limit:N0}، المستخدم {q.Used:N0}، المتبقي {q.Remaining:N0}.";

    public static string ImportExceeded(DataRowQuota q) =>
        $"الملف بيعدّي حد الصفوف المسموح. الحد {q.Limit:N0}، المستخدم {q.Used:N0}، المتبقي {q.Remaining:N0} صف بس.";
}

/// <summary>بيترمي من الـ import job لما الصفوف تعدّي الحصة، عشان نعمل rollback ونحفظ السبب.</summary>
public class DataRowLimitExceededException : Exception
{
    public DataRowLimitExceededException(string message) : base(message) { }
}
