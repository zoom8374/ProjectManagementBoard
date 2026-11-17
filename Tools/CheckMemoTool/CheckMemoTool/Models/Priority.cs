namespace CheckMemoTool.Models
{
    // 중요도: 상(high) → 중(medium) → 하(low) → 없음(null) 순으로 정렬
    public static class Priority
    {
        public const string High = "high";
        public const string Medium = "medium";
        public const string Low = "low";

        public static int Rank(string priority)
        {
            if (priority == High) return 0;
            if (priority == Medium) return 1;
            if (priority == Low) return 2;
            return 3;
        }

        // 알 수 없는 값은 '없음'(null)으로 취급
        public static string Normalize(string priority)
        {
            if (priority == High || priority == Medium || priority == Low) return priority;
            return null;
        }
    }
}
