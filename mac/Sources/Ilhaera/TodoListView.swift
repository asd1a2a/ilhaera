import SwiftUI
import IlhaeraCore

/// 날짜별로 묶은 목록.
struct TodoListView: View {
    @Environment(\.look) var look
    @EnvironmentObject var store: Store
    let onEdit: (TodoItem) -> Void

    var body: some View {
        let sections = Reminders.group(store.items, now: store.now)
        let anyOpen = sections.contains { $0.kind != .done }
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 0) {
                    if sections.isEmpty {
                        EmptyState()
                    } else if !anyOpen {
                        Text("남은 할 일이 없어요.")
                            .font(Fonts.ui(13))
                            .foregroundColor(look.sub)
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 22)
                    }
                    ForEach(sections) { section in
                        SectionBlock(section: section, onEdit: onEdit)
                    }
                }
                .padding(.bottom, 8)
            }
            // 유리 테마에서는 스크롤 막대 자리가 불투명한 띠로 보여서 숨긴다
            .scrollIndicators(look.glass ? .hidden : .automatic)
            .onChange(of: store.scrollTarget) { target in
                guard let target else { return }
                withAnimation(.easeOut(duration: 0.2)) { proxy.scrollTo(target, anchor: .center) }
                DispatchQueue.main.async { store.scrollTarget = nil }
            }
        }
    }
}

struct SectionBlock: View {
    @EnvironmentObject var store: Store
    let section: TodoSection
    let onEdit: (TodoItem) -> Void

    var body: some View {
        if section.kind == .done {
            DoneToggle(count: section.items.count, open: store.settings.showDone,
                       action: { store.toggleShowDone() },
                       onClear: { store.clearDone() })
            if store.settings.showDone {
                ForEach(section.items) { item in
                    RowView(item: item, section: section.kind, onEdit: onEdit).id(item.id)
                }
            }
        } else {
            SectionHeader(section: section)
            ForEach(section.items) { item in
                RowView(item: item, section: section.kind, onEdit: onEdit).id(item.id)
            }
        }
    }
}

struct SectionHeader: View {
    @Environment(\.look) var look
    let section: TodoSection

    var body: some View {
        HStack(spacing: 6) {
            Text(section.title)
                .font(Fonts.ui(11.5, .bold))
                .foregroundColor(section.kind == .overdue ? look.danger : look.sub)
            if let date = section.date {
                Text("\(TimeMath.month(date))/\(TimeMath.day(date))").font(Fonts.ui(11.5)).foregroundColor(look.faint)
                Text(TimeText.dayName(date)).font(Fonts.ui(11.5)).foregroundColor(dayColor(date))
            }
            Text("\(section.items.count)").font(Fonts.ui(11.5)).foregroundColor(look.faint)
        }
        .padding(.leading, 6)
        .padding(.top, 12)
        .padding(.bottom, 2)
    }

    private func dayColor(_ d: Date) -> Color {
        switch TimeMath.weekday(d) {
        case 0: return look.danger
        case 6: return look.saturday
        default: return look.faint
        }
    }
}

struct DoneToggle: View {
    @Environment(\.look) var look
    let count: Int
    let open: Bool
    let action: () -> Void
    let onClear: () -> Void

    @State var hoveringClear = false

