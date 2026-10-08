using AIClockBridge;
using System.Drawing;
using System.Drawing.Imaging;

int checks = 0;
void Equal<T>(T expected, T actual)
{
    if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
    checks++;
}

// 確認未知值不被當作 0%，以及韌體相同的四捨五入與警戒門檻。
foreach (double? unknown in new double?[] { null, -1, double.NaN, double.PositiveInfinity })
    Equal("--", HoloAiScene.Percent(unknown));
Equal("0%", HoloAiScene.Percent(0));
Equal("70%", HoloAiScene.Percent(69.5));
Equal("100%", HoloAiScene.Percent(120));
Equal(Color.Orange, HoloAiScene.UsageColor(70));
Equal(Color.Red, HoloAiScene.UsageColor(90));
Equal(HoloAiScene.Cyan, HoloAiScene.UsageColor(69));
Equal("--", HoloAiScene.Reset(null));
Equal("--", HoloAiScene.Reset(-1));
Equal("0m", HoloAiScene.Reset(0));
Equal("1h 1m", HoloAiScene.Reset(61));
Equal("1d 1h", HoloAiScene.Reset(1500));

var output = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "previews");
Directory.CreateDirectory(output);
foreach (var scenario in new[] { "normal", "unknown", "offline" })
{
    using var bitmap = new Bitmap(240, 240);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.Clear(Color.Black);
    bool unknown = scenario == "unknown";
    var claude = new HoloProvider("working", false, unknown ? null : 35, 61, unknown ? null : 72, 1500);
    var codex = new HoloProvider("idle", true, unknown ? null : 95, 18, unknown ? null : 48, 3000);
    HoloAiScene.Draw(graphics, claude, codex, scenario != "offline");
    // 抽查四條 bar 的有效區域，未知值應只有底色。
    Equal(unknown ? Color.FromArgb(21, 48, 57).ToArgb() : HoloAiScene.Cyan.ToArgb(), bitmap.GetPixel(20, 78).ToArgb());
    Equal(unknown ? Color.FromArgb(21, 48, 57).ToArgb() : Color.Orange.ToArgb(), bitmap.GetPixel(20, 120).ToArgb());
    Equal(unknown ? Color.FromArgb(21, 48, 57).ToArgb() : Color.Red.ToArgb(), bitmap.GetPixel(20, 186).ToArgb());
    Equal(unknown ? Color.FromArgb(21, 48, 57).ToArgb() : HoloAiScene.Cyan.ToArgb(), bitmap.GetPixel(20, 228).ToArgb());
    bitmap.Save(Path.Combine(output, $"holo-{scenario}.png"), ImageFormat.Png);
}
Console.WriteLine($"PASS: {checks} assertions; 3 rendered previews in {output}");
