// Renders the app icons from an SF Symbol, so they need no design files:
// Resources/AppIcon.icns (Mac) and Windows/src/TrackpadTurn.Windows/Resources/TrackpadTurn.ico.
// Usage: swift scripts/make-icon.swift [--windows-only]   (from the repository root; needs `iconutil`)
import AppKit

let symbolName = "rectangle.and.hand.point.up.left.filled"
let iconset = URL(filePath: "build/AppIcon.iconset", directoryHint: .isDirectory)
let output = URL(filePath: "Resources/AppIcon.icns")
let windowsOutput = URL(filePath: "Windows/src/TrackpadTurn.Windows/Resources/TrackpadTurn.ico")

/// - Parameter insetFraction: empty margin around the tile. macOS icons sit on a grid with ~10%;
///   Windows icons fill the canvas, so a tray icon stays legible at 16 px.
/// - Parameter symbol: at tray sizes (16–24 px) the full symbol blurs, so only the hand is drawn, larger.
func renderBitmap(
    pixels: Int, insetFraction: CGFloat, symbol: String = symbolName, symbolScale: CGFloat = 0.5,
    weight: NSFont.Weight = .medium
) -> NSBitmapImageRep {
    let size = CGFloat(pixels)
    let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels,
        bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
        colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
    )!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)

    let inset = size * insetFraction
    let tile = NSRect(x: inset, y: inset, width: size - inset * 2, height: size - inset * 2)
    let tilePath = NSBezierPath(roundedRect: tile, xRadius: tile.width * 0.225, yRadius: tile.width * 0.225)
    NSGradient(
        starting: NSColor(calibratedRed: 0.20, green: 0.55, blue: 0.98, alpha: 1),
        ending: NSColor(calibratedRed: 0.36, green: 0.22, blue: 0.85, alpha: 1)
    )!.draw(in: tilePath, angle: -60)

    let configuration = NSImage.SymbolConfiguration(pointSize: tile.width * symbolScale, weight: weight)
        .applying(NSImage.SymbolConfiguration(paletteColors: [.white]))
    if let symbol = NSImage(systemSymbolName: symbol, accessibilityDescription: nil)?
        .withSymbolConfiguration(configuration) {
        let symbolSize = symbol.size
        let origin = NSPoint(x: tile.midX - symbolSize.width / 2, y: tile.midY - symbolSize.height / 2)
        symbol.draw(in: NSRect(origin: origin, size: symbolSize))
    }

    NSGraphicsContext.restoreGraphicsState()
    return bitmap
}

func renderIcon(pixels: Int) -> Data {
    // macOS icon grid: the rounded square fills ~80% of the canvas.
    renderBitmap(pixels: pixels, insetFraction: 0.1).representation(using: .png, properties: [:])!
}

/// A Windows .ico with 32-bit BGRA bitmap entries: the format every Windows API and
/// System.Drawing.Icon read, unlike PNG-compressed entries.
func windowsIcon(sizes: [Int]) -> Data {
    var entries: [(size: Int, dib: Data)] = []
    for size in sizes {
        let bitmap = size <= 24
            ? renderBitmap(
                pixels: size, insetFraction: 0.0, symbol: "hand.point.up.left.fill", symbolScale: 0.62, weight: .semibold
            )
            : renderBitmap(pixels: size, insetFraction: 0.03)
        var dib = Data()
        func append<T: FixedWidthInteger>(_ value: T) { withUnsafeBytes(of: value.littleEndian) { dib.append(contentsOf: $0) } }
        // BITMAPINFOHEADER; the height counts the colour rows plus the AND-mask rows.
        append(UInt32(40)); append(Int32(size)); append(Int32(size * 2)); append(UInt16(1)); append(UInt16(32))
        append(UInt32(0)); append(UInt32(0)); append(Int32(0)); append(Int32(0)); append(UInt32(0)); append(UInt32(0))
        for row in (0..<size).reversed() { // bottom-up
            for column in 0..<size {
                let color = bitmap.colorAt(x: column, y: row)?.usingColorSpace(.deviceRGB) ?? .clear
                let alpha = color.alphaComponent
                // Straight (not premultiplied) BGRA.
                dib.append(UInt8((color.blueComponent * 255).rounded()))
                dib.append(UInt8((color.greenComponent * 255).rounded()))
                dib.append(UInt8((color.redComponent * 255).rounded()))
                dib.append(UInt8((alpha * 255).rounded()))
            }
        }
        // AND mask: all zero (the alpha channel decides), rows padded to 32 bits.
        dib.append(Data(count: ((size + 31) / 32) * 4 * size))
        entries.append((size, dib))
    }

    var ico = Data()
    func append<T: FixedWidthInteger>(_ value: T) { withUnsafeBytes(of: value.littleEndian) { ico.append(contentsOf: $0) } }
    append(UInt16(0)); append(UInt16(1)); append(UInt16(entries.count)) // ICONDIR
    var offset = 6 + 16 * entries.count
    for entry in entries { // ICONDIRENTRY; 0 means 256
        ico.append(UInt8(entry.size % 256)); ico.append(UInt8(entry.size % 256)); ico.append(0); ico.append(0)
        append(UInt16(1)); append(UInt16(32)); append(UInt32(entry.dib.count)); append(UInt32(offset))
        offset += entry.dib.count
    }
    for entry in entries { ico.append(entry.dib) }
    return ico
}

try windowsIcon(sizes: [16, 20, 24, 32, 40, 48, 64, 256]).write(to: windowsOutput)
print("Wrote \(windowsOutput.path)")
if CommandLine.arguments.contains("--windows-only") { exit(0) }

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
