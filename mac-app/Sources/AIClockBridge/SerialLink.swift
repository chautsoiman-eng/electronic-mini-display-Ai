import Foundation

/// Main-run-loop-owned USB transport. Nonblocking I/O, one frame per 125 ms,
/// bounded command queue and two replaceable image transfers. No UART burst
/// may overrun the firmware's 2 KB RX buffer during a screen redraw.
final class SerialLink {
    static weak var current: SerialLink?
    private let service: StatusService
    private let netMonitor: NetSpeedMonitor
    private let nowPlaying: NowPlayingMonitor
    private var fd: Int32 = -1
    private var linked = false
    private var portPath = ""
    private var openedAt = Date.distantPast
    private var lastScanAt = Date.distantPast
    private var lastReplyAt = Date.distantPast
    private var scanIndex = 0
    private var timer: Timer?
    private var splitter = SerialLineSplitter()
    private var pending = SerialPendingWrite()
    private var due = [Date](repeating: .distantPast, count: 5)
    private var commands: [Data] = []
    private var bulk: [(kind: String, rev: Int, frames: [Data])] = []
    private var info: Data?
    private var infoAt = Date.distantPast
    private(set) var sessionID = UUID()
    private var assembler: SpriteAssembler?
    private var spriteCompletion: ((Result<Data, Error>) -> Void)?
    private var spriteDeadline = Date.distantPast
    private var spriteCache: [String: (rev: Int, width: Int, height: Int, data: Data)] = [:]

    init(service: StatusService, netMonitor: NetSpeedMonitor, nowPlaying: NowPlayingMonitor) {
        self.service = service; self.netMonitor = netMonitor; self.nowPlaying = nowPlaying
    }
    func start() {
        SerialLink.current = self
        let t = Timer(timeInterval: 0.125, repeats: true) { [weak self] _ in self?.tick() }
        timer = t
        RunLoop.main.add(t, forMode: .common)
    }
    deinit { timer?.invalidate(); if fd >= 0 { close(fd) } }
    var isLinked: Bool { linked }
    var latestInfo: Data? { linked && Date().timeIntervalSince(infoAt) < 6 ? info : nil }

