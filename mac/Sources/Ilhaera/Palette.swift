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

/// Windows 버전과 같은 색. 종이(밝게) / 먹(어둡게).
/// 형광펜 = 곧 시작, 빨간펜 = 지난 일정, 초록 잉크 = 완료·강조, 토요일 파랑 / 일요일 빨강.
enum Palette {
    static func ns(_ light: UInt32, _ dark: UInt32) -> NSColor {
        NSColor(name: nil) { appearance in
            appearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? NSColor(hex: dark) : NSColor(hex: light)
        }
    }

    static func color(_ light: UInt32, _ dark: UInt32) -> Color { Color(nsColor: ns(light, dark)) }

    static let nsBack = ns(0xFFFFFF, 0x1E2023)
    static let back = Color(nsColor: nsBack)
    static let surface = color(0xF1F3F5, 0x2A2D31)
    static let hover = color(0xF5F6F8, 0x26292D)
    static let pressed = color(0xE6E9ED, 0x34383D)
    static let border = color(0xDDE1E6, 0x383C42)
    static let text = color(0x1D2127, 0xECEEF0)
    static let sub = color(0x5F6774, 0x9EA5AF)
    static let faint = color(0x9AA1AC, 0x6C737D)
    static let accent = color(0x1E7A57, 0x46C68A)
    static let onAccent = color(0xFFFFFF, 0x0E1A14)
    static let marker = color(0xFFEC85, 0x4C4320)
    static let markerText = color(0x4D3B00, 0xFFE27A)
    static let warn = color(0xA86200, 0xFFC24D)
    static let danger = color(0xD63B3B, 0xFF6B6B)
    static let saturday = color(0x2F6BD8, 0x74A3FF)

    static func weekday(_ d: Date) -> Color {
        switch TimeMath.weekday(d) {
        case 0: return danger
        case 6: return saturday
        default: return text
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
