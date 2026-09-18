using Microsoft.AspNetCore.Http;

namespace Bank.Shared.DTOs.CheckFileDTos;

public record CheckFileSummaryDto(
    Guid Id,
    string FileName,
    int TotalPlates,
    bool IsActive,
    string UploaderName,
    DateTime CreatedAt);

public class CreateCheckFileRequestDto
{
    public IFormFile File { get; set; } = null!;
}
