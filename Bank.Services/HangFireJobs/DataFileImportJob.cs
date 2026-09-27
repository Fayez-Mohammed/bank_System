using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Bank.DAL.Context;
using Bank.Services.Interfaces;
using Bank.Shared.Enums;
using CsvHelper;
using CsvHelper.Configuration;
using ExcelDataReader;
using Hangfire;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bank.Services.HangFireJobs;

[Queue("bulk-import")]
public class DataFileImportJob
{
    private const int BatchSize = 25_000;
    private const int BulkCopyTimeoutSeconds = 600;

    private readonly AppDbContext _dbContext;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<DataFileImportJob> _logger;

    static DataFileImportJob()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public DataFileImportJob(AppDbContext dbContext, IFileStorageService fileStorage, ILogger<DataFileImportJob> logger)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public async Task ImportAsync(Guid dataFileId, Guid userId, string storagePath, bool isAppend, CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Import job started for DataFile {DataFileId} (append={IsAppend})", dataFileId, isAppend);

        var connectionString = _dbContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Could not resolve the database connection string.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        if (!isAppend)
        {
            var deleteStopwatch = Stopwatch.StartNew();
            var replaced = await DeleteHistoricalPlatesAsync(connection, dataFileId, cancellationToken);
            deleteStopwatch.Stop();
            _logger.LogInformation(
                "PHASE delete: cleared {Count} existing rows for DataFile {DataFileId} in {ElapsedMs}ms",
                replaced, dataFileId, deleteStopwatch.ElapsedMilliseconds);
        }

        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var parseAndInsertStopwatch = Stopwatch.StartNew();

            await using var stream = await _fileStorage.OpenReadAsync(storagePath, cancellationToken);
            var extension = Path.GetExtension(storagePath).ToLowerInvariant();

            int totalInserted;
            if (extension == ".csv")
            {
                using var textReader = new StreamReader(stream);
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    MissingFieldFound = null,
                    BadDataFound = null
                };
                using var csv = new CsvReader(textReader, config);
                await csv.ReadAsync();
                csv.ReadHeader();
                var map = DetectColumnsFromHeaders(csv.HeaderRecord ?? Array.Empty<string>());

                using var readerWrapper = new CsvDataReaderWrapper(csv, map, dataFileId, userId);
                totalInserted = await ExecuteBulkCopyAsync(connection, transaction, readerWrapper, cancellationToken);
            }
            else
            {
                using var excelReader = ExcelReaderFactory.CreateReader(stream);
                var map = DetectColumnsFromExcel(excelReader);
                using var readerWrapper = new ExcelDataReaderWrapper(excelReader, map, dataFileId, userId);
                totalInserted = await ExecuteBulkCopyAsync(connection, transaction, readerWrapper, cancellationToken);
            }

            parseAndInsertStopwatch.Stop();
            _logger.LogInformation(
                "PHASE parse+insert: {Rows} rows for DataFile {DataFileId} in {ElapsedMs}ms ({RowsPerSec:F0} rows/sec)",
                totalInserted, dataFileId, parseAndInsertStopwatch.ElapsedMilliseconds,
                totalInserted / Math.Max(1.0, parseAndInsertStopwatch.Elapsed.TotalSeconds));

            var commitStopwatch = Stopwatch.StartNew();