    var body: some View {
        HStack(spacing: 4) {
            Button(action: action) {
                HStack(spacing: 8) {
                    Image(systemName: open ? "chevron.down" : "chevron.right").font(.system(size: 10, weight: .bold))
                    Text(open ? "완료 숨기기" : "완료한 일 보기").font(Fonts.ui(12))
                    Text("\(count)").font(Fonts.ui(12)).foregroundColor(look.faint)
                }
                .foregroundColor(look.sub)
                .padding(.horizontal, 8)
                .frame(height: 32)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            Spacer(minLength: 4)
            Button(action: onClear) {
                Text("완료한 일 모두 지우기")
                    .font(Fonts.ui(12))
                    .foregroundColor(hoveringClear ? look.danger : look.sub)
                    .lineLimit(1)
                    .padding(.horizontal, 8)
                    .frame(height: 32)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .help("지운 뒤 아래에 뜨는 되돌리기로 복구할 수 있어요")
            .onHover { hoveringClear = $0 }
        }
        .padding(.top, 6)
    }
}

struct RowView: View {
    @Environment(\.look) var look
    @EnvironmentObject var store: Store
    let item: TodoItem
    let section: SectionKind
    let onEdit: (TodoItem) -> Void

    @State var hovering = false

    var body: some View {
        let state = TimeText.state(item, now: store.now, leadMinutes: store.settings.reminderMinutes)
        HStack(alignment: .top, spacing: 6) {
            CheckCircle(done: item.done, overdue: state == .overdue) { store.toggle(item.id) }
                .padding(.top, -3)
            Text(item.text)
                .font(Fonts.ui(13.5))
                .strikethrough(item.done)
                .foregroundColor(item.done ? look.faint : look.text)
                .frame(maxWidth: .infinity, alignment: .leading)
                .fixedSize(horizontal: false, vertical: true)
            if hovering {
                IconButton(systemName: "xmark", help: "지우기", size: 22) { store.remove(item.id) }
            } else if let due = item.due {
                TimeColumn(item: item, due: due, state: state, section: section, now: store.now)
            }
        }
        .padding(.horizontal, 4)
        .padding(.vertical, 5)
        .background(RoundedRectangle(cornerRadius: 7).fill(hovering ? look.hover : Color.clear))
        .contentShape(Rectangle())
        .onHover { hovering = $0 }
        .onTapGesture(count: 2) { onEdit(item) }
        .contextMenu {
            Button("수정") { onEdit(item) }
            Button(item.isRecurring && !item.done ? "이번 회차 완료" : (item.done ? "완료 취소" : "완료로 표시")) {
                store.toggle(item.id)
            }
            Divider()
            Button("삭제") { store.remove(item.id) }
        }
    }
}

struct TimeColumn: View {
    @Environment(\.look) var look
    let item: TodoItem
    let due: Date
    let state: DueState
    let section: SectionKind
    let now: Date

    var body: some View {
        let soon = !item.done && state == .soon
        let overdue = !item.done && state == .overdue
        VStack(alignment: .trailing, spacing: 1) {
            Text(TimeText.hourMinute(due))
                .font(Fonts.num(15))
                .foregroundColor(item.done ? look.faint : (soon ? look.markerText : (overdue ? look.danger : look.text)))
                .padding(.horizontal, 4)
                .background(RoundedRectangle(cornerRadius: 3).fill(soon ? look.marker : Color.clear))
            if let sub = subText {
                HStack(spacing: 2) {
                    if item.isRecurring && !item.done && !soon {
                        Image(systemName: "repeat").font(.system(size: 8.5, weight: .bold))
                    }
                    Text(sub).font(Fonts.ui(10.5))
                }
                .foregroundColor(subColor(soon: soon, overdue: overdue))
                .padding(.trailing, 4)
            }
        }
    }

    private var subText: String? {
        if !item.done && state == .soon { return TimeText.relative(due, now: now) }
        if item.isRecurring && !item.done { return Recurrence.label(item.repeatRule, due) }
        let otherDay = TimeMath.dayDiff(due, now) != 0
        if section == .later || ((section == .overdue || item.done) && otherDay) {
            return "\(TimeMath.month(due))/\(TimeMath.day(due)) (\(TimeText.dayName(due)))"
        }
        return nil
    }

    private func subColor(soon: Bool, overdue: Bool) -> Color {
        if item.done { return look.faint }
        if soon { return look.warn }
        if overdue { return look.danger }
        return look.sub
    }
}

struct EmptyState: View {
    @Environment(\.look) var look
    var body: some View {
        VStack(spacing: 8) {
            Text("할 일이 없어요").font(Fonts.ui(14, .bold)).foregroundColor(look.text)
            Text("위 칸에 적고 Return을 누르면 추가돼요.\n\"내일 오후 3시 회의\"처럼 쓰면\n시간도 함께 정해져요.")
                .font(Fonts.ui(12.5))
                .foregroundColor(look.sub)
                .multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .padding(.top, 28)
    }
}
