import AppKit
import Foundation

let W = 1024, H = 500
guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: W, pixelsHigh: H,
        bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
        colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0) else { exit(1) }
let ctx = NSGraphicsContext(bitmapImageRep: rep)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = ctx
let size = NSSize(width: W, height: H)
let bounds = NSRect(origin: .zero, size: size)

// Background: diagonal green gradient
let g1 = NSColor(srgbRed: 0.18, green: 0.49, blue: 0.20, alpha: 1) // #2E7D32
let g2 = NSColor(srgbRed: 0.11, green: 0.37, blue: 0.13, alpha: 1) // #1B5E20
NSGradient(colors: [g1, g2])!.draw(in: bounds, angle: -35)

// Soft light panel behind the berry for contrast
let panelColor = NSColor(srgbRed: 0.90, green: 0.96, blue: 1.0, alpha: 0.14) // tint of #E6F4FE
let panel = NSBezierPath(roundedRect: NSRect(x: 70, y: 110, width: 280, height: 280), xRadius: 56, yRadius: 56)
panelColor.setFill(); panel.fill()

// Blueberry emoji
let berry = NSAttributedString(string: "\u{1FAD0}", attributes: [.font: NSFont(name: "Apple Color Emoji", size: 210)!])
let bsz = berry.size()
berry.draw(at: NSPoint(x: 70 + (280 - bsz.width)/2, y: 110 + (280 - bsz.height)/2))

// Title
let title = NSAttributedString(string: "Blueberry Mart", attributes: [
    .font: NSFont.systemFont(ofSize: 88, weight: .bold),
    .foregroundColor: NSColor.white
])
title.draw(at: NSPoint(x: 400, y: 270))

// Tagline
let tagPara = NSMutableParagraphStyle(); tagPara.lineSpacing = 4
let tagline = NSAttributedString(string: "Fresh groceries & blueberries,\ndelivered to your door.", attributes: [
    .font: NSFont.systemFont(ofSize: 36, weight: .medium),
    .foregroundColor: NSColor(white: 1.0, alpha: 0.92),
    .paragraphStyle: tagPara
])
tagline.draw(at: NSPoint(x: 402, y: 120))

NSGraphicsContext.restoreGraphicsState()
let outDir = "/Users/rs/Documents/Blueberry-Mart-app/BlueberryMartApp/store-assets"
try? FileManager.default.createDirectory(atPath: outDir, withIntermediateDirectories: true)
let out = "\(outDir)/feature-graphic.png"
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: out))
print("wrote \(out) (\(W)x\(H))")
