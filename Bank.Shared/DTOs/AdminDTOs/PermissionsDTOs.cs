using System;
using System.Collections.Generic;

namespace Bank.Shared.DTOs.AdminDTOs
{
    public class UserPermissionDto
    {
        // إضافة وحذف ملفات التشييك
        public bool CanManageCheckFiles { get; set; } = false;

        // رفع ملفات الداتا وإدارة الملفات المملوكة للمستخدم
        public bool CanUploadDataFiles { get; set; } = false;

        // السماح بإجراء الفرز، بشرط السماح بالوصول إلى ملف الداتا المحدد
        public bool CanSort { get; set; } = false;
    }

    public class UpdateUserPermissionDto : UserPermissionDto
    {
    }

    // ملف داتا مسموح للمستخدم يفرز عليه
    public class UserDataFilePermissionDto
    {
        public Guid DataFileId { get; set; }
        public string FileName { get; set; } = null!;
        public Guid GrantedByAdminId { get; set; }
        public string GrantedByAdminName { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
    }

    // القائمة الكاملة لملفات الفرز المسموحة — بتستبدل القائمة القديمة
    public class UpdateUserDataFilePermissionsDto
    {
        public List<Guid> DataFileIds { get; set; } = new();
    }
}
