using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Shared.DTOs.DataFileDTOs
{
    public class UpdateHistoricalPlateDto
    {
        public string RawPlate { get; set; } = null!;
        public string? VehicleType { get; set; }
        public string? District { get; set; }
        public string? Street { get; set; }
        public string? Location { get; set; }
        public string? RecordedAt { get; set; }
        public string? Notes { get; set; }
        public string? RecorderName { get; set; }
    }

    public record UpdateHistoricalPlateResponseDto(
        long HistoricalPlateId,
        string NormalizedPlate,
        int SyncedMatchResultsCount);

    public record DataFileImportStatusDto(
    Guid Id,
    string Status,          // Pending | Processing | Completed | Failed
    int RowCount,
    string? ImportError);
    public record DataFileSummaryDto(
        Guid Id,
        string FileName,
        int RowCount,
        bool IsActiveForFiltering,
        int SortOrder,
        bool RequiresPassword,
        string OwnerName,
        string ImportStatus,
        DateTime CreatedAt);

    public record DataFilesHeaderDto(
        int TotalFilesCount,
        long GroupTotalRows,
        long CurrentUserRows,
        string GroupName);

    public record DataFilesListResponseDto(
        DataFilesHeaderDto Header,
        List<DataFileSummaryDto> Items);

    public class CreateDataFileRequestDto
    {
        public IFormFile File { get; set; } = null!;
        public string? Password { get; set; }
       // public string? ColumnMapping { get; set; }   // JSON string, e.g. {"plate_col":8,"type_col":9}
    }

    public class AppendDataFileRequestDto
    {
        public IFormFile File { get; set; } = null!;
    }

    public record ReorderItemDto(Guid Id, int SortOrder);

    public class ReorderDataFilesRequestDto
    {
        public List<ReorderItemDto> Items { get; set; } = new();
    }
}
