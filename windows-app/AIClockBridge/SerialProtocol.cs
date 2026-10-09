using System.Text;
using System.Text.Json;

namespace AIClockBridge;

// Wire format of the USB serial link (same as mac-app/SerialLink.swift and
// firmware handleSerialFrame): newline-terminated ASCII frames.
//   bridge -> device:  #HELLO  #STATUS {json}  #NET {json}  #STOCK {json}
//                      #PC {json}  #WEATHER {json}  #TIME {"epoch":N}  #CMD {json}
//   device -> bridge:  #DEVICE {"name":"aiclock","fw":"x.y.z"}
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

    /// #CMD carrying any subset of display mode / brightness / mirror.
    public static byte[] Command(string display = null, int? brightness = null, bool? mirror = null)
    {
        var cmd = new Dictionary<string, object>();
        if (display != null) cmd["display"] = display;
        if (brightness is int level) cmd["brightness"] = Math.Clamp(level, 0, 100);
        if (mirror is bool m) cmd["mirror"] = m;
        return Frame("CMD", JsonSerializer.SerializeToUtf8Bytes(cmd));
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
