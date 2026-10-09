using AIClockBridge;
using System.Text;
using System.Text.Json;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
string Text(byte[] b) => Encoding.UTF8.GetString(b);
byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

// Framing matches firmware handleSerialFrame: "#TAG {json}\n".
var status = SerialProtocol.Frame("STATUS", Bytes("{\"claude\":{\"status\":\"working\"}}"));
Check(Text(status) == "#STATUS {\"claude\":{\"status\":\"working\"}}\n", "status frame layout");
Check(SerialProtocol.Frame("NET", Bytes("{\"a\":\n1}")) == null, "embedded newline rejected");
Check(SerialProtocol.Frame("NET", Bytes("{\"a\":\r1}")) == null, "embedded CR rejected");
Check(SerialProtocol.Frame("NET", null) == null, "missing payload rejected");
var big = "{\"x\":\"" + new string('a', SerialProtocol.MaxFrameBytes) + "\"}";
Check(SerialProtocol.Frame("STATUS", Bytes(big)) == null, "oversized frame rejected");
var fits = "{\"x\":\"" + new string('a', SerialProtocol.MaxFrameBytes - 20) + "\"}";
var fitFrame = SerialProtocol.Frame("PC", Bytes(fits));
Check(fitFrame != null && fitFrame.Length <= SerialProtocol.MaxFrameBytes, "frame at limit accepted");
Check(SerialProtocol.MaxFrameBytes < 1600, "stays inside firmware serialLine[1600]");
Check(Text(SerialProtocol.Hello) == "#HELLO\n", "hello frame");

// #TIME carries Unix seconds (firmware applySerialTime).
var time = Text(SerialProtocol.Time(new DateTimeOffset(2026, 10, 9, 7, 4, 0, TimeSpan.Zero)));
Check(time == "#TIME {\"epoch\":1791529440}\n", "time frame epoch");

// #CMD keys/values exactly as the firmware parses them.
JsonElement Cmd(byte[] frame)
{
    var s = Text(frame);
    Check(s.StartsWith("#CMD ") && s.EndsWith("\n"), "cmd frame layout");
    return JsonDocument.Parse(s[5..^1]).RootElement;
}
var display = Cmd(SerialProtocol.Command(display: "holo_ai"));
Check(display.GetProperty("display").GetString() == "holo_ai", "display command");
Check(!display.TryGetProperty("brightness", out _) && !display.TryGetProperty("mirror", out _),
    "display command carries nothing else");
Check(Cmd(SerialProtocol.Command(brightness: 140)).GetProperty("brightness").GetInt32() == 100, "brightness clamped high");
Check(Cmd(SerialProtocol.Command(brightness: -5)).GetProperty("brightness").GetInt32() == 0, "brightness clamped low");
Check(Cmd(SerialProtocol.Command(mirror: true)).GetProperty("mirror").GetBoolean(), "mirror on");
Check(!Cmd(SerialProtocol.Command(mirror: false)).GetProperty("mirror").GetBoolean(), "mirror off");

// Device handshake reply.
var reply = "#DEVICE {\"name\":\"aiclock\",\"fw\":\"0.6.0\"}";
Check(SerialProtocol.IsDeviceReply(reply), "device reply detected");
Check(!SerialProtocol.IsDeviceReply("[wifi] #DEVICE"), "log line is not a reply");
Check(SerialProtocol.FirmwareVersion(reply) == "0.6.0", "firmware version parsed");
Check(SerialProtocol.FirmwareVersion("#DEVICE {broken") == "", "malformed reply tolerated");
Check(SerialProtocol.FirmwareVersion("#DEVICE") == "", "reply without json tolerated");

// Windows PnP names of NodeMCU USB-UARTs.
Check(SerialProtocol.PortFromPnpName("USB-SERIAL CH340 (COM5)") == "COM5", "port from CH340 name");
Check(SerialProtocol.PortFromPnpName("Silicon Labs CP210x USB to UART Bridge (COM12)") == "COM12", "port from CP210x name");
Check(SerialProtocol.PortFromPnpName("Communications Port") == null, "no port in name");
Check(SerialProtocol.LooksLikeClockPort("USB-SERIAL CH340 (COM5)"), "CH340 accepted");
Check(SerialProtocol.LooksLikeClockPort("Silicon Labs CP210x USB to UART Bridge (COM12)"), "CP210x accepted");
Check(!SerialProtocol.LooksLikeClockPort("Communications Port (COM1)"), "built-in COM1 skipped");
Check(!SerialProtocol.LooksLikeClockPort("Intel(R) Active Management Technology - SOL (COM3)"), "AMT SOL skipped");

// Line splitting: partial reads, CRLF, blank lines, noise.
var splitter = new SerialLineSplitter();
var chunk1 = Bytes("[boot] hello\r\n#DEV");
Check(splitter.Push(chunk1, chunk1.Length).SequenceEqual(new[] { "[boot] hello" }), "first line split");
var chunk2 = Bytes("ICE {\"fw\":\"0.6.0\"}\r\n\r\n");
Check(splitter.Push(chunk2, chunk2.Length).SequenceEqual(new[] { "#DEVICE {\"fw\":\"0.6.0\"}" }),
    "line joined across reads, blank line dropped");
