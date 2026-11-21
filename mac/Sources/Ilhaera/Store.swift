import Foundation
import IlhaeraCore

struct UndoInfo: Identifiable {
    let id = UUID()
    let message: String
    let action: () -> Void
}

/// 할 일 데이터와 설정. 바뀔 때마다 바로 저장한다. (메인 스레드에서만 쓴다)
final class Store: ObservableObject {
    @Published private(set) var data: AppData
    @Published var now = Date()
    @Published var undo: UndoInfo?
    @Published var scrollTarget: String?

    let storage: Storage
    var onAlerts: (([ReminderAlert]) -> Void)?
    var onSettingsChanged: (() -> Void)?
    private(set) var lastSaveError: String?

    var items: [TodoItem] { data.items }
    var settings: AppSettings { data.settings }

    init(storage: Storage) {
        self.storage = storage
        self.data = storage.load()
    }

    // ── 설정 ──

    func updateSettings(_ change: (inout AppSettings) -> Void) {
        change(&data.settings)
        data.settings.normalize()
        save()
        onSettingsChanged?()
    }

    func toggleShowDone() {
        updateSettings { $0.showDone.toggle() }
    }

    // ── 할 일 ──

    @discardableResult
    func add(text: String, due: Date?, repeat rule: Repeat) -> String {
        let item = TodoItem.create(text: text, due: due, now: Date(), repeat: rule)
        data.items.append(item)
        save()
        checkReminders()
        return item.id
    }

    func toggle(_ id: String) {
        guard let i = index(of: id) else { return }
        let now = Date()
        if data.items[i].isRecurring && !data.items[i].done {
            // 반복 일정: 이번 회차만 완료하고 다음 회차로
            let before = data.items[i]
            data.items[i].completeOccurrence(now: now)
            save()
            if let next = data.items[i].due {
                showUndo("'\(before.text.oneLine)' 완료. 다음: \(TimeText.dueLabel(next, now: now))") { [weak self] in
                    self?.replace(before)
                }
            }
            return
        }
        data.items[i].setDone(!data.items[i].done, now: now)
        save()
    }

    func update(_ id: String, text: String, due: Date?, repeat rule: Repeat) {
        guard let i = index(of: id) else { return }
        data.items[i].text = text
        let newRule: Repeat = due == nil ? .none : rule
        if data.items[i].due != due || data.items[i].repeatRule != newRule {
            data.items[i].setDue(due, now: Date())
            data.items[i].repeatRule = newRule
        }
        save()
        checkReminders()
    }

    func remove(_ id: String) {
        guard let i = index(of: id) else { return }
        let item = data.items.remove(at: i)
        save()
        showUndo("'\(item.text.oneLine)' 지움") { [weak self] in
            guard let self else { return }
            if self.index(of: item.id) == nil {
                self.data.items.insert(item, at: min(i, self.data.items.count))
                self.save()
            }
        }
    }

    func clearDone() {
        let done = data.items.filter { $0.done }
        guard !done.isEmpty else { return }
        data.items.removeAll { $0.done }
        save()
        showUndo("완료한 일 \(done.count)개를 지웠어요") { [weak self] in
            guard let self else { return }
            for item in done where self.index(of: item.id) == nil { self.data.items.append(item) }
            self.save()
        }
    }

    func snooze(_ ids: [String], minutes: Int) {
        let at = Date().addingTimeInterval(Double(minutes) * 60)
        for id in ids {
            if let i = index(of: id), !data.items[i].done { data.items[i].snooze = at }
        }
        save()
    }

    func performUndo() {
        guard let info = undo else { return }
        undo = nil
        info.action()
    }

    private func replace(_ item: TodoItem) {
        guard let i = index(of: item.id) else { return }
        data.items[i] = item
        save()
    }

    private func index(of id: String) -> Int? { data.items.firstIndex { $0.id == id } }

    private func showUndo(_ message: String, action: @escaping () -> Void) {
        let info = UndoInfo(message: message, action: action)
        undo = info
        Task { @MainActor [weak self] in
            try? await Task.sleep(nanoseconds: 7_000_000_000)
            if self?.undo?.id == info.id { self?.undo = nil }
        }
    }

    // ── 알림 ──

    func tick() {
        now = Date()
        checkReminders()
    }

    func checkReminders() {
        let now = Date()
        var items = data.items
        var changed = Recurrence.rollForward(&items, now: now)
        let alerts = Reminders.collect(&items, now: now, leadMinutes: data.settings.reminderMinutes)
        if !alerts.isEmpty { changed = true }
        if changed {
            data.items = items
            save()
        }
        if !alerts.isEmpty { onAlerts?(alerts) }
    }

    func save() {
        do {
            try storage.save(data)
            lastSaveError = nil
        } catch {
            lastSaveError = error.localizedDescription
            NSLog("일해라: 저장 실패 \(error)")
        }
    }
}
