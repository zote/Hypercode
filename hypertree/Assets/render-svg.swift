// Rasteriza um SVG num PNG quadrado com o próprio AppKit, sem dependência externa.
// Uso:  swift render-svg.swift <entrada.svg> <saida.png> <lado-em-px>
import AppKit

let args = CommandLine.arguments
guard args.count == 4, let side = Int(args[3]) else {
    FileHandle.standardError.write("uso: render-svg.swift <entrada.svg> <saida.png> <lado-em-px>\n".data(using: .utf8)!)
    exit(2)
}
guard let image = NSImage(contentsOfFile: args[1]) else {
    FileHandle.standardError.write("não consegui abrir \(args[1])\n".data(using: .utf8)!)
    exit(1)
}

let rep = NSBitmapImageRep(
    bitmapDataPlanes: nil, pixelsWide: side, pixelsHigh: side,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!

NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
image.draw(in: NSRect(x: 0, y: 0, width: side, height: side))
NSGraphicsContext.restoreGraphicsState()

try rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: args[2]))
