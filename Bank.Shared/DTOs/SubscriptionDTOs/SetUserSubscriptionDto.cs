using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Shared.DTOs.SubscriptionDTOs
{
    public class SetUserSubscriptionDto
    {
        public int? Months { get; set; }          // يمدد من (الآن أو الانتهاء الحالي لو لسه ساري)
        public DateTime? ExpiresAt { get; set; }  // أو تاريخ صريح
        public bool Unlimited { get; set; }       // أو يشيل الحد
    }

    public class RegisterDeviceTokenDto
    {
        public string Token { get; set; } = null!;
    }

    public class RemoveDeviceTokenDto
    {
        public string Token { get; set; } = null!;
    }
}
