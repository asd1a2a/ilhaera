import Foundation

public enum AlertKind: Sendable { case upcoming, missed }

public struct ReminderAlert: Identifiable, Equatable, Sendable {
    public var id: String { item.id }
    public let item: TodoItem
    public let kind: AlertKind
    public let minutesLeft: Int
}

public enum SectionKind: String, Sendable { case overdue, today, tomorrow, later, noDate, done }

public struct TodoSection: Identifiable, Equatable, Sendable {
    public var id: String { kind.rawValue }
    public let kind: SectionKind
    public let title: String
    public let date: Date?
    public let items: [TodoItem]
}

public enum DueState: Sendable { case none, normal, soon, overdue }

/// 알림·정렬·묶음. Windows 버전 Reminders.cs 와 같은 규칙.
public enum Reminders {
    /// 컴퓨터가 꺼져 있어서 놓친 일정은 이 시간 이내인 것만 알려준다.
    public static let missedWindow: TimeInterval = 12 * 3600

    /// 알림을 보내야 할 항목을 찾고 notified 를 표시한다 (각 항목은 한 번만 알림).
    public static func collect(_ items: inout [TodoItem], now: Date, leadMinutes: Int) -> [ReminderAlert] {
        var alerts: [ReminderAlert] = []
        for i in items.indices {
            if items[i].done { continue }
            let due = items[i].due

            // "다시 알림"을 눌러 둔 항목
            if let snooze = items[i].snooze {
                if now < snooze { continue }
                items[i].snooze = nil
                items[i].notified = true
                if let d = due { alerts.append(make(items[i], d, now)) }
                continue
            }

            guard !items[i].notified, let d = due else { continue }
            if now < TimeMath.addMinutes(d, -leadMinutes) { continue }

            items[i].notified = true
            if now < d || now.timeIntervalSince(d) <= missedWindow {
                alerts.append(make(items[i], d, now))
            }
        }
        return alerts.sorted { ($0.item.due ?? .distantFuture) < ($1.item.due ?? .distantFuture) }
    }

    static func make(_ item: TodoItem, _ due: Date, _ now: Date) -> ReminderAlert {
        if now < due {
            let minutes = Int((due.timeIntervalSince(now) / 60).rounded(.up))
            return ReminderAlert(item: item, kind: .upcoming, minutesLeft: minutes)
        }
        return ReminderAlert(item: item, kind: .missed, minutesLeft: 0)
    }

    /// 지금 이후 가장 먼저 시작하는 미완료 일정.
    public static func nextUp(_ items: [TodoItem], now: Date) -> TodoItem? {
        items.filter { !$0.done && ($0.due.map { $0 >= now } ?? false) }
            .sorted { ($0.due!, $0.created) < ($1.due!, $1.created) }
            .first
    }

    /// 지난 일정 / 오늘 / 내일 / 이후 / 기한 없음 / 완료 (빈 묶음 제외).
    public static func group(_ items: [TodoItem], now: Date) -> [TodoSection] {
        let today = TimeMath.startOfDay(now)
        let tomorrow = TimeMath.addDays(today, 1)
        let open = items.filter { !$0.done }
        let dated = open.filter { $0.due != nil }.sorted { ($0.due!, $0.created) < ($1.due!, $1.created) }
        func day(_ i: TodoItem) -> Date { TimeMath.startOfDay(i.due!) }

        let sections = [
            TodoSection(kind: .overdue, title: "지난 일정", date: nil, items: dated.filter { $0.due! < now }),
            TodoSection(kind: .today, title: "오늘", date: today, items: dated.filter { $0.due! >= now && day($0) == today }),
            TodoSection(kind: .tomorrow, title: "내일", date: tomorrow, items: dated.filter { $0.due! >= now && day($0) == tomorrow }),
            TodoSection(kind: .later, title: "이후", date: nil, items: dated.filter { $0.due! >= now && day($0) > tomorrow }),
            TodoSection(kind: .noDate, title: "기한 없음", date: nil, items: open.filter { $0.due == nil }.sorted { $0.created < $1.created }),
            TodoSection(kind: .done, title: "완료", date: nil,
                        items: items.filter { $0.done }.sorted { ($0.completed ?? .distantPast) > ($1.completed ?? .distantPast) }),
        ]
        return sections.filter { !$0.items.isEmpty }
    }
}

public enum TimeText {
    static let dayNames = ["일", "월", "화", "수", "목", "금", "토"]

    public static func dayName(_ d: Date) -> String { dayNames[TimeMath.weekday(d)] }

    public static func hourMinute(_ d: Date) -> String {
        String(format: "%02d:%02d", TimeMath.hour(d), TimeMath.minute(d))
    }

    /// 예: "오후 3:00", "오전 12:05"
    public static func korean12(_ d: Date) -> String {
        let h = TimeMath.hour(d)
        let h12 = h % 12 == 0 ? 12 : h % 12
        return (h < 12 ? "오전 " : "오후 ") + "\(h12):" + String(format: "%02d", TimeMath.minute(d))
    }

    public static func shortDate(_ d: Date, now: Date) -> String {
        if TimeMath.year(d) == TimeMath.year(now) {
            return "\(TimeMath.month(d))/\(TimeMath.day(d)) (\(dayName(d)))"
        }
        return "\(TimeMath.year(d))/\(TimeMath.month(d))/\(TimeMath.day(d)) (\(dayName(d)))"
    }

    public static func dayWord(_ d: Date, now: Date) -> String? {
        switch TimeMath.dayDiff(d, now) {
        case -1: return "어제"
        case 0: return "오늘"
        case 1: return "내일"
        case 2: return "모레"
        default: return nil
        }
    }

    /// 예: "오늘 14:30", "11/25 (수) 09:00"
    public static func dueLabel(_ due: Date, now: Date) -> String {
        (dayWord(due, now: now) ?? shortDate(due, now: now)) + " " + hourMinute(due)
    }

    /// 예: "25분 후", "3시간 10분 후", "2일 후", "시간 지남"
    public static func relative(_ due: Date, now: Date) -> String {
        let seconds = due.timeIntervalSince(now)
        if seconds <= 0 { return "시간 지남" }
        let minutes = Int((seconds / 60).rounded(.up))
        if minutes < 60 { return "\(max(1, minutes))분 후" }
        if minutes < 24 * 60 {
            let h = minutes / 60, m = minutes % 60
            return m == 0 ? "\(h)시간 후" : "\(h)시간 \(m)분 후"
        }
        return "\(TimeMath.dayDiff(due, now))일 후"
    }

    public static func state(_ item: TodoItem, now: Date, leadMinutes: Int) -> DueState {
        guard let due = item.due else { return .none }
        if item.done { return .normal }
        if due <= now { return .overdue }
        if TimeMath.addMinutes(due, -leadMinutes) <= now { return .soon }
        return .normal
    }

    /// 기본 일정 시각: 지금부터 다음 정각 (예: 14:12 → 15:00).
    public static func defaultDue(_ now: Date) -> Date {
        TimeMath.at(now, TimeMath.hour(now), 0).addingTimeInterval(3600)
    }
}
