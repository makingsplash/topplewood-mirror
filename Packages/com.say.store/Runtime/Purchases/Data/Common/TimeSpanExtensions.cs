#if !SAY_INFRASTURCTURE

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Tools.Purchases.Data.Common
{
    public static class TimeSpanExtensions
    {
        public static string ToConfigString(this TimeSpan value) 
        {
            string timeString = String.Empty;
            AppendPart(ref timeString, "d", value.Days);
            AppendPart(ref timeString, "h", value.Hours);
            AppendPart(ref timeString, "m", value.Minutes);
            AppendPart(ref timeString, "s", value.Seconds);
            
            if (timeString.Length == 0)
            {
                timeString = "0s";
            }

            return timeString;
        }

        public static TimeSpan FromConfigString(string s)
        {
            int days = ParsePart(s, "d");
            int hours = ParsePart(s, "h");
            int minutes = ParsePart(s, "m");
            int seconds = ParsePart(s, "s");

            var span = TimeSpan.Zero.Add(TimeSpan.FromDays(days))
                .Add(TimeSpan.FromHours(hours))
                .Add(TimeSpan.FromMinutes(minutes))
                .Add(TimeSpan.FromSeconds(seconds));
            return span;
        }
        
        private static void AppendPart(ref string timeString, string postfix, int value)
        {
            if (value == 0)
            {
                return;
            }

            if (timeString.Length == 0)
            {
                timeString = value + postfix;
            }
            else
            {
                timeString = timeString + " " + value + postfix;
            }
        }
        private static int ParsePart(string value, string postfix)
        {
            var match = Regex.Match(value, "(\\d+)" + postfix);
            if (match.Success)
            {
                return int.Parse(match.Groups[1].Value);
            }
            return 0;
        }
    }

}
#endif