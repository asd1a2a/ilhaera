import Foundation

public struct ParseResult: Equatable, Sendable {
    public let text: String
    public let due: Date
    public let repeatRule: Repeat
}

/// "내일 오후 3시 회의", "금요일 2시 반 치과", "매일 9시 영양제" 처럼 입력한 문장에서
/// 날짜·시간·반복을 찾아 일정 시각과 나머지 제목으로 나눈다.
/// Windows 버전 QuickParse.cs, 홈페이지 todoWidgetParse.ts 와 같은 규칙.
public enum QuickParse {
    static let markers = "오전|오후|아침|점심|저녁|밤|새벽"
    static let weekdays = ["일", "월", "화", "수", "목", "금", "토"]
    static let particles = ["까지", "부터", "에는", "마다", "에", "쯤"]

    static let hourForm = regex("^(\(markers))?(\\d{1,2})시(?:(반)|(\\d{1,2})분)?$")
    static let colonForm = regex("^(\(markers))?(\\d{1,2}):(\\d{2})$")
    static let markerOnly = regex("^(\(markers))$")
    static let minuteToken = regex("^(\\d{1,2})분$")
    static let weekdayToken = regex("^([일월화수목금토])(?:요일|욜)$")
    static let slashDate = regex("^(\\d{1,2})/(\\d{1,2})$")
    static let monthDay = regex("^(\\d{1,2})월(\\d{1,2})일$")
    static let monthOnly = regex("^(\\d{1,2})월$")
    static let dayOnly = regex("^(\\d{1,2})일$")

    static func regex(_ pattern: String) -> NSRegularExpression {
        // 패턴은 고정 문자열이라 실패하지 않는다.
        return try! NSRegularExpression(pattern: pattern)
    }

    /// 전체 일치하면 캡처 그룹들(없는 그룹은 nil)을 돌려준다.
    static func match(_ re: NSRegularExpression, _ s: String) -> [String?]? {
        let ns = s as NSString
        guard let m = re.firstMatch(in: s, range: NSRange(location: 0, length: ns.length)) else { return nil }
        var groups: [String?] = []
        for i in 1..<m.numberOfRanges {
            let r = m.range(at: i)
            groups.append(r.location == NSNotFound ? nil : ns.substring(with: r))
        }
        return groups
    }

    static func strip(_ token: String) -> String {
        for p in particles where token.count > p.count && token.hasSuffix(p) {
            return String(token.dropLast(p.count))
        }
        return token
    }

    struct TimeMatch { var hour: Int; var minute: Int; var marker: String?; var hasMinutes: Bool }

    static func matchTime(_ t: String) -> TimeMatch? {
        if t == "정오" { return TimeMatch(hour: 12, minute: 0, marker: "정오", hasMinutes: true) }
        if let g = match(hourForm, t) {
            let hour = Int(g[1]!)!
            if hour > 24 { return nil }
            if g[2] != nil { return TimeMatch(hour: hour, minute: 30, marker: g[0], hasMinutes: true) }
            if let m = g[3] { return TimeMatch(hour: hour, minute: Int(m)!, marker: g[0], hasMinutes: true) }
            return TimeMatch(hour: hour, minute: 0, marker: g[0], hasMinutes: false)
        }
        if let g = match(colonForm, t) {
            let hour = Int(g[1]!)!, minute = Int(g[2]!)!
            if hour > 23 || minute > 59 { return nil }
            return TimeMatch(hour: hour, minute: minute, marker: g[0], hasMinutes: true)
        }
        return nil
    }

    /// 오전/오후가 없을 때: 1~6시는 오후, 7~11시는 날짜가 있으면 오전·없으면 다가오는 쪽.
    static func resolveHour(_ h: Int, marker: String?, hasDate: Bool, now: Date, minute: Int) -> Int {
        switch marker {
        case "정오": return 12
        case "오후", "저녁", "밤": return h < 12 ? h + 12 : h
        case "점심": return h <= 5 ? h + 12 : h
        case "오전", "아침", "새벽": return h == 12 ? 0 : h
        default: break
        }
        if h == 24 { return 0 }
        if h >= 12 || h == 0 { return h }
        if h <= 6 { return h + 12 }
        if hasDate { return h }
        let am = TimeMath.at(now, h, minute)
        if am > now { return h }
        let pm = TimeMath.at(now, h + 12, minute)
        return pm > now ? h + 12 : h
    }

    static func makeDate(month: Int, day: Int, today: Date) -> Date? {
        guard (1...12).contains(month), (1...31).contains(day) else { return nil }
        let year = TimeMath.year(today)
        for y in year...(year + 1) {
            if day > TimeMath.daysInMonth(year: y, month: month) { continue }
            let d = TimeMath.make(y, month, day)
            if d >= today { return d }
        }
        return nil
    }

    struct DateMatch { var date: Date; var consumed: Int; var isToday: Bool }