            await using (var updateCommand = connection.CreateCommand())
            {
                updateCommand.Transaction = transaction;
                updateCommand.CommandText = isAppend
                    ? "UPDATE data_files SET row_count = row_count + @rows, import_status = @status, import_error = NULL WHERE id = @id"
                    : "UPDATE data_files SET row_count = @rows, import_status = @status, is_active_for_filtering = 1, import_error = NULL WHERE id = @id";

                updateCommand.Parameters.AddWithValue("@rows", totalInserted);
                updateCommand.Parameters.AddWithValue("@status", nameof(DataFileImportStatus.Completed));
                updateCommand.Parameters.AddWithValue("@id", dataFileId);
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            commitStopwatch.Stop();

            totalStopwatch.Stop();
            _logger.LogInformation(
                "PHASE commit: {ElapsedMs}ms | TOTAL import for DataFile {DataFileId}: {TotalMs}ms ({TotalRows} rows)",
                commitStopwatch.ElapsedMilliseconds, dataFileId, totalStopwatch.ElapsedMilliseconds, totalInserted);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Import job failed for DataFile {DataFileId} after {ElapsedMs}ms", dataFileId, totalStopwatch.ElapsedMilliseconds);

            await _dbContext.DataFiles
                .Where(f => f.Id == dataFileId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(f => f.ImportStatus, DataFileImportStatus.Failed)
                    .SetProperty(f => f.ImportError, ex.Message),
                    cancellationToken);

            throw;
        }
    }

    [Queue("bulk-import")]
    public async Task DeleteAsync(Guid dataFileId, string storagePath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Delete job started for DataFile {DataFileId}", dataFileId);

        var connectionString = _dbContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Could not resolve the database connection string.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await DeleteHistoricalPlatesAsync(connection, dataFileId, cancellationToken);

        await _dbContext.DataFiles
            .Where(f => f.Id == dataFileId)
            .ExecuteDeleteAsync(cancellationToken);

        await _fileStorage.DeleteAsync(storagePath, cancellationToken);
        _logger.LogInformation("Delete job completed for DataFile {DataFileId}", dataFileId);
    }

    private static async Task<int> DeleteHistoricalPlatesAsync(
        SqlConnection connection, Guid dataFileId, CancellationToken cancellationToken)
    {
        // match_results بيشاور على historical_plates بـ Restrict، فلازم نمسح كل
        // match_results المرتبطة (من أي عملية فرز) قبل ما نمسح الصفوف نفسها،
        // وإلا الحذف الجماعي بيفشل لو فيه لوحات اتفرزت قبل كده.
        await using (var deleteMatchesCommand = connection.CreateCommand())
        {
            deleteMatchesCommand.CommandText =
                "DELETE mr FROM match_results mr " +
                "INNER JOIN historical_plates hp ON hp.id = mr.historical_plate_id " +
                "WHERE hp.data_file_id = @dataFileId";
            deleteMatchesCommand.Parameters.AddWithValue("@dataFileId", dataFileId);
            await deleteMatchesCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var totalDeleted = 0;
        int deletedInBatch;
        do
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"DELETE TOP ({BatchSize}) FROM historical_plates WHERE data_file_id = @dataFileId";
            command.Parameters.AddWithValue("@dataFileId", dataFileId);
            deletedInBatch = await command.ExecuteNonQueryAsync(cancellationToken);
            totalDeleted += deletedInBatch;
        }
        while (deletedInBatch > 0);

        return totalDeleted;
    }

    private static async Task<int> ExecuteBulkCopyAsync(
        SqlConnection connection, SqlTransaction transaction, IDataReader reader, CancellationToken cancellationToken)
    {
        using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, transaction)
        {
            DestinationTableName = "historical_plates",
            BatchSize = BatchSize,
            BulkCopyTimeout = BulkCopyTimeoutSeconds,
            EnableStreaming = true
        };

        bulkCopy.ColumnMappings.Add(0, "data_file_id");
        bulkCopy.ColumnMappings.Add(1, "user_id");
        bulkCopy.ColumnMappings.Add(2, "raw_plate");
        bulkCopy.ColumnMappings.Add(3, "normalized_plate");
        bulkCopy.ColumnMappings.Add(4, "vehicle_type");
        bulkCopy.ColumnMappings.Add(5, "district");
        bulkCopy.ColumnMappings.Add(6, "street");
        bulkCopy.ColumnMappings.Add(7, "location");
        bulkCopy.ColumnMappings.Add(8, "recorded_at");
        bulkCopy.ColumnMappings.Add(9, "notes");
        bulkCopy.ColumnMappings.Add(10, "recorder_name");

        await bulkCopy.WriteToServerAsync(reader, cancellationToken);
        return ((IRowCounter)reader).TotalReadRows;
    }

    private static ColumnMapping DetectColumnsFromExcel(IExcelDataReader reader)
    {
        var map = new ColumnMapping();
        while (reader.Read())
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var val = reader.GetValue(i)?.ToString()?.Trim() ?? string.Empty;
                AssignColumn(map, val, i);
            }
            if (map.PlateCol != -1) break;
        }
        return map;
    }

    private static ColumnMapping DetectColumnsFromHeaders(string[] headers)
    {
        var map = new ColumnMapping();
        for (int i = 0; i < headers.Length; i++)
        {
            AssignColumn(map, headers[i]?.Trim() ?? string.Empty, i);
        }
        return map;
    }

    private static void AssignColumn(ColumnMapping map, string header, int index)
    {
        if (header.Contains("لوحة") || header.Contains("اللوحة")) map.PlateCol = index;
        else if (header.Contains("ملاحظ")) map.NotesCol = index;
        else if (header.Contains("المسجل")) map.RecorderNameCol = index;
        else if (header.Contains("نوع") || header.Contains("السيارة")) map.VehicleTypeCol = index;
        else if (header.Equals("الشارع")) map.StreetCol = index;
        else if (header.Contains("الحي")) map.DistrictCol = index;
        else if (header.Contains("GPS") || header.Contains("الموقع") || header.Contains("رابط")) map.GpsCol = index;
        else if (header.Contains("تاريخ") || header.Contains("التسجيل")) map.RecordedAtCol = index;
    }

    private interface IRowCounter
    {
        int TotalReadRows { get; }
    }

    private sealed class ExcelDataReaderWrapper : DbDataReader, IRowCounter
    {
        private readonly IExcelDataReader _reader;
        private readonly ColumnMapping _map;
        private readonly Guid _dataFileId;
        private readonly Guid _userId;
        private readonly object?[] _currentValues = new object?[11];
        public int TotalReadRows { get; private set; }

        public ExcelDataReaderWrapper(IExcelDataReader reader, ColumnMapping map, Guid dataFileId, Guid userId)
        {
            _reader = reader;
            _map = map;
            _dataFileId = dataFileId;
            _userId = userId;
        }

        public override bool Read()
        {
            while (_reader.Read())
            {
                var rawPlate = _map.PlateCol != -1 ? _reader.GetValue(_map.PlateCol)?.ToString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(rawPlate) || rawPlate == "1" || rawPlate.Contains("لوحة"))
                    continue;

                _currentValues[0] = _dataFileId;
                _currentValues[1] = _userId;
                _currentValues[2] = rawPlate;
                _currentValues[3] = PlateNormalizer.Normalize(rawPlate);
                _currentValues[4] = _map.VehicleTypeCol != -1 ? _reader.GetValue(_map.VehicleTypeCol)?.ToString()?.Trim() : DBNull.Value;
                _currentValues[5] = _map.DistrictCol != -1 ? _reader.GetValue(_map.DistrictCol)?.ToString()?.Trim() : DBNull.Value;
                _currentValues[6] = _map.StreetCol != -1 ? _reader.GetValue(_map.StreetCol)?.ToString()?.Trim() : DBNull.Value;
                _currentValues[7] = _map.GpsCol != -1 ? _reader.GetValue(_map.GpsCol)?.ToString()?.Trim() : DBNull.Value;

                var recordedAtRaw = _map.RecordedAtCol != -1 ? _reader.GetValue(_map.RecordedAtCol)?.ToString()?.Trim() : null;
                _currentValues[8] = !string.IsNullOrWhiteSpace(recordedAtRaw) ? recordedAtRaw : DBNull.Value;

                _currentValues[9] = _map.NotesCol != -1 ? _reader.GetValue(_map.NotesCol)?.ToString()?.Trim() : DBNull.Value;
                _currentValues[10] = _map.RecorderNameCol != -1 ? _reader.GetValue(_map.RecorderNameCol)?.ToString()?.Trim() : DBNull.Value;

                TotalReadRows++;
                return true;
            }
            return false;
        }

        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));
        public override int Depth => 0;
        public override bool NextResult() => false;
        public override object GetValue(int ordinal) => _currentValues[ordinal] ?? DBNull.Value;
        public override int FieldCount => 11;
        public override bool HasRows => true;
        public override bool IsClosed => _reader.IsClosed;
        public override int RecordsAffected => -1;
        public override int GetValues(object[] values) { Array.Copy(_currentValues, values, 11); return 11; }
        public override bool IsDBNull(int ordinal) => _currentValues[ordinal] == null || _currentValues[ordinal] == DBNull.Value;
        public override string GetName(int ordinal) => ordinal.ToString();
        public override int GetOrdinal(string name) => int.Parse(name);
        public override string GetDataTypeName(int ordinal) => typeof(object).Name;
        public override Type GetFieldType(int ordinal) => typeof(object);
        public override System.Collections.IEnumerator GetEnumerator() => _currentValues.GetEnumerator();
        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => (char)GetValue(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
        public override string GetString(int ordinal) => (string)GetValue(ordinal);
        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    }

    private sealed class CsvDataReaderWrapper : DbDataReader, IRowCounter
    {
        private readonly CsvReader _csv;
        private readonly ColumnMapping _map;
        private readonly Guid _dataFileId;
        private readonly Guid _userId;
        private readonly object?[] _currentValues = new object?[11];
        public int TotalReadRows { get; private set; }

        public CsvDataReaderWrapper(CsvReader csv, ColumnMapping map, Guid dataFileId, Guid userId)
        {
            _csv = csv;
            _map = map;
            _dataFileId = dataFileId;
            _userId = userId;
        }

        public override bool Read()
        {
            while (_csv.Read())
            {
                var rawPlate = _map.PlateCol != -1 ? _csv.GetField(_map.PlateCol)?.Trim() : null;
                if (string.IsNullOrWhiteSpace(rawPlate) || rawPlate == "1" || rawPlate.Contains("لوحة"))
                    continue;

                _currentValues[0] = _dataFileId;
                _currentValues[1] = _userId;
                _currentValues[2] = rawPlate;
                _currentValues[3] = PlateNormalizer.Normalize(rawPlate);
                _currentValues[4] = _map.VehicleTypeCol != -1 ? _csv.GetField(_map.VehicleTypeCol)?.Trim() : DBNull.Value;
                _currentValues[5] = _map.DistrictCol != -1 ? _csv.GetField(_map.DistrictCol)?.Trim() : DBNull.Value;
                _currentValues[6] = _map.StreetCol != -1 ? _csv.GetField(_map.StreetCol)?.Trim() : DBNull.Value;
                _currentValues[7] = _map.GpsCol != -1 ? _csv.GetField(_map.GpsCol)?.Trim() : DBNull.Value;

                var recordedAtRaw = _map.RecordedAtCol != -1 ? _csv.GetField(_map.RecordedAtCol)?.Trim() : null;
                _currentValues[8] = !string.IsNullOrWhiteSpace(recordedAtRaw) ? recordedAtRaw : DBNull.Value;

                _currentValues[9] = _map.NotesCol != -1 ? _csv.GetField(_map.NotesCol)?.Trim() : DBNull.Value;
                _currentValues[10] = _map.RecorderNameCol != -1 ? _csv.GetField(_map.RecorderNameCol)?.Trim() : DBNull.Value;

                TotalReadRows++;
                return true;
            }
            return false;
        }

        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));
        public override int Depth => 0;
        public override bool NextResult() => false;
        public override object GetValue(int ordinal) => _currentValues[ordinal] ?? DBNull.Value;
        public override int FieldCount => 11;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override int GetValues(object[] values) { Array.Copy(_currentValues, values, 11); return 11; }
        public override bool IsDBNull(int ordinal) => _currentValues[ordinal] == null || _currentValues[ordinal] == DBNull.Value;
        public override string GetName(int ordinal) => ordinal.ToString();
        public override int GetOrdinal(string name) => int.Parse(name);
        public override string GetDataTypeName(int ordinal) => typeof(object).Name;
        public override Type GetFieldType(int ordinal) => typeof(object);
        public override System.Collections.IEnumerator GetEnumerator() => _currentValues.GetEnumerator();
        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => (char)GetValue(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
        public override string GetString(int ordinal) => (string)GetValue(ordinal);
        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
    }

    private class ColumnMapping
    {
        public int PlateCol { get; set; } = -1;
        public int VehicleTypeCol { get; set; } = -1;
        public int StreetCol { get; set; } = -1;
        public int DistrictCol { get; set; } = -1;
        public int GpsCol { get; set; } = -1;
        public int RecordedAtCol { get; set; } = -1;
        public int NotesCol { get; set; } = -1;
        public int RecorderNameCol { get; set; } = -1;
    }
}

