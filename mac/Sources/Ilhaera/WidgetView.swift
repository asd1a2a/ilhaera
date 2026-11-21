import SwiftUI
import IlhaeraCore

/// 위젯 본체: 일력 헤더 → 입력칸 → (해석 미리보기 / 시간 선택기) → 날짜별 목록 → 되돌리기.
struct WidgetView: View {
    @EnvironmentObject var store: Store
    let onMenu: () -> Void
    let onHide: () -> Void

    @State var text = ""
    @State var pickerOpen = false
    @State var pickerValue = PickerValue.from(TimeText.defaultDue(Date()), rule: .none)
    @State var parseDismissed = false
    @State var editing: TodoItem?
    @FocusState var inputFocused: Bool

    private var parsed: ParseResult? {
        if pickerOpen || parseDismissed { return nil }
        return QuickParse.parse(text, now: store.now)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HeaderView(onMenu: onMenu, onHide: onHide)
            inputBar
            if let p = parsed {
                ParseChip(result: p, lead: store.settings.reminderMinutes, now: store.now) { parseDismissed = true }
            }
            if pickerOpen {
                TimePickerView(value: $pickerValue, lead: store.settings.reminderMinutes, now: store.now)
                    .padding(.bottom, 2)
            }
            TodoListView(onEdit: { editing = $0 })
            if let undo = store.undo {
                UndoBar(info: undo) { store.performUndo() }
            }
        }
        .padding(EdgeInsets(top: 6, leading: 14, bottom: 12, trailing: 10))
        .frame(minWidth: 300, maxWidth: .infinity, minHeight: 340, maxHeight: .infinity, alignment: .top)
        .background(Palette.back.ignoresSafeArea())
        .ignoresSafeArea(.container, edges: .top)
        .sheet(item: $editing) { item in
            EditView(item: item, lead: store.settings.reminderMinutes,
                     onSave: { newText, due, rule in
                         store.update(item.id, text: newText, due: due, repeat: rule)
                         editing = nil
                     },
                     onCancel: { editing = nil })
        }
        .onChange(of: text) { newValue in
            if newValue.trimmed.isEmpty { parseDismissed = false }
        }
        .onAppear { inputFocused = true }
    }

    private var inputBar: some View {
        HStack(spacing: 6) {
            TextField("할 일 추가 (예: 내일 3시 회의)", text: $text)
                .textFieldStyle(.plain)
                .font(Fonts.ui(14))
                .focused($inputFocused)
                .onSubmit(add)
            IconButton(systemName: "clock", help: "날짜·시간 직접 고르기", active: pickerOpen) { togglePicker() }
            Button(action: add) {
                Image(systemName: "plus")
                    .font(.system(size: 13, weight: .bold))
                    .frame(width: 26, height: 26)
                    .background(RoundedRectangle(cornerRadius: 7).fill(Palette.accent))
                    .foregroundColor(Palette.onAccent)
            }
            .buttonStyle(.plain)
            .help("추가 (Return)")
        }
        .padding(.leading, 12)
        .padding(.trailing, 6)
        .padding(.vertical, 6)
        .background(RoundedRectangle(cornerRadius: 9).fill(Palette.surface))
    }

    private func togglePicker() {
        if !pickerOpen {
            if let p = parsed {
                pickerValue = PickerValue.from(p.due, rule: p.repeatRule)
            } else {
                pickerValue = PickerValue.from(TimeText.defaultDue(Date()), rule: .none)
            }
        }
        pickerOpen.toggle()
    }

    private func add() {
        let raw = text.trimmed
        guard !raw.isEmpty else {
            inputFocused = true
            return
        }
        var title = raw
        var due: Date?
        var rule: Repeat = .none
        if pickerOpen {
            let v = pickerValue.resolved(now: Date())
            due = v.due
            rule = v.rule
        } else if !parseDismissed, let p = QuickParse.parse(raw, now: Date()) {
            title = p.text
            due = p.due
            rule = p.repeatRule
        }
        let id = store.add(text: title, due: due, repeat: rule)
        text = ""
        parseDismissed = false
        pickerOpen = false
        store.scrollTarget = id
        inputFocused = true
    }
}

// ── 헤더: 일력 + 진행률 + 다음 일정 ──

struct HeaderView: View {
    @EnvironmentObject var store: Store
    let onMenu: () -> Void
    let onHide: () -> Void

    var body: some View {
        let now = store.now
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .center, spacing: 9) {
                Text("\(TimeMath.day(now))")
                    .font(Fonts.num(40, .bold))
                    .foregroundColor(Palette.weekday(now))
                VStack(alignment: .leading, spacing: 0) {
                    Text("\(TimeMath.month(now))월").font(Fonts.ui(11.5)).foregroundColor(Palette.sub)
                    Text(TimeText.dayName(now) + "요일").font(Fonts.ui(13.5, .bold)).foregroundColor(Palette.weekday(now))
                }
                Spacer()
                IconButton(systemName: "ellipsis", help: "메뉴", action: onMenu)
                IconButton(systemName: "minus", help: "숨기기 (메뉴 막대 아이콘으로 다시 열기)", action: onHide)
            }
            .padding(.top, 22)
            ProgressRow(items: store.items, now: now)
            if let next = Reminders.nextUp(store.items, now: now), let due = next.due {
                NextStrip(item: next, due: due, now: now, lead: store.settings.reminderMinutes)
                    .onTapGesture { store.scrollTarget = next.id }
            }
        }
    }
}

