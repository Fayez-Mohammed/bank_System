using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Bank.Services.Helpers;
using Bank.Services.HangFireJobs;   // PlateNormalizer
using Bank.Shared.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using SkiaSharp;
using Tesseract;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using Bank.Services.Interfaces.ICertificate;

#pragma warning disable CS0618

namespace Bank.Services.Implementations.CertificateServices;

/// <summary>
/// استخراج محلي بالكامل من شهادات الاسترداد، مع كشف تلقائي للجهة المُصدِرة:
///  • توثيق (Tawtheeq): النص العربي مقروء مباشرة من PdfPig، من غير OCR.
///  • سجل (Sijil): PdfPig للنص اللاتيني/الأرقام + الحروف الإنجليزية للوحة، و Tesseract (ara) للحقول العربية.
/// بيطلّع: اللوحة، المصنع، الموديل، اللون، اسم المؤجر.
/// الـ Service Singleton لأن عندها Pool من محركات Tesseract (المحرك الواحد مش Thread-safe).
/// ملف ara.traineddata مطلوب لشهادات سجل فقط.
/// </summary>
public sealed class CertificateExtractor : ICertificateExtractor, IDisposable
{
    private const string PlateAlphabet = "ابحدرسصطعقكلمنهوىي";   // حروف اللوحات السعودية
    private const double TawtheeqReferenceWidth = 595.0;            // عرض صفحة A4 اللي اتضبطت عليه إحداثيات توثيق

    private static readonly HashSet<string> LessorHeaderWords =
        new(StringComparer.Ordinal) { "اسم", "المؤجر", "طالب", "التنفيذ", "الاسم", "مسلا", "مسا" };

    private readonly ILogger<CertificateExtractor> _logger;
    private readonly TesseractEnginePool _pool;

    public CertificateExtractor(IConfiguration configuration, ILogger<CertificateExtractor> logger)
    {
        _logger = logger;
        var configuredTessDir = configuration["Certificates:TessdataPath"];
        var tessDir = string.IsNullOrWhiteSpace(configuredTessDir)
            ? Path.Combine(AppContext.BaseDirectory, "tessdata")
            : configuredTessDir;
        var engines = int.TryParse(configuration["Certificates:OcrEngineCount"], out var n) && n > 0 ? n : 2;
        _pool = new TesseractEnginePool(tessDir, engines);
    }

    public void Dispose() => _pool.Dispose();

