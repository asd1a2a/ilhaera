import Foundation

/// 반복 규칙. 매주 반복의 요일은 일정 시각(due)의 요일을 따른다.
public enum Repeat: String, CaseIterable, Sendable {
    case none, daily, weekdays, weekly
}

/// 할 일 하나. JSON 키와 날짜 형식은 Windows 버전과 같다 (todos.json 호환).
public struct TodoItem: Identifiable, Equatable, Sendable {
    public var id: String
    public var text: String
    public var done: Bool
    public var due: Date?
    public var notified: Bool
    public var created: Date
    public var completed: Date?
    public var snooze: Date?
    public var repeatRule: Repeat
    public var lastDone: Date?

    public init(id: String = UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased(),
                text: String, done: Bool = false, due: Date? = nil, notified: Bool = false,
                created: Date, completed: Date? = nil, snooze: Date? = nil,
                repeatRule: Repeat = .none, lastDone: Date? = nil) {
        self.id = id
        self.text = text
        self.done = done
        self.due = due
        self.notified = notified
        self.created = created
        self.completed = completed
        self.snooze = snooze
        self.repeatRule = repeatRule
        self.lastDone = lastDone
    }

    public static func create(text: String, due: Date?, now: Date, repeat rule: Repeat = .none) -> TodoItem {
        var item = TodoItem(text: text, created: now)
        item.setDue(due, now: now)
        if due != nil && rule != .none { item.repeatRule = rule }
        return item
    }

    public var isRecurring: Bool { repeatRule != .none && due != nil }

    /// 일정 시각을 바꾸고 알림 상태를 다시 계산한다. 이미 지난 시각이면 알림을 띄우지 않는다.
    public mutating func setDue(_ value: Date?, now: Date) {
        due = value.map(TimeMath.truncateToMinute)
        snooze = nil
        if let d = due { notified = d <= now } else { notified = false }
    }

    public mutating func setDone(_ value: Bool, now: Date) {
        done = value
        completed = value ? now : nil
        if value { snooze = nil }
    }

    /// 반복 일정의 이번 회차를 완료하고 다음 회차로 넘긴다.
    public mutating func completeOccurrence(now: Date) {
        guard isRecurring, let current = due else { return }
        lastDone = now
        var next = Recurrence.nextAfter(repeatRule, current)
        if TimeMath.startOfDay(next) < TimeMath.startOfDay(now) {
            next = Recurrence.firstOnOrAfterDate(repeatRule, day: now, timeOf: next, weekday: TimeMath.weekday(next))
        }
        setDue(next, now: now)
    }
}

extension TodoItem: Codable {
    enum CodingKeys: String, CodingKey {
        case id, text, done, due, notified, created, completed, snooze
        case repeatRule = "repeat"
        case lastDone
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = (try? c.decodeIfPresent(String.self, forKey: .id)) ?? UUID().uuidString.lowercased()
        text = (try? c.decodeIfPresent(String.self, forKey: .text)) ?? ""
        done = (try? c.decodeIfPresent(Bool.self, forKey: .done)) ?? false
        notified = (try? c.decodeIfPresent(Bool.self, forKey: .notified)) ?? false
        due = JSONDate.decode(c, .due)
        created = JSONDate.decode(c, .created) ?? Date()
        completed = JSONDate.decode(c, .completed)
        snooze = JSONDate.decode(c, .snooze)
        lastDone = JSONDate.decode(c, .lastDone)
        let raw = try? c.decodeIfPresent(String.self, forKey: .repeatRule)
        repeatRule = raw.flatMap(Repeat.init(rawValue:)) ?? .none
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encode(text, forKey: .text)
        try c.encode(done, forKey: .done)
        try c.encodeIfPresent(due.map(JSONDate.format), forKey: .due)
        try c.encode(notified, forKey: .notified)
        try c.encode(JSONDate.format(created), forKey: .created)
        try c.encodeIfPresent(completed.map(JSONDate.format), forKey: .completed)
        try c.encodeIfPresent(snooze.map(JSONDate.format), forKey: .snooze)
        if repeatRule != .none { try c.encode(repeatRule.rawValue, forKey: .repeatRule) }
        try c.encodeIfPresent(lastDone.map(JSONDate.format), forKey: .lastDone)
    }
}

/// 날짜는 "yyyy-MM-dd'T'HH:mm:ss" 로컬 시각 문자열로 저장한다.
enum JSONDate {
    static let formatter: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.calendar = Calendar(identifier: .gregorian)
        f.timeZone = TimeZone.current
        f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss"
        return f
    }()

    static func format(_ date: Date) -> String { formatter.string(from: date) }

    static func decode<K: CodingKey>(_ c: KeyedDecodingContainer<K>, _ key: K) -> Date? {
        guard let raw = try? c.decodeIfPresent(String.self, forKey: key), !raw.isEmpty else { return nil }
        return formatter.date(from: raw)
    }
}

public struct AppSettings: Codable, Equatable, Sendable {
    public var alwaysOnTop: Bool = false
    public var opacity: Int = 97
    public var reminderMinutes: Int = 30
    public var autoStartInitialized: Bool = false
    public var theme: String = "system"
    public var showDone: Bool = false

    public init() {}

    enum CodingKeys: String, CodingKey {
        case alwaysOnTop, opacity, reminderMinutes, autoStartInitialized, theme, showDone
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        alwaysOnTop = (try? c.decodeIfPresent(Bool.self, forKey: .alwaysOnTop)) ?? false
        opacity = (try? c.decodeIfPresent(Int.self, forKey: .opacity)) ?? 97
        reminderMinutes = (try? c.decodeIfPresent(Int.self, forKey: .reminderMinutes)) ?? 30
        autoStartInitialized = (try? c.decodeIfPresent(Bool.self, forKey: .autoStartInitialized)) ?? false
        theme = (try? c.decodeIfPresent(String.self, forKey: .theme)) ?? "system"
        showDone = (try? c.decodeIfPresent(Bool.self, forKey: .showDone)) ?? false
        normalize()
    }

    public mutating func normalize() {
        if opacity < 30 || opacity > 100 { opacity = 97 }
        if reminderMinutes < 1 || reminderMinutes > 24 * 60 { reminderMinutes = 30 }
        if theme != "light" && theme != "dark" { theme = "system" }
    }
}

public struct AppData: Codable, Equatable, Sendable {
    public var version: Int = 1
    public var items: [TodoItem] = []
    public var settings = AppSettings()

    public init() {}

    enum CodingKeys: String, CodingKey { case version, items, settings }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        version = (try? c.decodeIfPresent(Int.self, forKey: .version)) ?? 1
        let decoded = (try? c.decodeIfPresent([TodoItem].self, forKey: .items)) ?? []
        items = decoded.filter { !$0.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
        settings = (try? c.decodeIfPresent(AppSettings.self, forKey: .settings)) ?? AppSettings()
    }
}
