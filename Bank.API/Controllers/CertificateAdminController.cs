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

    /// <summary>تفاصيل المزامنة: مرحلة الفهرسة الأولى، آخر تشغيل (صفحات/طلبات Drive/ملفات جديدة)، الطابور، والـ workers.</summary>
    [HttpGet("sync-status")]
    [ProducesResponseType(typeof(ApiResponse<CertificateSyncStatusDto>), 200)]
    public async Task<IActionResult> GetSyncStatus(CancellationToken cancellationToken)
    {
        var result = await _certificateService.GetSyncStatusAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>فحص حي لـ Drive (قراءة بس): Changes API، المجلدات المشتركة، آخر PDFs، وتغييرات مستنية في الـ feed.</summary>
    [HttpPost("drive-check")]
    [ProducesResponseType(typeof(ApiResponse<CertificateDriveCheckDto>), 200)]
    public async Task<IActionResult> DriveCheck(CancellationToken cancellationToken)
    {
        var result = await _certificateService.CheckDriveAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>إعادة الفهرسة المسطّحة (بعد الفهرسة الأولى). مابتعملش تكرار ولا استخراج للملفات القديمة.</summary>
    [HttpPost("index/rebuild")]
    [ProducesResponseType(typeof(ApiResponse<bool>), 200)]
    public async Task<IActionResult> RebuildIndex(CancellationToken cancellationToken)
    {
        var result = await _certificateService.RebuildIndexAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType(typeof(ApiResponse<RetryResultDto>), 200)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        var result = await _certificateService.RetryAsync(id, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>إعادة معالجة كل الشهادات الفاشلة (includeStuck=true يضيف العالقين أكتر من الـ lease والمتأجلة).</summary>
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
