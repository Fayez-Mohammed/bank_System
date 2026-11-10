using System.Globalization;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Bank.Services.Helpers;

public sealed record DataFilePlateRow(
    string RawPlate, string? Notes, string? VehicleType, string? Street, string? District,
    string? Location, string? RecordedAt, string? RecorderName);

/// <summary>فشل متوقع في التصدير، رسالته آمنة تتعرض للمستخدم.</summary>
public sealed class DataFileExportException : Exception
{
    public DataFileExportException(string message) : base(message) { }
}

/// <summary>
/// بيصدّر صفوف ملف الداتا لملف xlsx واحد بالكتابة المتدفقة (من غير تحميل كل الصفوف في الذاكرة).
/// </summary>
public static class DataFileExcelExporter
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // الحد الأقصى لصفوف البيانات في شيت واحد (بعد صف الهيدر)
    private const int MaxDataRowsPerSheet = 1_048_575;

    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly string[] Headers =
        { "رقم اللوحة", "ملاحظات", "نوع السيارة", "الشارع", "الحي", "GPS", "تاريخ التسجيل", "اسم المسجّل" };
    private static readonly double[] Widths = { 18, 16, 16, 14, 14, 50, 20, 16 };
    private static readonly string[] ColumnLetters = { "A", "B", "C", "D", "E", "F", "G", "H" };

    public static string ToExportFileName(string? originalName)
    {
        var name = string.IsNullOrWhiteSpace(originalName)
            ? "data-file"
            : Path.GetFileNameWithoutExtension(originalName);

        if (string.IsNullOrWhiteSpace(name)) name = "data-file";
        return name + ".xlsx";
    }

    public static async Task<Stream> WriteAsync(IAsyncEnumerable<DataFilePlateRow> rows, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), $"datafile-export-{Guid.NewGuid():N}.xlsx");
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.SequentialScan);

        try
        {
            var tooManyRows = false;

            using (var doc = SpreadsheetDocument.Create(file, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = doc.AddWorkbookPart();
                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = BuildStylesheet();

                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var rowCount = 0;

                using (var sheet = new SheetWriter(worksheetPart, selected: true))
                {
                    await foreach (var row in rows.WithCancellation(ct))
                    {
                        if (rowCount >= MaxDataRowsPerSheet) { tooManyRows = true; break; }
                        sheet.AddRow(row);
                        rowCount++;
                    }
                }

                workbookPart.Workbook = new Workbook(new Sheets(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1U,
                    Name = "Sheet1"
                }));
            }

            if (tooManyRows)
                throw new DataFileExportException(
                    $"عدد الصفوف أكبر من الحد الأقصى لشيت Excel واحد ({MaxDataRowsPerSheet:N0} صف)، فمينفعش يتحمّل في شيت واحد.");

            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();       // DeleteOnClose بيمسح الملف المؤقت
            throw;
        }
    }

    private static Stylesheet BuildStylesheet()
    {
        var fonts = new Fonts(
            new Font(
                new FontSize { Val = 11 },
                new FontName { Val = "Calibri" }),
            new Font(
                new Bold(),
                new FontSize { Val = 11 },
                new FontName { Val = "Calibri" }),
            new Font(
                new Underline { Val = UnderlineValues.Single },
                new FontSize { Val = 11 },
                new Color { Rgb = "FF0563C1" },
                new FontName { Val = "Calibri" }))
        { Count = 3U };

        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }))
        { Count = 2U };

        var borders = new Borders(new Border()) { Count = 1U };

        var cellStyleFormats = new CellStyleFormats(
            new CellFormat { FontId = 0U, FillId = 0U, BorderId = 0U, NumberFormatId = 0U })
        { Count = 1U };

        var cellFormats = new CellFormats(
            new CellFormat { FontId = 0U, FillId = 0U, BorderId = 0U, NumberFormatId = 0U, FormatId = 0U },
            new CellFormat
            {
                FontId = 1U, FillId = 0U, BorderId = 0U, NumberFormatId = 0U, FormatId = 0U,
                ApplyFont = true
            },
            new CellFormat
            {
                FontId = 2U, FillId = 0U, BorderId = 0U, NumberFormatId = 0U, FormatId = 0U,
                ApplyFont = true
            })
        { Count = 3U };

        return new Stylesheet(fonts, fills, borders, cellStyleFormats, cellFormats);
    }

    private sealed class SheetWriter : IDisposable
    {
        private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        private readonly XmlWriter _xml;
        private int _row;

        public SheetWriter(WorksheetPart worksheetPart, bool selected)
        {
            var stream = worksheetPart.GetStream(FileMode.Create, FileAccess.Write);
            _xml = XmlWriter.Create(stream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                CloseOutput = false,
                Indent = false
            });

            _xml.WriteStartDocument(standalone: true);
            _xml.WriteStartElement("worksheet", Ns);

            _xml.WriteStartElement("sheetViews", Ns);
            _xml.WriteStartElement("sheetView", Ns);
            if (selected) _xml.WriteAttributeString("tabSelected", "1");
            _xml.WriteAttributeString("rightToLeft", "1");
            _xml.WriteAttributeString("workbookViewId", "0");
            _xml.WriteEndElement();
            _xml.WriteEndElement();

            _xml.WriteStartElement("cols", Ns);
            for (var i = 0; i < Widths.Length; i++)
            {
                _xml.WriteStartElement("col", Ns);
                _xml.WriteAttributeString("min", (i + 1).ToString(CultureInfo.InvariantCulture));
                _xml.WriteAttributeString("max", (i + 1).ToString(CultureInfo.InvariantCulture));
                _xml.WriteAttributeString("width", Widths[i].ToString(CultureInfo.InvariantCulture));
                _xml.WriteAttributeString("customWidth", "1");
                _xml.WriteEndElement();
            }
            _xml.WriteEndElement();

            _xml.WriteStartElement("sheetData", Ns);

            // صف الهيدر
            _row++;
            _xml.WriteStartElement("row", Ns);
            _xml.WriteAttributeString("r", _row.ToString(CultureInfo.InvariantCulture));
            for (var i = 0; i < Headers.Length; i++) WriteText(i, Headers[i], styleIndex: 1);
            _xml.WriteEndElement();
        }

        public void AddRow(DataFilePlateRow r)
        {
            _row++;
            _xml.WriteStartElement("row", Ns);
            _xml.WriteAttributeString("r", _row.ToString(CultureInfo.InvariantCulture));

            WriteText(0, r.RawPlate);
            WriteText(1, r.Notes);
            WriteText(2, r.VehicleType);
            WriteText(3, r.Street);
            WriteText(4, r.District);
            WriteGps(5, r.Location);
            WriteText(6, r.RecordedAt);
            WriteText(7, r.RecorderName);

            _xml.WriteEndElement();
        }

        private void WriteText(int col, string? value, int styleIndex = 0)
        {
            _xml.WriteStartElement("c", Ns);
            _xml.WriteAttributeString("r", ColumnLetters[col] + _row.ToString(CultureInfo.InvariantCulture));
            if (styleIndex > 0)
                _xml.WriteAttributeString("s", styleIndex.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrEmpty(value))
            {
                _xml.WriteAttributeString("t", "inlineStr");
                _xml.WriteStartElement("is", Ns);
                _xml.WriteStartElement("t", Ns);
                _xml.WriteAttributeString("xml", "space", null, "preserve");
                _xml.WriteString(Clean(value));
                _xml.WriteEndElement();
                _xml.WriteEndElement();
            }

            _xml.WriteEndElement();
        }

        private void WriteGps(int col, string? location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                WriteText(col, null);
                return;
            }

            _xml.WriteStartElement("c", Ns);
            _xml.WriteAttributeString("r", ColumnLetters[col] + _row.ToString(CultureInfo.InvariantCulture));
            _xml.WriteAttributeString("s", "2");
            _xml.WriteAttributeString("t", "str");
            _xml.WriteStartElement("f", Ns);
            _xml.WriteString("HYPERLINK(\"" + location.Replace("\"", "\"\"") + "\")");
            _xml.WriteEndElement();
            _xml.WriteStartElement("v", Ns);
            _xml.WriteString(Clean(location));
            _xml.WriteEndElement();
            _xml.WriteEndElement();
        }

        private static string Clean(string value)
        {
            var sb = new StringBuilder(value.Length);

            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];

                if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    sb.Append(ch).Append(value[++i]);
                }
                else if (XmlConvert.IsXmlChar(ch))
                {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            _xml.WriteEndElement(); // sheetData
            _xml.WriteEndElement(); // worksheet
            _xml.WriteEndDocument();
            _xml.Flush();
            _xml.Dispose();
        }
    }
}
