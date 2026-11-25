import XCTest
@testable import IlhaeraCore

/// Windows 버전 tests/Tests.cs 와 같은 사례. now = 2026-11-20 (금) 14:12:30
final class CoreTests: XCTestCase {
    let now = TimeMath.make(2026, 11, 20, 14, 12, 30)

    func fmt(_ d: Date) -> String {
        String(format: "%04d-%02d-%02d %02d:%02d", TimeMath.year(d), TimeMath.month(d), TimeMath.day(d), TimeMath.hour(d), TimeMath.minute(d))
    }

    // ── 문장 해석 ──

    func testQuickParse() {
        let cases: [(String, String, String)] = [
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
        ]
        for (input, text, due) in cases {
            let r = QuickParse.parse(input, now: now)
            XCTAssertEqual(r?.text, text, input)
            XCTAssertEqual(r.map { fmt($0.due) }, due, input)
            XCTAssertEqual(r?.repeatRule, Repeat.none, input)
        }
        for bad in ["오늘 할 일 정리", "3시간 공부", "내일 3시", "장보기", "2월 계획", "", "매일"] {
            XCTAssertNil(QuickParse.parse(bad, now: now), bad)
        }
    }

    func testQuickParseRepeat() {
        let cases: [(String, String, String, Repeat)] = [
            ("매일 9시 약 먹기", "약 먹기", "2026-11-21 09:00", .daily),
            ("매일 오후 3시 스트레칭", "스트레칭", "2026-11-20 15:00", .daily),
            ("매일 운동", "운동", "2026-11-21 09:00", .daily),
            ("평일 9시 스탠드업", "스탠드업", "2026-11-23 09:00", .weekdays),
            ("평일 오후 5시 업무일지", "업무일지", "2026-11-20 17:00", .weekdays),
            ("매주 월요일 10시 주간회의", "주간회의", "2026-11-23 10:00", .weekly),
            ("금요일마다 2시 회고", "회고", "2026-11-27 14:00", .weekly),
            ("매주 금요일 오후 6시 운동", "운동", "2026-11-20 18:00", .weekly),
        ]
        for (input, text, due, rule) in cases {
            let r = QuickParse.parse(input, now: now)
            XCTAssertEqual(r?.text, text, input)
            XCTAssertEqual(r.map { fmt($0.due) }, due, input)
            XCTAssertEqual(r?.repeatRule, rule, input)
        }
    }

    // ── 표기 ──

    func testLabels() {
        XCTAssertEqual(TimeText.dueLabel(TimeMath.make(2026, 11, 20, 9, 5), now: now), "오늘 09:05")
        XCTAssertEqual(TimeText.dueLabel(TimeMath.make(2026, 11, 21, 18, 0), now: now), "내일 18:00")
        XCTAssertEqual(TimeText.dueLabel(TimeMath.make(2026, 11, 25, 18, 0), now: now), "11/25 (수) 18:00")
        XCTAssertEqual(TimeText.dueLabel(TimeMath.make(2027, 1, 3, 8, 0), now: now), "2027/1/3 (일) 08:00")
        XCTAssertEqual(TimeText.relative(TimeMath.addMinutes(now, 25), now: now), "25분 후")
        XCTAssertEqual(TimeText.relative(TimeMath.addMinutes(now, 190), now: now), "3시간 10분 후")
        XCTAssertEqual(TimeText.relative(TimeMath.addMinutes(now, -5), now: now), "시간 지남")
        XCTAssertEqual(TimeText.relative(TimeMath.addDays(now, 3), now: now), "3일 후")
        XCTAssertEqual(TimeText.defaultDue(now), TimeMath.make(2026, 11, 20, 15, 0))
        XCTAssertEqual(TimeText.defaultDue(TimeMath.make(2026, 11, 20, 23, 40)), TimeMath.make(2026, 11, 21, 0, 0))
        XCTAssertEqual(TimeText.korean12(TimeMath.make(2026, 11, 20, 15, 0)), "오후 3:00")
        XCTAssertEqual(TimeText.korean12(TimeMath.make(2026, 11, 20, 0, 5)), "오전 12:05")
    }

