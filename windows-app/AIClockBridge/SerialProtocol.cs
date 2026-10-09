using System.Text;
using System.Text.Json;

namespace AIClockBridge;

// Wire format of the USB serial link (same as mac-app/SerialLink.swift and
// firmware handleSerialFrame): newline-terminated ASCII frames.
//   bridge -> device:  #HELLO  #STATUS {json}  #NET {json}
//                      #PC {json}  #WEATHER {json}  #TIME {"epoch":N}  #CMD {json}
//                      #MUSIC {json}  #IMG {"k","rev","row","d"}
//   device -> bridge:  #DEVICE {"name":"aiclock","fw":"x.y.z"}  #INFO {json}
//                      #NEED {"k","rev"}  #SPR {"slot","rev","total","off","d"}
// Kept free of System.IO.Ports so the framing can be tested without hardware.
static class SerialProtocol
{
    /// Firmware line buffer is 1600 bytes (incl. terminator); stay well inside it.
    public const int MaxFrameBytes = 1500;

    public static readonly byte[] Hello = Encoding.ASCII.GetBytes("#HELLO\n");

    /// "#TAG {json}\n", or null when the payload would not survive the device's
    /// line parser (embedded newline, or longer than its buffer).
    public static byte[] Frame(string tag, byte[] json)
    {
        if (json == null || Array.IndexOf(json, (byte)'\n') >= 0 || Array.IndexOf(json, (byte)'\r') >= 0)
            return null;
        var prefix = Encoding.ASCII.GetBytes($"#{tag} ");
        if (prefix.Length + json.Length + 1 > MaxFrameBytes) return null;
        var frame = new byte[prefix.Length + json.Length + 1];
        prefix.CopyTo(frame, 0);
        json.CopyTo(frame, prefix.Length);
        frame[^1] = (byte)'\n';
        return frame;
    }

    /// #TIME lets a wired-only clock (no WiFi, so no NTP) show real time.
    public static byte[] Time(DateTimeOffset now) =>
        Frame("TIME", JsonSerializer.SerializeToUtf8Bytes(new { epoch = now.ToUnixTimeSeconds() }));

    /// #CMD carrying any subset of display mode / brightness / mirror /
    /// web admin password (USB = physical access, so no current password).
    public static byte[] Command(string display = null, int? brightness = null, bool? mirror = null,
                                 string adminPassword = null, string spriteDump = null)
    {
        var cmd = new Dictionary<string, object>();
        if (spriteDump != null) cmd["sprite_dump"] = spriteDump;
        if (adminPassword != null) cmd["admin_password"] = adminPassword;
        if (display != null) cmd["display"] = display;
        if (brightness is int level) cmd["brightness"] = Math.Clamp(level, 0, 100);
        if (mirror is bool m) cmd["mirror"] = m;
        return Frame("CMD", JsonSerializer.SerializeToUtf8Bytes(cmd));
    }

    /// Music-page bitmap (cover 128x128 or text strip 232x44, RGB565 in the
    /// device's pushImage byte order) as #IMG frames. Each row is base64'd on
    /// its own and rows are comma-joined, so the firmware decodes a row
    /// straight into its 480-byte row buffer; as many rows per frame as fit.
    public static List<byte[]> ImageFrames(string kind, int rev, byte[] rgb565, int width)
    {
        var frames = new List<byte[]>();
        int rowBytes = width * 2;
        if (rgb565 == null || rowBytes == 0 || rgb565.Length % rowBytes != 0) return frames;
        int rows = rgb565.Length / rowBytes;
        int row = 0;
        while (row < rows)
        {
            byte[] best = null;
            int take = 0;
            for (int n = 1; row + n <= rows; n++)
            {
                var parts = Enumerable.Range(row, n)
                    .Select(r => Convert.ToBase64String(rgb565, r * rowBytes, rowBytes));
                var json = $"{{\"k\":\"{kind}\",\"rev\":{rev},\"row\":{row},\"d\":\"{string.Join(",", parts)}\"}}";
                var frame = Frame("IMG", Encoding.ASCII.GetBytes(json));
                if (frame == null) break;
                best = frame;
                take = n;
            }
            if (best == null) return new List<byte[]>(); // a single row cannot fit
            frames.Add(best);
            row += take;
        }
        return frames;
    }

