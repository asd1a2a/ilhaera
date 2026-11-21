using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TodoWidget.Core;

static class Tests
{
    static int failed, passed;
    static void Check(bool cond, string name)
    {
        if (cond) passed++; else { failed++; Console.WriteLine("FAIL: " + name); }
    }

    static int Main()
    {
        var now = new DateTime(2026, 11, 20, 14, 12, 30);

        // ── 시간 파싱 ──
        var cases = new (string, int, int)[] {
            ("14:30",14,30), ("1430",14,30), ("930",9,30), ("9",9,0), ("15시",15,0), ("14시30분",14,30),
            ("오후 2:30",14,30), ("오후 2시 반",14,30), ("오전 12시",0,0), ("오후 12시",12,0), ("pm 3",15,0),
            ("3pm",15,0), ("9.05",9,5), ("정오",12,0), ("자정",0,0), (" 07:05 ",7,5), ("저녁 7시",19,0), ("오후 13시",13,0),
        };
        foreach (var (s, h, m) in cases)
        {
            int hh, mm;
            bool ok = TimeText.TryParseTime(s, out hh, out mm);
            Check(ok && hh == h && mm == m, $"parse '{s}' => {hh}:{mm} (ok={ok})");
        }
        foreach (var bad in new[] { "", "abc", "25:00", "12:60", "오후 25시", "1:2:3" })
        {
            int hh, mm;
            Check(!TimeText.TryParseTime(bad, out hh, out mm), $"reject '{bad}'");
        }

        // ── 라벨 ──
        Check(TimeText.DueLabel(new DateTime(2026, 11, 20, 9, 5, 0), now) == "오늘 09:05", "label today");
        Check(TimeText.DueLabel(new DateTime(2026, 11, 21, 18, 0, 0), now) == "내일 18:00", "label tomorrow");
        Check(TimeText.DueLabel(new DateTime(2026, 11, 25, 18, 0, 0), now) == "11/25 (수) 18:00", "label weekday " + TimeText.DueLabel(new DateTime(2026, 11, 25, 18, 0, 0), now));
        Check(TimeText.DueLabel(new DateTime(2027, 1, 3, 8, 0, 0), now) == "2027/1/3 (일) 08:00", "label next year");
        Check(TimeText.Relative(now.AddMinutes(25), now) == "25분 후", "rel 25m");
        Check(TimeText.Relative(now.AddMinutes(190), now) == "3시간 10분 후", "rel 3h10m " + TimeText.Relative(now.AddMinutes(190), now));
        Check(TimeText.Relative(now.AddMinutes(-5), now) == "시간 지남", "rel past");
        Check(TimeText.Relative(now.AddDays(3), now) == "3일 후", "rel days");
        Check(TimeText.DefaultDue(now) == new DateTime(2026, 11, 20, 15, 0, 0), "default due");
        Check(TimeText.DefaultDue(new DateTime(2026, 11, 20, 23, 40, 0)) == new DateTime(2026, 11, 21, 0, 0, 0), "default due midnight");
        Check(TimeText.DateChoiceLabel(now.Date, now) == "오늘 · 11/20 (금)", "date choice today");

        // ── 알림 ──
        var a = TodoItem.Create("회의", new DateTime(2026, 11, 20, 15, 0, 0), now);        // 47분 남음 → 아직
        var b = TodoItem.Create("전화", new DateTime(2026, 11, 20, 14, 40, 0), now);       // 27분 남음 → 알림
        var c = TodoItem.Create("지난일", new DateTime(2026, 11, 20, 9, 0, 0), now);       // 추가 시점에 이미 지남 → 알림 없음
        var d = TodoItem.Create("시간없음", null, now);
        var e = TodoItem.Create("완료됨", new DateTime(2026, 11, 20, 14, 30, 0), now); e.SetDone(true, now);
        var items = new List<TodoItem> { a, b, c, d, e };
        Check(c.Notified, "past due at creation is pre-notified");

        var alerts = Reminders.Collect(items, now, 30);
        Check(alerts.Count == 1 && alerts[0].Item == b && alerts[0].Kind == AlertKind.Upcoming && alerts[0].MinutesLeft == 28, "collect first: " + string.Join(",", alerts.Select(x => x.Item.Text + x.MinutesLeft)));
        Check(Reminders.Collect(items, now, 30).Count == 0, "no duplicate alert");
        var later = now.AddMinutes(18.5); // 14:31 → 회의 29분 전
        alerts = Reminders.Collect(items, later, 30);
        Check(alerts.Count == 1 && alerts[0].Item == a && alerts[0].MinutesLeft == 29, "collect a at 14:31");

        // 컴퓨터가 꺼져 있다 켜진 경우: 놓친 일정
        var f = TodoItem.Create("아침 약속", new DateTime(2026, 11, 21, 9, 0, 0), now);
        var g = TodoItem.Create("오래된 일", new DateTime(2026, 11, 21, 9, 0, 0), now);
        alerts = Reminders.Collect(new[] { f }, new DateTime(2026, 11, 21, 9, 20, 0), 30);
        Check(alerts.Count == 1 && alerts[0].Kind == AlertKind.Missed, "missed alert after boot");
        alerts = Reminders.Collect(new[] { g }, new DateTime(2026, 11, 23, 9, 0, 0), 30);
        Check(alerts.Count == 0 && g.Notified, "very old missed is silent");

        // 시간 변경 시 알림 초기화
        b.SetDue(new DateTime(2026, 11, 20, 18, 0, 0), now);
        Check(!b.Notified, "reschedule resets notified");
        b.SetDue(new DateTime(2026, 11, 20, 13, 0, 0), now);
        Check(b.Notified, "reschedule into past stays silent");

        // ── 정렬 ──
        var sorted = Reminders.Sort(items);
        Check(sorted.Last() == e, "done items last");
        Check(sorted.IndexOf(d) == sorted.Count - 2, "no-due after due items");

        // ── 자연어 입력 ── (now = 2026-11-20 금 14:12)
        var qp = new (string input, string text, string due)[] {
            ("내일 오후 3시 회의", "회의", "2026-11-21 15:00"),
            ("회의 내일 3시", "회의", "2026-11-21 15:00"),
            ("3시 반 전화", "전화", "2026-11-20 15:30"),
            ("3시반에 전화", "전화", "2026-11-20 15:30"),
            ("9시 운동", "운동", "2026-11-20 21:00"),
            ("7시 저녁 약속", "저녁 약속", "2026-11-20 19:00"),
            ("월요일 10시 주간회의", "주간회의", "2026-11-23 10:00"),
            ("다음주 수요일 2시 치과", "치과", "2026-11-25 14:00"),
            ("이번주 일요일 등산", "등산", "2026-11-22 09:00"),
            ("12/24 저녁 7시 파티", "파티", "2026-12-24 19:00"),
            ("1월 3일 여행", "여행", "2027-01-03 09:00"),
            ("11월25일 10:30 면접", "면접", "2026-11-25 10:30"),
            ("내일까지 보고서 제출", "보고서 제출", "2026-11-21 09:00"),
            ("오전 9:30 병원", "병원", "2026-11-21 09:30"),
            ("점심 1시 약속", "약속", "2026-11-21 13:00"),
            ("모레 오후 2시 30분 미팅", "미팅", "2026-11-22 14:30"),
            ("16:45 택배 수령", "택배 수령", "2026-11-20 16:45"),
        };
        foreach (var q in qp)
        {
            string text; DateTime due;
            bool ok = QuickParse.TryParse(q.input, now, out text, out due);
            Check(ok && text == q.text && due.ToString("yyyy-MM-dd HH:mm") == q.due,
                $"quick '{q.input}' => ok={ok} '{text}' {due:yyyy-MM-dd HH:mm}");
        }
        foreach (var bad in new[] { "오늘 할 일 정리", "3시간 공부", "내일 3시", "장보기", "2월 계획", "" })
        {
            string text; DateTime due;
            Check(!QuickParse.TryParse(bad, now, out text, out due), $"quick reject '{bad}' => '{text}' {due:yyyy-MM-dd HH:mm}");
        }

        // ── 반복 일정 ── (now = 2026-11-20 금 14:12)
        var rp = new (string input, string text, string due, Repeat rep)[] {
            ("매일 9시 약 먹기", "약 먹기", "2026-11-21 09:00", Repeat.Daily),
            ("매일 오후 3시 스트레칭", "스트레칭", "2026-11-20 15:00", Repeat.Daily),
            ("매일 운동", "운동", "2026-11-21 09:00", Repeat.Daily),
            ("평일 9시 스탠드업", "스탠드업", "2026-11-23 09:00", Repeat.Weekdays),
            ("평일 오후 5시 업무일지", "업무일지", "2026-11-20 17:00", Repeat.Weekdays),
            ("매주 월요일 10시 주간회의", "주간회의", "2026-11-23 10:00", Repeat.Weekly),
            ("금요일마다 2시 회고", "회고", "2026-11-27 14:00", Repeat.Weekly),
            ("매주 금요일 오후 6시 운동", "운동", "2026-11-20 18:00", Repeat.Weekly),
        };
        foreach (var q in rp)
        {
            string text; DateTime due; Repeat rep;
            bool ok = QuickParse.TryParse(q.input, now, out text, out due, out rep);
            Check(ok && text == q.text && rep == q.rep && due.ToString("yyyy-MM-dd HH:mm") == q.due,
                $"repeat '{q.input}' => ok={ok} '{text}' {rep} {due:yyyy-MM-dd HH:mm}");
        }
        {
            string text; DateTime due; Repeat rep;
            Check(!QuickParse.TryParse("매일", now, out text, out due, out rep) && rep == Repeat.None, "repeat alone rejected");
        }

        // 다음 회차
        Check(Recurrence.NextAfter(Repeat.Daily, new DateTime(2026, 11, 20, 9, 0, 0)) == new DateTime(2026, 11, 21, 9, 0, 0), "next daily");
        Check(Recurrence.NextAfter(Repeat.Weekdays, new DateTime(2026, 11, 20, 9, 0, 0)) == new DateTime(2026, 11, 23, 9, 0, 0), "next weekday skips weekend");
        Check(Recurrence.NextAfter(Repeat.Weekly, new DateTime(2026, 11, 20, 9, 0, 0)) == new DateTime(2026, 11, 27, 9, 0, 0), "next weekly");
        Check(Recurrence.Label(Repeat.Weekly, new DateTime(2026, 11, 20)) == "매주 금", "weekly label");

        var daily = TodoItem.Create("약", new DateTime(2026, 11, 20, 15, 0, 0), now, Repeat.Daily);
        daily.CompleteOccurrence(now);
        Check(!daily.Done && daily.Due == new DateTime(2026, 11, 21, 15, 0, 0) && !daily.Notified && daily.LastDone == now, "complete occurrence");
        Check(daily.RepeatRaw == "daily", "repeat raw");

        // 컴퓨터가 꺼져 있었던 경우: 지난 회차를 오늘로 넘김
        var stale = TodoItem.Create("스탠드업", new DateTime(2026, 11, 17, 9, 0, 0), new DateTime(2026, 11, 16), Repeat.Weekdays);
        var stale2 = TodoItem.Create("주간", new DateTime(2026, 11, 9, 10, 0, 0), new DateTime(2026, 11, 8), Repeat.Weekly);
        Check(Recurrence.RollForward(new[] { stale, stale2 }, now), "roll changed");
        Check(stale.Due == new DateTime(2026, 11, 20, 9, 0, 0) && !stale.Notified, "roll weekday to today " + stale.Due);
        Check(stale2.Due == new DateTime(2026, 11, 23, 10, 0, 0), "roll weekly to next monday " + stale2.Due);
        var rolledAlerts = Reminders.Collect(new[] { stale }, now, 30);
        Check(rolledAlerts.Count == 1 && rolledAlerts[0].Kind == AlertKind.Missed, "rolled missed alert");
        var satNow = new DateTime(2026, 11, 21, 10, 0, 0);
        var wk = TodoItem.Create("평일", new DateTime(2026, 11, 20, 9, 0, 0), new DateTime(2026, 11, 19), Repeat.Weekdays);
        Recurrence.RollForward(new[] { wk }, satNow);
        Check(wk.Due == new DateTime(2026, 11, 23, 9, 0, 0), "weekday roll on saturday => monday " + wk.Due);

        // ── 다시 알림 ──
        var sz = TodoItem.Create("스누즈", now.AddMinutes(20), now);
        var szList = new[] { sz };
        Check(Reminders.Collect(szList, now, 30).Count == 1, "snooze first alert");
        sz.Snooze = now.AddMinutes(10);
        Check(Reminders.Collect(szList, now.AddMinutes(5), 30).Count == 0, "snooze waits");
        var again = Reminders.Collect(szList, now.AddMinutes(10), 30);
        Check(again.Count == 1 && again[0].Kind == AlertKind.Upcoming && again[0].MinutesLeft == 10 && sz.Snooze == null, "snooze fires once");
        Check(Reminders.Collect(szList, now.AddMinutes(11), 30).Count == 0, "snooze cleared");
        sz.Snooze = now.AddMinutes(30);
        again = Reminders.Collect(szList, now.AddMinutes(30), 30);
        Check(again.Count == 1 && again[0].Kind == AlertKind.Missed, "snooze after due => missed");
        sz.Snooze = now.AddMinutes(40); sz.SetDone(true, now);
        Check(sz.Snooze == null, "done clears snooze");

        // ── 묶음/다음 일정 ──
        var gNow = now;
        var g1 = TodoItem.Create("지남", gNow.AddHours(-1), gNow.AddHours(-3));
        var g2 = TodoItem.Create("오늘", gNow.AddHours(2), gNow);
        var g3 = TodoItem.Create("내일", gNow.Date.AddDays(1).AddHours(10), gNow);
        var g4 = TodoItem.Create("이후", gNow.Date.AddDays(5).AddHours(10), gNow);
        var g5 = TodoItem.Create("없음", null, gNow);
        var g6 = TodoItem.Create("완료", gNow.AddHours(3), gNow); g6.SetDone(true, gNow);
        var groups = Reminders.Group(new[] { g6, g5, g4, g3, g2, g1 }, gNow);
        Check(string.Join(",", groups.Select(x => x.Kind)) == "Overdue,Today,Tomorrow,Later,NoDate,Done", "group order " + string.Join(",", groups.Select(x => x.Kind)));
        Check(groups.All(x => x.Items.Count == 1) && groups[1].Items[0] == g2, "group items");
        Check(Reminders.NextUp(new[] { g1, g2, g3, g6 }, gNow) == g2, "next up");
        Check(Reminders.NextUp(new[] { g1, g5 }, gNow) == null, "no next up");

        // ── 저장/불러오기 ──
        var dir = Path.Combine(Path.GetTempPath(), "todowidget-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new Storage(dir);
            var empty = storage.Load();
            Check(empty.Items.Count == 0 && empty.Settings.ReminderMinutes == 30 && empty.Settings.OpacityPercent == 97, "load empty defaults");

            var data = new AppData();
            data.Items.AddRange(items);
            data.Settings.X = 100; data.Settings.Y = -20; data.Settings.Width = 320; data.Settings.Height = 480;
            data.Settings.ReminderMinutes = 15;
            storage.Save(data);
            storage.Save(data); // 두 번째 저장은 File.Replace 경로 + .bak 생성
            var json = File.ReadAllText(storage.FilePath);
            Check(json.Contains("회의") && json.Contains("\"due\""), "json readable utf8");
            Check(File.Exists(storage.FilePath + ".bak"), "backup created");

            var loaded = new Storage(dir).Load();
            Check(loaded.Items.Count == items.Count, "roundtrip count");
            var la = loaded.Items.First(i => i.Id == a.Id);
            Check(la.Text == "회의" && la.Due == a.Due && la.Notified == a.Notified && la.Created == a.Created, "roundtrip item");
            Check(loaded.Items.First(i => i.Id == e.Id).Done, "roundtrip done");
            Check(loaded.Settings.ReminderMinutes == 15 && loaded.Settings.Y == -20 && loaded.Settings.Width == 320, "roundtrip settings");

            // 설정이 빠진 옛 파일 → 기본값
            File.WriteAllText(storage.FilePath, "{\"items\":[{\"id\":\"x\",\"text\":\"옛 항목\"}]}");
            var old = storage.Load();
            Check(old.Items.Count == 1 && old.Settings.ReminderMinutes == 30 && old.Settings.OpacityPercent == 97, "missing settings => defaults");

            // 손상된 파일 → 백업에서 복구 + 손상본 보관
            File.WriteAllText(storage.FilePath, "{ broken");
            var recovered = storage.Load();
            Check(recovered.Items.Count == items.Count, "recover from .bak");
            Check(Directory.GetFiles(dir, "todos.corrupt-*.json").Length == 1, "corrupt copy kept");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }

        Console.WriteLine($"passed={passed} failed={failed}");
        return failed == 0 ? 0 : 1;
    }
}
