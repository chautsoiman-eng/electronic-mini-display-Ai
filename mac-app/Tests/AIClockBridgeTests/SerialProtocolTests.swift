import XCTest
import Darwin
@testable import AIClockBridge

final class SerialProtocolTests: XCTestCase {
    func testFrameBoundariesAndEscapedText() throws {
        XCTAssertNil(SerialProtocol.frame("MUSIC", Data("{}\n#CMD {}".utf8)))
        XCTAssertNil(SerialProtocol.frame("STATUS", Data(repeating: 65, count: 1500)))
        let f = try XCTUnwrap(SerialProtocol.frame("CMD", fields: ["display": "中文\n文字"]))
        XCTAssertEqual(f.filter { $0 == 10 }.count, 1)
        XCTAssertLessThanOrEqual(f.count, 1500)
    }
    func testImagesRoundTripEveryRowAndRevision() throws {
        for (kind, w, h) in [("cover", 128, 128), ("text", 232, 44)] {
            let source = Data((0..<(w * h * 2)).map { UInt8($0 % 251) })
            let frames = SerialProtocol.imageFrames(kind: kind, rev: 17, data: source)
            XCTAssertFalse(frames.isEmpty)
            var result = Data(), expectedRow = 0
            for f in frames {
                XCTAssertLessThanOrEqual(f.count, 1500)
                let obj = try XCTUnwrap(SerialProtocol.object(String(decoding: f, as: UTF8.self), tag: "IMG"))
                XCTAssertEqual(obj["row"] as? Int, expectedRow)
                XCTAssertEqual(obj["rev"] as? Int, 17)
                for row in (try XCTUnwrap(obj["d"] as? String)).split(separator: ",") {
                    let bytes = try XCTUnwrap(Data(base64Encoded: String(row)))
                    XCTAssertEqual(bytes.count, w * 2)
                    result.append(bytes); expectedRow += 1
                }
            }
            XCTAssertEqual(result, source)
            XCTAssertEqual(expectedRow, h)
        }
        XCTAssertTrue(SerialProtocol.imageFrames(kind: "cover", rev: 1, data: Data([0])).isEmpty)
    }
    func testSplitterFragmentationCRLFAndOversizedNoise() {
        var splitter = SerialLineSplitter()
        XCTAssertEqual(splitter.push(Data("#INF".utf8)), [])
        XCTAssertEqual(splitter.push(Data("O {}\r\nlog\n#SPR".utf8)), ["#INFO {}", "log"])
        XCTAssertEqual(splitter.push(Data(" {}\n".utf8)), ["#SPR {}"])
        XCTAssertEqual(splitter.push(Data(repeating: 65, count: 17000) + Data("#DEVICE {}\n#INFO {}\n".utf8)), ["#INFO {}"])
    }
    private func chunk(_ bytes: Data, off: Int, total: Int, rev: Int = 4, slot: String = "claude") -> String {
        String(decoding: SerialProtocol.frame("SPR", fields: ["slot": slot, "rev": rev,
            "total": total, "off": off, "d": bytes.base64EncodedString()])!, as: UTF8.self)
    }
    func testSpriteRejectsGapsRevisionChangesAndBadGeometry() {
        var a = SpriteAssembler(slot: "claude", rev: 4, width: 2, height: 1)
        let bytes = Data([1, 0, 1, 0, 2])
        XCTAssertNil(a.accept(chunk(bytes.prefix(2), off: 0, total: 5)))
        XCTAssertNil(a.accept(chunk(bytes.suffix(2), off: 3, total: 5)))
        XCTAssertNil(a.accept(chunk(bytes.suffix(3), off: 2, total: 5)))
        XCTAssertNil(a.accept(chunk(bytes, off: 0, total: 5, rev: 5)))
        XCTAssertNil(a.accept(chunk(Data([2, 0, 1, 0, 2]), off: 0, total: 5)))
        XCTAssertNil(a.accept(chunk(bytes, off: 0, total: Int.max)))
        XCTAssertNil(a.accept("#SPR {\"slot\":\"claude\",\"d\":\"!\"}"))
        XCTAssertNil(a.accept(chunk(bytes.prefix(2), off: 0, total: 5)))
        XCTAssertEqual(a.accept(chunk(bytes.suffix(3), off: 2, total: 5)), bytes)
    }
    func testShortWritesKeepFrameIntact() {
        var writer = SerialPendingWrite()
        let frame = Data("#MUSIC {}\n".utf8), now = Date()
        writer.begin(frame, now: now)
        writer.advance(3)
        XCTAssertEqual(writer.remaining, Data(frame.dropFirst(3)))
        writer.advance(0) // EAGAIN: leave remaining bytes untouched
        XCTAssertFalse(writer.isEmpty)
        XCTAssertEqual(writer.started, now)
        writer.advance(frame.count - 3)
        XCTAssertTrue(writer.isEmpty)
    }

