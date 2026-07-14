using System;
using SGS.Extensions;

namespace SGS.Infrastructure.Tools.Common
{
    public static class TimeLogic
    {
        /// <summary>
        /// Gets data from event schedule specified by start days of week and start time in that days
        /// Returns first event start time between minTime and maxTime
        /// </summary>
        /// <param name="daysOfWeek">days of week event starts</param>
        /// <param name="timeOfDay">event start time withinn day </param>
        /// <param name="minTime">interval to check start</param>
        /// <param name="maxTime">interval to check end</param>
        public static DateTime? GetNextTimeTrigger(DayOfWeek[] daysOfWeek, TimeSpan timeOfDay, DateTime minTime, DateTime maxTime)
        {
            var weekStart = minTime.Date.StartOfWeek();

            while (weekStart < maxTime)
            {
                foreach (var startDay in daysOfWeek)
                {
                    var timeStart = weekStart.AddDays((int)startDay) + timeOfDay;

                    if (timeStart > maxTime)
                    {
                        return null;
                    }

                    if (timeStart < minTime)
                    {
                        continue;
                    }

                    return timeStart;
                }

                weekStart = weekStart.AddDays(7);
            }
            return null;
        }
        
        /// <summary>
        /// Gets data from event schedule specified by start days of week, start time in that days and duration
        /// Returns event start time if any event is running at checkTime  
        /// </summary>
        /// <param name="daysOfWeek">days of week event starts</param>
        /// <param name="timeOfDay">event start time withinn day </param>
        /// <param name="duration">event duration</param>
        /// <param name="checkTime">time to check at</param>
        /// <returns>Event start  time if event is runing at checkTime, or null if event is not running</returns>
        public static DateTime? GetCurrentIntervalStart(DayOfWeek[] daysOfWeek, TimeSpan timeOfDay, TimeSpan duration, DateTime checkTime)
        {
            var weekStart = checkTime.Date.AddDays(-(int)checkTime.DayOfWeek);

            foreach (var startDay in daysOfWeek)
            {
                var timeStart = weekStart.AddDays((int)startDay) + timeOfDay;
                if (checkTime < timeStart)
                {
                    timeStart = timeStart.AddDays(-7);
                }

                if (checkTime < timeStart + duration)
                {
                    return timeStart;
                }
            }

            return null;
        }

    }
}