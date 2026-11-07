using Bank.Services.Interfaces.ICertificate;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.CertificateDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers;

/// <summary>حالة النظام، تشغيل المزامنة يدوياً، إعادة المعالجة، والتصحيح اليدوي.</summary>
[ApiController]
[Route("api/admin/certificates")]
[Authorize(Roles = "Admin,SuperAdmin")]
public class CertificateAdminController : ControllerBase
{
    private readonly ICertificateService _certificateService;

    public CertificateAdminController(ICertificateService certificateService)
    {
        _certificateService = certificateService;
    }

    /// <summary>حالة Drive (مُعدّ؟ شغال؟ إيميل الحساب) + آخر مزامنة + عدّادات الحالات. check=true لفحص حي مع جوجل.</summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<CertificateSystemStatusDto>), 200)]
    public async Task<IActionResult> GetStatus([FromQuery] bool check, CancellationToken cancellationToken)
    {
        var result = await _certificateService.GetSystemStatusAsync(check, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("sync")]
    [ProducesResponseType(typeof(ApiResponse<bool>), 200)]
    public async Task<IActionResult> TriggerSync(CancellationToken cancellationToken)
    {
        var result = await _certificateService.TriggerSyncAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>قايمة كاملة مسطّحة (طلبات قليلة) تصحّح الأسماء/المجلدات وتعلّم المحذوف. مفيش استخراج لأي ملف. بتتجدول يومياً.</summary>
    [HttpPost("reconcile")]
    [ProducesResponseType(typeof(ApiResponse<bool>), 200)]
    public async Task<IActionResult> TriggerReconcile(CancellationToken cancellationToken)
    {
        var result = await _certificateService.TriggerReconcileAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>فحص تشخيصي: هل القايمة المسطّحة والـ Changes API شغالين على حساب Drive ده؟ (قارن العدد بالـ Drive).</summary>
    [HttpGet("drive-probe")]
    [ProducesResponseType(typeof(ApiResponse<DriveProbeDto>), 200)]
    public async Task<IActionResult> DriveProbe(CancellationToken cancellationToken)
    {
        var result = await _certificateService.ProbeDriveAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>force=true لإعادة استخراج شهادة اتعدّلت يدوي (هيمسح التعديل).</summary>
    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType(typeof(ApiResponse<RetryResultDto>), 200)]
    public async Task<IActionResult> Retry(Guid id, [FromQuery] bool force, CancellationToken cancellationToken)
    {
        var result = await _certificateService.RetryAsync(id, force, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>إعادة معالجة كل الشهادات الفاشلة (includeStuck=true يضيف Processing العالقين من أكتر من ساعتين).</summary>
    [HttpPost("retry-failed")]
    [ProducesResponseType(typeof(ApiResponse<RetryResultDto>), 200)]
    public async Task<IActionResult> RetryFailed([FromQuery] bool includeStuck, CancellationToken cancellationToken)
    {
        var result = await _certificateService.RetryFailedAsync(includeStuck, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>تصحيح يدوي (لوحة/مصنع/موديل/لون/مؤجر).</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CertificateDetailDto>), 200)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCertificateDto dto, CancellationToken cancellationToken)
    {
        var result = await _certificateService.UpdateAsync(id, dto, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }
}
