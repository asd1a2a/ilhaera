import AppKit
import SwiftUI
import IlhaeraCore

/// 화면 오른쪽 아래에 뜨는 알림 창. 확인이나 다시 알림을 누를 때까지 남아 있다.
@MainActor
enum AlertPanels {
    static let snoozeMinutes = 10
    private static var open: [NSPanel] = []

    static func show(_ alerts: [ReminderAlert], onSnooze: @escaping ([String]) -> Void, onOpen: @escaping () -> Void) {
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 340, height: 160),
                            styleMask: [.titled, .fullSizeContentView, .nonactivatingPanel],
                            backing: .buffered, defer: false)
        panel.titleVisibility = .hidden
        panel.titlebarAppearsTransparent = true
        for kind in [NSWindow.ButtonType.closeButton, .miniaturizeButton, .zoomButton] {
            panel.standardWindowButton(kind)?.isHidden = true
        }
        panel.isMovableByWindowBackground = true
        panel.isReleasedWhenClosed = false
        panel.hidesOnDeactivate = false
        panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.backgroundColor = Palette.nsBack

        let close: () -> Void = { [weak panel] in
            guard let panel else { return }
            panel.orderOut(nil)
            open.removeAll { $0 === panel }
        }
        let ids = alerts.map { $0.item.id }
        let view = AlertView(alerts: alerts, now: Date(),
                             onOK: close,
                             onSnooze: { onSnooze(ids); close() },
                             onOpen: { onOpen(); close() })
        let host = NSHostingView(rootView: view)
        panel.contentView = host
        // 내용은 투명한 제목 표시줄 영역까지 올려 그리므로, 그 높이만큼 창을 줄인다
        let titlebarHeight = panel.frame.height - panel.contentLayoutRect.height
        let fitting = host.fittingSize
        panel.setContentSize(NSSize(width: fitting.width, height: max(100, fitting.height - titlebarHeight)))

        // 오른쪽 아래, 이미 떠 있는 알림 위로 쌓기
        if let screen = NSScreen.main {
            let area = screen.visibleFrame
            var bottom = area.minY + 16
            for p in open where p.isVisible { bottom = max(bottom, p.frame.maxY + 10) }
            if bottom + panel.frame.height > area.maxY { bottom = area.minY + 16 }
            panel.setFrameOrigin(NSPoint(x: area.maxX - panel.frame.width - 16, y: bottom))
        }
        open.append(panel)
        panel.orderFrontRegardless()
        NSSound(named: NSSound.Name("Glass"))?.play()
    }

    static func closeAll() {
        for p in open { p.orderOut(nil) }
        open.removeAll()
    }
}

struct AlertView: View {
    let alerts: [ReminderAlert]
    let now: Date
    let onOK: () -> Void
    let onSnooze: () -> Void
    let onOpen: () -> Void

    private var title: String {
        if alerts.allSatisfy({ $0.kind == .upcoming }) { return "곧 시작해요" }
        if alerts.allSatisfy({ $0.kind == .missed }) { return "시간이 지난 일정이 있어요" }
        return "할 일 알림"
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 6) {
                Image(systemName: "clock").font(.system(size: 13, weight: .semibold)).foregroundColor(Palette.accent)
                Text(title).font(Fonts.ui(14, .bold)).foregroundColor(Palette.text)
            }
            ForEach(alerts.prefix(4)) { alert in
                AlertRow(alert: alert, now: now)
                    .contentShape(Rectangle())
                    .onTapGesture(perform: onOpen)
            }
            if alerts.count > 4 {
                Text("외 \(alerts.count - 4)개 더 있어요").font(Fonts.ui(11.5)).foregroundColor(Palette.sub)
            }
            HStack(spacing: 8) {
                Spacer()
                Button("\(AlertPanels.snoozeMinutes)분 뒤 다시 알림", action: onSnooze)
                    .buttonStyle(.plain)
                    .font(Fonts.ui(12))
                    .foregroundColor(Palette.sub)
                    .padding(.horizontal, 10)
                Button(action: onOK) {
                    Text("확인")
                        .font(Fonts.ui(12.5, .bold))
                        .foregroundColor(Palette.onAccent)
                        .frame(width: 64, height: 28)
                        .background(RoundedRectangle(cornerRadius: 7).fill(Palette.accent))
                }
                .buttonStyle(.plain)
                .keyboardShortcut(.defaultAction)
            }
        }
        .padding(EdgeInsets(top: 16, leading: 18, bottom: 14, trailing: 16))
        .frame(width: 340, alignment: .leading)
        .background(Palette.back.ignoresSafeArea())
        .ignoresSafeArea(.container, edges: .top)
    }
}

struct AlertRow: View {
    let alert: ReminderAlert
    let now: Date

    var body: some View {
        let upcoming = alert.kind == .upcoming
        let due = alert.item.due ?? now
        HStack(alignment: .top, spacing: 10) {
            Text(TimeText.hourMinute(due))
                .font(Fonts.num(21, .bold))
                .foregroundColor(upcoming ? Palette.markerText : Palette.danger)
                .padding(.horizontal, 4)
                .background(RoundedRectangle(cornerRadius: 4).fill(upcoming ? Palette.marker : Color.clear))
            VStack(alignment: .leading, spacing: 2) {
                Text(alert.item.text.oneLine).font(Fonts.ui(13, .bold)).foregroundColor(Palette.text).lineLimit(2)
                Text(upcoming ? "\(alert.minutesLeft)분 후 시작" : missedText(due))
                    .font(Fonts.ui(11.5))
                    .foregroundColor(upcoming ? Palette.warn : Palette.danger)
            }
        }
    }

    private func missedText(_ due: Date) -> String {
        if let word = TimeText.dayWord(due, now: now), word != "오늘" { return word + " 시간이 지났어요" }
        return "시간이 지났어요"
    }
}
