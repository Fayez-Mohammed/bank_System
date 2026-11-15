namespace Bank.Shared.DTOs.SuperAdminDTOs;

public record DeleteUserResultDto(int TransferredDataFiles, int TransferredCheckFiles, string? TransferredToAdminName);
