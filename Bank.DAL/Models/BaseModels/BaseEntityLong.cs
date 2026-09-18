namespace Bank.DAL.Models.BaseModels;

/// <summary>
/// Base class for the high-volume "leaf" tables whose SQL Server primary key
/// is a bigint IDENTITY column (check_plates, recorded_plates, live_check_logs,
/// historical_plates, match_results, refresh_tokens, audit_logs).
/// These are kept as bigint (not string/GUID) on purpose: they are inserted
/// in bulk (2.7M+ rows for historical_plates) and a bigint identity is far
/// cheaper to index/cluster on than a GUID or string surrogate key.
/// </summary>
public abstract class BaseEntityLong
{
    public long Id { get; set; }
}
