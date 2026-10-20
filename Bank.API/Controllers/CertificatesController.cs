using System.Security.Claims;
using Bank.Services.Interfaces.ICertificate;
using Bank.Shared.DTOs;
using Bank.Shared.DTOs.CertificateDTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bank.API.Controllers;

/// <summary>
/// الشهادات: Drive واحد global، وكل مستخدم مسجّل (في مجموعة أو لا) يقدر يشوف ويبحث ويعرض وينزّل.
/// الفرز بس هو اللي بيتطلب صلاحية CanSort. (مفيش مشاركة: الشهادة نفسها بتمنع مشاركتها مع أي طرف آخر.)
/// </summary>
[ApiController]
[Route("api/certificates")]
[Authorize]
public class CertificatesController : ControllerBase
{
    private readonly ICertificateService _certificateService;
    private readonly ILogger<CertificatesController> _logger;

    public CertificatesController(ICertificateService certificateService, ILogger<CertificatesController> logger)
    {
        _certificateService = certificateService;
        _logger = logger;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    /// <summary>
    /// GET api/certificates?scope=latest-batch|today|all&amp;batchNo=&amp;searchTerm=&amp;status=&amp;pageNumber=&amp;pageSize=
    /// البحث: اسم الملف أو رقم اللوحة (مثال: ابم7263).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CertificateListItemDto>>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> GetAll([FromQuery] CertificateQueryDto query, CancellationToken cancellationToken)
    {
        var result = await _certificateService.ListAsync(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CertificateDetailDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _certificateService.GetAsync(id, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>الدفعات (كل دفعة = شهادات نزلت مع بعض في مزامنة واحدة)، الأحدث أولاً.</summary>
    [HttpGet("batches")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CertificateBatchDto>>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> GetBatches([FromQuery] PaginationParams pagination, CancellationToken cancellationToken)
    {
        var result = await _certificateService.ListBatchesAsync(pagination, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>ملخص أحدث دفعة (للزر "فرز على أحدث دفعة (N)").</summary>
    [HttpGet("batches/latest")]
    [ProducesResponseType(typeof(ApiResponse<CertificateBatchDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetLatestBatch(CancellationToken cancellationToken)
    {
        var result = await _certificateService.GetLatestBatchAsync(cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// فرز دفعة (أحدث دفعة لو BatchNo فاضي). بيعمل Referral Job بنفس نتيجة ملف الإحالة بالظبط
    /// (اسم الملف / النوع / اللون / المؤجر)، وتكمّل بـ api/referral-jobs/{id}/... زي أي إحالة.
    /// </summary>
    [HttpPost("sort")]
    [ProducesResponseType(typeof(ApiResponse<CertificateSortResultDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> Sort([FromBody] CertificateSortRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _certificateService.CreateSortAsync(CurrentUserId, request, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>عرض الـ PDF داخل المتصفح (inline).</summary>
    [HttpGet("{id:guid}/view")]
    [ProducesResponseType(typeof(FileStreamResult), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> View(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var (stream, _) = await _certificateService.OpenFileAsync(id, cancellationToken);
            return File(stream, "application/pdf");
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "View failed for certificate {Id}", id);
            return NotFound(ApiResponse<object>.Fail(ex.Message));
        }
    }

    /// <summary>تنزيل الـ PDF الأصلي. متاح لأي مستخدم مسجّل.</summary>
    [HttpGet("{id:guid}/download")]
    [ProducesResponseType(typeof(FileStreamResult), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var (stream, fileName) = await _certificateService.OpenFileAsync(id, cancellationToken);
            return File(stream, "application/pdf", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Download failed for certificate {Id}", id);
            return NotFound(ApiResponse<object>.Fail(ex.Message));
        }
    }
}