//using System.Data;
//using System.Data.Common;
//using System.Diagnostics;
//using System.Globalization;
//using System.Text;
//using Bank.DAL.Context;
//using Bank.Services.Interfaces;
//using Bank.Shared.Enums;
//using CsvHelper;
//using CsvHelper.Configuration;
//using ExcelDataReader;
//using Hangfire;
//using Microsoft.Data.SqlClient;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Logging;

//namespace Bank.Services.HangFireJobs;

//[Queue("bulk-import")]
//public class DataFileImportJob
//{
//    private const int BatchSize = 25_000;
//    private const int BulkCopyTimeoutSeconds = 600;

//    private readonly AppDbContext _dbContext;
//    private readonly IFileStorageService _fileStorage;
//    private readonly ILogger<DataFileImportJob> _logger;

//    static DataFileImportJob()
//    {
//        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
//    }

//    public DataFileImportJob(AppDbContext dbContext, IFileStorageService fileStorage, ILogger<DataFileImportJob> logger)
//    {
//        _dbContext = dbContext;
//        _fileStorage = fileStorage;
//        _logger = logger;
//    }

//    [AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
//    public async Task ImportAsync(Guid dataFileId, Guid userId, string storagePath, bool isAppend, CancellationToken cancellationToken)
//    {
//        var totalStopwatch = Stopwatch.StartNew();
//        _logger.LogInformation("Import job started for DataFile {DataFileId} (append={IsAppend})", dataFileId, isAppend);

