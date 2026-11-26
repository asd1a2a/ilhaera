import AppKit
import SwiftUI
import IlhaeraCore

extension NSColor {
    convenience init(hex: UInt32) {
        self.init(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255,
                  green: CGFloat((hex >> 8) & 0xFF) / 255,
                  blue: CGFloat(hex & 0xFF) / 255,
                  alpha: 1)
    }
}

/// 창 바탕색 (AppKit 쪽에서 쓰는 것만).
enum Palette {
    static let nsBack = NSColor(name: nil) { appearance in
        appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? NSColor(hex: 0x1E2023) : NSColor(hex: 0xFFFFFF)
    }
}

/// 0~1 범위의 색 성분. 강조 색을 밝게 하거나 글자색을 고를 때 쓴다.
struct RGB: Equatable {
    var r: Double
    var g: Double
    var b: Double

    init(hex: UInt32) {
        r = Double((hex >> 16) & 0xFF) / 255
        g = Double((hex >> 8) & 0xFF) / 255
        b = Double(hex & 0xFF) / 255
    }

    init(r: Double, g: Double, b: Double) {
        self.r = r
        self.g = g
        self.b = b
    }

    var color: Color { Color(.sRGB, red: r, green: g, blue: b, opacity: 1) }

    var hex: UInt32 {
        let R = UInt32((max(0, min(1, r)) * 255).rounded())
        let G = UInt32((max(0, min(1, g)) * 255).rounded())
        let B = UInt32((max(0, min(1, b)) * 255).rounded())
        return (R << 16) | (G << 8) | B
    }

    /// 사람 눈에 느껴지는 밝기 (0 어두움 ~ 1 밝음)
    var luminance: Double { 0.2126 * r + 0.7152 * g + 0.0722 * b }

    func mixed(with other: RGB, _ amount: Double) -> RGB {
        RGB(r: r + (other.r - r) * amount, g: g + (other.g - g) * amount, b: b + (other.b - b) * amount)
    }

    static let white = RGB(r: 1, g: 1, b: 1)
    static let black = RGB(r: 0, g: 0, b: 0)
}

/// 화면에 쓰는 색 한 벌. 테마(밝게·어둡게·리퀴드 글래스)와 사용자가 고른 강조 색·형광펜 색으로 만든다.
/// 형광펜 = 곧 시작, 빨간펜 = 지난 일정, 강조 색 = 완료·버튼, 토요일 파랑 / 일요일 빨강.
struct Look: Equatable {
    var glass: Bool
    var dark: Bool
    /// 리퀴드 글래스 바탕의 불투명한 정도 0~1
    var glassTint: Double
    /// 리퀴드 글래스에서 뒤 화면을 흐리게 할지 (꺼져 있으면 그대로 비친다)
    var glassBlur: Bool

    var back: Color
    var surface: Color
    var hover: Color
    var pressed: Color
    var border: Color
    var text: Color
    var sub: Color
    var faint: Color
    var accent: Color
    var onAccent: Color
    var marker: Color
    var markerText: Color
    var warn: Color
    var danger: Color
    var saturday: Color

