import AppKit

/// 메뉴 막대 앱 (Dock 에 아이콘 없음).
@main
enum IlhaeraEntry {
    @MainActor
    static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)
        withExtendedLifetime(delegate) {
            app.run()
        }
    }
}