    static func matchDate(_ tokens: [String], _ i: Int, now: Date) -> DateMatch? {
        let today = TimeMath.startOfDay(now)
        let t = strip(tokens[i])
        let fixed: [String: Int] = ["오늘": 0, "내일": 1, "모레": 2, "글피": 3]
        if let n = fixed[t] { return DateMatch(date: TimeMath.addDays(today, n), consumed: 1, isToday: n == 0) }

        if ["이번주", "다음주", "담주"].contains(t), i + 1 < tokens.count,
           let wd = match(weekdayToken, strip(tokens[i + 1])), let name = wd[0],
           let target = weekdays.firstIndex(of: name) {
            let monday = TimeMath.addDays(today, -((TimeMath.weekday(today) + 6) % 7))
            var d = TimeMath.addDays(monday, (target + 6) % 7)
            if t != "이번주" { d = TimeMath.addDays(d, 7) }
            return DateMatch(date: d, consumed: 2, isToday: d == today)
        }

        if let w = match(weekdayToken, t), let name = w[0], let target = weekdays.firstIndex(of: name) {
            let diff = (target - TimeMath.weekday(today) + 7) % 7
            return DateMatch(date: TimeMath.addDays(today, diff), consumed: 1, isToday: diff == 0)
        }

        if let g = match(slashDate, t) ?? match(monthDay, t) {
            guard let d = makeDate(month: Int(g[0]!)!, day: Int(g[1]!)!, today: today) else { return nil }
            return DateMatch(date: d, consumed: 1, isToday: false)
        }

        if let g = match(monthOnly, t), i + 1 < tokens.count, let dd = match(dayOnly, strip(tokens[i + 1])) {
            guard let d = makeDate(month: Int(g[0]!)!, day: Int(dd[0]!)!, today: today) else { return nil }
            return DateMatch(date: d, consumed: 2, isToday: false)
        }
        return nil
    }

    /// 문장에서 날짜·시간·반복을 찾는다. 찾지 못하거나 제목이 남지 않으면 nil.
    public static func parse(_ input: String, now: Date) -> ParseResult? {
        let tokens = input.split(whereSeparator: { $0 == " " || $0 == "\t" }).map(String.init)
        if tokens.isEmpty { return nil }
        var used = [Bool](repeating: false, count: tokens.count)
        var date: Date?
        var dateIsToday = false
        var hour = -1
        var minute = 0
        var marker: String?
        var rule: Repeat = .none

        func takeMinutes(_ i: Int) -> Int {
            guard i < tokens.count, !used[i] else { return 0 }
            let t = strip(tokens[i])
            if t == "반" { minute = 30; used[i] = true; return 1 }
            if let g = match(minuteToken, t) { minute = Int(g[0]!)!; used[i] = true; return 1 }
            return 0
        }

        var i = 0
        while i < tokens.count {
            defer { i += 1 }
            if used[i] { continue }
            let t = strip(tokens[i])

            if rule == .none {
                if t == "매일" || t == "매일매일" || t == "날마다" || tokens[i] == "날마다" {
                    rule = .daily; used[i] = true; continue
                }
                if t == "평일" || t == "주중" {
                    rule = .weekdays; used[i] = true; continue
                }
                if t == "매주" {
                    rule = .weekly; used[i] = true
                    if date == nil, i + 1 < tokens.count, match(weekdayToken, strip(tokens[i + 1])) != nil,
                       let d = matchDate(tokens, i + 1, now: now) {
                        date = d.date; dateIsToday = d.isToday; used[i + 1] = true; i += 1
                    }
                    continue
                }
                if tokens[i].hasSuffix("마다"), date == nil, match(weekdayToken, t) != nil {
                    if let d = matchDate(tokens, i, now: now) { date = d.date; dateIsToday = d.isToday }
                    rule = .weekly; used[i] = true; continue
                }
            }

            if date == nil, let d = matchDate(tokens, i, now: now) {
                date = d.date
                dateIsToday = d.isToday
                for k in 0..<d.consumed { used[i + k] = true }
                i += d.consumed - 1
                continue
            }

            if hour < 0 {
                if match(markerOnly, t) != nil, i + 1 < tokens.count, !used[i + 1],
                   let tm = matchTime(strip(tokens[i + 1])), tm.marker == nil {
                    marker = t
                    hour = tm.hour
                    minute = tm.minute
                    used[i] = true
                    used[i + 1] = true
                    i += 1
                    if !tm.hasMinutes { i += takeMinutes(i + 1) }
                    continue
                }
                if let tm = matchTime(t) {
                    marker = tm.marker
                    hour = tm.hour
                    minute = tm.minute
                    used[i] = true
                    if !tm.hasMinutes { i += takeMinutes(i + 1) }
                    continue
                }
            }
        }

        if date == nil && hour < 0 && rule == .none { return nil }
        let rest = tokens.enumerated().filter { !used[$0.offset] }.map { $0.element }.joined(separator: " ")
        if rest.isEmpty { return nil }

        if rule != .none {
            if hour < 0 { hour = 9; minute = 0; marker = "오전" }
            let h = resolveHour(hour, marker: marker, hasDate: true, now: now, minute: minute)
            guard (0...23).contains(h), minute <= 59 else { return nil }
            var start = TimeMath.at(date ?? TimeMath.startOfDay(now), h, minute)
            start = Recurrence.firstOnOrAfter(rule, start, weekday: TimeMath.weekday(start))
            if start <= now { start = Recurrence.nextAfter(rule, start) }
            return ParseResult(text: rest, due: start, repeatRule: rule)
        }

        let due: Date
        if hour >= 0 {
            let h = resolveHour(hour, marker: marker, hasDate: date != nil, now: now, minute: minute)
            guard (0...23).contains(h), minute <= 59 else { return nil }
            if let d = date {
                due = TimeMath.at(d, h, minute)
            } else {
                let candidate = TimeMath.at(now, h, minute)
                due = candidate <= now ? TimeMath.addDays(candidate, 1) : candidate
            }
        } else {
            guard let d = date, !dateIsToday else { return nil }
            due = TimeMath.at(d, 9, 0)
        }
        return ParseResult(text: rest, due: due, repeatRule: .none)
    }
}
