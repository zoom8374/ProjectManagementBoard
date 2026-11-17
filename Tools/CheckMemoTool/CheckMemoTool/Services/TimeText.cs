using System;
using System.Globalization;

namespace CheckMemoTool.Services
{
    // 날짜를 ISO 8601 문자열로 저장/읽기 (예: 2026-11-03T09:00:00+09:00)
    public static class TimeText
    {
        private const string Format = "yyyy-MM-dd'T'HH:mm:sszzz";

        public static string Now()
        {
            return DateTimeOffset.Now.ToString(Format, CultureInfo.InvariantCulture);
        }

        public static DateTimeOffset Parse(string text)
        {
            DateTimeOffset value;
            if (!string.IsNullOrEmpty(text) &&
                DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            {
                return value;
            }
            return DateTimeOffset.MinValue;
        }
    }
}
