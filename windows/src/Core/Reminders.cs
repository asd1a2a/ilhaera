using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TodoWidget.Core
{
    public enum AlertKind { Upcoming, Missed }

    public sealed class Alert
    {
        public TodoItem Item { get; set; }
        public AlertKind Kind { get; set; }
        public int MinutesLeft { get; set; }
    }

    public static class Reminders
    {
        /// <summary>컴퓨터가 꺼져 있어서 놓친 일정은 이 시간 이내인 것만 알려준다.</summary>
        public static readonly TimeSpan MissedWindow = TimeSpan.FromHours(12);

        /// <summary>
        /// 알림을 보내야 할 항목을 찾고 Notified 를 표시한다 (각 항목은 한 번만 알림).
        /// </summary>
        public static List<Alert> Collect(IEnumerable<TodoItem> items, DateTime now, int leadMinutes)
        {
            var alerts = new List<Alert>();
            foreach (var item in items)
            {
                if (item.Done) continue;
                var due = item.Due;

                // "다시 알림"을 눌러 둔 항목
                if (item.Snooze.HasValue)
                {
                    if (now < item.Snooze.Value) continue;
                    item.Snooze = null;
                    item.Notified = true;
                    if (due.HasValue) alerts.Add(Make(item, due.Value, now));
                    continue;
                }

                if (item.Notified || !due.HasValue) continue;
                if (now < due.Value.AddMinutes(-leadMinutes)) continue;

                item.Notified = true;
                if (now < due.Value || now - due.Value <= MissedWindow)
                    alerts.Add(Make(item, due.Value, now));
            }
            return alerts.OrderBy(a => a.Item.Due).ToList();
        }

        static Alert Make(TodoItem item, DateTime due, DateTime now)
        {
            if (now < due)
                return new Alert { Item = item, Kind = AlertKind.Upcoming, MinutesLeft = (int)Math.Ceiling((due - now).TotalMinutes) };
            return new Alert { Item = item, Kind = AlertKind.Missed };
        }

        /// <summary>지금 이후 가장 먼저 시작하는 미완료 일정.</summary>
        public static TodoItem NextUp(IEnumerable<TodoItem> items, DateTime now)
        {
            return items.Where(i => !i.Done && i.Due.HasValue && i.Due.Value >= now)
                        .OrderBy(i => i.Due.Value).ThenBy(i => i.Created)
                        .FirstOrDefault();
        }

        /// <summary>목록을 지난 일정 / 오늘 / 내일 / 이후 / 기한 없음 / 완료 로 나눈다 (빈 묶음 제외).</summary>
        public static List<Section> Group(IEnumerable<TodoItem> items, DateTime now)
        {
            var list = items.ToList();
            var today = now.Date;
            var open = list.Where(i => !i.Done).ToList();
            var dated = open.Where(i => i.Due.HasValue).OrderBy(i => i.Due.Value).ThenBy(i => i.Created).ToList();

            var sections = new List<Section>
            {
                new Section(SectionKind.Overdue, "지난 일정", null, dated.Where(i => i.Due.Value < now)),
                new Section(SectionKind.Today, "오늘", today, dated.Where(i => i.Due.Value >= now && i.Due.Value.Date == today)),
                new Section(SectionKind.Tomorrow, "내일", today.AddDays(1), dated.Where(i => i.Due.Value >= now && i.Due.Value.Date == today.AddDays(1))),
                new Section(SectionKind.Later, "이후", null, dated.Where(i => i.Due.Value >= now && i.Due.Value.Date > today.AddDays(1))),
                new Section(SectionKind.NoDate, "기한 없음", null, open.Where(i => !i.Due.HasValue).OrderBy(i => i.Created)),
                new Section(SectionKind.Done, "완료", null, list.Where(i => i.Done).OrderByDescending(i => i.Completed ?? DateTime.MinValue)),
            };
            return sections.Where(sec => sec.Items.Count > 0).ToList();
        }

        /// <summary>미완료 → 일정 시각 순(시각 없는 항목은 뒤) → 완료 항목(최근 완료 순).</summary>
        public static List<TodoItem> Sort(IEnumerable<TodoItem> items)
        {
            var list = items.ToList();
            var open = list.Where(i => !i.Done)
                .OrderBy(i => i.Due.HasValue ? 0 : 1)
                .ThenBy(i => i.Due ?? DateTime.MaxValue)
                .ThenBy(i => i.Created);
            var done = list.Where(i => i.Done)
                .OrderByDescending(i => i.Completed ?? DateTime.MinValue);
            return open.Concat(done).ToList();
        }
    }

    public enum SectionKind { Overdue, Today, Tomorrow, Later, NoDate, Done }

    public sealed class Section
    {
        public SectionKind Kind { get; private set; }
        public string Title { get; private set; }
        public DateTime? Date { get; private set; }
        public List<TodoItem> Items { get; private set; }

        public Section(SectionKind kind, string title, DateTime? date, IEnumerable<TodoItem> items)
        {
            Kind = kind;
            Title = title;
            Date = date;
            Items = items.ToList();
        }
    }

    public enum DueState { None, Normal, Soon, Overdue }

    public static class TimeText
    {
        const string DayNames = "일월화수목금토";

        public static string DayName(DateTime d)
        {
            return DayNames[(int)d.DayOfWeek].ToString();
        }

        public static string HourMinute(DateTime d)
        {
            return d.Hour.ToString("00", CultureInfo.InvariantCulture) + ":" + d.Minute.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>예: "오후 3:00", "오전 12:05"</summary>
        public static string Korean12(DateTime d)
        {
            int h = d.Hour % 12 == 0 ? 12 : d.Hour % 12;
            return (d.Hour < 12 ? "오전 " : "오후 ") + h + ":" + d.Minute.ToString("00", CultureInfo.InvariantCulture);
        }

        public static string ShortDate(DateTime d, DateTime now)
        {
            if (d.Year == now.Year)
                return string.Format(CultureInfo.InvariantCulture, "{0}/{1} ({2})", d.Month, d.Day, DayName(d));
            return string.Format(CultureInfo.InvariantCulture, "{0}/{1}/{2} ({3})", d.Year, d.Month, d.Day, DayName(d));
        }

        public static string DayWord(DateTime date, DateTime now)
        {
            int diff = (date.Date - now.Date).Days;
            switch (diff)
            {
                case -1: return "어제";
                case 0: return "오늘";
                case 1: return "내일";
                case 2: return "모레";
                default: return null;
            }
        }

        /// <summary>예: "오늘 14:30", "11/25 (수) 09:00"</summary>
        public static string DueLabel(DateTime due, DateTime now)
        {
            var word = DayWord(due, now);
            return (word ?? ShortDate(due, now)) + " " + HourMinute(due);
        }

        /// <summary>예: "25분 후", "3시간 10분 후", "2일 후", "지남"</summary>
        public static string Relative(DateTime due, DateTime now)
        {
            var diff = due - now;
            if (diff.TotalSeconds <= 0) return "시간 지남";
            if (diff.TotalMinutes < 60)
                return Math.Max(1, (int)Math.Ceiling(diff.TotalMinutes)) + "분 후";
            if (diff.TotalHours < 24)
            {
                int totalMin = (int)Math.Ceiling(diff.TotalMinutes);
                int h = totalMin / 60, m = totalMin % 60;
                return m == 0 ? h + "시간 후" : h + "시간 " + m + "분 후";
            }
            return (due.Date - now.Date).Days + "일 후";
        }

        public static DueState State(TodoItem item, DateTime now, int leadMinutes)
        {
            var due = item.Due;
            if (!due.HasValue) return DueState.None;
            if (item.Done) return DueState.Normal;
            if (due.Value <= now) return DueState.Overdue;
            if (due.Value.AddMinutes(-leadMinutes) <= now) return DueState.Soon;
            return DueState.Normal;
        }

        /// <summary>날짜 선택 목록용 라벨. 예: "오늘 · 11/20 (금)"</summary>
        public static string DateChoiceLabel(DateTime date, DateTime now)
        {
            var word = DayWord(date, now);
            var shortDate = ShortDate(date, now);
            return word != null ? word + " · " + shortDate : shortDate;
        }

        /// <summary>기본 일정 시각: 지금부터 다음 정각 (예: 14:12 → 15:00).</summary>
        public static DateTime DefaultDue(DateTime now)
        {
            var next = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0).AddHours(1);
            return next;
        }

        static readonly Regex Colon = new Regex(@"^(\d{1,2})(?:(?::|시|\.|h)(\d{1,2}|반)?분?)?$", RegexOptions.CultureInvariant);
        static readonly Regex Compact = new Regex(@"^(\d{1,2})(\d{2})$", RegexOptions.CultureInvariant);

        /// <summary>
        /// "14:30", "1430", "930", "오후 2:30", "2시 반", "pm 3", "15시" 등을 해석한다.
        /// </summary>
        public static bool TryParseTime(string input, out int hour, out int minute)
        {
            hour = minute = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;
            var s = input.Trim().ToLowerInvariant().Replace(" ", "");

            bool pm = false, am = false;
            if (s.Contains("오후") || s.Contains("저녁") || s.Contains("밤") || s.Contains("pm") || s.EndsWith("p"))
                pm = true;
            else if (s.Contains("오전") || s.Contains("아침") || s.Contains("새벽") || s.Contains("am") || s.EndsWith("a"))
                am = true;
            if (s == "정오") { hour = 12; return true; }
            if (s == "자정") { hour = 0; return true; }

            s = s.Replace("오후", "").Replace("오전", "").Replace("저녁", "").Replace("아침", "")
                 .Replace("새벽", "").Replace("밤", "").Replace("pm", "").Replace("am", "")
                 .TrimEnd('p', 'a');
            if (s.Length == 0) return false;

            int h, m = 0;
            var match = Colon.Match(s);
            if (match.Success)
            {
                h = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                if (match.Groups[2].Success)
                    m = match.Groups[2].Value == "반" ? 30 : int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                match = Compact.Match(s);
                if (!match.Success) return false;
                h = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                m = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            }

            if (pm && h >= 1 && h < 12) h += 12;
            if (am && h == 12) h = 0;
            if ((pm || am) && h > 23) return false;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;

            hour = h;
            minute = m;
            return true;
        }
    }
}
