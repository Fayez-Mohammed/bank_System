using System.Security.Claims;
using Bank.Services.Interfaces;
using Bank.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Bank.API.Authorization;

public enum AppPermission
{
    ManageCheckFiles,
    UploadDataFiles,
    Sort
}

/// <summary>
/// Policies بتتشيك من الداتابيز في كل طلب (IPermissionService) — مش من الـ JWT،
/// فأي تغيير من الأدمن بيسري فورًا. حطها على أي endpoint بـ [Authorize(Policy = PermissionPolicies.X)].
/// الـ policy بتشيك الصلاحية العامة بس؛ القواعد اللي محتاجة ملف معيّن (ملكية ملف الداتا،
/// ملفات الفرز المسموحة) فاضلة جوه الـ services.
/// </summary>
public static class PermissionPolicies
{
    public const string ManageCheckFiles = "Permission.ManageCheckFiles";
    public const string UploadDataFiles = "Permission.UploadDataFiles";
    public const string Sort = "Permission.Sort";

    public static void AddPermissionPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(ManageCheckFiles, p => p.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(AppPermission.ManageCheckFiles)));
        options.AddPolicy(UploadDataFiles, p => p.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(AppPermission.UploadDataFiles)));
        options.AddPolicy(Sort, p => p.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(AppPermission.Sort)));
    }
}

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(AppPermission permission) => Permission = permission;
    public AppPermission Permission { get; }
}

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissions;

    public PermissionAuthorizationHandler(IPermissionService permissions)
    {
        _permissions = permissions;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return;

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;

        var deniedReason = requirement.Permission switch
        {
            AppPermission.ManageCheckFiles => await _permissions.CanManageCheckFilesAsync(userId, cancellationToken)
                ? null : "ليس لديك صلاحية إضافة أو حذف ملفات التشييك.",
            AppPermission.UploadDataFiles => await _permissions.CanUploadDataFilesAsync(userId, cancellationToken)
                ? null : "ليس لديك صلاحية رفع ملفات الداتا.",
            AppPermission.Sort => await _permissions.GetSortDeniedReasonAsync(userId, cancellationToken),
            _ => "صلاحية غير معروفة."
        };

        if (deniedReason is null)
            context.Succeed(requirement);
        else
            context.Fail(new AuthorizationFailureReason(this, deniedReason));
    }
}

/// <summary>
/// لما policy صلاحية ترفض الطلب، بيرجع 403 بنفس شكل ApiResponse ورسالة عربي
/// (زي رفض الـ services) بدل 403 فاضي. أي رفض تاني بيمشي بالسلوك العادي.
/// </summary>
public sealed class PermissionDeniedResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var reason = authorizeResult.Forbidden
            ? authorizeResult.AuthorizationFailure?.FailureReasons
                .FirstOrDefault(r => r.Handler is PermissionAuthorizationHandler)?.Message
            : null;

        if (reason is null)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(reason, null, StatusCodes.Status403Forbidden));
    }
}