//        var connectionString = _dbContext.Database.GetConnectionString()
//            ?? throw new InvalidOperationException("Could not resolve the database connection string.");

//        await using var connection = new SqlConnection(connectionString);
//        await connection.OpenAsync(cancellationToken);

//        if (!isAppend)
//        {
//            var deleteStopwatch = Stopwatch.StartNew();
//            var replaced = await DeleteHistoricalPlatesAsync(connection, dataFileId, cancellationToken);
//            deleteStopwatch.Stop();
//            _logger.LogInformation(
//                "PHASE delete: cleared {Count} existing rows for DataFile {DataFileId} in {ElapsedMs}ms",
//                replaced, dataFileId, deleteStopwatch.ElapsedMilliseconds);
//        }

//        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

//        try
//        {
//            var parseAndInsertStopwatch = Stopwatch.StartNew();

//            await using var stream = await _fileStorage.OpenReadAsync(storagePath, cancellationToken);
//            var extension = Path.GetExtension(storagePath).ToLowerInvariant();

//            int totalInserted;
//            if (extension == ".csv")
//            {
//                using var textReader = new StreamReader(stream);
//                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
//                {
//                    HasHeaderRecord = true,
//                    MissingFieldFound = null,
//                    BadDataFound = null
//                };
//                using var csv = new CsvReader(textReader, config);
//                await csv.ReadAsync();
//                csv.ReadHeader();
//                var map = DetectColumnsFromHeaders(csv.HeaderRecord ?? Array.Empty<string>());

