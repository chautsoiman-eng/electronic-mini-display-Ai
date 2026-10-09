using AIClockBridge;
using System.Drawing;
using System.Drawing.Imaging;

var checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }

const string fixture = """
{"current":{"temperature_2m":26.4,"relative_humidity_2m":73,"apparent_temperature":28.1,"weather_code":2,"wind_speed_10m":11.7}}
""";
var weather = WeatherMonitor.Parse(fixture, "Taipei");
Check(weather.IsLive, "parsed weather is live");
Check(weather.TemperatureC == 26.4, "temperature parsed");
Check(weather.ApparentC == 28.1, "apparent temperature parsed");
Check(weather.HumidityPct == 73, "humidity parsed");
Check(weather.WindKph == 11.7, "wind parsed");
Check(weather.Condition == "PARTLY CLOUDY", "WMO condition mapped");
Check(weather.Location == "TAIPEI", "location normalized");
Check(WeatherMonitor.Condition(95) == "THUNDERSTORM", "thunderstorm mapping");
Check(WeatherMonitor.Condition(999) == "UNKNOWN", "unknown WMO code");
Check(WeatherScene.IconForCode(0) == WeatherIconKind.Clear, "clear icon mapping");
Check(WeatherScene.IconForCode(2) == WeatherIconKind.PartlyCloudy, "partly cloudy icon mapping");
Check(WeatherScene.IconForCode(48) == WeatherIconKind.Fog, "fog icon mapping");
Check(WeatherScene.IconForCode(63) == WeatherIconKind.Rain, "rain icon mapping");
Check(WeatherScene.IconForCode(82) == WeatherIconKind.Showers, "showers icon mapping");
Check(WeatherScene.IconForCode(75) == WeatherIconKind.Snow, "snow icon mapping");
Check(WeatherScene.IconForCode(95) == WeatherIconKind.Thunderstorm, "thunderstorm icon mapping");
Check(WeatherScene.IconForCode(null) == WeatherIconKind.None, "missing code has no fake icon");

var silence = SpectrumAnalyzer.Analyze(new float[SpectrumAnalyzer.FftSize], 48000);
Check(silence.Length == 24 && silence.All(x => x == 0), "silence produces 24 zero bars");
var tone = new float[SpectrumAnalyzer.FftSize];
for (var i = 0; i < tone.Length; i++) tone[i] = (float)(0.6 * Math.Sin(2 * Math.PI * 440 * i / 48000));
var levels = SpectrumAnalyzer.Analyze(tone, 48000);
Check(levels.Length == 24, "FFT produces exactly 24 bars");
Check(levels.All(x => x is >= 0 and <= 100), "FFT bars are clamped");
Check(levels.Max() >= 80, "audible sine produces a strong peak");
var peak = Array.IndexOf(levels, levels.Max());
Check(peak is >= 7 and <= 10, "440 Hz peak lands in expected logarithmic band");

var output = args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "previews");
Directory.CreateDirectory(output);
void Render(string name, WeatherSnapshot data, bool mirror)
{
    using var bitmap = new Bitmap(240, 240);
    using var graphics = Graphics.FromImage(bitmap);
    if (mirror)
    {
        graphics.TranslateTransform(240, 0);
        graphics.ScaleTransform(-1, 1);
    }
    WeatherScene.Draw(graphics, data, testData: true);
    var path = Path.Combine(output, name + ".png");
    bitmap.Save(path, ImageFormat.Png);
    Check(bitmap.Width == 240 && bitmap.Height == 240, name + " is native 240x240");
    for (var y = 0; y < 240; y++)
        for (var x = 0; x < 8; x++)
            Check(bitmap.GetPixel(x, y).ToArgb() == Color.Black.ToArgb(), name + " keeps safety margin");
}
Render("weather-live-test", weather, false);
Render("weather-missing-test", new(), false);
Render("weather-live-mirrored-test", weather, true);
using (var music = new Bitmap(240, 240))
using (var graphics = Graphics.FromImage(music))
{
    MusicScene.Draw(graphics, "Spectrum Test", "440 Hz fixture", 72, 240, true, null, levels);
    music.Save(Path.Combine(output, "music-spectrum-test.png"), ImageFormat.Png);
    Check(music.Width == 240 && music.Height == 240, "music spectrum is native 240x240");
    Check(levels.Count(x => x > 0) > 0, "music preview contains active spectrum bars");
}
Console.WriteLine($"PASS: {checks} Weather/spectrum/mirror assertions; 4 real 240x240 PNGs in {output}");
