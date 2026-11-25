import AppKit
import SwiftUI
import IlhaeraCore

/// 설정 창: 테마(리퀴드 글래스 포함), 투명도, 색, 알림 시점.
struct SettingsView: View {
    @EnvironmentObject var store: Store
    @Environment(\.look) var look

    static let accentPresets = ["#1E7A57", "#2F6BD8", "#7B4FD6", "#D6457B", "#D9771E", "#1A8F99", "#3A3F47"]
    static let markerPresets = ["#FFEC85", "#C7F089", "#A9DDFF", "#FFC2DA", "#FFD3A8"]
    static let leadChoices = [5, 10, 15, 30, 60, 120]

    var body: some View {
        Form {
            Section("테마") {
                Picker("모양", selection: setting(\.theme)) {
                    Text("시스템").tag("system")
                    Text("밝게").tag("light")
                    Text("어둡게").tag("dark")
                    Text("리퀴드 글래스").tag("glass")
                }
                .pickerStyle(.segmented)

                if store.settings.theme == "glass" {
                    Slider(value: intSetting(\.glassTint), in: 0...100, step: 5) {
                        Text("유리 투명도")
                    } minimumValueLabel: {
                        Text("투명").font(.caption)
                    } maximumValueLabel: {
                        Text("불투명").font(.caption)
                    }
                    Text("유리 뒤로 바탕화면이 얼마나 비칠지 정해요. 글자가 잘 안 보이면 오른쪽으로 옮기세요.")
                        .font(.caption)
                        .foregroundColor(.secondary)
                }

                Slider(value: intSetting(\.opacity), in: 30...100, step: 5) {
                    Text("위젯 전체 투명도")
                } minimumValueLabel: {
                    Text("30%").font(.caption)
                } maximumValueLabel: {
                    Text("100%").font(.caption)
                }
            }

            Section("색") {
                ColorRow(title: "강조 색", caption: "체크, 버튼, 진행률", presets: Self.accentPresets, hex: setting(\.accentColor))
                ColorRow(title: "형광펜 색", caption: "곧 시작할 일", presets: Self.markerPresets, hex: setting(\.markerColor))
            }

            Section("동작") {
                Picker("알림 시점", selection: setting(\.reminderMinutes)) {
                    ForEach(Self.leadChoices, id: \.self) { m in
                        Text(m < 60 ? "\(m)분 전" : "\(m / 60)시간 전").tag(m)
                    }
                }
                Toggle("항상 위에 표시", isOn: setting(\.alwaysOnTop))
            }

            Section {
                Button("색과 테마를 기본값으로 되돌리기") {
                    store.updateSettings { s in
                        s.theme = "system"
                        s.accentColor = AppSettings.defaultAccent
                        s.markerColor = AppSettings.defaultMarker
                        s.glassTint = AppSettings.defaultGlassTint
                        s.opacity = 97
                    }
                }
            }
        }
        .formStyle(.grouped)
        .frame(width: 460)
        .fixedSize(horizontal: false, vertical: true)
    }

    private func setting<T>(_ keyPath: WritableKeyPath<AppSettings, T>) -> Binding<T> {
        Binding(
            get: { store.settings[keyPath: keyPath] },
            set: { newValue in store.updateSettings { $0[keyPath: keyPath] = newValue } }
        )
    }

    private func intSetting(_ keyPath: WritableKeyPath<AppSettings, Int>) -> Binding<Double> {
        Binding(
            get: { Double(store.settings[keyPath: keyPath]) },
            set: { newValue in store.updateSettings { $0[keyPath: keyPath] = Int(newValue.rounded()) } }
        )
    }
}

/// 미리 고른 색 동그라미 + 직접 고르기.
struct ColorRow: View {
    let title: String
    let caption: String
    let presets: [String]
    @Binding var hex: String

    var body: some View {
        HStack(spacing: 8) {
            VStack(alignment: .leading, spacing: 1) {
                Text(title)
                Text(caption).font(.caption).foregroundColor(.secondary)
            }
            Spacer(minLength: 8)
            ForEach(presets, id: \.self) { preset in
                Button {
                    hex = preset
                } label: {
                    Circle()
                        .fill(RGB(hex: AppSettings.parseHex(preset) ?? 0).color)
                        .frame(width: 20, height: 20)
                        .overlay(Circle().strokeBorder(Color.primary.opacity(0.18), lineWidth: 1))
                        .overlay(
                            Circle()
                                .strokeBorder(Color.primary, lineWidth: 2)
                                .padding(-4)
                                .opacity(hex.uppercased() == preset ? 1 : 0)
                        )
                        .padding(4)
                        .contentShape(Circle())
                }
                .buttonStyle(.plain)
                .help(preset)
            }
            ColorPicker("직접 고르기", selection: colorBinding, supportsOpacity: false)
                .labelsHidden()
                .help("직접 고르기")
        }
    }

    private var colorBinding: Binding<Color> {
        Binding(
            get: { RGB(hex: AppSettings.parseHex(hex) ?? 0).color },
            set: { newColor in
                guard let c = NSColor(newColor).usingColorSpace(.sRGB) else { return }
                hex = AppSettings.hexString(RGB(r: Double(c.redComponent), g: Double(c.greenComponent), b: Double(c.blueComponent)).hex)
            }
        )
    }
}

/// 설정 창.
@MainActor
final class SettingsWindowController: NSObject {
    private var window: NSWindow?
    private let store: Store

    init(store: Store) {
        self.store = store
    }

    func show() {
        if window == nil {
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 460, height: 420),
                             styleMask: [.titled, .closable],
                             backing: .buffered, defer: false)
            w.title = "일해라 설정"
            w.isReleasedWhenClosed = false
            let root = Themed { SettingsView() }.environmentObject(store)
            let host = NSHostingView(rootView: root)
            w.contentView = host
            w.setContentSize(host.fittingSize)
            w.center()
            window = w
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }
}