var noise = new byte[20000];
Array.Fill(noise, (byte)'x');
splitter.Push(noise, noise.Length);
var after = Bytes("#DEVICE {}\n");
var afterLines = splitter.Push(after, after.Length).ToArray();
Check(afterLines.Length == 1 && afterLines[0].Length < 4000, "runaway noise discarded");
var partial = Bytes("#DEVICE {}\nxyz");
Check(splitter.Push(partial, 3).Count() == 0, "count limits consumed bytes");

// Pacing: never more than one frame per tick; intervals honoured; failures skipped.
var scheduler = new SerialFrameScheduler();
int pcBuilds = 0;
scheduler.Add("STATUS", TimeSpan.FromSeconds(5), () => Bytes("S"));
scheduler.Add("NET", TimeSpan.FromSeconds(2), () => Bytes("N"));
scheduler.Add("PC", TimeSpan.FromSeconds(1), () => { pcBuilds++; return Bytes("P"); });
scheduler.Add("WEATHER", TimeSpan.FromMinutes(1), () => throw new InvalidOperationException());
scheduler.Add("TIME", TimeSpan.FromSeconds(5), () => null);
var t0 = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
var sent = new Dictionary<string, int>();
int frames = 0, ticks = 0;
for (var t = t0; t < t0.AddSeconds(60); t = t.AddMilliseconds(250))
{
    ticks++;
    var next = scheduler.Next(t);
    if (next is not { } n) continue;
    if (n.Frame != null) frames++;
    sent[n.Tag] = sent.GetValueOrDefault(n.Tag) + 1;
}
Check(frames <= ticks, "at most one frame per tick");
Check(sent["STATUS"] is >= 12 and <= 13, $"status every 5s ({sent["STATUS"]})");
Check(sent["NET"] is >= 30 and <= 31, $"net every 2s ({sent["NET"]})");
Check(sent["PC"] is >= 58 and <= 60, $"pc every 1s ({sent["PC"]})");
Check(sent["WEATHER"] == 1, "throwing feed retried only on its interval");
Check(sent["TIME"] is >= 12 and <= 13, "missing payload does not stall others");
Check(pcBuilds == sent["PC"], "builder called once per send");
scheduler.Reset();
var firstAfterReset = new HashSet<string>();
for (int i = 0; i < 5; i++)
    if (scheduler.Next(t0.AddSeconds(61)) is { } r) firstAfterReset.Add(r.Tag);
Check(firstAfterReset.Count == 5, "reset makes every feed due immediately");

// USB can set/clear the device admin password (physical access).
var setPass = Cmd(SerialProtocol.Command(adminPassword: "s3cret"));
Check(setPass.GetProperty("admin_password").GetString() == "s3cret", "admin password over USB");
Check(Cmd(SerialProtocol.Command(adminPassword: "")).GetProperty("admin_password").GetString() == "",
    "empty admin password clears it");
Check(!Cmd(SerialProtocol.Command(display: "auto")).TryGetProperty("admin_password", out _),
    "password only sent when asked");

// #IMG: music bitmaps as whole rows, each base64'd on its own, every frame
// inside the firmware line buffer, and the rows reassemble to the original.
var rng = new Random(7);
foreach (var (kind, w, h) in new[] { ("cover", 128, 128), ("text", 232, 44) })
{
    var img = new byte[w * h * 2];
    rng.NextBytes(img);
    var imgFrames = SerialProtocol.ImageFrames(kind, 5, img, w);
    Check(imgFrames.Count > 0, kind + " frames produced");
    var rebuilt = new byte[img.Length];
    int nextRow = 0;
    foreach (var f in imgFrames)
    {
        Check(f.Length <= SerialProtocol.MaxFrameBytes && f[^1] == (byte)'\n', kind + " frame fits");
        var line = Text(f).TrimEnd('\n');
        Check(line.StartsWith("#IMG "), kind + " frame tag");
        using var doc = JsonDocument.Parse(line[5..]);
        var root = doc.RootElement;
        Check(root.GetProperty("k").GetString() == kind && root.GetProperty("rev").GetInt32() == 5, kind + " header");
        int row = root.GetProperty("row").GetInt32();
        Check(row == nextRow, kind + " rows in order");
        foreach (var part in root.GetProperty("d").GetString().Split(','))
        {
            var bytes = Convert.FromBase64String(part);
            Check(bytes.Length == w * 2 && bytes.Length <= 480, kind + " one row per part, fits rowBuf");
            bytes.CopyTo(rebuilt, nextRow * w * 2);
            nextRow++;
        }
    }
    Check(nextRow == h && rebuilt.SequenceEqual(img), kind + " reassembles exactly");
    Check(imgFrames.Count <= (kind == "cover" ? 43 : 22), $"{kind} uses few frames ({imgFrames.Count})");
}
Check(SerialProtocol.ImageFrames("cover", 1, Array.Empty<byte>(), 128).Count == 0, "no artwork, no frames");
Check(SerialProtocol.ImageFrames("cover", 1, new byte[100], 128).Count == 0, "partial row rejected");

