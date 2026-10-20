using Bank.Shared.DTOs.AdminDTOs;

namespace Bank.Services.Interfaces;

/// <summary>
/// قواعد السماح لكل عملية. بتتقري من الداتابيز في كل طلب (مش من الـ JWT) عشان
/// أي تغيير من الأدمن يسري فورًا من غير ما المستخدم يعمل login تاني.
/// </summary>
public interface IPermissionService
{
    // إضافة/حذف ملف تشييك: الأدمن والسوبر أدمن دايمًا، المستخدم بـ CanManageCheckFiles
    Task<bool> CanManageCheckFilesAsync(Guid userId, CancellationToken cancellationToken = default);

    // رفع ملف داتا: الأدمن والسوبر أدمن دايمًا، المستخدم بـ CanUploadDataFiles
    Task<bool> CanUploadDataFilesAsync(Guid userId, CancellationToken cancellationToken = default);

    // تنزيل/تعديل/إلحاق/حذف ملف داتا: المستخدم = ملف مملوك له + CanUploadDataFiles،
    // الأدمن = كل ملفات نطاقه الإداري، السوبر أدمن = كل الملفات
    Task<bool> CanManageDataFileAsync(Guid userId, Guid dataFileId, CancellationToken cancellationToken = default);

    // الفرز: null = مسموح، غير كده رسالة الرفض. المستخدم محتاج CanSort + إذن صريح لملف داتا واحد على الأقل
    Task<string?> GetSortDeniedReasonAsync(Guid userId, CancellationToken cancellationToken = default);

    // صلاحيات المستخدم الحالي عشان الفرونت يظهر/يخفي الحاجات — null لو المستخدم مش موجود أو موقوف
    Task<MyPermissionsDto?> GetMyPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);
}