//                using var readerWrapper = new CsvDataReaderWrapper(csv, map, dataFileId, userId);
//                totalInserted = await ExecuteBulkCopyAsync(connection, transaction, readerWrapper, cancellationToken);
//            }
//            else
//            {
//                using var excelReader = ExcelReaderFactory.CreateReader(stream);
//                var map = DetectColumnsFromExcel(excelReader);
//                using var readerWrapper = new ExcelDataReaderWrapper(excelReader, map, dataFileId, userId);
//                totalInserted = await ExecuteBulkCopyAsync(connection, transaction, readerWrapper, cancellationToken);
//            }

//            parseAndInsertStopwatch.Stop();
//            _logger.LogInformation(
//                "PHASE parse+insert: {Rows} rows for DataFile {DataFileId} in {ElapsedMs}ms ({RowsPerSec:F0} rows/sec)",
//                totalInserted, dataFileId, parseAndInsertStopwatch.ElapsedMilliseconds,
//                totalInserted / Math.Max(1.0, parseAndInsertStopwatch.Elapsed.TotalSeconds));

//            var commitStopwatch = Stopwatch.StartNew();

//            await using (var updateCommand = connection.CreateCommand())
//            {
//                updateCommand.Transaction = transaction;
//                updateCommand.CommandText = isAppend
//                    ? "UPDATE data_files SET row_count = row_count + @rows, import_status = @status, import_error = NULL WHERE id = @id"
//                    : "UPDATE data_files SET row_count = @rows, import_status = @status, is_active_for_filtering = 1, import_error = NULL WHERE id = @id";

//                updateCommand.Parameters.AddWithValue("@rows", totalInserted);
//                updateCommand.Parameters.AddWithValue("@status", nameof(DataFileImportStatus.Completed));
//                updateCommand.Parameters.AddWithValue("@id", dataFileId);
//                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
//            }

//            await transaction.CommitAsync(cancellationToken);
//            commitStopwatch.Stop();