    /// Exercises the actual nonblocking SerialLink using an OS pseudo-terminal,
    /// without starting media polling, using WiFi, or attaching a USB device.
    func testPseudoTerminalMusicSpriteCacheDisconnectAndReconnect() throws {
        var master: Int32 = -1, slave: Int32 = -1
        var path = [CChar](repeating: 0, count: 256)
        guard openpty(&master, &slave, &path, nil, nil) == 0 else {
            throw XCTSkip("Pseudo-terminals unavailable in this test environment")
        }
        defer { if master >= 0 { close(master) }; close(slave) }
        _ = fcntl(master, F_SETFL, O_NONBLOCK)
        let music = NowPlayingMonitor()
        let link = SerialLink(service: StatusService(), netMonitor: NetSpeedMonitor(), nowPlaying: music)
        XCTAssertTrue(link.openPort(String(cString: path)))
        func device(_ text: String) { _ = text.withCString { write(master, $0, strlen($0)) } }
        func receive() -> String {
            var result = Data(), bytes = [UInt8](repeating: 0, count: 8192)
            while true {
                let n = read(master, &bytes, bytes.count)
                if n <= 0 { break }
                result.append(contentsOf: bytes.prefix(n))
            }
            return String(decoding: result, as: UTF8.self)
        }
        device("#DEVICE {\"name\":\"wrong\"}\n")
        link.tick(); XCTAssertFalse(link.isLinked); _ = receive()
        device("#DEVICE {\"name\":\"aiclock\",\"fw\":\"test\"}\n")
        let now = Date()
        var output = ""
        for i in 0..<12 { link.tick(now: now.addingTimeInterval(Double(i) * 0.125)); output += receive() }
        XCTAssertTrue(link.isLinked)
        XCTAssertTrue(output.contains("#MUSIC "))
        XCTAssertTrue(output.contains("#TIME "))
        device("#INFO {\"mode\":\"claude\",\"sprite_rev\":4,\"claude\":{\"w\":2,\"h\":1}}\n")
        link.tick(); XCTAssertNotNil(link.latestInfo)
        var first: Data?
        link.requestSprite(slot: "claude") { first = try? $0.get() }
        for _ in 0..<3 { link.tick() }
        XCTAssertTrue(receive().contains("sprite_dump"))
        let sprite = Data([1, 0, 1, 0, 2])
        device(chunk(sprite, off: 0, total: sprite.count))
        link.tick(); XCTAssertEqual(first, sprite)
        var cached: Data?
        link.requestSprite(slot: "claude") { cached = try? $0.get() }
        XCTAssertEqual(cached, sprite)
        XCTAssertFalse(receive().contains("sprite_dump"))
        let image = music.serialImage(kind: "text")
        device("#NEED {\"k\":\"text\",\"rev\":\(image.rev)}\n")
        for _ in 0..<3 { link.tick() }
        XCTAssertTrue(receive().contains("#IMG "))
        var cancelled = false
        device("#INFO {\"mode\":\"claude\",\"sprite_rev\":5}\n")
        link.tick()
        link.requestSprite(slot: "claude") { if case .failure = $0 { cancelled = true } }
        let oldSession = link.sessionID
        close(master); master = -1
        link.tick()
        XCTAssertFalse(link.isLinked)
        XCTAssertNil(link.latestInfo)
        XCTAssertTrue(cancelled)
        XCTAssertNotEqual(link.sessionID, oldSession)
        // A new connection must handshake and must not reuse old #INFO/cache.
        var master2: Int32 = -1, slave2: Int32 = -1
        var path2 = [CChar](repeating: 0, count: 256)
        XCTAssertEqual(openpty(&master2, &slave2, &path2, nil, nil), 0)
        defer { close(master2); close(slave2) }
        XCTAssertTrue(link.openPort(String(cString: path2)))
        XCTAssertFalse(link.isLinked)
        XCTAssertNil(link.latestInfo)
        let hello = "#DEVICE {\"name\":\"aiclock\"}\n"
        _ = hello.withCString { write(master2, $0, strlen($0)) }
        link.tick()
        XCTAssertTrue(link.isLinked)
        XCTAssertNil(link.latestInfo)
        link.tick(now: Date().addingTimeInterval(13))
        XCTAssertFalse(link.isLinked) // silent device heartbeat deadline
    }
}