    /// #NEED {"k":"cover"|"text","rev":N} — the clock asks for a music bitmap.
    public static bool TryParseNeed(string line, out string kind, out int rev)
    {
        kind = null;
        rev = -1;
        if (!line.StartsWith("#NEED ", StringComparison.Ordinal)) return false;
        try
        {
            using var doc = JsonDocument.Parse(line[6..]);
            var root = doc.RootElement;
            if (!root.TryGetProperty("k", out var k) || k.ValueKind != JsonValueKind.String) return false;
            kind = k.GetString();
            if (kind != "cover" && kind != "text") return false;
            rev = root.TryGetProperty("rev", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : -1;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// #INFO {json} — the same JSON as GET /api/info, pushed every 2s on USB.
    public static string InfoJson(string line)
    {
        if (!line.StartsWith("#INFO ", StringComparison.Ordinal)) return null;
        var json = line[6..];
        try
        {
            using var _ = JsonDocument.Parse(json);
            return json;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsDeviceReply(string line) => line.StartsWith("#DEVICE", StringComparison.Ordinal);

    /// "fw" field of a #DEVICE line, or "" when absent/malformed.
    public static string FirmwareVersion(string line)
    {
        var brace = line.IndexOf('{');
        if (brace < 0) return "";
        try
        {
            using var doc = JsonDocument.Parse(line[brace..]);
            return doc.RootElement.TryGetProperty("fw", out var fw) && fw.ValueKind == JsonValueKind.String
                ? fw.GetString() : "";
        }
        catch (JsonException)
        {
            return "";
        }
    }

    /// USB-UART bridges used on NodeMCU boards (CH340/CH341, CP210x), matched
    /// against the Windows PnP device name, e.g. "USB-SERIAL CH340 (COM5)".
    public static bool LooksLikeClockPort(string pnpName)
    {
        if (string.IsNullOrEmpty(pnpName)) return false;
        var n = pnpName.ToUpperInvariant();
        return n.Contains("CH340") || n.Contains("CH341") || n.Contains("CH9102")
            || n.Contains("CP210") || n.Contains("USB-SERIAL") || n.Contains("USB SERIAL");
    }

    /// "COM5" out of "USB-SERIAL CH340 (COM5)".
    public static string PortFromPnpName(string pnpName)
    {
        if (pnpName == null) return null;
        var open = pnpName.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
        if (open < 0) return null;
        var close = pnpName.IndexOf(')', open);
        return close > open + 1 ? pnpName[(open + 1)..close] : null;
    }
}

/// Splits the device's byte stream into trimmed lines; drops runaway noise.
sealed class SerialLineSplitter
{
    const int MaxBuffered = 16384;
    readonly List<byte> _buf = new();

    public IEnumerable<string> Push(byte[] data, int count)
    {
        var lines = new List<string>();
        for (int i = 0; i < count; i++)
        {
            var b = data[i];
            if (b == '\n')
            {
                var line = Encoding.UTF8.GetString(_buf.ToArray()).Trim();
                _buf.Clear();
                if (line.Length > 0) lines.Add(line);
            }
            else if (_buf.Count < MaxBuffered)
            {
                _buf.Add(b);
            }
            else
            {
                _buf.Clear(); // runaway noise without newlines
            }
        }
        return lines;
    }
}

/// Round-robin frame pacing: at most one frame per tick so a burst of payloads
/// cannot overflow the ESP8266's 2 KB UART buffer during a slow screen draw.
sealed class SerialFrameScheduler
{
    sealed class Feed
    {
        public string Tag;
        public TimeSpan Interval;
        public Func<byte[]> Build;
        public DateTime Due;
    }

    readonly List<Feed> _feeds = new();

    public void Add(string tag, TimeSpan interval, Func<byte[]> build) =>
        _feeds.Add(new Feed { Tag = tag, Interval = interval, Build = build, Due = DateTime.MinValue });

    /// Makes every feed due immediately (fresh link).
    public void Reset()
    {
        foreach (var f in _feeds) f.Due = DateTime.MinValue;
    }

    /// The most overdue feed's frame, or null when nothing is due. A feed whose
    /// payload is unavailable or too large is skipped until its next interval.
    public (string Tag, byte[] Frame)? Next(DateTime now)
    {
        Feed pick = null;
        foreach (var f in _feeds)
            if (f.Due <= now && (pick == null || f.Due < pick.Due)) pick = f;
        if (pick == null) return null;
        pick.Due = now + pick.Interval;
        byte[] frame;
        try
        {
            frame = pick.Build();
        }
        catch (Exception)
        {
            frame = null;
        }
        return (pick.Tag, frame);
    }
}

/// Reassembles a sprite the clock streams as #SPR chunks (after #CMD
/// sprite_dump) into the /sprite/*/raw layout [1 byte frames][RGB565...].
/// Chunks must arrive in order; anything else restarts the transfer.
sealed class SpriteAssembler
{
    readonly string _slot;
    byte[] _buf;
    int _received;

    public SpriteAssembler(string slot) => _slot = slot;

    /// Feeds one line; returns the finished sprite when the last chunk lands.
    public byte[] Accept(string line)
    {
        if (!line.StartsWith("#SPR ", StringComparison.Ordinal)) return null;
        try
        {
            using var doc = JsonDocument.Parse(line[5..]);
            var root = doc.RootElement;
            if (root.GetProperty("slot").GetString() != _slot) return null;
            int total = root.GetProperty("total").GetInt32();
            int off = root.GetProperty("off").GetInt32();
            var data = Convert.FromBase64String(root.GetProperty("d").GetString() ?? "");
            if (total <= 1 || total > 1_000_000) return null;
            if (off == 0)
            {
                _buf = new byte[total];
                _received = 0;
            }
            if (_buf == null || _buf.Length != total || off != _received || off + data.Length > total)
            {
                _buf = null; // out of order: wait for a fresh dump
                return null;
            }
            data.CopyTo(_buf, off);
            _received += data.Length;
            if (_received < total) return null;
            var done = _buf;
            _buf = null;
            return done;
        }
        catch (Exception e) when (e is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
