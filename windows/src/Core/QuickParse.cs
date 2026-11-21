using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TodoWidget.Core
{
    /// <summary>
    /// "내일 오후 3시 회의", "금요일 2시 반 치과", "12/24 저녁 7시 파티" 처럼
    /// 입력한 문장에서 날짜·시간을 찾아 일정 시각과 나머지 제목으로 나눈다.
    /// </summary>
    public static class QuickParse
    {
        const string Markers = "오전|오후|아침|점심|저녁|밤|새벽";
        const string Weekdays = "일월화수목금토";

        static readonly Regex HourForm = new Regex(@"^(" + Markers + @")?(\d{1,2})시(?:(반)|(\d{1,2})분)?$", RegexOptions.CultureInvariant);
        static readonly Regex ColonForm = new Regex(@"^(" + Markers + @")?(\d{1,2}):(\d{2})$", RegexOptions.CultureInvariant);
        static readonly Regex MinuteToken = new Regex(@"^(\d{1,2})분$", RegexOptions.CultureInvariant);
        static readonly Regex WeekdayToken = new Regex(@"^([일월화수목금토])(?:요일|욜)$", RegexOptions.CultureInvariant);
        static readonly Regex SlashDate = new Regex(@"^(\d{1,2})/(\d{1,2})$", RegexOptions.CultureInvariant);
        static readonly Regex MonthDay = new Regex(@"^(\d{1,2})월(\d{1,2})일$", RegexOptions.CultureInvariant);
        static readonly Regex MonthOnly = new Regex(@"^(\d{1,2})월$", RegexOptions.CultureInvariant);
        static readonly Regex DayOnly = new Regex(@"^(\d{1,2})일$", RegexOptions.CultureInvariant);
        static readonly string[] Particles = { "까지", "부터", "에는", "마다", "에", "쯤" };

        public static bool TryParse(string input, DateTime now, out string text, out DateTime due)
        {
            Repeat repeat;
            return TryParse(input, now, out text, out due, out repeat);
        }

        /// <summary>반복("매일", "평일", "매주 월요일", "월요일마다")까지 함께 해석한다.</summary>
        public static bool TryParse(string input, DateTime now, out string text, out DateTime due, out Repeat repeat)
        {
            text = input;
            due = default(DateTime);
            repeat = Repeat.None;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var tokens = input.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var used = new bool[tokens.Length];
            DateTime? date = null;
            bool dateIsToday = false;
            int hour = -1, minute = 0;
            string marker = null;

            for (int i = 0; i < tokens.Length; i++)
            {
                if (used[i]) continue;
                var t = Strip(tokens[i]);

                if (repeat == Repeat.None)
                {
                    if (t == "매일" || t == "매일매일" || t == "날마다" || tokens[i] == "날마다")
                    {
                        repeat = Repeat.Daily; used[i] = true; continue;
                    }
                    if (t == "평일" || t == "주중")
                    {
                        repeat = Repeat.Weekdays; used[i] = true; continue;
                    }
                    if (t == "매주")
                    {
                        repeat = Repeat.Weekly; used[i] = true;
                        if (date == null && i + 1 < tokens.Length)
                        {
                            int c2; bool today2;
                            var d2 = MatchDate(tokens, i + 1, now, out c2, out today2);
                            if (d2.HasValue && WeekdayToken.IsMatch(Strip(tokens[i + 1])))
                            {
                                date = d2; dateIsToday = today2; used[i + 1] = true; i++;
                            }
                        }
                        continue;
                    }
                    if (tokens[i].EndsWith("마다", StringComparison.Ordinal) && date == null && WeekdayToken.IsMatch(t))
                    {
                        int c3; bool today3;
                        date = MatchDate(tokens, i, now, out c3, out today3);
                        dateIsToday = today3;
                        repeat = Repeat.Weekly; used[i] = true; continue;
                    }
                }

                if (date == null)
                {
                    int consumed;
                    var d = MatchDate(tokens, i, now, out consumed, out dateIsToday);
                    if (d.HasValue)
                    {
                        date = d;
                        for (int k = 0; k < consumed; k++) used[i + k] = true;
                        i += consumed - 1;
                        continue;
                    }
                }

                if (hour < 0)
                {
                    // "오후 3시" 처럼 따로 떨어진 오전/오후
                    if (Regex.IsMatch(t, "^(" + Markers + ")$") && i + 1 < tokens.Length && !used[i + 1])
                    {
                        int h, m; string mk; bool hasMin;
                        if (MatchTime(Strip(tokens[i + 1]), out h, out m, out mk, out hasMin) && mk == null)
                        {
                            marker = t;
                            hour = h; minute = m;
                            used[i] = used[i + 1] = true;
                            i++;
                            if (!hasMin) i += TakeMinutes(tokens, used, i + 1, ref minute);
                            continue;
                        }
                    }
                    int hh, mm; string mk2; bool hasMin2;
                    if (MatchTime(t, out hh, out mm, out mk2, out hasMin2))
                    {
                        marker = mk2;
                        hour = hh; minute = mm;
                        used[i] = true;
                        if (!hasMin2) i += TakeMinutes(tokens, used, i + 1, ref minute);
                        continue;
                    }
                }
            }

            if (date == null && hour < 0 && repeat == Repeat.None) return false;

            var rest = string.Join(" ", tokens.Where((tok, idx) => !used[idx])).Trim();
            if (rest.Length == 0) { repeat = Repeat.None; return false; }

            DateTime result;
            if (repeat != Repeat.None)
            {
                if (hour < 0) { hour = 9; minute = 0; marker = "오전"; }
                int rh = ResolveHour(hour, marker, true, now, minute);
                if (rh < 0 || rh > 23 || minute > 59) { repeat = Repeat.None; return false; }
                var start = (date ?? now.Date).Date.AddHours(rh).AddMinutes(minute);
                start = Recurrence.FirstOnOrAfter(repeat, start, start.DayOfWeek);
                if (start <= now) start = Recurrence.NextAfter(repeat, start);
                text = rest;
                due = start;
                return true;
            }
            if (hour >= 0)
            {
                int h = ResolveHour(hour, marker, date.HasValue, now, minute);
                if (h < 0 || h > 23 || minute > 59) return false;
                if (date.HasValue)
                {
                    result = date.Value.Date.AddHours(h).AddMinutes(minute);
                }
                else
                {
                    result = now.Date.AddHours(h).AddMinutes(minute);
                    if (result <= now) result = result.AddDays(1);
                }
            }
            else
            {
                // 날짜만 있으면 그날 오전 9시. "오늘"만 있는 문장은 일정으로 보지 않는다.
                if (dateIsToday) return false;
                result = date.Value.Date.AddHours(9);
            }

            text = rest;
            due = result;
            return true;
        }

        static string Strip(string token)
        {
            foreach (var p in Particles)
                if (token.Length > p.Length && token.EndsWith(p, StringComparison.Ordinal))
                    return token.Substring(0, token.Length - p.Length);
            return token;
        }

        static int TakeMinutes(string[] tokens, bool[] used, int i, ref int minute)
        {
            if (i >= tokens.Length || used[i]) return 0;
            var t = Strip(tokens[i]);
            if (t == "반") { minute = 30; used[i] = true; return 1; }
            var m = MinuteToken.Match(t);
            if (m.Success) { minute = Int(m.Groups[1]); used[i] = true; return 1; }
            return 0;
        }

        static bool MatchTime(string t, out int hour, out int minute, out string marker, out bool hasMinutes)
        {
            hour = -1; minute = 0; marker = null; hasMinutes = false;
            if (t == "정오") { hour = 12; marker = "정오"; hasMinutes = true; return true; }
            var m = HourForm.Match(t);
            if (m.Success)
            {
                marker = m.Groups[1].Success ? m.Groups[1].Value : null;
                hour = Int(m.Groups[2]);
                if (m.Groups[3].Success) { minute = 30; hasMinutes = true; }
                else if (m.Groups[4].Success) { minute = Int(m.Groups[4]); hasMinutes = true; }
                return hour <= 24;
            }
            m = ColonForm.Match(t);
            if (m.Success)
            {
                marker = m.Groups[1].Success ? m.Groups[1].Value : null;
                hour = Int(m.Groups[2]);
                minute = Int(m.Groups[3]);
                hasMinutes = true;
                return hour <= 23 && minute <= 59;
            }
            return false;
        }

        /// <summary>오전/오후가 없을 때: 1~6시는 오후, 7~11시는 날짜가 있으면 오전·없으면 다가오는 쪽.</summary>
        static int ResolveHour(int h, string marker, bool hasDate, DateTime now, int minute)
        {
            switch (marker)
            {
                case "정오": return 12;
                case "오후": case "저녁": case "밤": return h < 12 ? h + 12 : h;
                case "점심": return h <= 5 ? h + 12 : h;
                case "오전": case "아침": case "새벽": return h == 12 ? 0 : h;
            }
            if (h == 24) return 0;
            if (h >= 12 || h == 0) return h;
            if (h <= 6) return h + 12;
            if (hasDate) return h;
            var am = now.Date.AddHours(h).AddMinutes(minute);
            if (am > now) return h;
            var pm = am.AddHours(12);
            return pm > now ? h + 12 : h;
        }

        static DateTime? MatchDate(string[] tokens, int i, DateTime now, out int consumed, out bool isToday)
        {
            consumed = 1;
            isToday = false;
            var today = now.Date;
            var t = Strip(tokens[i]);
            switch (t)
            {
                case "오늘": isToday = true; return today;
                case "내일": return today.AddDays(1);
                case "모레": return today.AddDays(2);
                case "글피": return today.AddDays(3);
            }

            if ((t == "이번주" || t == "다음주" || t == "담주") && i + 1 < tokens.Length)
            {
                var wd = WeekdayToken.Match(Strip(tokens[i + 1]));
                if (wd.Success)
                {
                    consumed = 2;
                    var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                    int target = Weekdays.IndexOf(wd.Groups[1].Value[0]);
                    var d = monday.AddDays((target + 6) % 7);
                    if (t != "이번주") d = d.AddDays(7);
                    isToday = d == today;
                    return d;
                }
            }

            var w = WeekdayToken.Match(t);
            if (w.Success)
            {
                int target = Weekdays.IndexOf(w.Groups[1].Value[0]);
                int diff = (target - (int)today.DayOfWeek + 7) % 7;
                isToday = diff == 0;
                return today.AddDays(diff);
            }

            var s = SlashDate.Match(t);
            if (s.Success) return MakeDate(Int(s.Groups[1]), Int(s.Groups[2]), today);

            var md = MonthDay.Match(t);
            if (md.Success) return MakeDate(Int(md.Groups[1]), Int(md.Groups[2]), today);

            var mo = MonthOnly.Match(t);
            if (mo.Success && i + 1 < tokens.Length)
            {
                var dd = DayOnly.Match(Strip(tokens[i + 1]));
                if (dd.Success)
                {
                    consumed = 2;
                    return MakeDate(Int(mo.Groups[1]), Int(dd.Groups[1]), today);
                }
            }
            return null;
        }

        static DateTime? MakeDate(int month, int day, DateTime today)
        {
            if (month < 1 || month > 12 || day < 1 || day > 31) return null;
            for (int y = today.Year; y <= today.Year + 1; y++)
            {
                if (day > DateTime.DaysInMonth(y, month)) continue;
                var d = new DateTime(y, month, day);
                if (d >= today) return d;
            }
            return null;
        }

        static int Int(Group g) { return int.Parse(g.Value, CultureInfo.InvariantCulture); }
    }
}
