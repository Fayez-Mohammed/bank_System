using Bank.DAL.Models.BaseModels;
using Bank.Shared.Enums;

namespace Bank.Services.Helpers;

/// <summary>
/// المستخدم + الجروب الفعلي بتاعه. الأدمن مربوط بالجروب عن طريق Groups.AdminId
/// (u.AdministeredGroup) ومش شرط يكون u.GroupId متسجل له، فالأدمن بياخد الجروب اللي بيديره
/// (لو نشط)، وأي حد تاني (User / SuperAdmin) بياخد u.GroupId زي ما هو.
/// </summary>
public sealed class ScopedUser
{
    public ApplicationUser User { get; init; } = null!;
    public Guid? GroupId { get; init; }
    public string? GroupName { get; init; }
}

public static class UserScopeQueries
{
    // بتتترجم لـ SQL (LEFT JOIN على groups) — استخدمها قبل الـ Where/Select على اليوزر
    public static IQueryable<ScopedUser> WithEffectiveGroup(this IQueryable<ApplicationUser> users) =>
        users.Select(u => new ScopedUser
        {
            User = u,
            GroupId = u.Role == UserRole.Admin && u.AdministeredGroup != null && u.AdministeredGroup.IsActive
                ? (Guid?)u.AdministeredGroup.Id
                : u.GroupId,
            GroupName = u.Role == UserRole.Admin && u.AdministeredGroup != null && u.AdministeredGroup.IsActive
                ? u.AdministeredGroup.Name
                : u.Group != null ? u.Group.Name : null
        });
}
