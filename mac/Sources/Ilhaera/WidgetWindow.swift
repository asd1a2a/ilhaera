import AppKit
import SwiftUI

/// 바탕화면 위젯 창. 모든 데스크탑(Space)에 보이고, 데스크탑 보기·Mission Control 에도 제자리에 남는다.
@MainActor
final class WidgetWindowController: NSObject, NSWindowDelegate {
    let window: NSWindow
    private let store: Store
    private static let autosaveName = "IlhaeraWidget"

    init(store: Store, onMenu: @escaping () -> Void) {
        self.store = store
        window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 330, height: 540),
                          styleMask: [.titled, .closable, .resizable, .fullSizeContentView],
                          backing: .buffered, defer: false)
        super.init()

        window.title = "일해라"
        window.titleVisibility = .hidden
        window.titlebarAppearsTransparent = true
        for kind in [NSWindow.ButtonType.closeButton, .miniaturizeButton, .zoomButton] {
            window.standardWindowButton(kind)?.isHidden = true
        }
        window.isMovableByWindowBackground = true
        window.isReleasedWhenClosed = false
        window.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle, .fullScreenNone]
        window.minSize = NSSize(width: 300, height: 360)
        window.backgroundColor = Palette.nsBack
        window.delegate = self

        let root = Themed {
            WidgetView(onMenu: onMenu, onHide: { [weak self] in self?.hide() })
        }
        .environmentObject(store)
        window.contentView = NSHostingView(rootView: root)

        if !window.setFrameUsingName(Self.autosaveName) {
            resetPosition()
        }
        window.setFrameAutosaveName(Self.autosaveName)
        applySettings()
    }

    var isVisible: Bool { window.isVisible }

    func show() {
        NSApp.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
    }

    func hide() {
        window.orderOut(nil)
    }

    func toggle() {
        if window.isVisible { hide() } else { show() }
    }

    func resetPosition() {
        let area = (NSScreen.main ?? NSScreen.screens.first)?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        let size = NSSize(width: 330, height: min(540, area.height - 48))
        let origin = NSPoint(x: area.maxX - size.width - 24, y: area.maxY - size.height - 24)
        window.setFrame(NSRect(origin: origin, size: size), display: true)
    }

    func applySettings() {
        let s = store.settings
        window.level = s.alwaysOnTop ? .floating : .normal
        window.alphaValue = CGFloat(max(30, min(100, s.opacity))) / 100
        // 리퀴드 글래스: 창 바탕을 비워서 뒤 화면이 유리 너머로 비치게 한다
        let glass = s.theme == "glass"
        window.isOpaque = !glass
        window.backgroundColor = glass ? .clear : Palette.nsBack
        window.invalidateShadow()
    }

    // 닫기(⌘W)는 숨기기로
    func windowShouldClose(_ sender: NSWindow) -> Bool {
        hide()
        return false
    }
}