//            totalStopwatch.Stop();
//            _logger.LogInformation(
//                "PHASE commit: {ElapsedMs}ms | TOTAL import for DataFile {DataFileId}: {TotalMs}ms ({TotalRows} rows)",
//                commitStopwatch.ElapsedMilliseconds, dataFileId, totalStopwatch.ElapsedMilliseconds, totalInserted);
//        }
//        catch (Exception ex)
//        {
//            await transaction.RollbackAsync(cancellationToken);
//            _logger.LogError(ex, "Import job failed for DataFile {DataFileId} after {ElapsedMs}ms", dataFileId, totalStopwatch.ElapsedMilliseconds);

//            await _dbContext.DataFiles
//                .Where(f => f.Id == dataFileId)
//                .ExecuteUpdateAsync(setters => setters
//                    .SetProperty(f => f.ImportStatus, DataFileImportStatus.Failed)
//                    .SetProperty(f => f.ImportError, ex.Message),
//                    cancellationToken);

//            throw;
//        }
//    }

//    [Queue("bulk-import")]
//    public async Task DeleteAsync(Guid dataFileId, string storagePath, CancellationToken cancellationToken)
//    {
//        _logger.LogInformation("Delete job started for DataFile {DataFileId}", dataFileId);

//        var connectionString = _dbContext.Database.GetConnectionString()
//            ?? throw new InvalidOperationException("Could not resolve the database connection string.");

//        await using var connection = new SqlConnection(connectionString);
//        await connection.OpenAsync(cancellationToken);

//        await DeleteHistoricalPlatesAsync(connection, dataFileId, cancellationToken);

//        await _dbContext.DataFiles
//            .Where(f => f.Id == dataFileId)
//            .ExecuteDeleteAsync(cancellationToken);

//        await _fileStorage.DeleteAsync(storagePath, cancellationToken);
//        _logger.LogInformation("Delete job completed for DataFile {DataFileId}", dataFileId);
//    }

//    private static async Task<int> DeleteHistoricalPlatesAsync(
//        SqlConnection connection, Guid dataFileId, CancellationToken cancellationToken)
//    {
//        var totalDeleted = 0;
//        int deletedInBatch;
//        do
//        {
//            await using var command = connection.CreateCommand();
//            command.CommandText = $"DELETE TOP ({BatchSize}) FROM historical_plates WHERE data_file_id = @dataFileId";
//            command.Parameters.AddWithValue("@dataFileId", dataFileId);
//            deletedInBatch = await command.ExecuteNonQueryAsync(cancellationToken);
//            totalDeleted += deletedInBatch;
//        }
//        while (deletedInBatch > 0);

//        return totalDeleted;
//    }

//    private static async Task<int> ExecuteBulkCopyAsync(
//        SqlConnection connection, SqlTransaction transaction, IDataReader reader, CancellationToken cancellationToken)
//    {
//        using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, transaction)
//        {
//            DestinationTableName = "historical_plates",
//            BatchSize = BatchSize,
//            BulkCopyTimeout = BulkCopyTimeoutSeconds,
//            EnableStreaming = true
//        };

//        bulkCopy.ColumnMappings.Add(0, "data_file_id");
//        bulkCopy.ColumnMappings.Add(1, "user_id");
//        bulkCopy.ColumnMappings.Add(2, "raw_plate");
//        bulkCopy.ColumnMappings.Add(3, "normalized_plate");
//        bulkCopy.ColumnMappings.Add(4, "vehicle_type");
//        bulkCopy.ColumnMappings.Add(5, "district");
//        bulkCopy.ColumnMappings.Add(6, "street");
//        bulkCopy.ColumnMappings.Add(7, "location");
//        bulkCopy.ColumnMappings.Add(8, "recorded_at");

//        await bulkCopy.WriteToServerAsync(reader, cancellationToken);
//        return ((IRowCounter)reader).TotalReadRows;
//    }

//    private static ColumnMapping DetectColumnsFromExcel(IExcelDataReader reader)
//    {
//        var map = new ColumnMapping();
//        while (reader.Read())
//        {
//            for (int i = 0; i < reader.FieldCount; i++)
//            {
//                var val = reader.GetValue(i)?.ToString()?.Trim() ?? string.Empty;
//                AssignColumn(map, val, i);
//            }
//            if (map.PlateCol != -1) break;
//        }
//        return map;
//    }

//    private static ColumnMapping DetectColumnsFromHeaders(string[] headers)
//    {
//        var map = new ColumnMapping();
//        for (int i = 0; i < headers.Length; i++)
//        {
//            AssignColumn(map, headers[i]?.Trim() ?? string.Empty, i);
//        }
//        return map;
//    }