// #NEED / #INFO from the clock.
Check(SerialProtocol.TryParseNeed("#NEED {\"k\":\"cover\",\"rev\":3}", out var needKind, out var needRev)
    && needKind == "cover" && needRev == 3, "need cover parsed");
Check(SerialProtocol.TryParseNeed("#NEED {\"k\":\"text\",\"rev\":-1}", out needKind, out _) && needKind == "text",
    "need text parsed");
Check(!SerialProtocol.TryParseNeed("#NEED {\"k\":\"secrets\"}", out _, out _), "unknown bitmap rejected");
Check(!SerialProtocol.TryParseNeed("#NEED {broken", out _, out _), "malformed need ignored");
Check(SerialProtocol.InfoJson("#INFO {\"mode\":\"auto\",\"sprite_rev\":2}") == "{\"mode\":\"auto\",\"sprite_rev\":2}",
    "info json passed through");
Check(SerialProtocol.InfoJson("#INFO {cut off") == null, "truncated info ignored");
Check(SerialProtocol.InfoJson("[web] #INFO") == null, "log line is not info");
Check(Cmd(SerialProtocol.Command(spriteDump: "codex")).GetProperty("sprite_dump").GetString() == "codex",
    "sprite dump command");

// #SPR: sprite streamed by the clock in 384-byte chunks, as pumpSpriteDump sends them.
var sprite = new byte[1 + 6 * 111 * 120 * 2];
rng.NextBytes(sprite);
sprite[0] = 6;
string SprLine(string slot, int off, int len) =>
    $"#SPR {{\"slot\":\"{slot}\",\"rev\":4,\"total\":{sprite.Length},\"off\":{off},\"d\":\"{Convert.ToBase64String(sprite, off, len)}\"}}";
var asm = new SpriteAssembler("claude");
byte[] got = null;
for (int off = 0; off < sprite.Length; off += 384)
{
    Check(got == null, "not done before last chunk");
    Check(asm.Accept(SprLine("codex", off, Math.Min(384, sprite.Length - off))) == null, "other slot ignored");
    got = asm.Accept(SprLine("claude", off, Math.Min(384, sprite.Length - off)));
}
Check(got != null && got.SequenceEqual(sprite), "sprite reassembled exactly");
var gap = new SpriteAssembler("claude");
Check(gap.Accept(SprLine("claude", 0, 384)) == null, "first chunk accepted");
Check(gap.Accept(SprLine("claude", 768, 384)) == null, "gap detected");
Check(gap.Accept(SprLine("claude", 384, 384)) == null, "transfer restarts after a gap");
Check(gap.Accept("#SPR {not json") == null, "malformed chunk ignored");

// Bridge HTTP: hook events only from this PC and never from a browser page.
Check(MiniHttpServer.PostAllowed("127.0.0.1", new[] { "POST /event HTTP/1.1" }), "loopback curl allowed");
Check(MiniHttpServer.PostAllowed("::1", new[] { "POST /event HTTP/1.1" }), "IPv6 loopback allowed");
Check(!MiniHttpServer.PostAllowed("192.168.1.50", new[] { "POST /event HTTP/1.1" }), "LAN POST rejected");
Check(!MiniHttpServer.PostAllowed("", new string[0]), "unknown peer rejected");
Check(!MiniHttpServer.PostAllowed("127.0.0.1", new[] { "POST /event HTTP/1.1", "Origin: https://evil.example" }),
    "browser POST rejected");

int events = 0;
using (var server = new MiniHttpServer(0, new() { ["/status"] = () => Bytes("{}") },
    postRoutes: new() { ["/event"] = _ => { events++; return Bytes("{\"ok\":true}"); } },
    bindAddress: System.Net.IPAddress.Loopback))
{
    server.Start();
    using var http = new HttpClient();
    var url = $"http://127.0.0.1:{server.BoundPort}";
    var ok = await http.PostAsync(url + "/event", new StringContent("{\"agent\":\"claude\",\"event\":\"Stop\"}"));
    Check(ok.IsSuccessStatusCode && events == 1, "local hook event accepted");
    using var fromPage = new HttpRequestMessage(HttpMethod.Post, url + "/event")
    {
        Content = new StringContent("{\"agent\":\"claude\",\"event\":\"Stop\"}"),
    };
    fromPage.Headers.Add("Origin", "https://evil.example");
    var blocked = await http.SendAsync(fromPage);
    Check((int)blocked.StatusCode == 403 && events == 1, "browser-originated event blocked");
    var statusResp = await http.GetAsync(url + "/status");
    Check(statusResp.IsSuccessStatusCode, "status still readable");
    Check(!statusResp.Headers.Contains("Access-Control-Allow-Origin"), "no CORS for web pages");
}

Console.WriteLine($"PASS: {checks} serial protocol, wired music/sprite and bridge security assertions");
