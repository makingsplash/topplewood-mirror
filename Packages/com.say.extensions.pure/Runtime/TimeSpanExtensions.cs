using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SGS.Extensions
{
    public static class TimeSpanExtensions
    {
        public static string ToConfigString(this DateTime value)
        {
            var ut = value.ToUniversalTime();
            if (ut.Ticks % 864000000000L == 0)
            {
                return ut.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);    
            }
            return ut.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        public static DateTime FromDateTimeString(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return new DateTime();
            }
            
            if (DateTime.TryParseExact(s, "dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault,
                    out var dateTime))
            {
                return dateTime.ToUniversalTime();
            }

            if (DateTime.TryParseExact(s, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault,
                    out var date))
            {
                return date.ToUniversalTime();
            }

            return new DateTime();
        }

        public static string ToConfigString(this TimeSpan value) 
        {
            string timeString = String.Empty;
            if (value.Ticks < 0)
            {
                timeString = "-";
                value = new TimeSpan(-value.Ticks);
            }
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
            if (string.IsNullOrEmpty(s))
            {
                return TimeSpan.Zero;
            }
            int days = ParsePart(s, "d");
            int hours = ParsePart(s, "h");
            int minutes = ParsePart(s, "m");
            int seconds = ParsePart(s, "s");

            var span = TimeSpan.Zero.Add(TimeSpan.FromDays(days))
                .Add(TimeSpan.FromHours(hours))
                .Add(TimeSpan.FromMinutes(minutes))
                .Add(TimeSpan.FromSeconds(seconds));
            
            return s.Contains('-') ? new TimeSpan(-span.Ticks) : span;
        }
        
        public static string ToTimeOfDayString(this TimeSpan value)
        {
            if (value.Ticks > TimeSpan.TicksPerDay)
            {
                value = new TimeSpan(value.Ticks % TimeSpan.TicksPerDay);
            }
            
            string timeString = $"{value.Hours:00}:{value.Minutes:00}";

            if (value.Seconds > 0)
            {
                timeString +=  $":{value.Seconds:00}";
            }
            return timeString;
        }

        public static TimeSpan FromTimeOfDayString(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return TimeSpan.Zero;
            }
            
            var parts = s.Split(':');
            if (parts.Length < 2)
            {
                return TimeSpan.Zero;
            }

            int.TryParse(parts[0], out int hours);
            int.TryParse(parts[1], out int minutes);
            int seconds = 0;
            if (parts.Length >= 3)
            {
                int.TryParse(parts[2], out seconds);
            }
            
            var span = TimeSpan.Zero
                .Add(TimeSpan.FromHours(Math.Clamp(hours, 0, 23)))
                .Add(TimeSpan.FromMinutes(Math.Clamp(minutes, 0, 59)))
                .Add(TimeSpan.FromSeconds(Math.Clamp(seconds,0, 59)));
            
            return span;
        }
        
        public static DateTime StartOfWeek(this DateTime time)
        {
            return time.Date.AddDays(-(int)time.DayOfWeek);
        }
        
        public static TimeSpan TimeOfDay(this DateTime time)
        {
            return time - time.Date;
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