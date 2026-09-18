using CsvHelper;
using CsvHelper.Configuration;
using ExcelDataReader;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Bank.Services.Helpers
{
    public sealed record ExcelSheetInfo(int Index, string Name, int RowCount);

    public sealed record ExcelSheetData(int Index, string Name, List<List<string?>> Rows);

    public static class ExcelSheetReader
    {
        /// <summary>بيرجّع أسامي الـ Sheets وعدد صفوفها بس — من غير ما يجهّز محتوى الخلايا (أخف على الذاكرة).</summary>
        public static List<ExcelSheetInfo> GetSheetsInfo(Stream stream, string extension)
        {
            if (extension == ".csv")
            {
                using var textReader = new StreamReader(stream);
                var rowCount = 0;
                while (textReader.ReadLine() is not null) rowCount++;
                return new List<ExcelSheetInfo> { new(0, "CSV", rowCount) };
            }

            var result = new List<ExcelSheetInfo>();
            using var reader = ExcelReaderFactory.CreateReader(stream);
            var sheetIndex = 0;

            do
            {
                var rowCount = 0;
                while (reader.Read()) rowCount++; // بنعد الصفوف بس من غير قراءة الخلايا
                result.Add(new ExcelSheetInfo(sheetIndex, reader.Name, rowCount));
                sheetIndex++;
            }
            while (reader.NextResult());

            return result;
        }

        /// <summary>بيقرأ محتوى الـ Sheets المختارة بس (أو كلها لو allSheets=true).</summary>
        public static List<ExcelSheetData> ReadSelectedSheets(
            Stream stream, string extension, bool allSheets, List<int>? selectedIndexes)
        {
            if (extension == ".csv")
            {
                using var textReader = new StreamReader(stream);
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = false,
                    MissingFieldFound = null,
                    BadDataFound = null
                };
                using var csv = new CsvReader(textReader, config);

                var rows = new List<List<string?>>();
                while (csv.Read())
                {
                    var record = csv.Parser.Record;
                    rows.Add(record?.Select(v => (string?)v).ToList() ?? new List<string?>());
                }

                return new List<ExcelSheetData> { new(0, "CSV", rows) };
            }

            var selectedSet = selectedIndexes is null ? null : new HashSet<int>(selectedIndexes);
            var result = new List<ExcelSheetData>();

            using var reader = ExcelReaderFactory.CreateReader(stream);
            var sheetIndex = 0;

            do
            {
                var include = allSheets || (selectedSet?.Contains(sheetIndex) ?? false);
                if (!include)
                {
                    sheetIndex++;
                    continue;
                }

                var rows = new List<List<string?>>();
                while (reader.Read())
                {
                    var row = new List<string?>(reader.FieldCount);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        row.Add(reader.GetValue(i)?.ToString()?.Trim());
                    }
                    rows.Add(row);
                }

                result.Add(new ExcelSheetData(sheetIndex, reader.Name, rows));
                sheetIndex++;
            }
            while (reader.NextResult());

            return result;
        }
    }

}
