using Bank.DAL.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Bank.Services.Implementations.CertificateServices;

/// <summary>ملف Drive جاهز للفهرسة. Incoming = شهادة جديدة (Pending/New/في دفعة)، غير كده تاريخي (Indexed/Historical/BatchNo=0).</summary>
public sealed record IndexRow(
    string FileId, string Name, DateTime? CreatedAt, DateTime? ModifiedAt,
    string? ParentId, string? ParentName, long? Size, bool Incoming);

public sealed record IndexResult(int AlreadyIndexed, int InsertedNew, int InsertedHistorical, int Updated)
{
    public static readonly IndexResult Empty = new(0, 0, 0, 0);
}

/// <summary>
/// كل كتابات الفهرس بـ SQL مجمّع (statement واحد لكل صفحة بدل INSERT لكل ملف):
///  • INSERT .. ON CONFLICT (drive_file_id) DO NOTHING  => مفيش تكرار حتى لو مزامنتين اتقاطعوا.
///  • الملفات الموجودة بتتحدّث (اسم/مجلد/تعديل) من غير ما تتلمس حالتها أو بيانات استخراجها.
/// </summary>
public sealed class CertificateIndexWriter
{
    private readonly AppDbContext _db;

    public CertificateIndexWriter(AppDbContext db) => _db = db;

    private static DateTime? Utc(DateTime? d) => d.HasValue ? DateTime.SpecifyKind(d.Value, DateTimeKind.Utc) : null;

    private static NpgsqlParameter Arr(string name, NpgsqlDbType elementType, Array value) =>
        new(name, NpgsqlDbType.Array | elementType) { Value = value };

