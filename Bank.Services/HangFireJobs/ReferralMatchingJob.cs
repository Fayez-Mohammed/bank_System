using Bank.DAL.Context;
using Bank.Shared.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Bank.Services.HangFireJobs;


[Queue("bulk-import")]
public class ReferralMatchingJob
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ReferralMatchingJob> _logger;
    private readonly IConfiguration _configuration;

    public ReferralMatchingJob(AppDbContext dbContext, ILogger<ReferralMatchingJob> logger, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _logger = logger;
        _configuration = configuration;
    }

    [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ProcessAsync(Guid referralJobId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Referral matching job started for ReferralJob {ReferralJobId}", referralJobId);

        var job = await _dbContext.ReferralJobs
            .AsNoTracking()
            .Where(j => j.Id == referralJobId)
            .Select(j => new { j.FilterType, j.GroupId, j.UserId, j.User.Role })
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            _logger.LogWarning("ReferralJob {ReferralJobId} not found — aborting matching job", referralJobId);
            return;
        }

        var connectionString = _configuration.GetConnectionString("DefaultConnection")
?? throw new InvalidOperationException("Could not resolve the database connection string.");


        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            if (job.FilterType == FilterType.New)
            {
                await MarkSkippedFromActiveCheckFileAsync(connection, referralJobId, job.GroupId, job.UserId, cancellationToken);
            }

            await InsertMatchResultsAsync(
                connection, referralJobId, job.GroupId, job.UserId, onlyGrantedFiles: job.Role == UserRole.User, cancellationToken);
            await MarkMatchedAsync(connection, referralJobId, cancellationToken);

            var skippedCount = await CountByStatusAsync(connection, referralJobId, ReferralPlateStatus.Skipped, cancellationToken);
            var matchedCount = await CountByStatusAsync(connection, referralJobId, ReferralPlateStatus.Matched, cancellationToken);

            await using (var updateCommand = connection.CreateCommand())
            {
                updateCommand.CommandText =
                    "UPDATE referral_jobs SET skipped_plates_count = @skipped, matched_plates_count = @matched, status = @status WHERE id = @id";
                updateCommand.Parameters.AddWithValue("@skipped", skippedCount);
                updateCommand.Parameters.AddWithValue("@matched", matchedCount);
                updateCommand.Parameters.AddWithValue("@status", nameof(JobStatus.Completed));
                updateCommand.Parameters.AddWithValue("@id", referralJobId);
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Referral matching job completed for ReferralJob {ReferralJobId}: skipped={Skipped}, matched={Matched}",
                referralJobId, skippedCount, matchedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Referral matching job failed for ReferralJob {ReferralJobId}", referralJobId);

            await using var failCommand = connection.CreateCommand();
            failCommand.CommandText = "UPDATE referral_jobs SET status = @status WHERE id = @id";
            failCommand.Parameters.AddWithValue("@status", nameof(JobStatus.Failed));
            failCommand.Parameters.AddWithValue("@id", referralJobId);
            await failCommand.ExecuteNonQueryAsync(cancellationToken);

            throw;
        }
    }

    private static async Task MarkSkippedFromActiveCheckFileAsync(
        NpgsqlConnection connection, Guid referralJobId, Guid? groupId, Guid userId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE referral_plates rp
            SET status = 'Skipped'
            WHERE rp.referral_job_id = @jobId
              AND EXISTS (
                    SELECT 1
                    FROM check_plates cp
                    JOIN check_files cf ON cf.id = cp.check_file_id
                    WHERE cf.is_active = true
                      AND cp.normalized_plate = rp.normalized_plate
                      AND (
                            (@groupId IS NOT NULL AND cf.group_id = @groupId)
                         OR (@groupId IS NULL AND cf.group_id IS NULL AND cf.uploaded_by = @userId)
                      )
              );";

        command.Parameters.AddWithValue("@jobId", referralJobId);
        command.Parameters.Add("@groupId", NpgsqlDbType.Uuid).Value = (object?)groupId ?? DBNull.Value;
        command.Parameters.AddWithValue("@userId", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertMatchResultsAsync(
        NpgsqlConnection connection, Guid referralJobId, Guid? groupId, Guid userId, bool onlyGrantedFiles,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO match_results
                (referral_job_id, historical_plate_id, normalized_plate, found_vehicle_type,
                 found_district, found_street, location, found_recorded_at, source_data_file_id, matched_at)
            SELECT
                @jobId, hp.id, rp.normalized_plate, hp.vehicle_type,
                hp.district, hp.street, hp.location, hp.recorded_at, hp.data_file_id, now()
            FROM referral_plates rp
            JOIN historical_plates hp ON hp.normalized_plate = rp.normalized_plate
            JOIN data_files df ON df.id = hp.data_file_id AND df.is_active_for_filtering = true
            WHERE rp.referral_job_id = @jobId
              AND rp.status <> 'Skipped'
              AND (
                    (@groupId IS NOT NULL AND df.group_id = @groupId)
                 OR (@groupId IS NULL AND df.group_id IS NULL AND df.owner_id = @userId)
              )
              AND (
                    NOT @onlyGrantedFiles
                  OR @groupId IS NULL
                 OR EXISTS (
                        SELECT 1 FROM user_data_file_permissions udp
                        WHERE udp.user_id = @userId AND udp.data_file_id = df.id
                    )
              );";

        command.Parameters.AddWithValue("@jobId", referralJobId);
        command.Parameters.Add("@groupId", NpgsqlDbType.Uuid).Value = (object?)groupId ?? DBNull.Value;
        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue("@onlyGrantedFiles", onlyGrantedFiles);
        command.CommandTimeout = 300;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkMatchedAsync(NpgsqlConnection connection, Guid referralJobId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE referral_plates rp
            SET status = 'Matched'
            WHERE rp.referral_job_id = @jobId
              AND rp.status <> 'Skipped'
              AND EXISTS (
                    SELECT 1 FROM match_results mr
                    WHERE mr.referral_job_id = @jobId
                      AND mr.normalized_plate = rp.normalized_plate
              );";
       

        command.Parameters.AddWithValue("@jobId", referralJobId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountByStatusAsync(
        NpgsqlConnection connection, Guid referralJobId, ReferralPlateStatus status, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM referral_plates WHERE referral_job_id = @jobId AND status = @status";
        command.Parameters.AddWithValue("@jobId", referralJobId);
        command.Parameters.AddWithValue("@status", status.ToString());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }
}
