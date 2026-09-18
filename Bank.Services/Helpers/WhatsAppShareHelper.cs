using System.Net;

namespace Bank.Services.Helpers;

/// <summary>
/// أي زرار "مشاركة" في الأبليكيشن (نتائج الفرز، التسجيل، إلخ) معناه فتح
/// واتساب مباشرة بنص جاهز، مش export عام. البنك ده بيبني رابط
/// https://wa.me/?text=... اللي بيفتح شاشة اختيار جهة اتصال/جروب في واتساب
/// جاهزة بالنص المُجهّز — التطبيق (موبايل) هو اللي بيفتح الرابط ده فعليًا.
/// </summary>
public static class WhatsAppShareHelper
{
    /// <summary>
    /// مشاركة لجهة اتصال محددة (لو معانا رقم موبايل، بصيغة دولية من غير + أو أصفار زيادة، مثال: "9665xxxxxxxx").
    /// </summary>
    public static string BuildDirectShareLink(string phoneNumberInternational, string message)
    {
        var digitsOnly = new string(phoneNumberInternational.Where(char.IsDigit).ToArray());
        return $"https://wa.me/{digitsOnly}?text={WebUtility.UrlEncode(message)}";
    }

    /// <summary>مشاركة عامة — بتفتح واتساب ويختار المستخدم هو نفسه لمين يبعت (الحالة الأشيع هنا).</summary>
    public static string BuildGenericShareLink(string message)
    {
        return $"https://wa.me/?text={WebUtility.UrlEncode(message)}";
    }
}
