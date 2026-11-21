import Foundation

/// ~/Library/Application Support/Ilhaera/todos.json 에 저장하고, 손상되면 백업에서 복구한다.
/// 파일 형식은 Windows 버전(%APPDATA%\TodoWidget\todos.json)과 같다.
public final class Storage {
    public let directory: URL
    public let fileURL: URL
    var backupURL: URL { fileURL.appendingPathExtension("bak") }

    public init(directory: URL) throws {
        self.directory = directory
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        fileURL = directory.appendingPathComponent("todos.json")
    }

    public static var defaultDirectory: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Library/Application Support")
        return base.appendingPathComponent("Ilhaera", isDirectory: true)
    }

    public func load() -> AppData {
        let fm = FileManager.default
        if fm.fileExists(atPath: fileURL.path) {
            if let data = try? read(fileURL) { return data }
            // 손상된 파일은 지우지 않고 따로 보관한다.
            let stamp = Int(Date().timeIntervalSince1970)
            try? fm.copyItem(at: fileURL, to: directory.appendingPathComponent("todos.corrupt-\(stamp).json"))
        }
        if fm.fileExists(atPath: backupURL.path), let data = try? read(backupURL) { return data }
        return AppData()
    }

    public func save(_ data: AppData) throws {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .withoutEscapingSlashes]
        let bytes = try encoder.encode(data)
        let fm = FileManager.default
        if fm.fileExists(atPath: fileURL.path) {
            try? fm.removeItem(at: backupURL)
            try? fm.copyItem(at: fileURL, to: backupURL)
        }
        try bytes.write(to: fileURL, options: .atomic)
    }

    func read(_ url: URL) throws -> AppData {
        let bytes = try Data(contentsOf: url)
        return try JSONDecoder().decode(AppData.self, from: bytes)
    }
}
