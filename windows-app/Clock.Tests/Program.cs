using AIClockBridge;
using System.Drawing;
using System.Drawing.Imaging;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }

var taipei = ClockScene.TaipeiTimeZone();
var newYear = ClockScene.FromUtc(new DateTimeOffset(2026, 12, 31, 16, 0, 0, TimeSpan.Zero), zone: taipei);
Check(ClockScene.TimeText(newYear) == "00:00", "24-hour midnight");
Check(ClockScene.DateText(newYear) == "2027-01-01", "Taipei cross-day date");
Check(ClockScene.WeekdayText(newYear) == "FRI", "weekday after cross-day conversion");

var afternoon = ClockScene.FromUtc(new DateTimeOffset(2026, 10, 9, 7, 4, 0, TimeSpan.Zero), zone: taipei);
Check(ClockScene.TimeText(afternoon) == "15:04", "24-hour afternoon format");
Check(ClockScene.DateText(afternoon) == "2026-10-09", "ISO date format");
Check(ClockScene.WeekdayText(afternoon) == "FRI", "weekday format");
Check(afternoon.MinuteKey == ClockScene.FromUtc(
    new DateTimeOffset(2026, 10, 9, 7, 4, 59, TimeSpan.Zero), zone: taipei).MinuteKey,
    "same minute does not redraw");
Check(afternoon.MinuteKey != ClockScene.FromUtc(
    new DateTimeOffset(2026, 10, 9, 7, 5, 0, TimeSpan.Zero), zone: taipei).MinuteKey,
    "next minute redraws");

var unsynced = new ClockSnapshot(false, default, ClockScene.DefaultTimeZoneLabel, new());
Check(ClockScene.TimeText(unsynced) == "--:--", "unsynchronized time is not fabricated");
Check(ClockScene.WeekdayText(unsynced) == "WAITING FOR NTP", "unsynchronized status");
Check(ClockScene.TemperatureText(new()) == "--", "missing weather temperature");
Check(ClockScene.ConditionText(new()) == "WEATHER --", "missing weather condition");
Check(ClockScene.TemperatureText(new(26.4, "clear")) == "26C", "weather temperature formatting");
Check(ClockScene.ConditionText(new(26.4, "clear")) == "CLEAR", "weather condition formatting");
Check(ClockScene.ConditionText(new(26.4, "exceptionally long weather")) == "EXCEPTIONALLY ",
    "weather condition stays within its field");

var output = args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "previews");
Directory.CreateDirectory(output);
void Render(string name, ClockSnapshot snapshot)
{
    using var bitmap = new Bitmap(240, 240);
    using var graphics = Graphics.FromImage(bitmap);
    ClockScene.Draw(graphics, snapshot, testData: true);
    var path = Path.Combine(output, $"clock-{name}.png");
    bitmap.Save(path, ImageFormat.Png);
    Check(bitmap.Width == 240 && bitmap.Height == 240, name + " native 240x240");
    for (int y = 232; y < 240; y++)
        for (int x = 0; x < 240; x++)
            Check(bitmap.GetPixel(x, y).ToArgb() == Color.Black.ToArgb(), name + " bottom safety margin");
    for (int y = 0; y < 240; y++)
        for (int x = 0; x < 8; x++)
            Check(bitmap.GetPixel(x, y).ToArgb() == Color.Black.ToArgb(), name + " left safety margin");
    using var loaded = Image.FromFile(path);
    Check(loaded.RawFormat.Guid == ImageFormat.Png.Guid, name + " real PNG");
}

Render("taipei-test", afternoon with { Weather = new(26.4, "CLEAR") });
Render("midnight-test", newYear);
Render("unsynced-test", unsynced);
Console.WriteLine($"PASS: {checks} Clock assertions; 3 rendered 240x240 PNGs in {output}");
