using Bank.DAL.Models.BaseModels;
using NetTopologySuite.Geometries;

namespace Bank.DAL.Models.SystemModels;

 
public class RecordingGpsPoint : BaseEntityLong
{
    public Guid SessionId { get; set; }
    public RecordingSession Session { get; set; } = null!;

    public Point Location { get; set; } = null!;

    public DateTime CapturedAt { get; set; }

    public double? AccuracyMeters { get; set; }

     public bool IsManual { get; set; }
}
