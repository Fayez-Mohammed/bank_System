namespace Bank.Services.Interfaces;

/// <summary>
/// كاش لمجموعة اللوحات المطبَّعة بتاعة ملف التشيك النشط — مفيدة لأي مكان
/// بيعمل lookup سريع للوحة واحدة (Live Check، التسجيل الصوتي أثناء المشي).
/// النسخة دي بـ IMemoryCache (in-process) كبداية — لو عندك Redis مُعدّ
/// بالفعل في المشروع، استبدل الـ implementation بس والـ interface يفضل زي ما هو.
/// </summary>
public interface ICheckPlateCacheService
{
    Task<bool> IsPlateInActiveCheckFileAsync(Guid? groupId, Guid userId, string normalizedPlate, CancellationToken cancellationToken = default);

    /// <summary>لازم تتنادى بعد أي رفع/تفعيل/تعطيل/حذف لملف تشيك عشان الكاش يفضل متزامن مع الداتابيز.</summary>
    void InvalidateActiveCheckFileCache(Guid? groupId, Guid userId);
}
