import SwiftUI
import IlhaeraCore

/// 할 일 수정 시트.
struct EditView: View {
    @Environment(\.look) var look
    let item: TodoItem
    let lead: Int
    let onSave: (String, Date?, Repeat) -> Void
    let onCancel: () -> Void

    @State var text: String
    @State var hasTime: Bool
    @State var value: PickerValue

    init(item: TodoItem, lead: Int, onSave: @escaping (String, Date?, Repeat) -> Void, onCancel: @escaping () -> Void) {
        self.item = item
        self.lead = lead
        self.onSave = onSave
        self.onCancel = onCancel
        _text = State(initialValue: item.text)
        _hasTime = State(initialValue: item.due != nil)
        _value = State(initialValue: PickerValue.from(item.due ?? TimeText.defaultDue(Date()),
                                                      rule: item.due != nil ? item.repeatRule : .none))
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("할 일 수정").font(Fonts.ui(15, .bold))
            TextField("할 일", text: $text, axis: .vertical)
                .lineLimit(1...4)
                .textFieldStyle(.plain)
                .font(Fonts.ui(14))
                .padding(10)
                .background(RoundedRectangle(cornerRadius: 8).fill(look.glass ? look.pressed : look.surface))
            Toggle("시간 정하기", isOn: $hasTime)
                .toggleStyle(.checkbox)
                .font(Fonts.ui(13))
            if hasTime {
                TimePickerView(value: $value, lead: lead, now: Date())
            }
            HStack {
                Spacer()
                Button("취소", action: onCancel)
                    .keyboardShortcut(.cancelAction)
                Button("저장", action: save)
                    .keyboardShortcut(.defaultAction)
                    .disabled(text.trimmed.isEmpty)
            }
        }
        .padding(20)
        .frame(width: 380)
    }

    private func save() {
        let trimmed = text.trimmed
        guard !trimmed.isEmpty else { return }
        if hasTime {
            let v = value.resolved(now: Date())
            onSave(trimmed, v.due, v.rule)
        } else {
            onSave(trimmed, nil, .none)
        }
    }
}
