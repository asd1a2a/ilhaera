import Foundation

/// 달력 계산 도우미 (그레고리력, 현재 시간대).
public enum TimeMath {
    public static var calendar: Calendar {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone.current
        c.locale = Locale(identifier: "ko_KR")
        return c
    }

    public static func startOfDay(_ d: Date) -> Date { calendar.startOfDay(for: d) }

    public static func addDays(_ d: Date, _ n: Int) -> Date {
        calendar.date(byAdding: .day, value: n, to: d) ?? d.addingTimeInterval(Double(n) * 86400)
    }

    public static func addMinutes(_ d: Date, _ n: Int) -> Date { d.addingTimeInterval(Double(n) * 60) }

    /// 그날(day)의 h시 m분.
    public static func at(_ day: Date, _ h: Int, _ m: Int) -> Date {
        var comps = calendar.dateComponents([.year, .month, .day], from: day)
        comps.hour = h
        comps.minute = m
        comps.second = 0
        return calendar.date(from: comps) ?? day
    }

    public static func make(_ y: Int, _ mo: Int, _ d: Int, _ h: Int = 0, _ mi: Int = 0, _ s: Int = 0) -> Date {
        var comps = DateComponents()
        comps.year = y; comps.month = mo; comps.day = d
        comps.hour = h; comps.minute = mi; comps.second = s
        return calendar.date(from: comps)!
    }

    public static func truncateToMinute(_ d: Date) -> Date {
        let c = calendar.dateComponents([.year, .month, .day, .hour, .minute], from: d)
        return calendar.date(from: c) ?? d
    }

    /// 0 = 일요일 … 6 = 토요일
    public static func weekday(_ d: Date) -> Int { calendar.component(.weekday, from: d) - 1 }
    public static func hour(_ d: Date) -> Int { calendar.component(.hour, from: d) }
    public static func minute(_ d: Date) -> Int { calendar.component(.minute, from: d) }
    public static func day(_ d: Date) -> Int { calendar.component(.day, from: d) }
    public static func month(_ d: Date) -> Int { calendar.component(.month, from: d) }
    public static func year(_ d: Date) -> Int { calendar.component(.year, from: d) }

    /// d 와 now 사이의 날짜 차이 (자정 기준).
    public static func dayDiff(_ d: Date, _ now: Date) -> Int {
        calendar.dateComponents([.day], from: startOfDay(now), to: startOfDay(d)).day ?? 0
    }

    public static func daysInMonth(year: Int, month: Int) -> Int {
        let first = make(year, month, 1)
        return calendar.range(of: .day, in: .month, for: first)?.count ?? 31
    }
}
