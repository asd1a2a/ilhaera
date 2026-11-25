import AppKit
import IlhaeraCore

/// 메뉴 막대 아이콘·위젯·알림을 관리하는 앱 본체.
@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var store: Store!
    private var widget: WidgetWindowController!
    private var settingsWindow: SettingsWindowController!
    private var statusItem: NSStatusItem!
    private let menu = NSMenu()
    private var timer: Timer?

    private static let opacityChoices = [100, 95, 90, 80, 70, 60]
    private static let leadChoices = [5, 10, 15, 30, 60, 120]

    func applicationDidFinishLaunching(_ notification: Notification) {
        let storage: Storage
        do {
            storage = try Storage(directory: Storage.defaultDirectory)
        } catch {
            let alert = NSAlert()
            alert.messageText = "데이터 폴더를 열 수 없어요."
            alert.informativeText = error.localizedDescription
            alert.runModal()
            NSApp.terminate(nil)
            return
        }

        store = Store(storage: storage)
        applyTheme()
        widget = WidgetWindowController(store: store, onMenu: { [weak self] in self?.popUpMenu() })
        settingsWindow = SettingsWindowController(store: store)
        store.onAlerts = { [weak self] alerts in self?.showAlerts(alerts) }
        store.onSettingsChanged = { [weak self] in
            self?.applyTheme()
            self?.widget.applySettings()
        }

        setUpStatusItem()
        widget.show()
        // 자동 화면 확인(CI)용: 설정 창을 바로 연다
        if CommandLine.arguments.contains("--settings") { settingsWindow.show() }

        if !store.settings.autoStartInitialized {
            LoginItem.setEnabled(true)
            store.updateSettings { $0.autoStartInitialized = true }
        } else {
            LoginItem.refreshPath()
        }

        timer = Timer.scheduledTimer(withTimeInterval: 15, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.store.tick() }
        }
        NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.store.tick() }
        }
        // 꺼져 있는 동안 놓친 일정 포함, 첫 알림 확인
        DispatchQueue.main.async { [weak self] in self?.store.checkReminders() }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        widget.show()
        return true
    }

    // ── 알림 ──

    private func showAlerts(_ alerts: [ReminderAlert]) {
        AlertPanels.show(alerts, store: store,
                         onSnooze: { [weak self] ids in self?.store.snooze(ids, minutes: AlertPanels.snoozeMinutes) },
                         onOpen: { [weak self] in self?.widget.show() })
    }

    // ── 테마 ──

    private func applyTheme() {
        switch store.settings.theme {
        case "dark": NSApp.appearance = NSAppearance(named: .darkAqua)
        case "light": NSApp.appearance = NSAppearance(named: .aqua)
        default: NSApp.appearance = nil
        }
    }

    // ── 메뉴 막대 ──

    private func setUpStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = statusItem.button {
            let image = NSImage(systemSymbolName: "checklist", accessibilityDescription: "일해라")
                ?? NSImage(systemSymbolName: "checkmark.circle", accessibilityDescription: "일해라")
            image?.isTemplate = true
            button.image = image
            button.toolTip = "일해라"
        }
        menu.delegate = self
        menu.autoenablesItems = false
        statusItem.menu = menu
    }

    private func popUpMenu() {
        menuNeedsUpdate(menu)
        menu.popUp(positioning: nil, at: NSEvent.mouseLocation, in: nil)
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        let s = store.settings

        menu.addItem(item(widget.isVisible ? "위젯 숨기기" : "위젯 보이기") { [weak self] in self?.widget.toggle() })
        menu.addItem(.separator())

        menu.addItem(item("항상 위에 표시", checked: s.alwaysOnTop) { [weak self] in
            self?.store.updateSettings { $0.alwaysOnTop.toggle() }
        })

        let opacity = NSMenu()
        for p in Self.opacityChoices {
            opacity.addItem(item(p == 100 ? "불투명" : "\(p)%", checked: s.opacity == p) { [weak self] in
                self?.store.updateSettings { $0.opacity = p }
            })
        }
        menu.addItem(submenu("투명도", opacity))

        let lead = NSMenu()
        for m in Self.leadChoices {
            lead.addItem(item(m < 60 ? "\(m)분 전" : "\(m / 60)시간 전", checked: s.reminderMinutes == m) { [weak self] in
                self?.store.updateSettings { $0.reminderMinutes = m }
                self?.store.checkReminders()
            })
        }
        menu.addItem(submenu("알림 시점", lead))

        let theme = NSMenu()
        for (value, title) in [("system", "시스템 설정 따르기"), ("light", "밝게"), ("dark", "어둡게"), ("glass", "리퀴드 글래스")] {
            theme.addItem(item(title, checked: s.theme == value) { [weak self] in
                self?.store.updateSettings { $0.theme = value }
            })
        }
        menu.addItem(submenu("테마", theme))

        menu.addItem(item("로그인 시 자동 실행", checked: LoginItem.isEnabled) {
            LoginItem.setEnabled(!LoginItem.isEnabled)
        })
        let settings = item("설정… (색, 유리 투명도)") { [weak self] in self?.settingsWindow.show() }
        settings.keyEquivalent = ","
        menu.addItem(settings)
        menu.addItem(.separator())

        let clear = item("완료한 일 모두 지우기") { [weak self] in self?.store.clearDone() }
        clear.isEnabled = store.items.contains { $0.done }
        menu.addItem(clear)
        menu.addItem(item("위치·크기 초기화") { [weak self] in
            self?.widget.show()
            self?.widget.resetPosition()
        })
        menu.addItem(item("데이터 폴더 열기") { [weak self] in
            guard let self else { return }
            NSWorkspace.shared.activateFileViewerSelecting([self.store.storage.fileURL])
        })
        menu.addItem(.separator())
        let quit = item("일해라 종료") { NSApp.terminate(nil) }
        quit.keyEquivalent = "q"
        menu.addItem(quit)
    }

    private func item(_ title: String, checked: Bool = false, _ handler: @escaping () -> Void) -> NSMenuItem {
        let action = MenuAction(handler)
        let mi = NSMenuItem(title: title, action: #selector(MenuAction.run), keyEquivalent: "")
        mi.target = action
        mi.representedObject = action // target 은 weak 라서 여기서 붙잡아 둔다
        mi.state = checked ? .on : .off
        mi.isEnabled = true
        return mi
    }

    private func submenu(_ title: String, _ sub: NSMenu) -> NSMenuItem {
        let mi = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        mi.submenu = sub
        return mi
    }

    func applicationWillTerminate(_ notification: Notification) {
        store?.save()
        AlertPanels.closeAll()
    }
}

final class MenuAction: NSObject {
    private let handler: () -> Void
    init(_ handler: @escaping () -> Void) { self.handler = handler }
    @objc func run() { handler() }
}
