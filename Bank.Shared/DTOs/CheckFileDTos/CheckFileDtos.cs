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
public class ReplaceBankPlatesRequestDto
{
    public IFormFile File { get; set; } = null!;
    public string BankName { get; set; } = null!;
}

public record ReplaceBankPlatesResultDto(
    string BankName, int DeletedCount, int InsertedCount, int SkippedDuplicateCount,
    string? PlateColumn, string? VehicleTypeColumn, string? ChassisColumn);