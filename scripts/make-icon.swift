// Renders Resources/AppIcon.icns from an SF Symbol, so the icon needs no design files.
// Usage: swift scripts/make-icon.swift   (from the repository root; needs `iconutil`)
import AppKit

let symbolName = "rectangle.and.hand.point.up.left.filled"
let iconset = URL(filePath: "build/AppIcon.iconset", directoryHint: .isDirectory)
let output = URL(filePath: "Resources/AppIcon.icns")

func renderIcon(pixels: Int) -> Data {
    let size = CGFloat(pixels)
    let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels,
        bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
        colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
    )!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)

    // macOS icon grid: the rounded square fills ~80% of the canvas.
    let inset = size * 0.1
    let tile = NSRect(x: inset, y: inset, width: size - inset * 2, height: size - inset * 2)
    let tilePath = NSBezierPath(roundedRect: tile, xRadius: tile.width * 0.225, yRadius: tile.width * 0.225)
    NSGradient(
        starting: NSColor(calibratedRed: 0.20, green: 0.55, blue: 0.98, alpha: 1),
        ending: NSColor(calibratedRed: 0.36, green: 0.22, blue: 0.85, alpha: 1)
    )!.draw(in: tilePath, angle: -60)

    let configuration = NSImage.SymbolConfiguration(pointSize: tile.width * 0.5, weight: .medium)
        .applying(NSImage.SymbolConfiguration(paletteColors: [.white]))
    if let symbol = NSImage(systemSymbolName: symbolName, accessibilityDescription: nil)?
        .withSymbolConfiguration(configuration) {
        let symbolSize = symbol.size
        let origin = NSPoint(x: tile.midX - symbolSize.width / 2, y: tile.midY - symbolSize.height / 2)
        symbol.draw(in: NSRect(origin: origin, size: symbolSize))
    }

    NSGraphicsContext.restoreGraphicsState()
    return bitmap.representation(using: .png, properties: [:])!
}

try? FileManager.default.removeItem(at: iconset)
try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)
for points in [16, 32, 128, 256, 512] {
    try renderIcon(pixels: points).write(to: iconset.appending(path: "icon_\(points)x\(points).png"))
    try renderIcon(pixels: points * 2).write(to: iconset.appending(path: "icon_\(points)x\(points)@2x.png"))
}

let iconutil = Process()
iconutil.executableURL = URL(filePath: "/usr/bin/iconutil")
iconutil.arguments = ["-c", "icns", iconset.path, "-o", output.path]
try iconutil.run()
iconutil.waitUntilExit()
print(iconutil.terminationStatus == 0 ? "Wrote \(output.path)" : "iconutil failed")
