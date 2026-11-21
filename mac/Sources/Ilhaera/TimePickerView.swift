import SwiftUI
import IlhaeraCore

/// 시간 선택기의 값.
struct PickerValue: Equatable {
    var day: Date
    var hour: Int
    var minute: Int
    var rule: Repeat

    static func from(_ due: Date, rule: Repeat) -> PickerValue {
        PickerValue(day: TimeMath.startOfDay(due), hour: TimeMath.hour(due), minute: TimeMath.minute(due), rule: rule)
    }

    /// 선택한 값. 반복이면 첫 회차로 맞춘다.
    func resolved(now: Date) -> (due: Date, rule: Repeat) {
        var due = TimeMath.at(day, hour, minute)
        if rule != .none {
            due = Recurrence.firstOnOrAfter(rule, due, weekday: TimeMath.weekday(due))
            if due <= now { due = Recurrence.nextAfter(rule, due) }
        }
        return (due, rule)
    }
}

/// 날짜 칩(오늘/내일/모레/달력) + 휴대폰 알람식 시간 + 빠른 선택 + 반복 + 알림 시각 요약.
struct TimePickerView: View {
    @Binding var value: PickerValue
    let lead: Int
    let now: Date

    @State var calendarOpen = false

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            dateRow
            timeRow
            FlowLayout(spacing: 6) {
                ChipButton(title: "30분 뒤") { applyRelative(30) }
                ChipButton(title: "1시간 뒤") { applyRelative(60) }
                ChipButton(title: "오전 9시") { applyClock(9) }
                ChipButton(title: "오후 6시") { applyClock(18) }
            }
            FlowLayout(spacing: 6) {
                ChipButton(title: "반복 안 함", selected: value.rule == .none) { value.rule = .none }
                ChipButton(title: "매일", systemImage: "repeat", selected: value.rule == .daily) { value.rule = .daily }
                ChipButton(title: "평일", systemImage: "repeat", selected: value.rule == .weekdays) { value.rule = .weekdays }
                ChipButton(title: "매주 " + TimeText.dayName(value.day), systemImage: "repeat", selected: value.rule == .weekly) {
                    value.rule = .weekly
                }
            }
            summary
        }
    }

    // ── 날짜 ──

    private var today: Date { TimeMath.startOfDay(now) }
    private var dayOffset: Int { TimeMath.dayDiff(value.day, now) }

    private var dateRow: some View {
        FlowLayout(spacing: 6) {
            ForEach(0..<3, id: \.self) { offset in
                ChipButton(title: TimeText.dayWord(TimeMath.addDays(today, offset), now: now) ?? "", selected: dayOffset == offset) {
                    value.day = TimeMath.addDays(today, offset)
                }
            }
            ChipButton(title: dayOffset >= 0 && dayOffset <= 2 ? "날짜 고르기" : TimeText.shortDate(value.day, now: now),
                       systemImage: "calendar",
                       selected: dayOffset < 0 || dayOffset > 2) {
                calendarOpen = true
            }
            .popover(isPresented: $calendarOpen, arrowEdge: .bottom) {
                DatePicker("", selection: dayBinding, in: today..., displayedComponents: .date)
                    .datePickerStyle(.graphical)
                    .labelsHidden()
                    .environment(\.locale, Locale(identifier: "ko_KR"))
                    .padding(12)
                    .frame(width: 250)
            }
        }
    }

    private var dayBinding: Binding<Date> {
        Binding(
            get: { value.day },
            set: { newValue in
                value.day = TimeMath.startOfDay(newValue)
                calendarOpen = false
            }
        )
    }

    // ── 시간 ──

    private var timeRow: some View {
        HStack(alignment: .center, spacing: 18) {
            ampmToggle
            HStack(alignment: .center, spacing: 2) {
                Spinner(text: "\(value.hour % 12 == 0 ? 12 : value.hour % 12)", up: { stepHour(1) }, down: { stepHour(-1) })
                Text(":").font(Fonts.num(26, .bold)).foregroundColor(Palette.faint).padding(.bottom, 3)
                Spinner(text: String(format: "%02d", value.minute), up: { stepMinute(1) }, down: { stepMinute(-1) })
            }
            Spacer(minLength: 0)
        }
    }

    private var ampmToggle: some View {
        HStack(spacing: 0) {
            ampmButton("오전", selected: value.hour < 12) { if value.hour >= 12 { value.hour -= 12 } }
            ampmButton("오후", selected: value.hour >= 12) { if value.hour < 12 { value.hour += 12 } }
        }
        .padding(3)
        .background(Capsule().fill(Palette.surface))
    }

    private func ampmButton(_ title: String, selected: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title)
                .font(Fonts.ui(12, .semibold))
                .foregroundColor(selected ? Palette.text : Palette.faint)
                .frame(width: 42, height: 24)
                .background(Capsule().fill(selected ? Palette.back : Color.clear))
                .contentShape(Capsule())
        }
        .buttonStyle(.plain)
    }

    private func stepHour(_ delta: Int) {
        let base = value.hour < 12 ? 0 : 12
        let h12 = ((value.hour % 12 + delta) % 12 + 12) % 12
        value.hour = base + h12
    }

    private func stepMinute(_ delta: Int) {
        if delta > 0 {
            value.minute = (value.minute / 5 + 1) * 5 % 60
        } else {
            value.minute = value.minute % 5 != 0 ? value.minute - value.minute % 5 : (value.minute + 55) % 60
        }
    }

    private func applyRelative(_ minutes: Int) {
        var t = Date().addingTimeInterval(Double(minutes) * 60)
        let m = (TimeMath.minute(t) + 4) / 5 * 5
        t = TimeMath.at(t, TimeMath.hour(t), 0).addingTimeInterval(Double(m) * 60)
        value.day = TimeMath.startOfDay(t)
        value.hour = TimeMath.hour(t)
        value.minute = TimeMath.minute(t)
    }

    private func applyClock(_ hour: Int) {
        let current = Date()
        var t = TimeMath.at(value.day, hour, 0)
        if t <= current { t = TimeMath.at(current, hour, 0) }
        if t <= current { t = TimeMath.addDays(t, 1) }
        value.day = TimeMath.startOfDay(t)
        value.hour = hour
        value.minute = 0
    }

    // ── 요약: 실제로 언제 알리는지 ──

    private var summary: some View {
        let (text, color) = summaryText()
        return HStack(spacing: 5) {
            Image(systemName: "bell").font(.system(size: 11, weight: .semibold))
            Text(text).font(Fonts.ui(11.5)).lineLimit(1).truncationMode(.tail)
        }
        .foregroundColor(color)
    }

    private func summaryText() -> (String, Color) {
        let current = Date()
        let (due, rule) = value.resolved(now: current)
        let alert = TimeMath.addMinutes(due, -lead)
        let leadText = lead % 60 == 0 ? "\(lead / 60)시간" : "\(lead)분"
        func day(_ d: Date) -> String { TimeText.dayWord(d, now: current) ?? TimeText.shortDate(d, now: current) }
        if rule == .none {
            if due <= current { return ("이미 지난 시간이에요. 알림 없이 추가돼요.", Palette.danger) }
            if alert <= current { return ("\(day(due)) \(TimeText.korean12(due)) 일정, 추가하면 바로 알려요", Palette.warn) }
            return ("\(day(alert)) \(TimeText.korean12(alert))에 알려요 (\(leadText) 전)", Palette.sub)
        }
        let first = alert > current ? alert : due
        return ("\(day(first)) \(TimeText.korean12(first)) 첫 알림, 이후 \(Recurrence.label(rule, due)) 반복", Palette.sub)
    }
}

/// ▲ 숫자 ▼
struct Spinner: View {
    let text: String
    let up: () -> Void
    let down: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            ArrowButton(systemName: "chevron.up", action: up)
            Text(text)
                .font(Fonts.num(30, .bold))
                .foregroundColor(Palette.text)
                .frame(minWidth: 46)
            ArrowButton(systemName: "chevron.down", action: down)
        }
    }
}

struct ArrowButton: View {
    let systemName: String
    let action: () -> Void
    @State var hovering = false

    var body: some View {
        Button(action: action) {
            Image(systemName: systemName)
                .font(.system(size: 10, weight: .bold))
                .foregroundColor(hovering ? Palette.accent : Palette.faint)
                .frame(width: 46, height: 14)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
    }
}
