using AIClockBridge;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Text;
using System.Text.Json;

int count = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); count++; }
foreach (double? invalid in new double?[] { null, -1, 101, double.NaN, double.PositiveInfinity })
    Check(PcTelemetry.Percent(invalid) == null, "invalid percent");
Check(PcTelemetry.Percent(0) == 0, "zero is valid");
Check(PcTelemetry.Percent(100) == 100, "100 is valid");
Check(PcTelemetry.Temperature(151) == null, "invalid temp");
Check(PcTelemetry.Temperature(0) == 0, "zero temp");
Check(PcMonitorScene.Pct(null) == "--" && PcMonitorScene.Temp(null) == "--", "unknown labels");

var history = new PcHistory();
history.Add(100, 50); history.Add(103, 60);
Check(history.Snapshot().SequenceEqual(new double?[] { 50, null, null, 60 }), "history gap");
for (int i = 104; i < 180; i++) history.Add(i, i % 100);
Check(history.Snapshot().Length == 60, "bounded history");
Check(history.Snapshot()[^1] == 79, "history order");

Check(GpuStatsReader.Aggregate(new[] { ("pid_1_luid_A_phys_0_eng_0_engtype_3D", 25.0),
    ("pid_2_luid_A_phys_0_eng_0_engtype_3D", 30.0), ("pid_1_luid_A_phys_0_eng_1_engtype_Copy", 40.0) }) == 55, "GPU per engine aggregation");
Check(GpuStatsReader.Aggregate(new[] { ("bad", 30.0) }) == null, "GPU invalid instance");
Check(GpuStatsReader.Aggregate(new[] { ("pid_1_luid_A", 150.0) }) == 100, "GPU cap");
Check(PcMonitor.ParseNvidia("0, 10, 33\n1, 20, 48\n") == (20.0, 48.0), "NVIDIA multiple cards");
Check(PcMonitor.ParseNvidia("0, N/A, [Not Supported]\n") == (null, null), "NVIDIA unknown");
Check(PcMonitor.ParseNvidia("0, 999, 999\n") == (null, null), "NVIDIA invalid");

var normal = new PcTelemetry { Ts = 100, Seq = 3, Stale = false, CpuPct = 35, GpuPct = 92, MemPct = 60,
    CpuTempC = 65, GpuTempC = 48, CpuHistory = Enumerable.Range(0, 60).Select(i => (double?)(20 + i % 35)).ToArray() };
Check(!normal.At(105).Stale, "fresh boundary");
var stale = normal.At(106);
Check(stale.Stale && stale.CpuPct == null && stale.GpuPct == null && stale.CpuTempC == null, "stale blanks values");
Check(normal.At(99).Stale, "clock reversal");
using (var json = JsonDocument.Parse(normal.ToJson()))
{
    Check(json.RootElement.GetProperty("cpu_history").GetArrayLength() == 60, "JSON history");
    Check(json.RootElement.GetProperty("interval_ms").GetInt32() == 1000, "JSON interval");
}
using (var json = JsonDocument.Parse(new PcTelemetry().ToJson()))
    Check(json.RootElement.GetProperty("cpu_temp_c").ValueKind == JsonValueKind.Null, "JSON unknown remains null");
Check(normal.ToJson().Length + 5 < 1600, "fits serial frame");

// 真正 HTTP server 的 /pc 端點，限制 localhost，不讀帳號或探測區網。
using (var server = new MiniHttpServer(0, new() { ["/pc"] = () => normal.ToJson() }, bindAddress: IPAddress.Loopback))
{
    server.Start();
    using var client = new HttpClient();
    var response = await client.GetByteArrayAsync($"http://127.0.0.1:{server.BoundPort}/pc");
    Check(response.SequenceEqual(normal.ToJson()), "HTTP /pc payload");
}

var output = args.FirstOrDefault(a => a != "--live") ?? Path.Combine(AppContext.BaseDirectory, "previews");
Directory.CreateDirectory(output);
void Render(string name, PcTelemetry data)
{
    using var bitmap = new Bitmap(240, 240);
    using var g = Graphics.FromImage(bitmap);
    g.Clear(Color.Black);
    PcMonitorScene.Draw(g, data, true);
    bitmap.Save(Path.Combine(output, $"pc-{name}.png"), ImageFormat.Png);
    Check(bitmap.GetPixel(20, 58).ToArgb() == (data.Stale || data.CpuPct == null
        ? Color.FromArgb(21, 48, 57) : PcMonitorScene.Cyan).ToArgb(), name + " CPU bar");
}
Render("normal", normal);
Render("unknown", new PcTelemetry { Stale = false });
Render("stale", stale);
File.WriteAllBytes(Path.Combine(output, "pc-fixture.json"), normal.ToJson());
if (args.Contains("--live"))
{
    using var monitor = new PcMonitor();
    monitor.Start();
    await Task.Delay(7000);
    var live = monitor.Snapshot();
    Check(live.Seq >= 3 && live.CpuHistory.Length >= 3 && !live.Stale, "live sampler");
    Check(live.CpuPct.HasValue && live.MemPct.HasValue, "live CPU/RAM");
    File.WriteAllBytes(Path.Combine(output, "pc-live.json"), live.ToJson());
    Render("live", live);
    Console.WriteLine(Encoding.UTF8.GetString(live.ToJson()));
}
Console.WriteLine($"PASS: {count} assertions; previews in {output}");
