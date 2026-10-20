// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "TrackpadTurn",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "TrackpadTurn", targets: ["TrackpadTurn"]),
    ],
    targets: [
        // Pure gesture → game-input logic. No AppKit/CoreGraphics, so it is unit-testable.
        .target(name: "TrackpadTurnCore"),
        // Menu bar app: event tap, input synthesis, addon installer, settings UI.
        .executableTarget(
            name: "TrackpadTurn",
            dependencies: ["TrackpadTurnCore"]
        ),
        .testTarget(
            name: "TrackpadTurnCoreTests",
            dependencies: ["TrackpadTurnCore"]
        ),
    ]
)
