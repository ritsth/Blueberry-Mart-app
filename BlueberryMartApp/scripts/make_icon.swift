import AppKit
import Foundation

let bgColor = NSColor(srgbRed: 230.0/255.0, green: 244.0/255.0, blue: 254.0/255.0, alpha: 1.0) // #E6F4FE

func makeIcon(emoji: String, px: Int, frac: CGFloat, bg: NSColor?, vNudge: CGFloat, path: String) {
    guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px,
            bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
            colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { return }
    guard let ctx = NSGraphicsContext(bitmapImageRep: rep) else { return }
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = ctx
    let size = CGFloat(px)
    if let bg = bg {
        bg.setFill()
        NSRect(x: 0, y: 0, width: size, height: size).fill()
    }
    let fontSize = size * frac
    let font = NSFont(name: "Apple Color Emoji", size: fontSize)!
    let s = NSAttributedString(string: emoji, attributes: [.font: font])
    let sz = s.size()
    // center, with a small vertical nudge (emoji baseline sits slightly low)
    let origin = NSPoint(x: (size - sz.width) / 2.0,
                         y: (size - sz.height) / 2.0 + size * vNudge)
    s.draw(at: origin)
    NSGraphicsContext.restoreGraphicsState()
    guard let png = rep.representation(using: .png, properties: [:]) else { return }
    try! png.write(to: URL(fileURLWithPath: path))
    print("wrote \(path) (\(px)x\(px))")
}

let dir = "/Users/rs/Documents/Blueberry-Mart-app/BlueberryMartApp/assets"
let berry = "\u{1FAD0}" // 🫐

// Full app icon (iOS + fallback): blueberry on brand background
makeIcon(emoji: berry, px: 1024, frac: 0.66, bg: bgColor, vNudge: 0.0, path: "\(dir)/icon.png")
// Android adaptive foreground: transparent, smaller to stay inside the circular safe zone
makeIcon(emoji: berry, px: 512, frac: 0.52, bg: nil, vNudge: 0.0, path: "\(dir)/android-icon-foreground.png")
// Web favicon
makeIcon(emoji: berry, px: 48, frac: 0.72, bg: bgColor, vNudge: 0.0, path: "\(dir)/favicon.png")