    /// True means queued, not acknowledged: #CMD has no ACK in this protocol.
    func trySendCommand(_ fields: [String: Any]) -> Bool {
        guard linked, commands.count < 32, let frame = SerialProtocol.frame("CMD", fields: fields) else { return false }
        commands.append(frame)
        return true
    }
    func requestSprite(slot: String, completion: @escaping (Result<Data, Error>) -> Void) {
        guard linked, let data = latestInfo, let info = DeviceClient.parseInfo(data),
              slot == "claude" || slot == "codex" else { completion(.failure(Self.transferError)); return }
        let w = slot == "claude" ? info.claudeW : info.codexW
        let h = slot == "claude" ? info.claudeH : info.codexH
        if let cached = spriteCache[slot], cached.rev == info.spriteRev, cached.width == w, cached.height == h {
            completion(.success(cached.data)); return
        }
        guard spriteCompletion == nil, trySendCommand(["sprite_dump": slot]) else {
            completion(.failure(Self.transferError)); return
        }
        assembler = SpriteAssembler(slot: slot, rev: info.spriteRev, width: w, height: h)
        spriteCompletion = completion
        spriteDeadline = Date().addingTimeInterval(60) // up to eight frames at 384 B / 60 ms
    }
    private static var transferError: Error {
        NSError(domain: "SerialLink", code: 1, userInfo: [NSLocalizedDescriptionKey: "USB 传输中断、忙碌或超时，请重试"])
    }
    private func finishSprite(_ result: Result<Data, Error>) {
        let callback = spriteCompletion
        spriteCompletion = nil; assembler = nil
        callback?(result)
    }
    func tick(now: Date = Date()) {
        if spriteCompletion != nil, now > spriteDeadline { finishSprite(.failure(Self.transferError)) }
        if fd < 0 {
            if now.timeIntervalSince(lastScanAt) >= 5 { lastScanAt = now; scanAndOpen() }
            return
        }
        readPending()
        guard fd >= 0 else { return }
        if linked && now.timeIntervalSince(lastReplyAt) > 12 || !linked && now.timeIntervalSince(openedAt) > 30 {
            closePort(); return
        }
        if !pending.isEmpty {
            if now.timeIntervalSince(pending.started) > 2 { closePort() } else { flushPending() }
            return
        }
        if !linked {
            if now >= due[0] { due[0] = now.addingTimeInterval(3); send(Data("#HELLO\n".utf8), now: now) }
            return
        }
        // Periodic data takes priority over bulk; commands cannot starve the
        // heartbeat. Earliest deadline gives each feed its turn after a link.
        if let i = due.indices.filter({ due[$0] <= now }).min(by: { due[$0] < due[$1] }) {
            let intervals: [TimeInterval] = [3, 5, 2, 60, 1]
            due[i] = now.addingTimeInterval(intervals[i])
            var frame: Data?
            switch i {
            case 0: frame = Data("#HELLO\n".utf8)
            case 1: frame = SerialProtocol.frame("STATUS", service.snapshot().jsonData())
            case 2:
                let stats = SystemStatsMonitor.shared.snapshot()
                frame = SerialProtocol.frame("NET", netMonitor.jsonData(cpu: stats.cpu, mem: stats.mem))
            case 3: frame = SerialProtocol.frame("TIME", fields: ["epoch": Int(now.timeIntervalSince1970)])
            default: frame = SerialProtocol.frame("MUSIC", nowPlaying.jsonData(maxTextLength: 80))
            }
            if let frame { send(frame, now: now) }
            return
        }
        if !commands.isEmpty { send(commands.removeFirst(), now: now); return }
        while !bulk.isEmpty {
            // A track change invalidates queued rows, even before the next #NEED.
            guard nowPlaying.serialImage(kind: bulk[0].kind).rev == bulk[0].rev else { bulk.removeFirst(); continue }
            let frame = bulk[0].frames.removeFirst()
            if bulk[0].frames.isEmpty { bulk.removeFirst() }
            send(frame, now: now)
            return
        }
    }
    private func handle(_ line: String) {
        if let obj = SerialProtocol.object(line, tag: "DEVICE"), obj["name"] as? String == "aiclock" {
            lastReplyAt = Date()
            if !linked { linked = true; due = [Date](repeating: .distantPast, count: 5) }
            return
        }
        guard linked else { return }
        if let obj = SerialProtocol.object(line, tag: "INFO"), obj["mode"] is String, obj["sprite_rev"] is Int,
           let data = try? JSONSerialization.data(withJSONObject: obj) {
            info = data; infoAt = Date(); lastReplyAt = infoAt
            let rev = obj["sprite_rev"] as? Int
            spriteCache = spriteCache.filter { $0.value.rev == rev }
            if let a = assembler, a.rev != rev { finishSprite(.failure(Self.transferError)) }
        } else if let obj = SerialProtocol.object(line, tag: "NEED"), let kind = obj["k"] as? String,
                  kind == "cover" || kind == "text", let requested = obj["rev"] as? Int, requested >= 0 {
            let image = nowPlaying.serialImage(kind: kind)
            guard image.rev == requested else { return } // next #MUSIC advertises current revision
            // A repeated request must not continually restart a slow transfer.
            if bulk.contains(where: { $0.kind == kind && $0.rev == image.rev }) { return }
            bulk.removeAll { $0.kind == kind }
            let frames = SerialProtocol.imageFrames(kind: kind, rev: image.rev, data: image.data)
            if !frames.isEmpty { bulk.append((kind, image.rev, frames)) }
        } else if line.hasPrefix("#SPR "), let a = assembler, let data = assembler?.accept(line) {
            spriteCache[a.slot] = (a.rev, a.width, a.height, data)
            finishSprite(.success(data))
        }
    }
    private func scanAndOpen() {
        let names = ((try? FileManager.default.contentsOfDirectory(atPath: "/dev")) ?? []).filter {
            $0.hasPrefix("cu.usbserial") || $0.hasPrefix("cu.wchusbserial") || $0.hasPrefix("cu.SLAB_USBtoUART")
        }.sorted()
        guard !names.isEmpty else { return }
        for _ in names.indices {
            let name = names[scanIndex % names.count]; scanIndex = (scanIndex + 1) % names.count
            if openPort("/dev/" + name) { return }
        }
    }
    func openPort(_ path: String) -> Bool {
        let f = open(path, O_RDWR | O_NOCTTY | O_NONBLOCK)
        guard f >= 0 else { return false }
        var tio = termios()
        guard tcgetattr(f, &tio) == 0 else { close(f); return false }
        cfmakeraw(&tio)
        cfsetspeed(&tio, speed_t(B115200))
        tio.c_cflag |= tcflag_t(CLOCAL | CREAD)
        tio.c_cflag &= ~tcflag_t(HUPCL)
        guard tcsetattr(f, TCSANOW, &tio) == 0 else { close(f); return false }
        var bits: Int32 = 0x006 // DTR + RTS low: let the ESP run
        _ = ioctl(f, 0x8004_746B, &bits)
        fd = f; portPath = path; openedAt = Date(); sessionID = UUID()
        due = [Date](repeating: .distantPast, count: 5)
        return true
    }
    private func closePort() {
        if fd >= 0 { close(fd) }
        fd = -1; linked = false; portPath = ""; info = nil; infoAt = .distantPast
        splitter = SerialLineSplitter(); pending = SerialPendingWrite()
        commands.removeAll(); bulk.removeAll(); spriteCache.removeAll(); sessionID = UUID()
        finishSprite(.failure(Self.transferError))
    }
    private func send(_ data: Data, now: Date) {
        pending.begin(data, now: now)
        flushPending()
    }
    private func flushPending() {
        let data = pending.remaining
        let n = data.withUnsafeBytes { write(fd, $0.baseAddress, data.count) }
        if n > 0 { pending.advance(n) }
        else if n < 0 && errno != EAGAIN && errno != EINTR { closePort() }
    }
    private func readPending() {
        var bytes = [UInt8](repeating: 0, count: 4096)
        // Bound work per tick so noisy serial devices cannot block the UI.
        for _ in 0..<16 {
            let n = read(fd, &bytes, bytes.count)
            if n > 0 {
                for line in splitter.push(Data(bytes.prefix(n))) { handle(line) }
            } else {
                if n == 0 || (n < 0 && errno != EAGAIN && errno != EINTR) { closePort() }
                return
            }
        }
    }
}
