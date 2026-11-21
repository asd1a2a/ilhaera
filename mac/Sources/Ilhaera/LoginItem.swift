import Foundation
import ServiceManagement

/// 로그인 시 자동 실행. 시스템 로그인 항목(SMAppService)을 먼저 쓰고, 안 되면 LaunchAgent 로 대신한다.
enum LoginItem {
    static let label = "com.asd1a2a.ilhaera"

    static var launchAgentURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/LaunchAgents/\(label).plist")
    }

    static var isEnabled: Bool {
        SMAppService.mainApp.status == .enabled || FileManager.default.fileExists(atPath: launchAgentURL.path)
    }

    static func setEnabled(_ enabled: Bool) {
        if enabled {
            do {
                try SMAppService.mainApp.register()
            } catch {
                NSLog("일해라: 로그인 항목 등록 실패, LaunchAgent 사용 \(error)")
                writeLaunchAgent()
            }
        } else {
            try? SMAppService.mainApp.unregister()
            try? FileManager.default.removeItem(at: launchAgentURL)
        }
    }

    /// 앱을 다른 폴더로 옮긴 뒤 실행했을 때 LaunchAgent 경로를 갱신.
    static func refreshPath() {
        guard FileManager.default.fileExists(atPath: launchAgentURL.path) else { return }
        writeLaunchAgent()
    }

    private static func writeLaunchAgent() {
        let plist: [String: Any] = [
            "Label": label,
            "ProgramArguments": ["/usr/bin/open", "-a", Bundle.main.bundlePath],
            "RunAtLoad": true,
        ]
        do {
            try FileManager.default.createDirectory(at: launchAgentURL.deletingLastPathComponent(), withIntermediateDirectories: true)
            let data = try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
            try data.write(to: launchAgentURL, options: .atomic)
        } catch {
            NSLog("일해라: LaunchAgent 저장 실패 \(error)")
        }
    }
}
