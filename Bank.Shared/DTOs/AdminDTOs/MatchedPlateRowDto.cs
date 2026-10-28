namespace Bank.Shared.DTOs.AdminDTOs
{
    public class MatchedPlateRowDto
    {
        public string Source { get; set; } = null!;          // "Archive" أو "LiveCheck" — الـ Id مش فريد بين الجدولين
        public long Id { get; set; }

        public Guid UserId { get; set; }
        public string UserFullName { get; set; } = null!;
        public string? RecorderName { get; set; }

        public string NormalizedPlate { get; set; } = null!;
        public string RawPlate { get; set; } = null!;
        public string? VehicleType { get; set; }
        public string? District { get; set; }
        public string? Street { get; set; }
        public string? Notes { get; set; }                   // الأرشيف بس
        public string? LocationLink { get; set; }
        public DateTime RecordedAt { get; set; }             // الأرشيف: RecordedAt — اللايف: CheckedAt

        public Guid? SessionId { get; set; }                 // الأرشيف: SourceSessionId — اللايف: CheckSessionId
        public Guid? AppendedToDataFileId { get; set; }      // الأرشيف بس
    }
}
