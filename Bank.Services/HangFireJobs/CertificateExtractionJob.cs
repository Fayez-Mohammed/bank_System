using Bank.DAL.Context;
using Bank.Services.Implementations.CertificateServices;
using Bank.Shared.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Bank.Services.HangFireJobs;

/// <summary>
/// قديم: كان Job لكل شهادة. الاستخراج دلوقتي بيتم بـ CertificateExtractionWorker (الـ DB هو الـ queue) بدل آلاف jobs في Hangfire.
/// الكلاس ده متساب بس عشان أي Job قديمة لسه في Hangfire storage ما تتكسرش: بتتحول لـ "رجّعها Pending وصحّي الـ worker".
/// </summary>
[Queue("certificates")]
public class CertificateExtractionJob
{
    private readonly AppDbContext _db;
    private readonly ICertificateWorkGate _gate;

    public CertificateExtractionJob(AppDbContext db, ICertificateWorkGate gate)
    {
        _db = db;
        _gate = gate;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task ExtractAsync(Guid certificateId, CancellationToken ct)
    {
        var cert = await _db.Certificates.FirstOrDefaultAsync(c => c.Id == certificateId, ct);
        if (cert == null || cert.Origin != CertificateOrigin.New) return;

        if (cert.Status == CertificateStatus.Pending) _gate.Signal();
    }
}
