using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Services.Helpers
{
    public static class FileHelper
    {
        public static string? ValidateUploadedFile(IFormFile file,long MaxUploadSizeBytes ,string[] AllowedExtensions)
        {
            if (file is null || file.Length == 0)
            {
                return "لازم ترفع ملف.";
            }

            if (file.Length > MaxUploadSizeBytes)
            {
                return $"حجم الملف أكبر من الحد المسموح ({MaxUploadSizeBytes / 1024 / 1024} MB).";
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
            {
                return $"صيغة الملف غير مدعومة. المسموح: {string.Join(", ", AllowedExtensions)}";
            }

            return null;
        }
    }
}