    // ── 알림 ──

    func testReminders() {
        let a = TodoItem.create(text: "회의", due: TimeMath.make(2026, 11, 20, 15, 0), now: now)
        let b = TodoItem.create(text: "전화", due: TimeMath.make(2026, 11, 20, 14, 40), now: now)
        let c = TodoItem.create(text: "지난일", due: TimeMath.make(2026, 11, 20, 9, 0), now: now)
        let d = TodoItem.create(text: "시간없음", due: nil, now: now)
        var e = TodoItem.create(text: "완료됨", due: TimeMath.make(2026, 11, 20, 14, 30), now: now)
        e.setDone(true, now: now)
        var items = [a, b, c, d, e]
        XCTAssertTrue(c.notified, "past due at creation is pre-notified")

        var alerts = Reminders.collect(&items, now: now, leadMinutes: 30)
        XCTAssertEqual(alerts.map { $0.item.text }, ["전화"])
        XCTAssertEqual(alerts.first?.kind, .upcoming)
        XCTAssertEqual(alerts.first?.minutesLeft, 28)
        XCTAssertTrue(Reminders.collect(&items, now: now, leadMinutes: 30).isEmpty, "no duplicate alert")

        alerts = Reminders.collect(&items, now: now.addingTimeInterval(18.5 * 60), leadMinutes: 30)
        XCTAssertEqual(alerts.map { $0.item.text }, ["회의"])
        XCTAssertEqual(alerts.first?.minutesLeft, 29)

        var missed = [TodoItem.create(text: "아침 약속", due: TimeMath.make(2026, 11, 21, 9, 0), now: now)]
        alerts = Reminders.collect(&missed, now: TimeMath.make(2026, 11, 21, 9, 20), leadMinutes: 30)
        XCTAssertEqual(alerts.first?.kind, .missed)

        var old = [TodoItem.create(text: "오래된 일", due: TimeMath.make(2026, 11, 21, 9, 0), now: now)]
        alerts = Reminders.collect(&old, now: TimeMath.make(2026, 11, 23, 9, 0), leadMinutes: 30)
        XCTAssertTrue(alerts.isEmpty)
        XCTAssertTrue(old[0].notified)

        var r = b
        r.setDue(TimeMath.make(2026, 11, 20, 18, 0), now: now)
        XCTAssertFalse(r.notified, "reschedule resets notified")
        r.setDue(TimeMath.make(2026, 11, 20, 13, 0), now: now)
        XCTAssertTrue(r.notified, "reschedule into past stays silent")
    }

    func testSnooze() {
        var items = [TodoItem.create(text: "스누즈", due: TimeMath.addMinutes(now, 20), now: now)]
        XCTAssertEqual(Reminders.collect(&items, now: now, leadMinutes: 30).count, 1)
        items[0].snooze = TimeMath.addMinutes(now, 10)
        XCTAssertTrue(Reminders.collect(&items, now: TimeMath.addMinutes(now, 5), leadMinutes: 30).isEmpty)
        let again = Reminders.collect(&items, now: TimeMath.addMinutes(now, 10), leadMinutes: 30)
        XCTAssertEqual(again.first?.kind, .upcoming)
        XCTAssertEqual(again.first?.minutesLeft, 10)
        XCTAssertNil(items[0].snooze)
        XCTAssertTrue(Reminders.collect(&items, now: TimeMath.addMinutes(now, 11), leadMinutes: 30).isEmpty)
        items[0].snooze = TimeMath.addMinutes(now, 30)
        XCTAssertEqual(Reminders.collect(&items, now: TimeMath.addMinutes(now, 30), leadMinutes: 30).first?.kind, .missed)
        items[0].snooze = TimeMath.addMinutes(now, 40)
        items[0].setDone(true, now: now)
        XCTAssertNil(items[0].snooze)
    }

    // ── 반복 ──