//    private static void AssignColumn(ColumnMapping map, string header, int index)
//    {
//        if (header.Contains("لوحة") || header.Contains("اللوحة")) map.PlateCol = index;
//        else if (header.Contains("نوع") || header.Contains("السيارة")) map.VehicleTypeCol = index;
//        else if (header.Equals("الشارع")) map.StreetCol = index;
//        else if (header.Contains("الحي")) map.DistrictCol = index;
//        else if (header.Contains("GPS") || header.Contains("الموقع") || header.Contains("رابط")) map.GpsCol = index;
//        else if (header.Contains("تاريخ") || header.Contains("التسجيل")) map.RecordedAtCol = index;
//    }

//    private interface IRowCounter
//    {
//        int TotalReadRows { get; }
//    }

//    private sealed class ExcelDataReaderWrapper : DbDataReader, IRowCounter
//    {
//        private readonly IExcelDataReader _reader;
//        private readonly ColumnMapping _map;
//        private readonly Guid _dataFileId;
//        private readonly Guid _userId;
//        private readonly object?[] _currentValues = new object?[9];
//        public int TotalReadRows { get; private set; }

//        public ExcelDataReaderWrapper(IExcelDataReader reader, ColumnMapping map, Guid dataFileId, Guid userId)
//        {
//            _reader = reader;
//            _map = map;
//            _dataFileId = dataFileId;
//            _userId = userId;
//        }

//        public override bool Read()
//        {
//            while (_reader.Read())
//            {
//                var rawPlate = _map.PlateCol != -1 ? _reader.GetValue(_map.PlateCol)?.ToString()?.Trim() : null;
//                if (string.IsNullOrWhiteSpace(rawPlate) || rawPlate == "1" || rawPlate.Contains("لوحة"))
//                    continue;

//                _currentValues[0] = _dataFileId;
//                _currentValues[1] = _userId;
//                _currentValues[2] = rawPlate;
//                _currentValues[3] = PlateNormalizer.Normalize(rawPlate);
//                _currentValues[4] = _map.VehicleTypeCol != -1 ? _reader.GetValue(_map.VehicleTypeCol)?.ToString()?.Trim() : DBNull.Value;
//                _currentValues[5] = _map.DistrictCol != -1 ? _reader.GetValue(_map.DistrictCol)?.ToString()?.Trim() : DBNull.Value;
//                _currentValues[6] = _map.StreetCol != -1 ? _reader.GetValue(_map.StreetCol)?.ToString()?.Trim() : DBNull.Value;
//                _currentValues[7] = _map.GpsCol != -1 ? _reader.GetValue(_map.GpsCol)?.ToString()?.Trim() : DBNull.Value;

//                var recordedAtRaw = _map.RecordedAtCol != -1 ? _reader.GetValue(_map.RecordedAtCol)?.ToString()?.Trim() : null;
//                _currentValues[8] = !string.IsNullOrWhiteSpace(recordedAtRaw) ? recordedAtRaw : DBNull.Value;

//                TotalReadRows++;
//                return true;
//            }
//            return false;
//        }

//        public override object this[int ordinal] => GetValue(ordinal);
//        public override object this[string name] => GetValue(GetOrdinal(name));
//        public override int Depth => 0;
//        public override bool NextResult() => false;
//        public override object GetValue(int ordinal) => _currentValues[ordinal] ?? DBNull.Value;
//        public override int FieldCount => 9;
//        public override bool HasRows => true;
//        public override bool IsClosed => _reader.IsClosed;
//        public override int RecordsAffected => -1;
//        public override int GetValues(object[] values) { Array.Copy(_currentValues, values, 9); return 9; }
//        public override bool IsDBNull(int ordinal) => _currentValues[ordinal] == null || _currentValues[ordinal] == DBNull.Value;
//        public override string GetName(int ordinal) => ordinal.ToString();
//        public override int GetOrdinal(string name) => int.Parse(name);
//        public override string GetDataTypeName(int ordinal) => typeof(object).Name;
//        public override Type GetFieldType(int ordinal) => typeof(object);
//        public override System.Collections.IEnumerator GetEnumerator() => _currentValues.GetEnumerator();
//        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
//        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
//        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
//        public override char GetChar(int ordinal) => (char)GetValue(ordinal);
//        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
//        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
//        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
//        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
//        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
//        public override string GetString(int ordinal) => (string)GetValue(ordinal);
//        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
//        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
//        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
//        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
//    }