    // =====================================================================
    //  Public API
    // =====================================================================
    public async Task<CertificateExtractionResult> ExtractAsync(
        byte[] pdfBytes, string? fileName = null, CancellationToken ct = default)
    {
        CertificateProvider provider;
        try
        {
            provider = DetectProvider(pdfBytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException("تعذّر قراءة الـ PDF: " + ex.Message, ex);
        }

        return provider == CertificateProvider.Tawtheeq
            ? ExtractTawtheeq(pdfBytes, fileName)
            : await ExtractSijilAsync(pdfBytes, ct);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // =====================================================================
    //  Provider detection
    // =====================================================================
    private static CertificateProvider DetectProvider(byte[] pdfBytes)
    {
        using var doc = PdfDocument.Open(pdfBytes);
        var page = doc.GetPage(1);
        var text = string.Join(" ", page.GetWords().Select(w => w.Text));

        // معايير حاسمة لشهادات توثيق
        if (text.Contains("ARN") ||
            text.Contains("TAW") ||
            text.Contains("توثيق") ||
            text.Contains("سند تنفيذي") ||
            Regex.IsMatch(text, @"CRN000\d+"))
        {
            return CertificateProvider.Tawtheeq;
        }

        return CertificateProvider.Sijil;
    }

    // =====================================================================
    //  توثيق: استخراج نصي مباشر (من غير OCR)
    // =====================================================================
    private CertificateExtractionResult ExtractTawtheeq(byte[] pdfBytes, string? fileName)
    {
        using var doc = PdfDocument.Open(pdfBytes);
        var page = doc.GetPage(1);
        var words = page.GetWords().ToList();
        double k = page.Width / TawtheeqReferenceWidth;

        // ---------- 1) رقم السند (للتحقق إن الملف شهادة فعلاً) + الـ VIN ----------
        var fullText = string.Join(" ", words.Select(w => w.Text));
        var certMatch = Regex.Match(fullText, @"(?<!\d)10\d{5}(?!\d)");
        var certNo = certMatch.Success ? certMatch.Value : null;

        var vinWord = words.FirstOrDefault(w =>
            Regex.IsMatch(w.Text, @"(?<![A-Z0-9])[A-HJ-NPR-Z0-9]{17}(?![A-Z0-9])"));
        if (vinWord == null)
            throw new InvalidDataException("شهادة توثيق بدون رقم هيكل (VIN): تعذّر تحديد جدول بيانات الأصل.");

        var vin = Regex.Match(vinWord.Text, @"[A-HJ-NPR-Z0-9]{17}").Value;
        double vinY = vinWord.BoundingBox.Bottom;

        // ---------- 2) اسم المؤجر ----------
        // بين عنوان "الصفة في العقد: المؤجر" وعنوان "الصفة في العقد: المستأجر"، على يسار كلمة "الاسم" في نفس السطر.
        string lessor = "";

        var lessorHeader = words.FirstOrDefault(w =>
            (w.Text.Contains("المؤجر") || w.Text.Contains("رجؤمل")) &&
            words.Any(o => Math.Abs(o.BoundingBox.Bottom - w.BoundingBox.Bottom) < 5 &&
                           (o.Text.Contains("الصفة") || o.Text.Contains("ةفصل"))));

        var lesseeHeader = words.FirstOrDefault(w =>
            (w.Text.Contains("المستأجر") || w.Text.Contains("رجأتسمل")) &&
            words.Any(o => Math.Abs(o.BoundingBox.Bottom - w.BoundingBox.Bottom) < 5 &&
                           (o.Text.Contains("الصفة") || o.Text.Contains("ةفصل"))));

        if (lessorHeader != null)
        {
            double topY = lessorHeader.BoundingBox.Bottom;
            double bottomY = lesseeHeader != null ? lesseeHeader.BoundingBox.Top : topY - 120;

            var nameLabel = words.FirstOrDefault(w =>
                w.BoundingBox.Bottom < topY &&
                w.BoundingBox.Bottom > bottomY &&
                w.BoundingBox.Left >= page.Width * 0.70 &&                 // خانة الاسم في أقصى اليمين
                (w.Text.Contains("الاسم") || w.Text.Contains("مسلا") || w.Text.StartsWith("مسا")));

            if (nameLabel != null)
            {
                double labelRowY = nameLabel.BoundingBox.Bottom;
                var lessorWords = words
                    .Where(w => Math.Abs(w.BoundingBox.Bottom - labelRowY) <= 5)                       // نفس السطر
                    .Where(w => w.BoundingBox.Right < nameLabel.BoundingBox.Left &&
                                w.BoundingBox.Left >= page.Width * 0.40)
                    .OrderByDescending(w => w.BoundingBox.Left)                                        // من اليمين لليسار
                    .Select(w => w.Text);

                lessor = CleanLessorName(NormalizeTawtheeqTokens(lessorWords));
            }
        }

        // ---------- 3) جدول الأصل (من صف الـ VIN وحتى 55 نقطة تحته) ----------
        var assetWords = words.Where(w => w.BoundingBox.Bottom >= vinY - 55 && w.BoundingBox.Top <= vinY + 20).ToList();

        var rows = new List<List<Word>>();
        foreach (var w in assetWords.OrderByDescending(w => w.BoundingBox.Bottom))
        {
            var r = rows.FirstOrDefault(row => Math.Abs(row[0].BoundingBox.Bottom - w.BoundingBox.Bottom) < 6);
            if (r == null) rows.Add(new List<Word> { w });
            else r.Add(w);
        }

        int vRow = rows.FindIndex(r => r.Any(w => w == vinWord || w.Text.Contains(vin)));
        if (vRow == -1) vRow = 0;

        string GetCellText(int rowOffset, double minX, double maxX)
        {
            int targetRow = vRow + rowOffset;
            if (targetRow >= rows.Count) return "";
            var cellWords = rows[targetRow]
                .Where(w =>
                {
                    double midX = (w.BoundingBox.Left + w.BoundingBox.Right) / 2.0;
                    return midX >= minX * k && midX <= maxX * k;
                })
                .OrderByDescending(w => w.BoundingBox.Left)   // من اليمين لليسار
                .Select(w => w.Text);

            return NormalizeTawtheeqTokens(cellWords);
        }

        // الصف 0 (صف الـ VIN): الموديل في العمود الأوسط
        // الصف 1: اللوحة في العمود الأوسط
        // الصف 2: اللون في العمود الأوسط | المصنع في العمود الأيمن
        string rawModel = GetCellText(0, 180, 380);
        string rawPlate = GetCellText(1, 180, 380);
        string rawColor = GetCellText(2, 180, 380);
        string rawMake = GetCellText(2, 380, 560);

        var makeV = Snap(rawMake, CertificateLexicon.Makes);
        var modelV = Snap(rawModel, CertificateLexicon.Models);
        var colorV = ExtractBestColor(rawColor, CertificateLexicon.Colors);

        // ---------- 4) اللوحة ----------
        var digitsMatch = Regex.Match(rawPlate, @"\d+");
        string plateDigits = digitsMatch.Success ? digitsMatch.Value : "";
        var digitsFromFileName = false;
        if (string.IsNullOrEmpty(plateDigits) && !string.IsNullOrEmpty(fileName))
        {
            var fnDigits = Regex.Match(fileName, @"\d{3,4}");
            if (fnDigits.Success) { plateDigits = fnDigits.Value; digitsFromFileName = true; }
        }

        string plateLetters = string.Concat(Regex.Matches(rawPlate, @"[\u0621-\u064A]").Select(m => m.Value));

        // ---------- 5) المراجعة ----------
        var reasons = new List<string>();
        if (string.IsNullOrEmpty(certNo)) reasons.Add("رقم السند غير موجود");
        if (string.IsNullOrEmpty(plateDigits)) reasons.Add("أرقام اللوحة غير موجودة");
        if (digitsFromFileName) reasons.Add("أرقام اللوحة اتاخدت من اسم الملف (مش من نص الشهادة)");
        if (string.IsNullOrEmpty(plateLetters)) reasons.Add("حروف اللوحة غير موجودة");
        if (string.IsNullOrEmpty(lessor)) reasons.Add("اسم المؤجر غير موجود");

        var normalized = string.IsNullOrEmpty(plateLetters) && string.IsNullOrEmpty(plateDigits)
            ? null
            : PlateNormalizer.Normalize(plateLetters + plateDigits);

        _logger.LogInformation(
            "Tawtheeq certificate extracted natively: plate={Plate} lessor={Lessor} review={Review}",
            plateLetters + plateDigits, lessor, reasons.Count > 0);

        return new CertificateExtractionResult(
            CertificateProvider.Tawtheeq,
            plateLetters, NullIfEmpty(plateDigits), normalized,
            NullIfEmpty(makeV.Value), NullIfEmpty(modelV.Value), NullIfEmpty(colorV.Value), NullIfEmpty(lessor),
            reasons.Count > 0, reasons);
    }

    private static string NormalizeTawtheeqTokens(IEnumerable<string> words)
    {
        var cleanTokens = new List<string>();
        foreach (var raw in words)
        {
            var w = raw.Replace(":", "").Replace("ـ", "").Trim();
            if (string.IsNullOrWhiteSpace(w)) continue;

            // استبعاد العناوين الثابتة المعتدلة والمعكوسة
            if (CertificateLexicon.TawtheeqStopWords.Contains(w)) continue;

            // الأرقام والكلمات الإنجليزية تبقى كما هي
            if (w.All(c => char.IsDigit(c) || char.IsAsciiLetter(c)))
            {
                cleanTokens.Add(w);
                continue;
            }

            // الكلمات العربية تُعكس حروفها لتعديل الترتيب البصري المعكوس
            var chars = w.ToCharArray();
            Array.Reverse(chars);
            var reversed = new string(chars);

            if (!CertificateLexicon.TawtheeqStopWords.Contains(reversed))
                cleanTokens.Add(reversed);
        }

        return StripHeaderPrefixes(string.Join(" ", cleanTokens).Trim());
    }

    private static string StripHeaderPrefixes(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        string[] prefixes =
        {
            "نوع الأصل", "نوع الاصل", "نوع المركبة", "نوع المركبه", "نوع",
            "المصنع", "الماركة", "الماركه", "الموديل", "طراز", "ماركة",
            "لون الأصل", "لون الاصل", "لون المركبة", "لون المركبه", "لون",
            "رقم اللوحة", "رقم اللوحه", "رقم الهيكل", "رقم", "الاسم"
        };

        string cleaned = text.Replace(":", " ").Trim();
        foreach (var p in prefixes.OrderByDescending(p => p.Length))
        {
            if (cleaned.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(p.Length).Trim();
        }

        return cleaned;
    }

    /// <summary>
    /// تنظيف اسم المؤجر من بقايا العناوين. بنشيل كلمات العناوين كـ tokens كاملة (مش substring)،
    /// وأي حرف منفصل (ا م و ر) متبقي من حواف العنوان بنشيله من الأطراف بس، عشان "و" جوه اسم شركة ماتتشالش.
    /// </summary>
    private static string CleanLessorName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var tokens = text.Replace(":", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !LessorHeaderWords.Contains(t))
            .ToList();

        while (tokens.Count > 0 && tokens[0].Length == 1 && "امور".Contains(tokens[0])) tokens.RemoveAt(0);
        while (tokens.Count > 0 && tokens[^1].Length == 1 && "امور".Contains(tokens[^1])) tokens.RemoveAt(tokens.Count - 1);

        return string.Join(" ", tokens);
    }

    // =====================================================================
    //  سجل: PdfPig (هندسة + لاتيني) + OCR محلي للحقول العربية
    // =====================================================================
    private async Task<CertificateExtractionResult> ExtractSijilAsync(byte[] pdfBytes, CancellationToken ct)
    {
        CertLayout lay;
        try
        {
            lay = ReadSijilLayout(pdfBytes);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException("تعذّر قراءة الـ PDF: " + ex.Message, ex);
        }

        if (lay.CertNo == null && lay.Vin == null)
            throw new InvalidDataException("الملف مش شهادة استرداد معروفة (مفيش رقم شهادة ولا VIN).");
        if (lay.Vin == null || !lay.Cells.ContainsKey("plate"))
            throw new InvalidDataException("شكل الشهادة مش مطابق للقالب المعروف (الـ layout مش متعرّف عليه).");

        using var lease = await _pool.AcquireAsync(ct);
        using var ms = new MemoryStream(pdfBytes);
        using var bmp = Conversion.ToImage(ms, options: new RenderOptions { Dpi = 300 });
        double sx = bmp.Width / lay.PageW, sy = bmp.Height / lay.PageH;

        var plate = RunOcr(lease.Engine, bmp, sx, sy, lay, "plate", true, lay.PlateGlyphs > 0 ? lay.PlateGlyphs : null, ct);
        var make = RunOcr(lease.Engine, bmp, sx, sy, lay, "make", false, null, ct);
        var model = RunOcr(lease.Engine, bmp, sx, sy, lay, "model", false, null, ct);
        var color = RunOcr(lease.Engine, bmp, sx, sy, lay, "color", false, null, ct);
        var lessorOcr = RunOcr(lease.Engine, bmp, sx, sy, lay, "lessor", false, null, ct);

        var makeV = Snap(make.Text, CertificateLexicon.Makes);
        var modelV = Snap(model.Text, CertificateLexicon.Models);
        var colorV = ExtractBestColor(color.Text, CertificateLexicon.Colors);
        var lessor = CleanLessorName(lessorOcr.Text);

        // حروف اللوحة: اللاتيني (Text Layer) أولاً، والـ OCR للتأكيد/Fallback
        string? mapped = null;
        if (!string.IsNullOrEmpty(lay.EnglishPlateLetters))
        {
            var sb = new StringBuilder();
            foreach (var ch in lay.EnglishPlateLetters.ToUpperInvariant())
            {
                if (CertificateLexicon.EnglishToArabicPlateLetters.TryGetValue(ch, out var ar)) sb.Append(ar);
            }
            if (sb.Length == lay.EnglishPlateLetters.Length) mapped = sb.ToString();
        }

        var finalLetters = mapped ?? plate.Text;

        var reasons = new List<string>();
        if (lay.CertNo == null) reasons.Add("رقم الشهادة غير موجود");
        if (string.IsNullOrEmpty(lay.PlateDigits)) reasons.Add("أرقام اللوحة غير موجودة");
        if (string.IsNullOrEmpty(finalLetters)) reasons.Add("حروف اللوحة غير موجودة");
        else if (mapped == null && (plate.Text.Length == 0 || plate.Votes < 3))
            reasons.Add($"اتفاق ضعيف على حروف اللوحة ({plate.Votes}/{plate.Total})");
        else if (lay.PlateGlyphs > 0 && finalLetters.Length != lay.PlateGlyphs)
            reasons.Add("عدد حروف اللوحة لا يطابق عدد الـ glyphs في الشهادة");
        if (string.IsNullOrEmpty(lessor)) reasons.Add("اسم المؤجر غير موجود");

        void Check(string label, Decision d)
        {
            if (d.Text.Length == 0 || d.Votes < 2 || d.Conf < 0.5)
                reasons.Add($"{label}: ثقة منخفضة ({d.Votes}/{d.Total}, {d.Conf:P0})");
        }
        Check("المصنع", make);
        Check("الموديل", model);
        Check("اللون", color);

        var digits = lay.PlateDigits;
        var normalized = string.IsNullOrEmpty(finalLetters) && string.IsNullOrEmpty(digits)
            ? null
            : PlateNormalizer.Normalize(finalLetters + digits);

        _logger.LogInformation(
            "Sijil certificate extracted: plate={Plate} lessor={Lessor} fromEnglish={FromEnglish} review={Review}",
            finalLetters + digits, lessor, mapped != null, reasons.Count > 0);

        return new CertificateExtractionResult(
            CertificateProvider.Sijil,
            finalLetters, digits, normalized,
            NullIfEmpty(makeV.Value), NullIfEmpty(modelV.Value), NullIfEmpty(colorV.Value), NullIfEmpty(lessor),
            reasons.Count > 0, reasons);
    }

    // ---------------------------------------------------------------------
    //  سجل: هندسة الصفحة بـ PdfPig
    // ---------------------------------------------------------------------
    private static double X0(Letter l) => Math.Min(l.StartBaseLine.X, l.EndBaseLine.X);
    private static double X1(Letter l) => Math.Max(l.StartBaseLine.X, l.EndBaseLine.X);

    // تجميع الحروف إلى كتل أفقية متجاورة (من اليمين لليسار)
    private static List<List<Letter>> Cluster(IEnumerable<Letter> letters, double gap)
    {
        var clusters = new List<List<Letter>>();
        double minX0 = 0;
        foreach (var l in letters.OrderByDescending(X1))
        {
            if (clusters.Count > 0 && minX0 - X1(l) < gap)
            {
                clusters[^1].Add(l);
                minX0 = Math.Min(minX0, X0(l));
            }
            else
            {
                clusters.Add(new List<Letter> { l });
                minX0 = X0(l);
            }
        }
        return clusters;
    }

    private static CertLayout ReadSijilLayout(byte[] pdfBytes)
    {
        using var doc = PdfDocument.Open(pdfBytes);
        var page = doc.GetPage(1);
        double W = page.Width, H = page.Height;
        var words = page.GetWords().ToList();

        string? Find(string pattern) =>
            words.Select(w => Regex.Match(w.Text, pattern)).FirstOrDefault(m => m.Success)?.Value;

        var certNo = Find(@"REPO-\d{3}-\d{8}");
        var cells = new Dictionary<string, CertCell>();

        // -------------------------------------------------------------
        // خلية "اسم المؤجر": بنثبّت على الرقم الوطني الموحد (700…) أو السجل التجاري (101…)
        // والاسم بيكون فوقهم مباشرة، قبل عنوان "اسم المؤجر:" الواقع في أقصى اليمين.
        // -------------------------------------------------------------
        var unifiedNoWord = words.FirstOrDefault(w => Regex.IsMatch(w.Text, @"700\d{7}"));
        var crWord = words.FirstOrDefault(w => Regex.IsMatch(w.Text, @"101\d{7}"));
        if (unifiedNoWord != null || crWord != null)
        {
            var anchor = crWord ?? unifiedNoWord!;
            double refY = anchor.BoundingBox.Top;
            cells["lessor"] = new CertCell("lessor", W * 0.60, W * 0.78, refY + 26, refY + 7);
        }

        var vinWord = words.FirstOrDefault(w =>
        {
            var m = Regex.Match(w.Text, @"(?<![A-Z0-9])[A-HJ-NPR-Z0-9]{17}(?![A-Z0-9])");
            return m.Success && m.Value.Any(char.IsLetter) && m.Value.Any(char.IsDigit);
        });

        if (vinWord == null)
            return new CertLayout(W, H, certNo, null, null, 0, cells);

        var vin = Regex.Match(vinWord.Text, @"[A-HJ-NPR-Z0-9]{17}").Value;
        double vinBase = vinWord.Letters.Average(l => l.StartBaseLine.Y);

        // حروف قسم "بيانات الأصل المؤجر": من صف الـ VIN لحد ~55pt فوقه (العنوان خارج النطاق)
        var asset = page.Letters.Where(l => l.Value != " " &&
                                            l.StartBaseLine.Y > vinBase - 6 && l.StartBaseLine.Y < vinBase + 55).ToList();
        var rows = new List<List<Letter>>();
        foreach (var l in asset.OrderByDescending(l => l.StartBaseLine.Y))
        {
            var row = rows.FirstOrDefault(r => Math.Abs(r[0].StartBaseLine.Y - l.StartBaseLine.Y) < 4);
            if (row == null) rows.Add(new List<Letter> { l }); else row.Add(l);
        }

        // الصف 0: لوحة عربي | المصنع    الصف 1: لوحة إنجليزي | الموديل    الصف 2: اللون | VIN
        string?[,] map = { { "plate", "make" }, { "english_plate", "model" }, { "color", null } };
        string? plateDigits = null;
        string? englishPlateLetters = null;
        int plateGlyphs = 0;

        for (int ri = 0; ri < rows.Count && ri < 3; ri++)
        {
            var row = rows[ri];
            double baseY = row.Average(l => l.StartBaseLine.Y);
            double size = row.Select(l => l.PointSize).Where(s => s > 0).DefaultIfEmpty(8).Average();
            double topPdf = baseY + 1.25 * size, botPdf = baseY - 0.55 * size;

            for (int half = 0; half < 2; half++)
            {
                var key = map[ri, half];
                if (key == null) continue;

                var hl = row.Where(l => (half == 0) == ((X0(l) + X1(l)) / 2 < W / 2)).ToList();
                if (hl.Count == 0) continue;

                if (key == "plate")
                {
                    var digits = hl.Where(l => l.Value.Length == 1 && char.IsAsciiDigit(l.Value[0])).OrderBy(X0).ToList();
                    plateDigits = digits.Count > 0 ? string.Concat(digits.Select(d => d.Value)) : null;

                    var nonDigits = hl.Except(digits).ToList();
                    var clusters = Cluster(nonDigits, 10);
                    if (clusters.Count < 2) continue;

                    var valueLetters = clusters.Skip(1).SelectMany(c => c).ToList();
                    plateGlyphs = valueLetters.Count;

                    double minX = valueLetters.Min(X0);
                    if (digits.Count > 0)
                    {
                        double maxDigitX = digits.Max(X1);
                        minX = Math.Max(minX - 2, maxDigitX + 1.5);
                    }
                    cells[key] = new CertCell(key, minX, valueLetters.Max(X1) + 4, topPdf, botPdf);
                }
                else if (key == "english_plate")
                {
                    // الترتيب من اليمين لليسار ليطابق اتجاه قراءة الحروف العربية في اللوحة
                    var engLetters = hl.Where(l => l.Value.Length == 1 && char.IsAsciiLetter(l.Value[0]))
                                       .OrderByDescending(X0)
                                       .Select(l => l.Value[0])
                                       .ToArray();
                    if (engLetters.Length > 0) englishPlateLetters = new string(engLetters);
                }
                else if (key == "color")
                {
                    double minX = Math.Max(0, hl.Min(X0) - 15);
                    double maxX = hl.Max(X1) + 15;
                    cells[key] = new CertCell(key, minX, maxX, topPdf, botPdf);
                }
                else
                {
                    var clusters = Cluster(hl, 10);
                    if (clusters.Count < 2) continue;

                    var valueLetters = clusters.Skip(1).SelectMany(c => c).ToList();
                    cells[key] = new CertCell(key, valueLetters.Min(X0) - 5, valueLetters.Max(X1) + 5, topPdf, botPdf);
                }
            }
        }

        return new CertLayout(W, H, certNo, vin, plateDigits, plateGlyphs, cells, englishPlateLetters);
    }

    // ---------------------------------------------------------------------
    //  سجل: OCR
    // ---------------------------------------------------------------------
    private Decision RunOcr(
        TesseractEngine engine, SKBitmap bmp, double sx, double sy, CertLayout lay,
        string key, bool isPlate, int? mustLen, CancellationToken ct)
    {
        if (!lay.Cells.TryGetValue(key, out var c)) return Decision.Empty;

        var results = new List<(string Text, double Conf)>();
        var psms = isPlate
            ? new[] { PageSegMode.SingleLine, PageSegMode.SingleBlock }
            : new[] { PageSegMode.SingleLine, PageSegMode.SingleWord };

        engine.SetVariable("tessedit_char_whitelist", isPlate ? PlateAlphabet : "");
        int total = 0;

        foreach (var pad in new[] { 3.0, 5.0 })
        {
            foreach (var scale in new[] { 2, 3 })
            {
                ct.ThrowIfCancellationRequested();

                int l = (int)Math.Max(0, (c.X0 - pad) * sx);
                int r = (int)Math.Min(bmp.Width, (c.X1 + pad) * sx);
                int t = (int)Math.Max(0, (lay.PageH - c.TopPdf - pad) * sy);
                int b = (int)Math.Min(bmp.Height, (lay.PageH - c.BotPdf + pad) * sy);

                var png = MakeCrop(bmp, l, t, r, b, scale);
                using var pix = Pix.LoadFromMemory(png);

                foreach (var psm in psms)
                {
                    total++;
                    using var tp = engine.Process(pix, psm);
                    var text = Clean(tp.GetText(), isPlate);
                    if (text.Length > 0 && (mustLen == null || text.Length == mustLen))
                        results.Add((text, tp.GetMeanConfidence()));
                }
            }
        }

        return Vote(results, total);
    }

    private static byte[] MakeCrop(SKBitmap src, int l, int t, int r, int b, int scale)
    {
        const int border = 30;
        int w = Math.Max(1, r - l), h = Math.Max(1, b - t);
        using var dst = new SKBitmap(w * scale + 2 * border, h * scale + 2 * border);
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawBitmap(src, new SKRect(l, t, r, b),
                          new SKRect(border, border, border + w * scale, border + h * scale), paint);
        using var img = SKImage.FromBitmap(dst);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string Clean(string s, bool plate)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (ch >= '\u0621' && ch <= '\u064A' && ch != '\u0640') sb.Append(ch);
            else if (!plate && char.IsWhiteSpace(ch)) sb.Append(' ');
        }
        var t = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return plate ? t.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا') : t;
    }

    private static Decision Vote(List<(string Text, double Conf)> r, int total)
    {
        if (r.Count == 0) return new Decision("", 0, total, 0);
        var best = r.GroupBy(x => x.Text)
                    .Select(g => new { g.Key, N = g.Count(), C = g.Average(x => x.Conf) })
                    .OrderByDescending(x => x.N).ThenByDescending(x => x.C).First();
        return new Decision(best.Key, best.N, total, best.C);
    }

    // =====================================================================
    //  Lexicon snapping (مشترك بين النوعين)
    // =====================================================================
    private static string NormAr(string s) =>
        s.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ى', 'ي').Replace('ة', 'ه');

    private static int Lev(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                                   d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    /// <summary>
    /// (1) احتواء كـ token مستقل (الأطول أولاً)  (2) Levenshtein على النص كله  (3) Levenshtein على كل كلمة.
    /// كل مرحلة بتحترم حدّها فقط (مفيش تسريب بين المراحل).
    /// </summary>
    private static (string Value, bool Snapped) Snap(string text, string[] lexicon)
    {
        if (string.IsNullOrWhiteSpace(text)) return (text, false);

        string cleaned = StripHeaderPrefixes(text);
        string n = NormAr(cleaned);
        var sorted = lexicon.OrderByDescending(c => c.Length).ToList();

        foreach (var cand in sorted)
        {
            if (Regex.IsMatch(n, $@"(^|\s){Regex.Escape(NormAr(cand))}(\s|$)"))
                return (cand, true);
        }

        string? best = null;
        int bestD = int.MaxValue;
        foreach (var cand in sorted)
        {
            var dd = Lev(n, NormAr(cand));
            if (dd < bestD) { bestD = dd; best = cand; }
        }
        int allowed = n.Length >= 8 ? 2 : n.Length >= 4 ? 1 : 0;
        if (best != null && bestD <= allowed) return (best, true);

        var words = n.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? tokenBest = null;
        int tokenBestD = int.MaxValue;
        foreach (var cand in sorted)
        {
            string normCand = NormAr(cand);
            int candAllowed = normCand.Length >= 5 ? 1 : 0;
            foreach (var w in words)
            {
                int d = Lev(w, normCand);
                if (d <= candAllowed && d < tokenBestD) { tokenBestD = d; tokenBest = cand; }
            }
        }
        if (tokenBest != null) return (tokenBest, true);

        return (cleaned, false);
    }

    private static (string Value, bool Snapped) ExtractBestColor(string ocrText, string[] lexicon)
    {
        if (string.IsNullOrWhiteSpace(ocrText)) return ("", false);

        string cleaned = NormAr(StripHeaderPrefixes(ocrText))
            .Replace("لون", "")
            .Replace("المركبه", "")
            .Replace("المركبة", "")
            .Replace("الأصل", "")
            .Replace("الاصل", "")
            .Replace(":", "")
            .Trim();

        var sortedLexicon = lexicon.OrderByDescending(c => c.Length).ToList();

        foreach (var color in sortedLexicon)
        {
            if (cleaned.Contains(NormAr(color))) return (color, true);
        }

        string[] words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string? bestMatch = null;
        int lowestDistance = int.MaxValue;

        foreach (var color in sortedLexicon)
        {
            string normColor = NormAr(color);

            int distFull = Lev(cleaned, normColor);
            if (distFull < lowestDistance && distFull <= 2)
            {
                lowestDistance = distFull;
                bestMatch = color;
            }

            foreach (var word in words)
            {
                int distWord = Lev(word, normColor);
                if (distWord < lowestDistance && distWord <= (normColor.Length >= 4 ? 1 : 0))
                {
                    lowestDistance = distWord;
                    bestMatch = color;
                }
            }
        }

        return (bestMatch ?? cleaned, bestMatch != null);
    }

    // =====================================================================
    //  Types
    // =====================================================================
    private sealed record CertCell(string Name, double X0, double X1, double TopPdf, double BotPdf);

    private sealed record CertLayout(
        double PageW, double PageH, string? CertNo, string? Vin,
        string? PlateDigits, int PlateGlyphs,
        Dictionary<string, CertCell> Cells, string? EnglishPlateLetters = null);

    private sealed record Decision(string Text, int Votes, int Total, double Conf)
    {
        public static Decision Empty => new("", 0, 8, 0);
    }

    /// <summary>Pool بسيط: كل محرك Tesseract بيتحمّل مرة واحدة ويتعاد استخدامه (ara.traineddata ~12MB).</summary>
    private sealed class TesseractEnginePool : IDisposable
    {
        private readonly string _tessDir;
        private readonly SemaphoreSlim _semaphore;
        private readonly ConcurrentBag<TesseractEngine> _idle = new();
        private readonly List<TesseractEngine> _all = new();
        private readonly object _gate = new();

        public TesseractEnginePool(string tessDir, int size)
        {
            _tessDir = tessDir;
            _semaphore = new SemaphoreSlim(size, size);
        }

        public async Task<Lease> AcquireAsync(CancellationToken ct)
        {
            await _semaphore.WaitAsync(ct);
            try
            {
                if (!_idle.TryTake(out var engine))
                {
                    if (!File.Exists(Path.Combine(_tessDir, "ara.traineddata")))
                        throw new InvalidOperationException(
                            $"ara.traineddata مش موجود في: {_tessDir} (مطلوب لشهادات سجل فقط. حمّله من tessdata_best وظبّط Certificates:TessdataPath).");

                    engine = new TesseractEngine(_tessDir, "ara", EngineMode.LstmOnly);
                    lock (_gate) _all.Add(engine);
                }
                return new Lease(this, engine);
            }
            catch
            {
                _semaphore.Release();
                throw;
            }
        }

        private void Release(TesseractEngine engine)
        {
            _idle.Add(engine);
            _semaphore.Release();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                foreach (var e in _all) e.Dispose();
                _all.Clear();
            }
            _semaphore.Dispose();
        }

        public sealed class Lease : IDisposable
        {
            private readonly TesseractEnginePool _pool;
            private bool _released;
            public TesseractEngine Engine { get; }

            public Lease(TesseractEnginePool pool, TesseractEngine engine)
            {
                _pool = pool;
                Engine = engine;
            }

            public void Dispose()
            {
                if (_released) return;
                _released = true;
                _pool.Release(Engine);
            }
        }
    }
}