    static func make(settings: AppSettings, dark: Bool) -> Look {
        let glass = settings.theme == "glass"
        func pick(_ light: UInt32, _ darkHex: UInt32) -> Color { RGB(hex: dark ? darkHex : light).color }

        // 강조 색: 어두운 바탕에서는 조금 밝게
        let accentBase = RGB(hex: AppSettings.parseHex(settings.accentColor) ?? 0x1E7A57)
        let accentRGB: RGB
        if !dark {
            accentRGB = accentBase
        } else if settings.accentColor.uppercased() == AppSettings.defaultAccent {
            accentRGB = RGB(hex: 0x46C68A)
        } else {
            accentRGB = accentBase.mixed(with: .white, 0.28)
        }
        let onAccent = accentRGB.luminance > 0.6 ? RGB(hex: 0x0E1A14).color : Color.white

        // 형광펜: 밝은 바탕에서는 그 색 그대로, 어두운 바탕에서는 어둡게 깔고 글자를 그 색으로
        let markerBase = RGB(hex: AppSettings.parseHex(settings.markerColor) ?? 0xFFEC85)
        let marker: Color
        let markerText: Color
        if dark {
            marker = markerBase.mixed(with: RGB(hex: 0x1E2023), 0.72).color
            markerText = markerBase.mixed(with: .white, 0.15).color
        } else {
            marker = markerBase.color
            // 밝은 형광펜 위에는 짙은 글자, 짙은 색을 골랐으면 흰 글자
            markerText = markerBase.luminance > 0.5 ? markerBase.mixed(with: .black, 0.74).color : Color.white
        }

        if glass {
            return Look(
                glass: true, dark: dark, glassTint: Double(settings.glassTint) / 100, glassBlur: settings.glassBlur,
                back: .clear,
                surface: dark ? Color.white.opacity(0.10) : Color.white.opacity(0.42),
                hover: dark ? Color.white.opacity(0.08) : Color.black.opacity(0.05),
                pressed: dark ? Color.white.opacity(0.16) : Color.black.opacity(0.10),
                border: dark ? Color.white.opacity(0.18) : Color.white.opacity(0.65),
                text: pick(0x14171C, 0xF4F5F7),
                sub: pick(0x3B424D, 0xC9CED6),
                faint: pick(0x687080, 0x9299A4),
                accent: accentRGB.color, onAccent: onAccent,
                marker: dark ? markerBase.color.opacity(0.30) : markerBase.color.opacity(0.92),
                markerText: markerText,
                warn: pick(0x8F5200, 0xFFC24D),
                danger: pick(0xC62F2F, 0xFF7A7A),
                saturday: pick(0x2459C4, 0x8AB2FF))
        }
        return Look(
            glass: false, dark: dark, glassTint: 1, glassBlur: false,
            back: pick(0xFFFFFF, 0x1E2023),
            surface: pick(0xF1F3F5, 0x2A2D31),
            hover: pick(0xF5F6F8, 0x26292D),
            pressed: pick(0xE6E9ED, 0x34383D),
            border: pick(0xDDE1E6, 0x383C42),
            text: pick(0x1D2127, 0xECEEF0),
            sub: pick(0x5F6774, 0x9EA5AF),
            faint: pick(0x9AA1AC, 0x6C737D),
            accent: accentRGB.color, onAccent: onAccent,
            marker: marker, markerText: markerText,
            warn: pick(0xA86200, 0xFFC24D),
            danger: pick(0xD63B3B, 0xFF6B6B),
            saturday: pick(0x2F6BD8, 0x74A3FF))
    }

    func weekday(_ d: Date) -> Color {
        switch TimeMath.weekday(d) {
        case 0: return danger
        case 6: return saturday
        default: return text
        }
    }
}

private struct LookKey: EnvironmentKey {
    static let defaultValue = Look.make(settings: AppSettings(), dark: false)
}

extension EnvironmentValues {
    var look: Look {
        get { self[LookKey.self] }
        set { self[LookKey.self] = newValue }
    }
}

/// 설정과 시스템 밝기에 맞는 색을 아래 화면에 내려 준다.
struct Themed<Content: View>: View {
    @EnvironmentObject var store: Store
    @Environment(\.colorScheme) var scheme
    let content: Content

    init(@ViewBuilder content: () -> Content) {
        self.content = content()
    }

    var body: some View {
        content.environment(\.look, Look.make(settings: store.settings, dark: scheme == .dark))
    }
}

// ── 리퀴드 글래스 ──

/// 창 뒤 화면을 흐리게 비추는 유리 바탕.
struct BlurBehindWindow: NSViewRepresentable {
    func makeNSView(context: Context) -> NSVisualEffectView {
        let view = NSVisualEffectView()
        view.blendingMode = .behindWindow
        view.state = .active
        view.material = .hudWindow
        return view
    }

    func updateNSView(_ view: NSVisualEffectView, context: Context) {}
}

/// 위젯·알림 창의 바탕. 유리 테마면 흐림 + 색 막(투명도 조절) + 위쪽 반사광, 아니면 단색.
/// 유리 테마에서 "뒤 화면 흐리게"가 꺼져 있으면 흐림과 반사광 없이 색 막만 깔아서 뒤가 그대로 비친다.
struct WindowBackground: View {
    @Environment(\.look) var look
    /// 알림 창처럼 꼭 읽혀야 하는 곳은 설정과 상관없이 흐리게 깐다
    var alwaysBlur = false