    private async Task<T> WithOpenConnectionAsync<T>(Func<NpgsqlConnection, Task<T>> action)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        var opened = false;
        if (conn.State != System.Data.ConnectionState.Open) { await conn.OpenAsync(); opened = true; }
        try { return await action(conn); }
        finally { if (opened) await conn.CloseAsync(); }
    }

    /// <param name="touch">true: حدّث last_seen_at لكل الموجود (Baseline/Reconcile). false: حدّث بس اللي اتغيّر.</param>
    public Task<IndexResult> UpsertAsync(
        IReadOnlyList<IndexRow> rows, DateTime seenAt, int batchNo, bool touch, CancellationToken ct)
    {
        if (rows.Count == 0) return Task.FromResult(IndexResult.Empty);

        return WithOpenConnectionAsync(async conn =>
        {
            var ids = rows.Select(r => r.FileId).ToArray();

            // 1) الموجود فعلاً
            var existing = new HashSet<string>(StringComparer.Ordinal);
            await using (var cmd = new NpgsqlCommand(
                "SELECT drive_file_id FROM certificates WHERE drive_file_id = ANY(@ids)", conn))
            {
                cmd.Parameters.Add(Arr("ids", NpgsqlDbType.Text, ids));
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct)) existing.Add(rd.GetString(0));
            }

            var toInsert = rows.Where(r => !existing.Contains(r.FileId)).ToList();
            var toUpdate = rows.Where(r => existing.Contains(r.FileId)).ToList();

            // 2) تحديث الميتاداتا للموجود (من غير لمس status/origin/batch/بيانات الاستخراج)
            var updated = 0;
            if (toUpdate.Count > 0)
            {
                await using var cmd = new NpgsqlCommand(@"
                    UPDATE certificates c SET
                        file_name          = left(v.name, 500),
                        parent_folder_id   = v.parent,
                        parent_folder_name = COALESCE(v.parent_name, c.parent_folder_name),
                        drive_modified_at  = v.modified,
                        size_bytes         = COALESCE(v.size, c.size_bytes),
                        last_seen_at       = @seen,
                        is_deleted_in_drive = false,
                        deleted_in_drive_at = NULL
                    FROM unnest(@ids, @names, @modified, @parents, @parentNames, @sizes)
                         AS v(id, name, modified, parent, parent_name, size)
                    WHERE c.drive_file_id = v.id
                      AND (@touch
                           OR c.file_name IS DISTINCT FROM left(v.name, 500)
                           OR c.parent_folder_id IS DISTINCT FROM v.parent
                           OR c.is_deleted_in_drive)", conn);
                cmd.Parameters.Add(Arr("ids", NpgsqlDbType.Text, toUpdate.Select(r => r.FileId).ToArray()));
                cmd.Parameters.Add(Arr("names", NpgsqlDbType.Text, toUpdate.Select(r => r.Name).ToArray()));
                cmd.Parameters.Add(Arr("modified", NpgsqlDbType.TimestampTz, toUpdate.Select(r => Utc(r.ModifiedAt)).ToArray()));
                cmd.Parameters.Add(Arr("parents", NpgsqlDbType.Text, toUpdate.Select(r => r.ParentId).ToArray()));
                cmd.Parameters.Add(Arr("parentNames", NpgsqlDbType.Text, toUpdate.Select(r => r.ParentName).ToArray()));
                cmd.Parameters.Add(Arr("sizes", NpgsqlDbType.Bigint, toUpdate.Select(r => r.Size).ToArray()));
                cmd.Parameters.AddWithValue("seen", NpgsqlDbType.TimestampTz, DateTime.SpecifyKind(seenAt, DateTimeKind.Utc));
                cmd.Parameters.AddWithValue("touch", NpgsqlDbType.Boolean, touch);
                updated = await cmd.ExecuteNonQueryAsync(ct);
            }

            // 3) إدخال الجديد. ON CONFLICT DO NOTHING = أمان ضد التزامن (مثلاً Changes + Scan في نفس اللحظة).
            int insertedNew = 0, insertedHistorical = 0;
            if (toInsert.Count > 0)
            {
                await using var cmd = new NpgsqlCommand(@"
                    INSERT INTO certificates
                        (id, drive_file_id, file_name, batch_no, discovered_at, provider, status, origin,
                         drive_created_at, drive_modified_at, parent_folder_id, parent_folder_name, size_bytes,
                         last_seen_at, is_manually_edited, is_deleted_in_drive, attempt_count)
                    SELECT gen_random_uuid(), v.id, left(v.name, 500),
                           CASE WHEN v.incoming THEN @batch ELSE 0 END,
                           now(), 'Sijil',
                           CASE WHEN v.incoming THEN 'Pending' ELSE 'Indexed' END,
                           CASE WHEN v.incoming THEN 'New' ELSE 'Historical' END,
                           COALESCE(v.created, now()), v.modified, v.parent, v.parent_name, v.size,
                           @seen, false, false, 0
                    FROM unnest(@ids, @names, @created, @modified, @parents, @parentNames, @sizes, @incoming)
                         AS v(id, name, created, modified, parent, parent_name, size, incoming)
                    ON CONFLICT (drive_file_id) DO NOTHING
                    RETURNING status", conn);
                cmd.Parameters.Add(Arr("ids", NpgsqlDbType.Text, toInsert.Select(r => r.FileId).ToArray()));
                cmd.Parameters.Add(Arr("names", NpgsqlDbType.Text, toInsert.Select(r => r.Name).ToArray()));
                cmd.Parameters.Add(Arr("created", NpgsqlDbType.TimestampTz, toInsert.Select(r => Utc(r.CreatedAt)).ToArray()));
                cmd.Parameters.Add(Arr("modified", NpgsqlDbType.TimestampTz, toInsert.Select(r => Utc(r.ModifiedAt)).ToArray()));
                cmd.Parameters.Add(Arr("parents", NpgsqlDbType.Text, toInsert.Select(r => r.ParentId).ToArray()));
                cmd.Parameters.Add(Arr("parentNames", NpgsqlDbType.Text, toInsert.Select(r => r.ParentName).ToArray()));
                cmd.Parameters.Add(Arr("sizes", NpgsqlDbType.Bigint, toInsert.Select(r => r.Size).ToArray()));
                cmd.Parameters.Add(Arr("incoming", NpgsqlDbType.Boolean, toInsert.Select(r => r.Incoming).ToArray()));
                cmd.Parameters.AddWithValue("batch", NpgsqlDbType.Integer, batchNo);
                cmd.Parameters.AddWithValue("seen", NpgsqlDbType.TimestampTz, DateTime.SpecifyKind(seenAt, DateTimeKind.Utc));

                await using var rd = await cmd.ExecuteReaderAsync(ct);
                while (await rd.ReadAsync(ct))
                {
                    if (rd.GetString(0) == "Pending") insertedNew++; else insertedHistorical++;
                }
            }

            // اللي كان "جديد" لكن سبقنا عليه سباق تاني بيتحسب already-indexed
            var raced = toInsert.Count - insertedNew - insertedHistorical;
            return new IndexResult(existing.Count + raced, insertedNew, insertedHistorical, updated);
        });
    }

    public Task<int> MarkDeletedAsync(IReadOnlyCollection<string> driveFileIds, CancellationToken ct)
    {
        if (driveFileIds.Count == 0) return Task.FromResult(0);
        return WithOpenConnectionAsync(async conn =>
        {
            await using var cmd = new NpgsqlCommand(@"
                UPDATE certificates SET is_deleted_in_drive = true, deleted_in_drive_at = now()
                WHERE drive_file_id = ANY(@ids) AND is_deleted_in_drive = false", conn);
            cmd.Parameters.Add(Arr("ids", NpgsqlDbType.Text, driveFileIds.ToArray()));
            return await cmd.ExecuteNonQueryAsync(ct);
        });
    }

    /// <summary>عدد الصفوف اللي ماتشافتش في آخر Reconcile كامل (مرشّحة للحذف).</summary>
    public Task<(int Candidates, int Active)> CountSweepCandidatesAsync(DateTime seenAt, CancellationToken ct) =>
        WithOpenConnectionAsync(async conn =>
        {
            await using var cmd = new NpgsqlCommand(@"
                SELECT count(*) FILTER (WHERE COALESCE(last_seen_at, '-infinity') < @seen),
                       count(*)
                FROM certificates WHERE is_deleted_in_drive = false", conn);
            cmd.Parameters.AddWithValue("seen", NpgsqlDbType.TimestampTz, DateTime.SpecifyKind(seenAt, DateTimeKind.Utc));
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            await rd.ReadAsync(ct);
            return ((int)rd.GetInt64(0), (int)rd.GetInt64(1));
        });

    public Task<int> SweepMissingAsync(DateTime seenAt, CancellationToken ct) =>
        WithOpenConnectionAsync(async conn =>
        {
            await using var cmd = new NpgsqlCommand(@"
                UPDATE certificates SET is_deleted_in_drive = true, deleted_in_drive_at = now()
                WHERE is_deleted_in_drive = false AND COALESCE(last_seen_at, '-infinity') < @seen", conn);
            cmd.Parameters.AddWithValue("seen", NpgsqlDbType.TimestampTz, DateTime.SpecifyKind(seenAt, DateTimeKind.Utc));
            return await cmd.ExecuteNonQueryAsync(ct);
        });
}
