// hp-rank-helper: the macOS half of the uploader's opt-in rank reader (Heroesprofile.Uploader.RankCapture,
// MacCaptureBackend / MacTextReader). Shipped next to the app's executable in the macOS Ranks builds only;
// release-desktop.yml builds it with swiftc. The app runs it as a separate program because Apple's screen
// capture and text reading are Swift / Objective-C APIs.
//
//   hp-rank-helper capture     Screenshot of the Heroes of the Storm window (only that window), as a PNG on
//                              standard output. macOS 14 or later (ScreenCaptureKit's SCScreenshotManager).
//   hp-rank-helper ocr         Reads the PNG on standard input with Vision; prints what it read, one reading
//                              per line - the whole line first, then each part's alternatives.
//   hp-rank-helper permission  Whether the app may record the screen; asks macOS (which prompts the user) if
//                              not yet decided.
//
// Exit codes: 0 done, 1 failed, 2 the game's window isn't open, 3 no Screen Recording permission, 4 this macOS
// is too old. Nothing is written anywhere but standard output.

import CoreGraphics
import Foundation
import ImageIO
import ScreenCaptureKit
import UniformTypeIdentifiers
import Vision

let gameTitle = "Heroes of the Storm"

enum Exit: Int32 {
    case ok = 0, failed = 1, noWindow = 2, noPermission = 3, tooOld = 4
}

func finish(_ code: Exit, _ message: String? = nil) -> Never {
    if let message {
        FileHandle.standardError.write(Data((message + "\n").utf8))
    }
    exit(code.rawValue)
}

func pngData(_ image: CGImage) -> Data? {
    let data = NSMutableData()
    guard let destination = CGImageDestinationCreateWithData(data, UTType.png.identifier as CFString, 1, nil) else {
        return nil
    }
    CGImageDestinationAddImage(destination, image, nil)
    return CGImageDestinationFinalize(destination) ? data as Data : nil
}

func capture() async -> Never {
    guard #available(macOS 14.0, *) else {
        finish(.tooOld, "capturing a window needs macOS 14 or later")
    }
    guard CGPreflightScreenCaptureAccess() else {
        finish(.noPermission, "Screen Recording isn't allowed")
    }
    do {
        let content = try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: true)
        let candidates = content.windows.filter { window in
            let titled = window.title?.hasPrefix(gameTitle) ?? false
            let app = window.owningApplication?.applicationName.hasPrefix(gameTitle) ?? false
            return (titled || app) && window.frame.width >= 320 && window.frame.height >= 240
        }
        guard let window = candidates.max(by: { $0.frame.width * $0.frame.height < $1.frame.width * $1.frame.height }) else {
            finish(.noWindow)
        }

        let filter = SCContentFilter(desktopIndependentWindow: window)
        let scale = CGFloat(filter.pointPixelScale)
        let configuration = SCStreamConfiguration()
        configuration.width = Int(window.frame.width * scale)
        configuration.height = Int(window.frame.height * scale)
        configuration.showsCursor = false
        let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: configuration)

        guard let data = pngData(image) else {
            finish(.failed, "couldn't encode the capture")
        }
        FileHandle.standardOutput.write(data)
        finish(.ok)
    } catch {
        finish(.failed, "capture failed: \(error)")
    }
}

func ocr() -> Never {
    let input = FileHandle.standardInput.readDataToEndOfFile()
    guard let source = CGImageSourceCreateWithData(input as CFData, nil),
          let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else {
        finish(.failed, "standard input isn't an image")
    }

    let request = VNRecognizeTextRequest()
    request.recognitionLevel = .accurate
    // Player names aren't words: correcting them towards a dictionary only makes them worse.
    request.usesLanguageCorrection = false
    if #available(macOS 13.0, *) {
        request.automaticallyDetectsLanguage = true
    }
    if let languages = try? request.supportedRecognitionLanguages() {
        request.recognitionLanguages = languages
    }
    do {
        try VNImageRequestHandler(cgImage: image, options: [:]).perform([request])
    } catch {
        finish(.failed, "text reading failed: \(error)")
    }

    let pieces = (request.results ?? []).sorted { $0.boundingBox.minX < $1.boundingBox.minX }
    var readings: [String] = []
    let whole = pieces.compactMap { $0.topCandidates(1).first?.string }.joined(separator: " ")
    if !whole.isEmpty {
        readings.append(whole)
    }
    for piece in pieces {
        for candidate in piece.topCandidates(3) where !readings.contains(candidate.string) {
            readings.append(candidate.string)
        }
    }
    print(readings.joined(separator: "\n"))
    finish(.ok)
}

func permission() -> Never {
    if CGPreflightScreenCaptureAccess() {
        print("granted")
        finish(.ok)
    }
    let granted = CGRequestScreenCaptureAccess()
    print(granted ? "granted" : "denied")
    finish(granted ? .ok : .noPermission)
}

switch CommandLine.arguments.dropFirst().first {
case "capture":
    Task {
        await capture()
    }
    dispatchMain()
case "ocr":
    ocr()
case "permission":
    permission()
default:
    finish(.failed, "usage: hp-rank-helper capture | ocr | permission")
}
