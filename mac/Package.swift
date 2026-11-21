// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "Ilhaera",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "Ilhaera", targets: ["Ilhaera"]),
    ],
    targets: [
        .target(name: "IlhaeraCore", path: "Sources/IlhaeraCore"),
        .executableTarget(name: "Ilhaera", dependencies: ["IlhaeraCore"], path: "Sources/Ilhaera"),
        .testTarget(name: "IlhaeraCoreTests", dependencies: ["IlhaeraCore"], path: "Tests/IlhaeraCoreTests"),
    ]
)
