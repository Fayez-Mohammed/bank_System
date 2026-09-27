using System;
using System.Collections.Generic;
using System.Text;

namespace Bank.Services.Helpers
{
    public static class ReferralHeaderRowDetector
    {
        private const int MaxScanRows = 30;
        private const double DataDigitFractionThreshold = 0.4;
        private const int MinNonEmptyCellsForData = 2;

        /// <returns>index الصف اللي المفروض يتعامل معاه كهيدر (0-based). لو مقدرناش نحدد صف بيانات واضح، بيرجّع 0 (نفس السلوك القديم).</returns>
        public static int Detect(List<List<string?>> rows)
        {
            var scanLimit = Math.Min(rows.Count, MaxScanRows);

            for (var i = 0; i < scanLimit; i++)
            {
                if (IsLikelyDataRow(rows[i]))
                {
                    return i == 0 ? 0 : i - 1;
                }
            }

            return 0;
        }

        private static bool IsLikelyDataRow(List<string?> row)
        {
            var nonEmpty = 0;
            var withDigits = 0;

            foreach (var cell in row)
            {
                if (string.IsNullOrWhiteSpace(cell)) continue;
                nonEmpty++;
                if (cell.Any(char.IsDigit)) withDigits++;
            }

            if (nonEmpty < MinNonEmptyCellsForData) return false;

            return (double)withDigits / nonEmpty >= DataDigitFractionThreshold;
        }
    }

}
