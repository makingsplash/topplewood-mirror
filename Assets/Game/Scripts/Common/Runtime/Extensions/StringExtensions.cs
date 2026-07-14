namespace Common.Extensions
{
    public static class StringExtensions
    {
        public static bool IsNullOrEmpty(this string str) =>
            string.IsNullOrEmpty(str);
        
        public static bool IsNotNullOrEmpty(this string str) =>
            !string.IsNullOrEmpty(str);
    }
}
