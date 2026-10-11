import Foundation

/// Shared firmware limits: 1600-byte line buffer; RGB565 rows are big endian.
enum SerialProtocol {
    static let maxFrameBytes = 1500
    static func frame(_ tag: String, _ json: Data) -> Data? {
        guard !json.contains(10), !json.contains(13) else { return nil }
        let data = Data("#\(tag) ".utf8) + json + Data([10])
        return data.count <= maxFrameBytes ? data : nil
    }
    static func frame(_ tag: String, fields: [String: Any]) -> Data? {
        guard let json = try? JSONSerialization.data(withJSONObject: fields) else { return nil }
        return frame(tag, json)
    }
    static func object(_ line: String, tag: String) -> [String: Any]? {
        let prefix = "#\(tag) "
        guard line.hasPrefix(prefix) else { return nil }
        return (try? JSONSerialization.jsonObject(with: Data(line.dropFirst(prefix.count).utf8))) as? [String: Any]
    }
    static func imageFrames(kind: String, rev: Int, data: Data) -> [Data] {
        let w: Int, h: Int
        switch kind {
        case "cover": (w, h) = (128, 128)
        case "text": (w, h) = (232, 44)
        default: return []
        }
        guard rev >= 0, data.count == w * h * 2 else { return [] }
        var frames: [Data] = [], row = 0
        while row < h {
            var parts: [String] = [], best: Data?, take = 0
            for r in row..<h {
                parts.append(data.subdata(in: (r * w * 2)..<((r + 1) * w * 2)).base64EncodedString())
                guard let f = frame("IMG", fields: ["k": kind, "rev": rev, "row": row,
                                                      "d": parts.joined(separator: ",")]) else { break }
                best = f
                take = parts.count
            }
            guard let best else { return [] }
            frames.append(best)
            row += take
        }
        return frames
    }
}

struct SerialLineSplitter {
    private var buffer = Data()
    private var dropping = false
    mutating func push(_ bytes: Data) -> [String] {
        var lines: [String] = []
        for b in bytes {
            if b == 10 {
                if !dropping, let line = String(data: buffer, encoding: .utf8) {
                    lines.append(line.trimmingCharacters(in: .whitespacesAndNewlines))
                }
                buffer.removeAll(keepingCapacity: true)
                dropping = false
            } else if !dropping {
                if buffer.count < 16384 { buffer.append(b) }
                else { buffer.removeAll(keepingCapacity: true); dropping = true }
            }
        }
        return lines
    }
}

/// One in-order dump, tied to the requested revision and geometry. Invalid
/// transfers never enter the cache. A new offset-zero chunk can restart it.
struct SpriteAssembler {
    let slot: String
    let rev: Int
    let width: Int
    let height: Int
    private var buffer = Data()
    private var total = 0
    init(slot: String, rev: Int, width: Int, height: Int) {
        self.slot = slot; self.rev = rev; self.width = width; self.height = height
    }
    mutating func accept(_ line: String) -> Data? {
        guard let obj = SerialProtocol.object(line, tag: "SPR"), obj["slot"] as? String == slot else { return nil }
        guard obj["rev"] as? Int == rev,
              let size = obj["total"] as? Int, size > 1, size <= 1 + 8 * 120 * 120 * 2,
              let off = obj["off"] as? Int, off >= 0,
              let encoded = obj["d"] as? String, let chunk = Data(base64Encoded: encoded),
              !chunk.isEmpty, chunk.count <= 384,
              width > 0, width <= 120, height > 0, height <= 120 else {
            buffer.removeAll(); total = 0; return nil
        }
        if off == 0 { buffer.removeAll(); total = size }
        guard size == total, off == buffer.count, chunk.count <= size - off else {
            buffer.removeAll(); total = 0; return nil
        }
        buffer.append(chunk)
        guard buffer.count == total else { return nil }
        defer { buffer.removeAll(); total = 0 }
        guard let count = buffer.first, count > 0, count <= 8,
              buffer.count == 1 + Int(count) * width * height * 2 else { return nil }
        return buffer
    }
}

/// Keeps unwritten bytes intact across short writes and EAGAIN. No other
/// frame may be interleaved before this one is completely written.
struct SerialPendingWrite {
    private(set) var data = Data()
    private(set) var offset = 0
    private(set) var started = Date.distantPast
    var isEmpty: Bool { offset == data.count }
    mutating func begin(_ frame: Data, now: Date) {
        precondition(isEmpty)
        data = frame; offset = 0; started = now
    }
    mutating func advance(_ count: Int) { offset += count }
    var remaining: Data { Data(data.dropFirst(offset)) }
}