    func testRecurrence() {
        let fri9 = TimeMath.make(2026, 11, 20, 9, 0)
        XCTAssertEqual(Recurrence.nextAfter(.daily, fri9), TimeMath.make(2026, 11, 21, 9, 0))
        XCTAssertEqual(Recurrence.nextAfter(.weekdays, fri9), TimeMath.make(2026, 11, 23, 9, 0))
        XCTAssertEqual(Recurrence.nextAfter(.weekly, fri9), TimeMath.make(2026, 11, 27, 9, 0))
        XCTAssertEqual(Recurrence.label(.weekly, fri9), "매주 금")

        var daily = TodoItem.create(text: "약", due: TimeMath.make(2026, 11, 20, 15, 0), now: now, repeat: .daily)
        daily.completeOccurrence(now: now)
        XCTAssertFalse(daily.done)
        XCTAssertEqual(daily.due, TimeMath.make(2026, 11, 21, 15, 0))
        XCTAssertFalse(daily.notified)
        XCTAssertEqual(daily.lastDone, now)

        var items = [
            TodoItem.create(text: "스탠드업", due: TimeMath.make(2026, 11, 17, 9, 0), now: TimeMath.make(2026, 11, 16), repeat: .weekdays),
            TodoItem.create(text: "주간", due: TimeMath.make(2026, 11, 9, 10, 0), now: TimeMath.make(2026, 11, 8), repeat: .weekly),
        ]
        XCTAssertTrue(Recurrence.rollForward(&items, now: now))
        XCTAssertEqual(items[0].due, TimeMath.make(2026, 11, 20, 9, 0))
        XCTAssertFalse(items[0].notified)
        XCTAssertEqual(items[1].due, TimeMath.make(2026, 11, 23, 10, 0))
        var first = [items[0]]
        XCTAssertEqual(Reminders.collect(&first, now: now, leadMinutes: 30).first?.kind, .missed)

        var wk = [TodoItem.create(text: "평일", due: fri9, now: TimeMath.make(2026, 11, 19), repeat: .weekdays)]
        Recurrence.rollForward(&wk, now: TimeMath.make(2026, 11, 21, 10, 0))
        XCTAssertEqual(wk[0].due, TimeMath.make(2026, 11, 23, 9, 0))
    }

    // ── 묶음 ──

    func testGrouping() {
        let g1 = TodoItem.create(text: "지남", due: now.addingTimeInterval(-3600), now: now.addingTimeInterval(-3 * 3600))
        let g2 = TodoItem.create(text: "오늘", due: now.addingTimeInterval(2 * 3600), now: now)
        let g3 = TodoItem.create(text: "내일", due: TimeMath.at(TimeMath.addDays(now, 1), 10, 0), now: now)
        let g4 = TodoItem.create(text: "이후", due: TimeMath.at(TimeMath.addDays(now, 5), 10, 0), now: now)
        let g5 = TodoItem.create(text: "없음", due: nil, now: now)
        var g6 = TodoItem.create(text: "완료", due: now.addingTimeInterval(3 * 3600), now: now)
        g6.setDone(true, now: now)
        let groups = Reminders.group([g6, g5, g4, g3, g2, g1], now: now)
        XCTAssertEqual(groups.map { $0.kind }, [.overdue, .today, .tomorrow, .later, .noDate, .done])
        XCTAssertTrue(groups.allSatisfy { $0.items.count == 1 })
        XCTAssertEqual(Reminders.nextUp([g1, g2, g3, g6], now: now)?.text, "오늘")
        XCTAssertNil(Reminders.nextUp([g1, g5], now: now))
    }

    // ── 설정 ──

