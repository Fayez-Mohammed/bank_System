namespace Bank.Services.Interfaces;

public interface IPlateColumnDetector
{
    /// <summary>
    /// بيحاول يحدد أي عمود هو عمود رقم اللوحة، من غير أي column mapping يدوي.
    /// أولوية 1: اسم العمود نفسه (لو موجود هيدر واضح). أولوية 2: fallback
    /// إحصائي بيقارن شكل القيم في عينة من الصفوف مع pattern رقم اللوحة.
    /// بيرجع null لو مقدرش يحدد عمود بثقة كافية.
    /// </summary>
    int? DetectPlateColumn(IReadOnlyList<string?> headerRow, IEnumerable<IReadOnlyList<string?>> sampleDataRows);
}