    var body: some View {
        if look.glass {
            let blur = (look.glassBlur || alwaysBlur) && !CommandLine.arguments.contains("--no-blur")
            let film = look.dark ? RGB(hex: 0x16181C).color : Color.white
            ZStack {
                if blur {
                    BlurBehindWindow()
                    film.opacity(look.glassTint)
                    // 유리 위쪽 가장자리에 맺히는 반사광
                    LinearGradient(colors: [Color.white.opacity(look.dark ? 0.08 : 0.22), Color.white.opacity(0)],
                                   startPoint: .top, endPoint: UnitPoint(x: 0.5, y: 0.3))
                } else {
                    // 완전히 비우면 빈 곳을 눌러도 뒤 창이 눌리므로 눈에 안 보일 만큼만 남긴다
                    film.opacity(max(0.01, look.glassTint))
                }
            }
            .ignoresSafeArea()
        } else {
            look.back.ignoresSafeArea()
        }
    }
}

/// 입력칸·다음 일정 줄 같은 판. 유리 테마에서는 반투명 판 + 빛 받는 가장자리.
struct SurfaceBackground: View {
    @Environment(\.look) var look
    let radius: CGFloat
    var fill: Color?

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: radius, style: .continuous)
        if look.glass {
            shape.fill(fill ?? look.surface)
                .overlay(
                    shape.strokeBorder(
                        LinearGradient(colors: [Color.white.opacity(look.dark ? 0.35 : 0.9), Color.white.opacity(look.dark ? 0.04 : 0.2)],
                                       startPoint: .topLeading, endPoint: .bottomTrailing),
                        lineWidth: 1))
                .shadow(color: Color.black.opacity(look.dark ? 0.25 : 0.08), radius: 6, x: 0, y: 2)
        } else {
            shape.fill(fill ?? look.surface)
        }
    }
}

/// 판을 까는 방식: macOS 26 이상의 유리 테마에서는 시스템 리퀴드 글래스, 그 밖에는 직접 그린 판.
struct SurfaceModifier: ViewModifier {
    @Environment(\.look) var look
    let radius: CGFloat
    let fill: Color?

    func body(content: Content) -> some View {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *), look.glass {
            content.glassEffect(fill.map { Glass.regular.tint($0) } ?? Glass.regular,
                                in: RoundedRectangle(cornerRadius: radius, style: .continuous))
        } else {
            content.background(SurfaceBackground(radius: radius, fill: fill))
        }
        #else
        content.background(SurfaceBackground(radius: radius, fill: fill))
        #endif
    }
}

extension View {
    func surface(radius: CGFloat, fill: Color? = nil) -> some View {
        modifier(SurfaceModifier(radius: radius, fill: fill))
    }
}

/// 흐림 없는 유리에서는 글자가 바탕화면 그림 위에 바로 놓인다. 바탕과 같은 쪽 빛(밝게: 흰빛, 어둡게: 그늘)을 둘러 읽히게 한다.
struct ClearGlassLegibility: ViewModifier {
    @Environment(\.look) var look

    func body(content: Content) -> some View {
        if look.glass && !look.glassBlur {
            content.shadow(color: (look.dark ? Color.black : Color.white).opacity(0.55), radius: 2.5, x: 0, y: 0)
        } else {
            content
        }
    }
}

enum Fonts {
    /// 날짜·시각 숫자: 좁은 폭 + 고정폭 숫자 (Windows 버전의 Bahnschrift 역할)
    static func num(_ size: CGFloat, _ weight: Font.Weight = .semibold) -> Font {
        Font.system(size: size, weight: weight).width(.condensed).monospacedDigit()
    }

    static func ui(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font {
        Font.system(size: size, weight: weight)
    }
}

extension String {
    var oneLine: String { replacingOccurrences(of: "\r", with: " ").replacingOccurrences(of: "\n", with: " ") }
    var trimmed: String { trimmingCharacters(in: .whitespacesAndNewlines) }
}
