using Bank.DAL.Models.BaseModels;

namespace Bank.DAL.Models.SystemModels;

// نسخة جاهزة من نتيجة "الأحدث" (أحدث عملية فرز مكتملة) — صف واحد بس لكل
// مساحة عمل (جروب أو يوزر مستقل). ReferralJobId هو الـ cache key: لو الأحدث
// فعليًا اتغيّر، بيتم تجهيز نسخة جديدة وقت الطلب نفسه (مفيش Background Job).
public class MapResult : BaseEntityLong
{
    public Guid? GroupId { get; set; }
    public Guid UserId { get; set; }

    public Guid ReferralJobId { get; set; }

    // مصفوفة MapResultRowDto متسلسلة كـ JSON (jsonb في Postgres)
    public string ResultJson { get; set; } = null!;

    public int CarCount { get; set; }
    public int UnresolvedCount { get; set; }
    public DateTime PreparedAt { get; set; }
}
