namespace Bank.Services.Interfaces;

public interface IAiTranscriptionService
{
    Task<AiTranscriptionResult> TranscribeAsync(
        Stream audioStream, string fileName, string contentType, CancellationToken cancellationToken = default);
}

// مطابق تمامًا لشكل استجابة الـ AI الحقيقي الجديد
public record AiTranscriptionResult(int RecordingDurationMs, string TimestampReference, List<AiCarResult> Cars);

public record AiCarResult(
    string PlateNumber,
    int TimestampMs,
    int? TimestampStartMs,
    int? TimestampEndMs,
    string? VehicleType,
    string? Notes);

