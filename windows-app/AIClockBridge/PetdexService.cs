using System.Text.Json;
using ImageMagick;

namespace AIClockBridge;

// Pet animation source: petdex.dev's public manifest (3300+ open-source
// "Codex pets"). Each pet is one 1536x1872 WebP spritesheet laid out on a
// fixed 8x9 grid of 192x208 frames; each row is one named animation (the
// row/frame-count table below mirrors petdex's own pet-states definition).
// We crop a row, composite the frames onto black at the device slot size,
// encode a looping GIF, and POST it to the clock, which re-decodes it
// on-device into its own format. Magick.NET does the WebP decode + GIF
// encode (System.Drawing can do neither).

record PetdexPet(string Slug, string DisplayName, string Kind, string SpritesheetUrl);

record PetdexAnimState(string Id, string Label, int Row, int Frames, int DurationMs);

static class PetdexService
{
    public const string ManifestUrl = "https://assets.petdex.dev/manifests/petdex-v1.json";

    public const int FrameW = 192, FrameH = 208;
    /// Device firmware caps custom animations at 8 frames (MAX_CUSTOM_FRAMES).
    public const int MaxFrames = 8;

    public static readonly PetdexAnimState[] States =
    {
        new("idle", "待机 Idle", 0, 6, 1100),
        new("running-right", "右跑 Run Right", 1, 8, 1060),
        new("running-left", "左跑 Run Left", 2, 8, 1060),
        new("waving", "挥手 Waving", 3, 4, 700),
        new("jumping", "跳跃 Jumping", 4, 5, 840),
        new("failed", "失败 Failed", 5, 8, 1220),
        new("waiting", "等待 Waiting", 6, 6, 1010),
        new("running", "原地跑 Running", 7, 6, 820),
        new("review", "思考 Review", 8, 6, 1030),
    };

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    static List<PetdexPet> _cachedPets;

    public static async Task<List<PetdexPet>> LoadManifest()
    {
        if (_cachedPets != null) return _cachedPets;
        string body;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            body = await Http.GetStringAsync(ManifestUrl, cts.Token);
        }
        catch (Exception e)
        {
            throw new DeviceException($"petdex manifest 下载失败：{e.Message}");
        }
        try
        {
            using var doc = JsonDocument.Parse(body);
            var pets = new List<PetdexPet>();
            foreach (var item in doc.RootElement.GetProperty("pets").EnumerateArray())
            {
                if (!item.TryGetProperty("slug", out var slug)
                    || !item.TryGetProperty("spritesheetUrl", out var sheet)) continue;
                var slugStr = slug.GetString();
                pets.Add(new PetdexPet(
                    slugStr,
                    item.TryGetProperty("displayName", out var dn) ? dn.GetString() : slugStr,
                    item.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "",
                    sheet.GetString()));
            }
            _cachedPets = pets;
            return pets;
        }
        catch (Exception)
        {
            throw new DeviceException("petdex manifest 解析失败");
        }
    }

    public static async Task<MagickImage> DownloadSpritesheet(PetdexPet pet)
    {
        byte[] data;
        try
        {
            data = await Http.GetByteArrayAsync(pet.SpritesheetUrl);
        }
        catch (Exception e)
        {
            throw new DeviceException($"spritesheet 下载失败：{e.Message}");
        }
        try
        {
            return new MagickImage(data);
        }
        catch (Exception)
        {
            throw new DeviceException("spritesheet 解码失败（WebP）");
        }
    }

    /// Crops `state`'s row out of the sheet and encodes a looping GIF at
    /// targetW x targetH: frames aspect-fit, composited onto black (matches
    /// the clock's black background and avoids GIF transparency compositing
    /// surprises in the on-device decoder).
    public static byte[] BuildGif(MagickImage sheet, PetdexAnimState state,
                                  int targetW, int targetH)
    {
        var frameCount = Math.Min(state.Frames, MaxFrames);
        // 拒絕超出圖集的列，避免原生裁切回傳空白幀。
        if (frameCount <= 0 || state.Row < 0 || state.Row >= 9 ||
            sheet.Width != FrameW * 8 || sheet.Height != FrameH * 9 ||
            targetW <= 0 || targetH <= 0) return null;
        // GIF delays are centiseconds; floor at 5cs like the Mac app's 0.05s
        var delayCs = Math.Max(5, state.DurationMs / state.Frames / 10);

        // aspect-fit rect inside the target slot
        var scale = Math.Min((double)targetW / FrameW, (double)targetH / FrameH);
        var drawW = Math.Max(1, (int)Math.Round(FrameW * scale));
        var drawH = Math.Max(1, (int)Math.Round(FrameH * scale));
        var drawX = (targetW - drawW) / 2;
        var drawY = (targetH - drawH) / 2;

        try
        {
            // 每一幀持有獨立影像，集合負責釋放；黑底合成後不保留透明色。
            using var gif = new MagickImageCollection();
            for (int i = 0; i < frameCount; i++)
            {
                using var scaled = sheet.Clone();
                scaled.Crop(new MagickGeometry(i * FrameW, state.Row * FrameH,
                    (uint)FrameW, (uint)FrameH));
                scaled.ResetPage();
                scaled.Resize((uint)drawW, (uint)drawH);
                var frame = new MagickImage(MagickColors.Black, (uint)targetW, (uint)targetH);
                gif.Add(frame);
                frame.Composite(scaled, drawX, drawY, CompositeOperator.Over);
                frame.Alpha(AlphaOption.Off);
                frame.AnimationDelay = (uint)delayCs;
                frame.AnimationTicksPerSecond = 100;
                frame.AnimationIterations = 0;
                frame.GifDisposeMethod = GifDisposeMethod.Background;
            }
            return gif.ToByteArray(MagickFormat.Gif);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