struct ProgressRow: View {
    let items: [TodoItem]
    let now: Date

    var body: some View {
        let today = TimeMath.startOfDay(now)
        // 반복 일정은 오늘 회차를 끝내면 다음 회차로 넘어가므로 "오늘 완료"로 함께 센다.
        let doneToday: (TodoItem) -> Bool = { item in
            if item.done { return true }
            guard item.isRecurring, let last = item.lastDone, let due = item.due else { return false }
            return TimeMath.startOfDay(last) == today && TimeMath.startOfDay(due) != today
        }
        let todays = items.filter { item in
            guard let due = item.due else { return false }
            return TimeMath.startOfDay(due) == today || (doneToday(item) && !item.done)
        }
        let doneCount = todays.filter(doneToday).count
        let open = items.filter { !$0.done }.count
        HStack(spacing: 10) {
            if !todays.isEmpty {
                Text("오늘 \(doneCount)/\(todays.count)").font(Fonts.ui(11.5)).foregroundColor(Palette.sub)
                GeometryReader { geo in
                    ZStack(alignment: .leading) {
                        Capsule().fill(Palette.surface)
                        Capsule().fill(Palette.accent)
                            .frame(width: max(doneCount > 0 ? 4 : 0, geo.size.width * CGFloat(doneCount) / CGFloat(todays.count)))
                    }
                }
                .frame(height: 4)
            } else if !items.isEmpty {
                Text(open > 0 ? "남은 일 \(open)개" : "모두 끝냈어요").font(Fonts.ui(11.5)).foregroundColor(Palette.sub)
            }
        }
        .frame(height: 16)
    }
}

struct NextStrip: View {
    let item: TodoItem
    let due: Date
    let now: Date
    let lead: Int

    var body: some View {
        let soon = TimeText.state(item, now: now, leadMinutes: lead) == .soon
        let when = TimeMath.dayDiff(due, now) == 0
            ? TimeText.hourMinute(due)
            : (TimeText.dayWord(due, now: now) ?? "\(TimeMath.month(due))/\(TimeMath.day(due))") + " " + TimeText.hourMinute(due)
        HStack(spacing: 8) {
            Text("다음").font(Fonts.ui(11)).opacity(0.75)
            Text(when).font(Fonts.num(15, .bold))
            Text(item.text.oneLine).font(Fonts.ui(12.5)).lineLimit(1).truncationMode(.tail)
            Spacer(minLength: 4)
            Text(TimeText.relative(due, now: now)).font(Fonts.ui(11)).opacity(0.85)
        }
        .padding(.horizontal, 12)
        .frame(height: 38)
        .foregroundColor(soon ? Palette.markerText : Palette.text)
        .background(RoundedRectangle(cornerRadius: 9).fill(soon ? Palette.marker : Palette.surface))
        .contentShape(Rectangle())
    }
}

struct ParseChip: View {
    let result: ParseResult
    let lead: Int
    let now: Date
    let onClear: () -> Void

    var body: some View {
        let day = TimeText.dayWord(result.due, now: now) ?? TimeText.shortDate(result.due, now: now)
        let when = result.repeatRule == .none
            ? day + " " + TimeText.hourMinute(result.due)
            : Recurrence.label(result.repeatRule, result.due) + " " + TimeText.hourMinute(result.due)
        let leadText = lead % 60 == 0 ? "\(lead / 60)시간" : "\(lead)분"
        let rest = result.repeatRule == .none ? "일정으로 추가, \(leadText) 전에 알려요" : "반복 일정으로 추가, 첫 회 \(day)"
        HStack(spacing: 5) {
            Image(systemName: "clock").font(.system(size: 11, weight: .semibold)).foregroundColor(Palette.accent)
            Text(when).font(Fonts.num(13, .bold)).foregroundColor(Palette.accent)
            Text(rest).font(Fonts.ui(11.5)).foregroundColor(Palette.sub).lineLimit(1).truncationMode(.tail)
            Spacer(minLength: 0)
            IconButton(systemName: "xmark", help: "시간 없이 추가", size: 20, action: onClear)
        }
        .padding(.top, -4)
    }
}

struct UndoBar: View {
    let info: UndoInfo
    let onUndo: () -> Void

    var body: some View {
        HStack {
            Text(info.message).font(Fonts.ui(12)).foregroundColor(Palette.sub).lineLimit(1).truncationMode(.tail)
            Spacer(minLength: 8)
            Button("되돌리기", action: onUndo)
                .buttonStyle(.plain)
                .font(Fonts.ui(12, .bold))
                .foregroundColor(Palette.accent)
        }
        .padding(.horizontal, 12)
        .frame(height: 34)
        .background(RoundedRectangle(cornerRadius: 8).fill(Palette.surface))
    }
}
