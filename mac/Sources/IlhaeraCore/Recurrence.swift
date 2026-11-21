import Foundation

/// 반복 일정 계산. Windows 버전 Recurrence.cs 와 같은 규칙.
public enum Recurrence {
    static func matches(_ rule: Repeat, _ d: Date, weekday: Int) -> Bool {
        switch rule {
        case .weekdays:
            let w = TimeMath.weekday(d)
            return w != 0 && w != 6
        case .weekly:
            return TimeMath.weekday(d) == weekday
        default:
            return true
        }
    }

    /// candidate 와 같거나 이후인 첫 회차 (시각은 그대로).
    public static func firstOnOrAfter(_ rule: Repeat, _ candidate: Date, weekday: Int) -> Date {
        var d = candidate
        var i = 0
        while i < 8 && !matches(rule, d, weekday: weekday) {
            d = TimeMath.addDays(d, 1)
            i += 1
        }
        return d
    }

    /// due 다음 회차.
    public static func nextAfter(_ rule: Repeat, _ due: Date) -> Date {
        switch rule {
        case .none: return due
        case .weekly: return TimeMath.addDays(due, 7)
        default: return firstOnOrAfter(rule, TimeMath.addDays(due, 1), weekday: TimeMath.weekday(due))
        }
    }

    /// day 날짜 또는 그 이후의 첫 회차. 시각은 timeOf 의 시·분.
    public static func firstOnOrAfterDate(_ rule: Repeat, day: Date, timeOf: Date, weekday: Int) -> Date {
        let candidate = TimeMath.at(day, TimeMath.hour(timeOf), TimeMath.minute(timeOf))
        return firstOnOrAfter(rule, candidate, weekday: weekday)
    }

    public static func label(_ rule: Repeat, _ due: Date) -> String {
        switch rule {
        case .daily: return "매일"
        case .weekdays: return "평일"
        case .weekly: return "매주 " + TimeText.dayName(due)
        case .none: return ""
        }
    }

    /// 지난 날의 회차를 오늘(또는 그 이후) 회차로 넘긴다. 바뀐 항목이 있으면 true.
    @discardableResult
    public static func rollForward(_ items: inout [TodoItem], now: Date) -> Bool {
        var changed = false
        for i in items.indices {
            guard !items[i].done, items[i].isRecurring, let due = items[i].due else { continue }
            if TimeMath.startOfDay(due) >= TimeMath.startOfDay(now) { continue }
            items[i].due = firstOnOrAfterDate(items[i].repeatRule, day: now, timeOf: due, weekday: TimeMath.weekday(due))
            items[i].snooze = nil
            items[i].notified = false // 오늘 회차가 이미 지났으면 "놓친 일정"으로 한 번 알린다
            changed = true
        }
        return changed
    }
}
