using Bank.DAL.Context;
using Bank.DAL.Models.SystemModels;
using Bank.Services.Helpers;
using Bank.Services.Interfaces.ICertificate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Bank.Services.Implementations.CertificateServices;

/// <summary>ملف PDF جاهز يتسجّل في الفهرس.</summary>
public sealed record IndexedFileRow(
    string DriveFileId, string Name, DateTime? CreatedAt, DateTime? ModifiedAt, string? ParentId, long? Size);

public sealed record FileUpsertCounts(int NewPending, int NewIndexed, int Updated);

public sealed record ScopeChanges(IReadOnlyList<string> NowInScope, IReadOnlyList<string> NowOutOfScope);

/// <summary>
/// كل كتابة الفهرس في الـ DB (raw SQL بـ unnest + ON CONFLICT): صفحة كاملة (لحد 1000 عنصر) في round trip واحد،
/// ومن غير ما أي تكرار (نفس الصفحة مرتين، مزامنة اتقطعت واتعادت، Changes + reconcile لنفس الملف) يعمل صف تاني أو job تاني.
/// بيستخدم نفس connection الـ DbContext (ونفس الـ transaction لو مفتوحة).
/// </summary>
public sealed class CertificateIndexStore
{
    private const string StatusPending = "Pending";
    private const string StatusIndexed = "Indexed";

    private readonly AppDbContext _db;

    public CertificateIndexStore(AppDbContext db) => _db = db;

    // ============================ ADO helpers ============================

    private async Task<NpgsqlCommand> CommandAsync(string sql, CancellationToken ct)
    {
        await _db.Database.OpenConnectionAsync(ct);   // بيفضل مفتوح لحد ما الـ DbContext يتقفل
        var cmd = (NpgsqlCommand)_db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 300;
        cmd.Transaction = (NpgsqlTransaction?)_db.Database.CurrentTransaction?.GetDbTransaction();
        return cmd;
    }

    private static NpgsqlParameter TextArray(string name, IEnumerable<string?> values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = values.ToArray() };

    // ============================ Advisory lock ============================

