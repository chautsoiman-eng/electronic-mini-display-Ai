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

Console.WriteLine($"PASS: {checks} serial protocol assertions");
