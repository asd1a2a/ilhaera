import SwiftUI

/// 칩이 넘치면 다음 줄로 넘기는 배치.
struct FlowLayout: Layout {
    var spacing: CGFloat = 6

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        let maxWidth = proposal.width ?? .infinity
        var x: CGFloat = 0
        var y: CGFloat = 0
        var rowHeight: CGFloat = 0
        var width: CGFloat = 0
        for view in subviews {
            let size = view.sizeThatFits(.unspecified)
            if x > 0 && x + size.width > maxWidth {
                x = 0
                y += rowHeight + spacing
                rowHeight = 0
            }
            x += size.width + spacing
            rowHeight = max(rowHeight, size.height)
            width = max(width, x - spacing)
        }
        return CGSize(width: proposal.width ?? width, height: y + rowHeight)
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        var x = bounds.minX
        var y = bounds.minY
        var rowHeight: CGFloat = 0
        for view in subviews {
            let size = view.sizeThatFits(.unspecified)
            if x > bounds.minX && x + size.width > bounds.maxX {
                x = bounds.minX
                y += rowHeight + spacing
                rowHeight = 0
            }
            view.place(at: CGPoint(x: x, y: y), proposal: ProposedViewSize(size))
            x += size.width + spacing
            rowHeight = max(rowHeight, size.height)
        }
    }
}

struct ChipButton: View {
    @Environment(\.look) var look
    let title: String
    var systemImage: String? = nil
    var selected = false
    let action: () -> Void

    @State var hovering = false

    var body: some View {
        Button(action: action) {
            HStack(spacing: 4) {
                if let systemImage {
                    Image(systemName: systemImage).font(.system(size: 10.5, weight: .semibold))
                }
                Text(title).font(Fonts.ui(12, selected ? .semibold : .regular))
            }
            .padding(.horizontal, 11)
            .frame(height: 26)
            .background(Capsule().fill(fill))
            .foregroundColor(selected ? look.onAccent : look.text)
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
    }

    private var fill: Color {
        if selected { return look.accent }
        return hovering ? look.pressed : look.surface
    }
}

struct IconButton: View {
    @Environment(\.look) var look
    let systemName: String
    var help: String = ""
    var active = false
    var size: CGFloat = 26
    let action: () -> Void

    @State var hovering = false

    var body: some View {
        Button(action: action) {
            Image(systemName: systemName)
                .font(.system(size: 13, weight: .semibold))
                .frame(width: size, height: size)
                .background(RoundedRectangle(cornerRadius: 6).fill(hovering || active ? look.hover : Color.clear))
                .foregroundColor(active ? look.accent : (hovering ? look.text : look.sub))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(help)
        .onHover { hovering = $0 }
    }
}

struct CheckCircle: View {
    @Environment(\.look) var look
    let done: Bool
    let overdue: Bool
    let action: () -> Void

    @State var hovering = false

    var body: some View {
        Button(action: action) {
            ZStack {
                if done {
                    Circle().fill(look.accent)
                    Image(systemName: "checkmark")
                        .font(.system(size: 9, weight: .heavy))
                        .foregroundColor(look.onAccent)
                } else {
                    Circle().fill(hovering ? look.accent.opacity(0.18) : Color.clear)
                    Circle().strokeBorder(hovering ? look.accent : (overdue ? look.danger : look.faint), lineWidth: 1.5)
                }
            }
            .frame(width: 18, height: 18)
            .padding(4)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
    }
}
