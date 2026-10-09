using System.IO.Ports;
using System.Management;

namespace AIClockBridge;

// Wired (USB serial) transport to the clock — Windows port of
// mac-app/SerialLink.swift. For WiFi networks with client isolation, or for
// skipping WiFi setup entirely: finds the NodeMCU's CH340/CP210x COM port,
// handshakes (#HELLO -> #DEVICE), then pushes the payloads the device would
// otherwise poll over HTTP (see SerialProtocol for the frame list). With no
// WiFi at all, the clock's #INFO heartbeat, #NEED image requests and #SPR
// sprite dumps keep the tray, mirror and music page working. Device log
// lines are ignored.
//
// NOTE: Windows opens COM ports exclusively — quit the bridge before flashing
// with PlatformIO/esptool, or the upload cannot open the port.
sealed class SerialLink : IDisposable
{
    /// The running link, for DeviceClient's wired control fallback.
    public static SerialLink Current { get; private set; }

    // One frame per tick at most: feeds when due, otherwise one queued #IMG
    // chunk (~1.2 KB), which keeps the line below ~80% of 115200 baud so the
    // ESP8266's 2 KB RX buffer survives a slow screen draw.
    static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(125);
    static readonly TimeSpan InfoFreshFor = TimeSpan.FromSeconds(6);
    static readonly TimeSpan HelloEvery = TimeSpan.FromSeconds(3);
    static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan RescanEvery = TimeSpan.FromSeconds(5);

    readonly SerialFrameScheduler _scheduler;
    SerialLineSplitter _splitter = new();
    readonly object _ioLock = new();
    readonly CancellationTokenSource _stop = new();
    Thread _thread;

    SerialPort _port;
    DateTime _openedAt, _lastHelloAt, _lastScanAt = DateTime.MinValue;
    int _scanIndex;
    volatile bool _linked;
    volatile string _portName = "";
    volatile string _firmware = "";

    // wired-only extras
    readonly object _extrasLock = new();
    readonly List<(string Kind, byte[] Frame)> _bulk = new();
    string _infoJson;
    DateTime _infoAt = DateTime.MinValue;
    string _spriteSlot;
    SpriteAssembler _assembler;
    TaskCompletionSource<byte[]> _spriteTcs;

    /// Current music bitmap for #NEED: ("cover"|"text") -> (rev, RGB565 rows, width).
    public Func<string, (int Rev, byte[] Data, int Width)> ImageSource;