    func testSettingsColorsAndGlass() throws {
        var s = AppSettings()
        XCTAssertEqual(s.accentColor, "#1E7A57")
        XCTAssertEqual(s.markerColor, "#FFEC85")
        XCTAssertEqual(s.glassTint, 35)
        XCTAssertEqual(AppSettings.parseHex("#2F6BD8"), 0x2F6BD8)
        XCTAssertEqual(AppSettings.parseHex("2f6bd8"), 0x2F6BD8)
        XCTAssertNil(AppSettings.parseHex("#12345"))
        XCTAssertNil(AppSettings.parseHex("blue"))
        XCTAssertEqual(AppSettings.hexString(0x2F6BD8), "#2F6BD8")

        s.theme = "glass"; s.accentColor = "nope"; s.glassTint = 250
        s.normalize()
        XCTAssertEqual(s.theme, "glass")
        XCTAssertEqual(s.accentColor, "#1E7A57")
        XCTAssertEqual(s.glassTint, 100)
        s.theme = "neon"; s.normalize()
        XCTAssertEqual(s.theme, "system")

        // 저장했다가 다시 읽어도 그대로
        var custom = AppSettings()
        custom.theme = "glass"; custom.accentColor = "#7B4FD6"; custom.markerColor = "#A9DDFF"; custom.glassTint = 10
        let decoded = try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(custom))
        XCTAssertEqual(decoded, custom)
        // 예전 파일(새 항목 없음)은 기본값
        let old = try JSONDecoder().decode(AppSettings.self, from: Data("{\"theme\":\"dark\"}".utf8))
        XCTAssertEqual(old.accentColor, "#1E7A57")
        XCTAssertEqual(old.glassTint, 35)
    }

    // ── 저장 ──

    func testStorageRoundTripAndRecovery() throws {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent("ilhaera-test-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: dir) }
        let storage = try Storage(directory: dir)
        let empty = storage.load()
        XCTAssertTrue(empty.items.isEmpty)
        XCTAssertEqual(empty.settings.reminderMinutes, 30)
        XCTAssertEqual(empty.settings.opacity, 97)

        var data = AppData()
        data.items = [
            TodoItem.create(text: "회의", due: TimeMath.make(2026, 11, 20, 15, 0), now: now),
            TodoItem.create(text: "약", due: TimeMath.make(2026, 11, 20, 21, 0), now: now, repeat: .daily),
            TodoItem.create(text: "메모", due: nil, now: now),
        ]
        data.settings.reminderMinutes = 15
        try storage.save(data)
        try storage.save(data)
        let json = try String(contentsOf: storage.fileURL, encoding: .utf8)
        XCTAssertTrue(json.contains("회의"))
        XCTAssertTrue(json.contains("\"due\" : \"2026-11-20T15:00:00\""))
        XCTAssertTrue(json.contains("\"repeat\" : \"daily\""))
        XCTAssertTrue(FileManager.default.fileExists(atPath: storage.backupURL.path))

        let loaded = try Storage(directory: dir).load()
        XCTAssertEqual(loaded.items, data.items)
        XCTAssertEqual(loaded.settings.reminderMinutes, 15)

        // Windows 버전이 만든 파일도 읽을 수 있어야 한다
        let windowsJSON = """
        {"version":1,"items":[{"id":"a","text":"옛 항목","done":false,"due":"2026-11-21T10:00:00","notified":false,"created":"2026-11-20T08:00:00"}],
         "settings":{"x":100,"y":20,"width":320,"height":480,"opacity":95,"reminderMinutes":30,"theme":"dark"}}
        """
        try windowsJSON.write(to: storage.fileURL, atomically: true, encoding: .utf8)
        let fromWindows = storage.load()
        XCTAssertEqual(fromWindows.items.first?.text, "옛 항목")
        XCTAssertEqual(fromWindows.items.first.flatMap { $0.due }.map(fmt), "2026-11-21 10:00")
        XCTAssertEqual(fromWindows.settings.theme, "dark")

        // 손상된 파일 → 백업에서 복구 + 손상본 보관 (두 번 저장해야 백업도 data 가 된다)
        try storage.save(data)
        try storage.save(data)
        try "{ broken".write(to: storage.fileURL, atomically: true, encoding: .utf8)
        let recovered = storage.load()
        XCTAssertEqual(recovered.items.count, 3)
        let corrupt = try FileManager.default.contentsOfDirectory(atPath: dir.path).filter { $0.hasPrefix("todos.corrupt-") }
        XCTAssertEqual(corrupt.count, 1)
    }
}