//    private sealed class CsvDataReaderWrapper : DbDataReader, IRowCounter
//    {
//        private readonly CsvReader _csv;
//        private readonly ColumnMapping _map;
//        private readonly Guid _dataFileId;
//        private readonly Guid _userId;
//        private readonly object?[] _currentValues = new object?[9];
//        public int TotalReadRows { get; private set; }

//        public CsvDataReaderWrapper(CsvReader csv, ColumnMapping map, Guid dataFileId, Guid userId)
//        {
//            _csv = csv;
//            _map = map;
//            _dataFileId = dataFileId;
//            _userId = userId;
//        }

//        public override bool Read()
//        {
//            while (_csv.Read())
//            {
//                var rawPlate = _map.PlateCol != -1 ? _csv.GetField(_map.PlateCol)?.Trim() : null;
//                if (string.IsNullOrWhiteSpace(rawPlate) || rawPlate == "1" || rawPlate.Contains("لوحة"))
//                    continue;

//                _currentValues[0] = _dataFileId;
//                _currentValues[1] = _userId;
//                _currentValues[2] = rawPlate;
//                _currentValues[3] = PlateNormalizer.Normalize(rawPlate);
//                _currentValues[4] = _map.VehicleTypeCol != -1 ? _csv.GetField(_map.VehicleTypeCol)?.Trim() : DBNull.Value;
//                _currentValues[5] = _map.DistrictCol != -1 ? _csv.GetField(_map.DistrictCol)?.Trim() : DBNull.Value;
//                _currentValues[6] = _map.StreetCol != -1 ? _csv.GetField(_map.StreetCol)?.Trim() : DBNull.Value;
//                _currentValues[7] = _map.GpsCol != -1 ? _csv.GetField(_map.GpsCol)?.Trim() : DBNull.Value;

//                var recordedAtRaw = _map.RecordedAtCol != -1 ? _csv.GetField(_map.RecordedAtCol)?.Trim() : null;
//                _currentValues[8] = !string.IsNullOrWhiteSpace(recordedAtRaw) ? recordedAtRaw : DBNull.Value;

//                TotalReadRows++;
//                return true;
//            }
//            return false;
//        }

//        public override object this[int ordinal] => GetValue(ordinal);
//        public override object this[string name] => GetValue(GetOrdinal(name));
//        public override int Depth => 0;
//        public override bool NextResult() => false;
//        public override object GetValue(int ordinal) => _currentValues[ordinal] ?? DBNull.Value;
//        public override int FieldCount => 9;
//        public override bool HasRows => true;
//        public override bool IsClosed => false;
//        public override int RecordsAffected => -1;
//        public override int GetValues(object[] values) { Array.Copy(_currentValues, values, 9); return 9; }
//        public override bool IsDBNull(int ordinal) => _currentValues[ordinal] == null || _currentValues[ordinal] == DBNull.Value;
//        public override string GetName(int ordinal) => ordinal.ToString();
//        public override int GetOrdinal(string name) => int.Parse(name);
//        public override string GetDataTypeName(int ordinal) => typeof(object).Name;
//        public override Type GetFieldType(int ordinal) => typeof(object);
//        public override System.Collections.IEnumerator GetEnumerator() => _currentValues.GetEnumerator();
//        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
//        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
//        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
//        public override char GetChar(int ordinal) => (char)GetValue(ordinal);
//        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
//        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
//        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
//        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
//        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
//        public override string GetString(int ordinal) => (string)GetValue(ordinal);
//        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
//        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
//        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
//        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
//    }

//    private class ColumnMapping
//    {
//        public int PlateCol { get; set; } = -1;
//        public int VehicleTypeCol { get; set; } = -1;
//        public int StreetCol { get; set; } = -1;
//        public int DistrictCol { get; set; } = -1;
//        public int GpsCol { get; set; } = -1;
//        public int RecordedAtCol { get; set; } = -1;
//    }
//}
////public static class PlateNormalizer
////{
////    public static string Normalize(string rawPlate)
////    {
////        if (string.IsNullOrWhiteSpace(rawPlate)) return string.Empty;

////        return rawPlate.Replace(" ", "").Trim().ToUpperInvariant();
////    }
////}