    /// <summary>
    /// قفل على مستوى الـ session في Postgres (مش Hangfire lock بيستنى 30 دقيقة): لو في مزامنة شغالة بيرجع false فوراً.
    /// بيتفك لوحده لو الـ process مات (الـ connection بيتقفل).
    /// </summary>
    public async Task<bool> TryAcquireLockAsync(long key, CancellationToken ct)
    {
        await using var cmd = await CommandAsync("SELECT pg_try_advisory_lock(@k)", ct);
        cmd.Parameters.AddWithValue("k", key);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task ReleaseLockAsync(long key)
    {
        await using var cmd = await CommandAsync("SELECT pg_advisory_unlock(@k)", CancellationToken.None);
        cmd.Parameters.AddWithValue("k", key);
        await cmd.ExecuteScalarAsync();
    }

    // ============================ State ============================

    public async Task<CertificateSyncState> LoadStateAsync(CancellationToken ct)
    {
        var state = await _db.CertificateSyncStates.FirstOrDefaultAsync(s => s.Id == CertificateSyncState.SingletonId, ct);
        if (state != null) return state;

        await using (var cmd = await CommandAsync(
                         "INSERT INTO certificate_sync_state (id, baseline_phase, baseline_nodes_listed, baseline_files_listed, " +
                         "baseline_files_in_scope, consecutive_failures, updated_at) " +
                         "VALUES (@id, 'NotStarted', 0, 0, 0, 0, now()) ON CONFLICT (id) DO NOTHING", ct))
        {
            cmd.Parameters.AddWithValue("id", CertificateSyncState.SingletonId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        return await _db.CertificateSyncStates.FirstAsync(s => s.Id == CertificateSyncState.SingletonId, ct);
    }

    // ============================ Nodes (folders / shortcuts) ============================

    /// <summary>بيحوّل عنصر Drive لصف node لو مجلد أو اختصار لمجلد/PDF، وإلا null.</summary>
    public static CertificateDriveNode? ToNode(DriveItem item)
    {
        CertificateDriveNodeKind kind;
        if (item.IsFolder) kind = CertificateDriveNodeKind.Folder;
        else if (item.IsShortcut && item.ShortcutTargetMimeType == DriveItem.FolderMime) kind = CertificateDriveNodeKind.FolderShortcut;
        else if (item.IsShortcut && item.ShortcutTargetMimeType == DriveItem.PdfMime) kind = CertificateDriveNodeKind.FileShortcut;
        else return null;

        return new CertificateDriveNode
        {
            Id = item.Id,
            Kind = kind,
            Name = TextUtil.Truncate(item.Name, 500) ?? "",
            ParentId = item.ParentId,
            ShortcutTargetId = kind == CertificateDriveNodeKind.Folder ? null : item.ShortcutTargetId,
            IsSharedRoot = item.SharedWithMe
        };
    }

    /// <summary>Upsert لمجموعة nodes. بيرجّع عدد الصفوف اللي اتضافت أو اتغيّرت فعلاً (0 = الشجرة زي ما هي).</summary>
    public async Task<int> UpsertNodesAsync(IReadOnlyCollection<CertificateDriveNode> nodes, CancellationToken ct)
    {
        if (nodes.Count == 0) return 0;
        var list = nodes.GroupBy(n => n.Id).Select(g => g.Last()).ToList();

        const string sql = """
            INSERT INTO certificate_drive_nodes AS n
                (id, kind, name, parent_id, shortcut_target_id, is_shared_root, in_scope, needs_scan, is_removed, updated_at)
            SELECT u.id, u.kind, u.name, u.parent_id, u.target_id, u.shared, false, false, false, now()
            FROM unnest(@ids, @kinds, @names, @parents, @targets, @shared)
                 AS u(id, kind, name, parent_id, target_id, shared)
            ON CONFLICT (id) DO UPDATE SET
                kind = EXCLUDED.kind,
                name = EXCLUDED.name,
                parent_id = EXCLUDED.parent_id,
                shortcut_target_id = EXCLUDED.shortcut_target_id,
                is_shared_root = EXCLUDED.is_shared_root,
                is_removed = false,
                updated_at = now()
            WHERE n.kind IS DISTINCT FROM EXCLUDED.kind
               OR n.name IS DISTINCT FROM EXCLUDED.name
               OR n.parent_id IS DISTINCT FROM EXCLUDED.parent_id
               OR n.shortcut_target_id IS DISTINCT FROM EXCLUDED.shortcut_target_id
               OR n.is_shared_root IS DISTINCT FROM EXCLUDED.is_shared_root
               OR n.is_removed
            """;

        await using var cmd = await CommandAsync(sql, ct);
        cmd.Parameters.Add(TextArray("ids", list.Select(n => n.Id)));
        cmd.Parameters.Add(TextArray("kinds", list.Select(n => n.Kind.ToString())));
        cmd.Parameters.Add(TextArray("names", list.Select(n => n.Name)));
        cmd.Parameters.Add(TextArray("parents", list.Select(n => n.ParentId)));
        cmd.Parameters.Add(TextArray("targets", list.Select(n => n.ShortcutTargetId)));
        cmd.Parameters.Add(new NpgsqlParameter("shared", NpgsqlDbType.Array | NpgsqlDbType.Boolean)
        { Value = list.Select(n => n.IsSharedRoot).ToArray() });
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>مجلدات مش متاحة للحساب (404): بتتسجّل كـ removed عشان مانسألش جوجل عليها تاني.</summary>
    public async Task InsertUnreachableNodesAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        const string sql = """
            INSERT INTO certificate_drive_nodes (id, kind, name, is_shared_root, in_scope, needs_scan, is_removed, updated_at)
            SELECT id, 'Folder', '', false, false, false, true, now() FROM unnest(@ids) AS u(id)
            ON CONFLICT (id) DO NOTHING
            """;
        await using var cmd = await CommandAsync(sql, ct);
        cmd.Parameters.Add(TextArray("ids", ids.Distinct()));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> MarkNodesRemovedAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return 0;
        await using var cmd = await CommandAsync(
            "UPDATE certificate_drive_nodes SET is_removed = true, needs_scan = false, updated_at = now() " +
            "WHERE id = ANY(@ids) AND NOT is_removed", ct);
        cmd.Parameters.Add(TextArray("ids", ids.Distinct()));
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>من الـ IDs دي: مين معروف عندنا، ومين منهم جوه النطاق.</summary>
    public async Task<Dictionary<string, bool>> GetNodeScopeAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        var result = new Dictionary<string, bool>();
        if (ids.Count == 0) return result;

        await using var cmd = await CommandAsync(
            "SELECT id, in_scope AND NOT is_removed FROM certificate_drive_nodes WHERE id = ANY(@ids)", ct);
        cmd.Parameters.Add(TextArray("ids", ids.Distinct()));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result[reader.GetString(0)] = reader.GetBoolean(1);
        return result;
    }

    /// <summary>ملفات PDF من دول عليها اختصار (shortcut) جوه النطاق.</summary>
    public async Task<HashSet<string>> GetInScopeShortcutTargetsAsync(IReadOnlyCollection<string> fileIds, CancellationToken ct)
    {
        var result = new HashSet<string>();
        if (fileIds.Count == 0) return result;

        await using var cmd = await CommandAsync(
            "SELECT DISTINCT shortcut_target_id FROM certificate_drive_nodes " +
            "WHERE kind = 'FileShortcut' AND in_scope AND NOT is_removed AND shortcut_target_id = ANY(@ids)", ct);
        cmd.Parameters.Add(TextArray("ids", fileIds.Distinct()));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(reader.GetString(0));
        return result;
    }

    /// <summary>
    /// بيعيد حساب InScope لكل الشجرة في query واحدة (recursive CTE على الـ DB، مش على Drive).
    /// الجذور = المجلدات المشتركة مباشرة؛ الحواف = parent → child و shortcut → target.
    /// markNewFoldersForScan: المجلدات اللي دخلت النطاق جديد بتتعلّم NeedsScan (بعد الفهرسة الأولى بس).
    /// المجلدات اللي خرجت من النطاق: الشهادات اللي جوّاها بتتعلّم RemovedFromDrive.
    /// </summary>
    public async Task<ScopeChanges> RecomputeScopeAsync(bool markNewFoldersForScan, CancellationToken ct)
    {
        const string sql = """
            WITH RECURSIVE edges AS (
                SELECT parent_id AS p, id AS c FROM certificate_drive_nodes
                WHERE parent_id IS NOT NULL AND NOT is_removed
                UNION ALL
                SELECT id, shortcut_target_id FROM certificate_drive_nodes
                WHERE kind = 'FolderShortcut' AND shortcut_target_id IS NOT NULL AND NOT is_removed
            ), scope(id) AS (
                SELECT id FROM certificate_drive_nodes WHERE is_shared_root AND NOT is_removed
                UNION
                SELECT e.c FROM edges e JOIN scope s ON e.p = s.id
            ), computed AS (
                SELECT n.id, (s.id IS NOT NULL AND NOT n.is_removed) AS new_scope
                FROM certificate_drive_nodes n
                LEFT JOIN (SELECT DISTINCT id FROM scope) s ON s.id = n.id
            )
            UPDATE certificate_drive_nodes n SET
                in_scope = t.new_scope,
                needs_scan = CASE WHEN t.new_scope THEN (n.needs_scan OR (@mark AND n.kind = 'Folder')) ELSE false END,
                updated_at = now()
            FROM computed t
            WHERE t.id = n.id AND n.in_scope IS DISTINCT FROM t.new_scope
            RETURNING n.id, n.kind, n.in_scope
            """;

        var nowIn = new List<string>();
        var nowOut = new List<string>();
        await using (var cmd = await CommandAsync(sql, ct))
        {
            cmd.Parameters.AddWithValue("mark", markNewFoldersForScan);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetString(0);
                if (reader.GetBoolean(2)) nowIn.Add(id);
                else if (reader.GetString(1) == nameof(CertificateDriveNodeKind.Folder)) nowOut.Add(id);
            }
        }

        if (nowOut.Count > 0)
        {
            foreach (var chunk in nowOut.Chunk(5000))
                await MarkFilesRemovedByParentAsync(chunk, "الملف اتنقل برّه المجلدات المشتركة قبل الاستخراج.", ct);
        }

        return new ScopeChanges(nowIn, nowOut);
    }

    /// <summary>اختصارات ملفات دخلت النطاق جديد (من القائمة دي).</summary>
    public async Task<List<(string ShortcutId, string TargetId)>> GetFileShortcutsAsync(
        IReadOnlyCollection<string> nodeIds, CancellationToken ct)
    {
        var result = new List<(string, string)>();
        if (nodeIds.Count == 0) return result;
        await using var cmd = await CommandAsync(
            "SELECT id, shortcut_target_id FROM certificate_drive_nodes " +
            "WHERE id = ANY(@ids) AND kind = 'FileShortcut' AND shortcut_target_id IS NOT NULL AND in_scope AND NOT is_removed", ct);
        cmd.Parameters.Add(TextArray("ids", nodeIds));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add((reader.GetString(0), reader.GetString(1)));
        return result;
    }

    public async Task<List<string>> GetFoldersNeedingScanAsync(int limit, CancellationToken ct)
    {
        var result = new List<string>();
        await using var cmd = await CommandAsync(
            "SELECT id FROM certificate_drive_nodes WHERE needs_scan AND in_scope AND NOT is_removed " +
            "ORDER BY updated_at LIMIT @limit", ct);
        cmd.Parameters.AddWithValue("limit", limit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(reader.GetString(0));
        return result;
    }

    public async Task<int> CountFoldersNeedingScanAsync(CancellationToken ct)
    {
        await using var cmd = await CommandAsync(
            "SELECT count(*) FROM certificate_drive_nodes WHERE needs_scan AND in_scope AND NOT is_removed", ct);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task ClearNeedsScanAsync(string folderId, CancellationToken ct)
    {
        await using var cmd = await CommandAsync(
            "UPDATE certificate_drive_nodes SET needs_scan = false WHERE id = @id", ct);
        cmd.Parameters.AddWithValue("id", folderId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ============================ Files (certificates) ============================

    public async Task<int> NextBatchNoAsync(CancellationToken ct)
    {
        await using var cmd = await CommandAsync("SELECT COALESCE(MAX(batch_no), 0) + 1 FROM certificates", ct);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    /// <summary>
    /// Upsert صفحة ملفات. ملف جديد: Pending (BatchNo = batchNo) لو اتعمل في Drive بعد cutoff، وإلا Indexed (BatchNo = 0).
    /// ملف موجود: بيتحدث الاسم/المجلد/التاريخ بس (الحالة والبيانات المستخرجة مابتتلمسش)، ولو كان removed بيرجع.
    /// insertOnly: مابيحدّثش الموجود (لأهداف الاختصارات، عشان المجلد الحقيقي يفضل هو المتسجّل).
    /// </summary>
    public async Task<FileUpsertCounts> UpsertFilesAsync(
        IReadOnlyCollection<IndexedFileRow> files, DateTime cutoff, int batchNo, bool insertOnly, CancellationToken ct)
    {
        if (files.Count == 0) return new FileUpsertCounts(0, 0, 0);
        var list = files.GroupBy(f => f.DriveFileId).Select(g => g.Last()).ToList();
        var now = DateTime.UtcNow;

        const string sql = """
            WITH up AS (
                INSERT INTO certificates AS c
                    (drive_file_id, file_name, batch_no, status, drive_created_at, drive_modified_at, parent_folder_id, file_size)
                SELECT u.id, u.name,
                       CASE WHEN u.pending THEN @batch ELSE 0 END,
                       CASE WHEN u.pending THEN 'Pending' ELSE 'Indexed' END,
                       u.created, u.modified, u.parent, u.size
                FROM unnest(@ids, @names, @created, @modified, @parents, @sizes, @pending)
                     AS u(id, name, created, modified, parent, size, pending)
                ON CONFLICT (drive_file_id) DO UPDATE SET
                    file_name = EXCLUDED.file_name,
                    drive_modified_at = EXCLUDED.drive_modified_at,
                    parent_folder_id = EXCLUDED.parent_folder_id,
                    file_size = EXCLUDED.file_size,
                    is_removed_from_drive = false,
                    removed_from_drive_at = NULL
                WHERE NOT @insertOnly AND (
                       c.file_name IS DISTINCT FROM EXCLUDED.file_name
                    OR c.parent_folder_id IS DISTINCT FROM EXCLUDED.parent_folder_id
                    OR c.drive_modified_at IS DISTINCT FROM EXCLUDED.drive_modified_at
                    OR c.file_size IS DISTINCT FROM EXCLUDED.file_size
                    OR c.is_removed_from_drive)
                RETURNING c.status, (xmax = 0) AS inserted
            )
            SELECT count(*) FILTER (WHERE inserted AND status = 'Pending'),
                   count(*) FILTER (WHERE inserted AND status <> 'Pending'),
                   count(*) FILTER (WHERE NOT inserted)
            FROM up
            """;

        await using var cmd = await CommandAsync(sql, ct);
        cmd.Parameters.AddWithValue("batch", batchNo);
        cmd.Parameters.AddWithValue("insertOnly", insertOnly);
        cmd.Parameters.Add(TextArray("ids", list.Select(f => f.DriveFileId)));
        cmd.Parameters.Add(TextArray("names", list.Select(f => TextUtil.Truncate(f.Name, 500) ?? f.DriveFileId)));
        cmd.Parameters.Add(new NpgsqlParameter("created", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
        { Value = list.Select(f => f.CreatedAt ?? now).ToArray() });
        cmd.Parameters.Add(new NpgsqlParameter("modified", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
        { Value = list.Select(f => f.ModifiedAt).ToArray() });
        cmd.Parameters.Add(TextArray("parents", list.Select(f => f.ParentId)));
        cmd.Parameters.Add(new NpgsqlParameter("sizes", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
        { Value = list.Select(f => f.Size).ToArray() });
        cmd.Parameters.Add(new NpgsqlParameter("pending", NpgsqlDbType.Array | NpgsqlDbType.Boolean)
        { Value = list.Select(f => IsNew(f, cutoff, now)).ToArray() });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new FileUpsertCounts(
            (int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2));
    }

    /// <summary>القاعدة الوحيدة للتمييز: اتعمل في Drive بعد الحد الفاصل = جديد (محتاج استخراج).</summary>
    public static bool IsNew(IndexedFileRow f, DateTime cutoff, DateTime now) => (f.CreatedAt ?? now) >= cutoff;

    private const string RemoveSetClause = """
        is_removed_from_drive = true,
        removed_from_drive_at = now(),
        status = CASE WHEN status = 'Pending' THEN 'Failed' ELSE status END,
        status_note = CASE WHEN status = 'Pending' THEN @note ELSE status_note END,
        next_attempt_at = NULL
        """;

    /// <summary>الملف اتمسح / اتنقل للسلة / الصلاحية اتشالت. Pending بيبقى Failed عشان الدفعة ماتفضلش مستنياه.</summary>
    public async Task<int> MarkFilesRemovedAsync(IReadOnlyCollection<string> driveFileIds, string note, CancellationToken ct)
    {
        if (driveFileIds.Count == 0) return 0;
        await using var cmd = await CommandAsync(
            $"UPDATE certificates SET {RemoveSetClause} WHERE drive_file_id = ANY(@ids) AND NOT is_removed_from_drive", ct);
        cmd.Parameters.Add(TextArray("ids", driveFileIds.Distinct()));
        cmd.Parameters.AddWithValue("note", note);
        var n = await cmd.ExecuteNonQueryAsync(ct);
        if (n > 0) CertificateMetrics.SyncFilesRemoved.Add(n);
        return n;
    }

    private async Task<int> MarkFilesRemovedByParentAsync(IReadOnlyCollection<string> folderIds, string note, CancellationToken ct)
    {
        await using var cmd = await CommandAsync(
            $"UPDATE certificates SET {RemoveSetClause} WHERE parent_folder_id = ANY(@ids) AND NOT is_removed_from_drive", ct);
        cmd.Parameters.Add(TextArray("ids", folderIds));
        cmd.Parameters.AddWithValue("note", note);
        var n = await cmd.ExecuteNonQueryAsync(ct);
        if (n > 0) CertificateMetrics.SyncFilesRemoved.Add(n);
        return n;
    }
}
