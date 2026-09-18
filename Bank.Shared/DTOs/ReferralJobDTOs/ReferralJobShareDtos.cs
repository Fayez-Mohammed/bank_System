namespace Bank.Shared.DTOs.ReferralJobDTOs;

public class AppendUnmatchedToCheckFileRequestDto
{
    public string? BankName { get; set; }
}

public record AppendUnmatchedResultDto(int AppendedCount, int SkippedAlreadyInCheckFileCount);

public record WhatsAppShareLinkDto(string ShareUrl, string MessagePreview);
