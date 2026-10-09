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

// 從韌體標頭讀出內建 Claude／Codex 桌寵第 0 格，預覽與裝置使用同一份圖。
static Bitmap LoadBuiltInPet(string name, int w, int h)
{
    var dir = AppContext.BaseDirectory;
    while (dir != null && !File.Exists(Path.Combine(dir, "firmware", "include", "img", $"{name}_sprite.h")))
        dir = Path.GetDirectoryName(dir);
    if (dir == null) throw new Exception($"{name}_sprite.h not found");
    var text = File.ReadAllText(Path.Combine(dir, "firmware", "include", "img", $"{name}_sprite.h"));
    var body = text[(text.IndexOf($"{name}_sprite_0[", StringComparison.Ordinal))..];
    body = body[(body.IndexOf('{') + 1)..body.IndexOf('}')];
    var values = body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(v => v.StartsWith("0x") ? Convert.ToUInt16(v, 16) : ushort.Parse(v)).ToArray();
    if (values.Length != w * h) throw new Exception($"{name} frame has {values.Length} pixels");
    var bmp = new Bitmap(w, h);
    for (int i = 0; i < values.Length; i++)
    {
        // 標頭存的是 byte-swap 過的 RGB565（pushImage 線序）
        int c = ((values[i] & 0xFF) << 8) | (values[i] >> 8);
        bmp.SetPixel(i % w, i / w, Color.FromArgb((c >> 11 & 0x1F) * 255 / 31, (c >> 5 & 0x3F) * 255 / 63, (c & 0x1F) * 255 / 31));
    }
    return bmp;
}

using var claudePet = LoadBuiltInPet("claude", 111, 120);
using var codexPet = LoadBuiltInPet("codex", 120, 120);
var claudeRect = HoloAiScene.PetRect(HoloAiScene.ClaudePetY, 111, 120);
var codexRect = HoloAiScene.PetRect(HoloAiScene.CodexPetY, 120, 120);
Equal(new Rectangle(16, 40, 56, 60), claudeRect);
Equal(new Rectangle(14, 146, 60, 60), codexRect);
Equal(true, claudeRect.Right <= HoloAiScene.BarX && codexRect.Right <= HoloAiScene.BarX);
Equal(true, claudeRect.Bottom <= 136 && codexRect.Top > 136 && codexRect.Bottom <= 232);

var output = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "previews");
Directory.CreateDirectory(output);
var track = Color.FromArgb(21, 48, 57).ToArgb();
bool AnyLit(Bitmap b, Rectangle r)
{
    for (int y = r.Top; y < r.Bottom; y++)
        for (int x = r.Left; x < r.Right; x++)
            if (b.GetPixel(x, y).ToArgb() != Color.Black.ToArgb()) return true;
    return false;
}
foreach (var scenario in new[] { "normal", "unknown", "offline", "no-pets" })
{
    using var bitmap = new Bitmap(240, 240);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.Clear(Color.Black);
    bool unknown = scenario == "unknown";
    bool pets = scenario != "no-pets";
    var claude = new HoloProvider("working", false, unknown ? null : 35, 61, unknown ? null : 72, 1500);
    var codex = new HoloProvider("idle", true, unknown ? null : 95, 18, unknown ? null : 48, 3000);
    HoloAiScene.Draw(graphics, claude, codex, scenario != "offline",
        pets ? claudePet : null, pets ? codexPet : null);
    // 抽查四條 bar 的有效區域，未知值應只有底色。
    Equal(unknown ? track : HoloAiScene.Cyan.ToArgb(), bitmap.GetPixel(90, 77).ToArgb());
    Equal(unknown ? track : Color.Orange.ToArgb(), bitmap.GetPixel(90, 116).ToArgb());
    Equal(unknown ? track : Color.Red.ToArgb(), bitmap.GetPixel(90, 182).ToArgb());
    Equal(unknown ? track : HoloAiScene.Cyan.ToArgb(), bitmap.GetPixel(90, 221).ToArgb());
    // 桌寵只出現在左側框內；沒有圖時保持全黑。
    Equal(pets, AnyLit(bitmap, claudeRect));
    Equal(pets, AnyLit(bitmap, codexRect));
    Equal(false, AnyLit(bitmap, new Rectangle(0, 232, 240, 8)));
    bitmap.Save(Path.Combine(output, $"holo-{scenario}.png"), ImageFormat.Png);
}
Console.WriteLine($"PASS: {checks} assertions; 4 rendered previews in {output}");
