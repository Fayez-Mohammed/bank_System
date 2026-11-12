using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Fonts.TrueType;
using UglyToad.PdfPig.Fonts.TrueType.Parser;
using UglyToad.PdfPig.Parser.Parts;
using UglyToad.PdfPig.Tokens;

namespace Bank.Services.Implementations.CertificateServices;

/// <summary>
/// قراءة الحروف العربية من الـ text layer مباشرة، من غير OCR.
///
/// شهادات سجل بتكتب العربي بخط Type0 (Identity-H) مضمّن في الملف، يعني كود الحرف = رقم الـ glyph
/// في الخط. جدول ToUnicode اللي في الملف ناقص (بيغطي الإنجليزي بس)، فـ PdfPig بيرجّع رقم الـ glyph نفسه
/// كحرف (مثلاً U+0363 بدل "ر"). بس الخط المضمّن نفسه فيه جدول cmap سليم (Unicode → glyph)،
/// فبنعكسه (glyph → حرف عربي) ونفك بيه الحروف.
///
/// النتيجة ثابتة على أي نظام تشغيل: مفيش Tesseract ولا rendering ولا خطوط النظام.
/// لو أي glyph ما اتفكّش، الدالة بترجّع null والكود بيرجع للـ OCR زي الأول.
/// </summary>
internal sealed class EmbeddedGlyphDecoder
{
    // اسم الخط (BaseFont) → (رقم glyph → الحرف/الحروف المنطقية)
    private readonly Dictionary<string, Dictionary<int, string>> _fonts;

    private EmbeddedGlyphDecoder(Dictionary<string, Dictionary<int, string>> fonts) => _fonts = fonts;

    public bool HasFonts => _fonts.Count > 0;

    /// <summary>بيبني الجدول من خطوط Type0/Identity-H المضمّنة في الصفحة. عمره ما بيرمي exception.</summary>
    public static EmbeddedGlyphDecoder Build(PdfDocument doc, Page page)
    {
        var result = new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);
        var conflicted = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var scanner = doc.Structure.TokenScanner;
            if (!page.Dictionary.TryGet(NameToken.Resources, scanner, out DictionaryToken? resources) ||
                !resources.TryGet(NameToken.Font, scanner, out DictionaryToken? fonts))
                return new EmbeddedGlyphDecoder(result);