    public SerialLink(SerialFrameScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    public bool IsLinked => _linked;
    public string PortName => _portName;
    public string Firmware => _firmware;

    public void Start()
    {
        Current = this;
        _thread = new Thread(Run) { IsBackground = true, Name = "serial-link" };
        _thread.Start();
    }

    /// Sends one frame if the clock is linked; false otherwise (or on write failure).
    public bool TrySend(byte[] frame)
    {
        if (frame == null || !_linked) return false;
        return Write(frame);
    }

    /// Latest #INFO JSON (same shape as GET /api/info) while it is fresh.
    public string LatestInfoJson
    {
        get
        {
            lock (_extrasLock)
                return _linked && DateTime.UtcNow - _infoAt < InfoFreshFor ? _infoJson : null;
        }
    }

    /// Asks the clock to stream a sprite over USB (#CMD sprite_dump) and
    /// returns it in the /sprite/*/raw layout, or null on timeout/unplug.
    /// One transfer at a time; ~15-20 s for the built-in 6-frame sprites.
    public async Task<byte[]> RequestSpriteAsync(string slot, TimeSpan timeout)
    {
        TaskCompletionSource<byte[]> tcs;
        lock (_extrasLock)
        {
            if (_spriteTcs != null) return null; // another dump is running
            tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _spriteTcs = tcs;
            _spriteSlot = slot;
            _assembler = new SpriteAssembler(slot);
        }
        try
        {
            if (!TrySend(SerialProtocol.Command(spriteDump: slot))) return null;
            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            return done == tcs.Task ? tcs.Task.Result : null;
        }
        finally
        {
            lock (_extrasLock)
            {
                if (_spriteTcs == tcs)
                {
                    _spriteTcs = null;
                    _assembler = null;
                    _spriteSlot = null;
                }
            }
        }
    }

    void QueueImage(string kind, int rev)
    {
        var source = ImageSource;
        if (source == null) return;
        (int Rev, byte[] Data, int Width) img;
        try
        {
            img = source(kind);
        }
        catch (Exception)
        {
            return;
        }
        // the clock drops rows for a stale rev, so always send what we have now
        var frames = SerialProtocol.ImageFrames(kind, img.Rev, img.Data, img.Width);
        lock (_extrasLock)
        {
            _bulk.RemoveAll(b => b.Kind == kind); // a newer request supersedes
            foreach (var f in frames) _bulk.Add((kind, f));
        }
    }

    void HandleDeviceLine(string line)
    {
        var info = SerialProtocol.InfoJson(line);
        if (info != null)
        {
            lock (_extrasLock)
            {
                _infoJson = info;
                _infoAt = DateTime.UtcNow;
            }
            return;
        }
        if (SerialProtocol.TryParseNeed(line, out var kind, out var rev))
        {
            QueueImage(kind, rev);
            return;
        }
        if (line.StartsWith("#SPR ", StringComparison.Ordinal))
        {
            TaskCompletionSource<byte[]> tcs = null;
            byte[] done = null;
            lock (_extrasLock)
            {
                if (_assembler != null)
                {
                    done = _assembler.Accept(line);
                    if (done != null) tcs = _spriteTcs;
                }
            }
            tcs?.TrySetResult(done);
        }
    }

    void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                Step(DateTime.UtcNow);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"[serial] {e.Message}");
                ClosePort();
            }
            _stop.Token.WaitHandle.WaitOne(Tick);
        }
        ClosePort();
    }

    void Step(DateTime now)
    {
        if (_port == null)
        {
            if (now - _lastScanAt < RescanEvery) return;
            _lastScanAt = now;
            OpenNextCandidate(now);
            return;
        }
        ReadPending();
        if (_port == null) return;
        if (!_linked)
        {
            if (now - _openedAt > HandshakeTimeout)
            {
                ClosePort();
                return;
            }
            if (now - _lastHelloAt > HelloEvery)
            {
                _lastHelloAt = now;
                Write(SerialProtocol.Hello);
            }
            return;
        }
        var next = _scheduler.Next(now);
        if (next is { Frame: not null } f)
        {
            Write(f.Frame);
            return;
        }
        byte[] bulk = null;
        lock (_extrasLock)
        {
            if (_bulk.Count > 0)
            {
                bulk = _bulk[0].Frame;
                _bulk.RemoveAt(0);
            }
        }
        if (bulk != null) Write(bulk);
    }

    // MARK: - port lifecycle

    /// COM ports that look like a NodeMCU USB-UART. Falls back to every port
    /// when WMI is unavailable, so the link still works on locked-down PCs.
    static List<string> Candidates()
    {
        var ports = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (ManagementBaseObject o in searcher.Get())
            {
                using (o)
                {
                    var name = o["Name"] as string;
                    var port = SerialProtocol.PortFromPnpName(name);
                    if (port != null && SerialProtocol.LooksLikeClockPort(name)) ports.Add(port);
                }
            }
        }
        catch (Exception)
        {
            ports.AddRange(SerialPort.GetPortNames());
        }
        ports.Sort(StringComparer.OrdinalIgnoreCase);
        return ports;
    }

    void OpenNextCandidate(DateTime now)
    {
        var candidates = Candidates();
        for (int tried = 0; tried < candidates.Count; tried++)
        {
            var name = candidates[_scanIndex++ % candidates.Count];
            var port = new SerialPort(name, 115200)
            {
                // DTR/RTS low: the NodeMCU auto-reset circuit must let the ESP run.
                DtrEnable = false,
                RtsEnable = false,
                ReadTimeout = 50,
                WriteTimeout = 500,
            };
            try
            {
                port.Open();
            }
            catch (Exception)
            {
                port.Dispose(); // busy (e.g. a flasher or serial monitor) or vanished
                continue;
            }
            lock (_ioLock) _port = port;
            _splitter = new SerialLineSplitter();
            _portName = name;
            _linked = false;
            _openedAt = now;
            _lastHelloAt = DateTime.MinValue;
            Console.Error.WriteLine($"[serial] trying {name}");
            return;
        }
    }

    void ClosePort()
    {
        SerialPort port;
        lock (_ioLock)
        {
            port = _port;
            _port = null;
        }
        if (port == null) return;
        Console.Error.WriteLine($"[serial] closed {_portName}");
        try { port.Dispose(); } catch (Exception) { }
        _linked = false;
        _portName = "";
        _firmware = "";
        TaskCompletionSource<byte[]> pending;
        lock (_extrasLock)
        {
            _bulk.Clear();
            _infoJson = null;
            pending = _spriteTcs;
        }
        pending?.TrySetResult(null);
    }

    // MARK: - I/O

    bool Write(byte[] data)
    {
        lock (_ioLock)
        {
            if (_port == null) return false;
            try
            {
                _port.Write(data, 0, data.Length);
                return true;
            }
            catch (Exception)
            {
                // unplugged or write stalled; fall through to close outside the lock
            }
        }
        ClosePort();
        return false;
    }

    void ReadPending()
    {
        var buf = new byte[4096];
        while (true)
        {
            int n;
            lock (_ioLock)
            {
                if (_port == null) return;
                try
                {
                    if (_port.BytesToRead == 0) break;
                    n = _port.Read(buf, 0, buf.Length);
                }
                catch (TimeoutException)
                {
                    break;
                }
                catch (Exception)
                {
                    n = -1;
                }
            }
            if (n < 0)
            {
                ClosePort(); // unplugged
                return;
            }
            foreach (var line in _splitter.Push(buf, n))
            {
                if (_linked && !SerialProtocol.IsDeviceReply(line))
                {
                    HandleDeviceLine(line);
                    continue;
                }
                if (!SerialProtocol.IsDeviceReply(line) || _linked) continue;
                _firmware = SerialProtocol.FirmwareVersion(line);
                _linked = true;
                _scheduler.Reset(); // push everything immediately
                Console.Error.WriteLine($"[serial] linked {_portName}: {line}");
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
        if (Current == this) Current = null;
        _stop.Dispose();
    }
}
