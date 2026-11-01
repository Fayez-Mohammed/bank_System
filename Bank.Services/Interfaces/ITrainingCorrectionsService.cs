using Bank.Shared.DTOs.TrainingDTOs;

namespace Bank.Services.Interfaces;

public interface ITrainingCorrectionsService
{
    Task<PlateCorrectionFeedDto> GetAfterAsync(long afterId, int limit, CancellationToken cancellationToken = default);
}