            foreach (var entry in fonts.Data)
            {
                try
                {
                    if (!DirectObjectFinder.TryGet(entry.Value, scanner, out DictionaryToken? font)) continue;
                    if (!font.TryGet(NameToken.Subtype, scanner, out NameToken? subtype) || subtype.Data != "Type0") continue;
                    if (!font.TryGet(NameToken.Encoding, scanner, out NameToken? enc) || enc.Data != "Identity-H") continue;
                    if (!font.TryGet(NameToken.BaseFont, scanner, out NameToken? baseFont)) continue;
                    if (!font.TryGet(NameToken.DescendantFonts, scanner, out ArrayToken? desc) || desc.Length == 0) continue;
                    if (!DirectObjectFinder.TryGet(desc[0], scanner, out DictionaryToken? cid)) continue;

                    // لازم الكود = رقم الـ glyph (CIDToGIDMap غايب أو Identity)، غير كده الجدول مش صالح.
                    if (cid.Data.TryGetValue(NameToken.CidToGidMap.Data, out var mapTok) &&
                        !(DirectObjectFinder.TryGet(mapTok, scanner, out NameToken? mapName) && mapName.Data == "Identity"))
                        continue;

                    if (!cid.TryGet(NameToken.FontDescriptor, scanner, out DictionaryToken? fd)) continue;
                    if (!fd.TryGet(NameToken.FontFile2, scanner, out StreamToken? fontFile)) continue;

                    var bytes = fontFile.Decode(doc.Structure.FilterProvider, scanner);
                    var ttf = TrueTypeFontParser.Parse(new TrueTypeDataBytes(bytes.ToArray()));
                    var cmap = ttf.TableRegister.CMapTable;
                    if (cmap == null) continue;

                    var map = new Dictionary<int, string>();
                    foreach (var (from, to) in ArabicRanges)
                    {
                        for (int cp = from; cp <= to; cp++)
                        {
                            if (!cmap.TryGetGlyphIndex(cp, out int gid) || gid <= 0) continue;
                            var logical = ToLogicalArabic(cp);
                            if (logical == null) continue;
                            // نفس الـ glyph ممكن يتسجل للحرف الأساسي وشكله (مثلاً U+0631 و U+FEAD) → نفس النتيجة.
                            if (map.TryGetValue(gid, out var existing) && existing != logical)
                                map[gid] = Ambiguous;   // غامض → ما نستخدموش
                            else
                                map[gid] = logical;
                        }
                    }
                    if (map.Count == 0) continue;
                    // كود أقل من 0x80 مش هنقدر نفرّقه عن الـ ASCII اللي جدول ToUnicode بيطلّعه (شرطة، نقطتين...)،
                    // فلو أي حرف عربي واخد glyph رقمه صغير، الخط ده مش آمن نفكّه، والـ OCR يكمّل.
                    if (map.Keys.Any(g => g < 0x80)) continue;

                    var name = baseFont.Data;
                    if (conflicted.Contains(name)) continue;
                    if (result.TryGetValue(name, out var prev))
                    {
                        // خطين بنفس الاسم (subset مختلفين): لازم يتفقوا، وإلا نتجاهل الاسم ده خالص.
                        foreach (var kv in map)
                        {
                            if (prev.TryGetValue(kv.Key, out var pv) && pv != kv.Value)
                            {
                                conflicted.Add(name);
                                result.Remove(name);
                                break;
                            }
                            prev[kv.Key] = kv.Value;
                        }
                    }
                    else
                    {
                        result[name] = map;
                    }
                }
                catch
                {
                    // خط واحد بايظ مايوقفش الباقي.
                }
            }
        }
        catch
        {
            // أي مشكلة في البنية = مفيش فك، والـ OCR هيشتغل زي الأول.
        }

        return new EmbeddedGlyphDecoder(result);
    }

    private const string Ambiguous = "\uFFFD";

    /// <summary>
    /// النص المنطقي (RTL) لمجموعة Letters عربية: بنرتّبهم من اليمين للشمال بالمكان، وجوه الـ Letter الواحد
    /// الـ glyphs مخزّنة بالترتيب البصري فبنعكسها (الـ ligature زي "لا" بتفضل سليمة لأنها string واحد).
    /// المسافات بتيجي من حرف المسافة في الملف نفسه (أماكن الـ glyphs في PdfPig مش دقيقة كفاية نحسب منها فراغ).
    /// الشرطات والنقطتين بيتشالوا.
    /// بيرجّع null لو: أي glyph مش معروف، أو فيه حروف/أرقام لاتيني (ترتيبها LTR جوه سطر RTL، فمش هنخاطر).
    /// </summary>
    public string? DecodeArabicText(IEnumerable<Letter> letters)
    {
        var sb = new StringBuilder();
        foreach (var l in letters.OrderByDescending(l => Math.Max(l.StartBaseLine.X, l.EndBaseLine.X)))
        {
            _fonts.TryGetValue(l.FontName ?? "", out var map);
            var value = l.Value;
            for (int i = value.Length - 1; i >= 0; i--)
            {
                char c = value[i];
                if (c < 0x80)
                {
                    if (char.IsAsciiLetterOrDigit(c)) return null;
                    if (char.IsWhiteSpace(c)) sb.Append(' ');
                    continue;                                    // شرطة / نقطتين / علامات
                }
                if (map == null || !map.TryGetValue(c, out var ar) || ar == Ambiguous) return null;
                sb.Append(ar);
            }
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    private static readonly (int From, int To)[] ArabicRanges =
    {
        (0x0621, 0x064A),   // الحروف الأساسية
        (0xFB50, 0xFDFF),   // Presentation Forms-A
        (0xFE70, 0xFEFC),   // Presentation Forms-B (أشكال الحروف المتصلة/المنفصلة + لا)
    };

    /// <summary>كود عربي (أساسي أو presentation form) → الحروف الأساسية المنطقية، أو null لو مش حرف.</summary>
    private static string? ToLogicalArabic(int cp)
    {
        var s = char.ConvertFromUtf32(cp).Normalize(NormalizationForm.FormKC);
        if (s.Length == 0) return null;
        foreach (var ch in s)
            if (ch < '\u0621' || ch > '\u064A' || ch == '\u0640') return null;
        return s;
    }
}
