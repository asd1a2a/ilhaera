using System;
using System.Collections.Generic;

namespace TodoWidget.Core
{
    public enum Repeat { None, Daily, Weekdays, Weekly }

    /// <summary>반복 일정 계산. 매주 반복의 요일은 일정 시각(Due)의 요일을 따른다.</summary>
    public static class Recurrence
    {
        public static Repeat Parse(string raw)
        {
            switch (raw)
            {
                case "daily": return Repeat.Daily;
                case "weekdays": return Repeat.Weekdays;
                case "weekly": return Repeat.Weekly;
                default: return Repeat.None;
            }
        }

        public static string ToRaw(Repeat r)
        {
            switch (r)
            {
                case Repeat.Daily: return "daily";
                case Repeat.Weekdays: return "weekdays";
                case Repeat.Weekly: return "weekly";
                default: return null;
            }
        }

        static bool Matches(Repeat r, DateTime date, DayOfWeek weeklyDay)
        {
            switch (r)
            {
                case Repeat.Weekdays: return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
                case Repeat.Weekly: return date.DayOfWeek == weeklyDay;
                default: return true;
            }
        }

        /// <summary>candidate 와 같거나 이후인 첫 회차 (시각은 그대로).</summary>
        public static DateTime FirstOnOrAfter(Repeat r, DateTime candidate, DayOfWeek weeklyDay)
        {
            var d = candidate;
            for (int i = 0; i < 8 && !Matches(r, d, weeklyDay); i++) d = d.AddDays(1);
            return d;
        }

        /// <summary>due 다음 회차.</summary>
        public static DateTime NextAfter(Repeat r, DateTime due)
        {
            if (r == Repeat.None) return due;
            if (r == Repeat.Weekly) return due.AddDays(7);
            return FirstOnOrAfter(r, due.AddDays(1), due.DayOfWeek);
        }

        /// <summary>그날(date) 또는 그 이후의 첫 회차. 시각은 timeOfDay.</summary>
        public static DateTime FirstOnOrAfterDate(Repeat r, DateTime date, TimeSpan timeOfDay, DayOfWeek weeklyDay)
        {
            return FirstOnOrAfter(r, date.Date + timeOfDay, weeklyDay);
        }

        public static string Label(Repeat r, DateTime due)
        {
            switch (r)
            {
                case Repeat.Daily: return "매일";
                case Repeat.Weekdays: return "평일";
                case Repeat.Weekly: return "매주 " + TimeText.DayName(due);
                default: return null;
            }
        }

        /// <summary>
        /// 지난 날의 회차를 오늘(또는 그 이후) 회차로 넘긴다. 컴퓨터가 꺼져 있던 경우 포함.
        /// 바뀐 항목이 있으면 true.
        /// </summary>
        public static bool RollForward(IEnumerable<TodoItem> items, DateTime now)
        {
            bool changed = false;
            foreach (var item in items)
            {
                if (item.Done || !item.IsRecurring) continue;
                var due = item.Due.Value;
                if (due.Date >= now.Date) continue;
                var next = FirstOnOrAfterDate(item.Repeat, now.Date, due.TimeOfDay, due.DayOfWeek);
                item.Due = next;
                item.Snooze = null;
                item.Notified = false; // 오늘 회차가 이미 지났으면 "놓친 일정"으로 한 번 알린다
                changed = true;
            }
            return changed;
        }
    }
}
