using AIClockBridge;
using ImageMagick;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;

internal static class Program
{
    static int count;
    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        count++;
    }

    [STAThread]
    static void Main(string[] args)
    {
        // 九列各有八種不同色塊，透明邊界用來驗證裁切與黑底合成。
        using var source = new MagickImage(MagickColors.Transparent, 1536, 1872);
        for (int row = 0; row < 9; row++)
            for (int col = 0; col < 8; col++)
            {
                using var block = new MagickImage(new MagickColor(
                    (byte)(30 + col * 25), (byte)(30 + row * 22), 100), 96, 104);
                source.Composite(block, col * 192 + 48, row * 208 + 52, CompositeOperator.Over);
            }
        source.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
        using var sheet = new MagickImage(source.ToByteArray(MagickFormat.WebP));
        Check(sheet.Width == 1536 && sheet.Height == 1872, "WebP round trip");
        foreach (var state in PetdexService.States)
            foreach (int width in new[] { 111, 120 })
                Validate(sheet, state, width);
        Validate(sheet, new("cap", "cap", 0, 12, 1200), 120);
        Check(PetdexService.BuildGif(sheet, new("empty", "", 0, 0, 0), 120, 120) == null, "empty state");
        Check(PetdexService.BuildGif(sheet, new("invalid", "", 20, 1, 100), 120, 120) == null, "invalid row");

        // 呼叫真實 WinForms 事件，立即清除選擇、換快取或關窗，再處理非同步回呼。
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        for (int iteration = 0; iteration < 12; iteration++)
        {
            using var form = (Form)Activator.CreateInstance(typeof(PetPickerForm), true);
            var pet = new PetdexPet("fixture", "Fixture", "", "");
            Field("_filtered").SetValue(form, new List<PetdexPet> { pet });
            Field("_sheetCache").SetValue(form, (pet.Slug, new MagickImage(sheet)));
            var list = (ListBox)Field("_listBox").GetValue(form);
            list.Items.Add("Fixture");
            form.Show();
            list.SelectedIndex = 0;
            if (iteration % 3 == 0)
            {
                form.Dispose();
                Pump(300);
                Check(form.IsDisposed, "close while converting");
                continue;
            }
            if (iteration % 3 == 1)
            {
                list.SelectedIndex = -1;
                var cached = ((string Slug, MagickImage Image))Field("_sheetCache").GetValue(form);
                cached.Image.Dispose();
                Field("_sheetCache").SetValue(form, null);
                Pump(300);
                Check(((PictureBox)Field("_preview").GetValue(form)).Image == null, "stale result discarded");
                continue;
            }
            var preview = (PictureBox)Field("_preview").GetValue(form);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (preview.Image == null && DateTime.UtcNow < deadline) Pump(10);
            Check(preview.Image != null, "animated preview");
            var stream = (MemoryStream)Field("_previewStream").GetValue(form);
            Check(stream.CanRead, "preview stream remains open");
            // 測試上傳轉檔與角色切換交錯，假裝置不接觸真實硬體。
            var upload = (Task)typeof(PetPickerForm).GetMethod("UploadTapped",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
            list.SelectedIndex = -1;
            var cache = ((string Slug, MagickImage Image))Field("_sheetCache").GetValue(form);
            cache.Image.Dispose();
            Field("_sheetCache").SetValue(form, null);
            while (!upload.IsCompleted) Pump(10);
            upload.GetAwaiter().GetResult();
            Check(!stream.CanRead, "stream disposed on selection change");
            Check(DeviceClient.UploadCount == 0, "stale upload cancelled");
            Field("_sheetCache").SetValue(form, (pet.Slug, new MagickImage(sheet)));
            list.SelectedIndex = 0;
            deadline = DateTime.UtcNow.AddSeconds(10);
            while (preview.Image == null && DateTime.UtcNow < deadline) Pump(10);
            var successfulUpload = (Task)typeof(PetPickerForm).GetMethod("UploadTapped",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
            while (!successfulUpload.IsCompleted) Pump(10);
            successfulUpload.GetAwaiter().GetResult();
            Check(DeviceClient.UploadCount == 1 && DeviceClient.LastSlot == "claude" &&
                DeviceClient.LastGif?.Length > 0, "selected GIF passed to upload");
            DeviceClient.UploadCount = 0;
            var closingStream = (MemoryStream)Field("_previewStream").GetValue(form);
            form.Dispose();
            Check(!closingStream.CanRead, "stream disposed on close");
        }
        if (args.Contains("--live"))
        {
            // 額外的真實 petdex 下載驗證；CI 使用離線 fixture，避免外站中斷影響回歸。
            var pets = PetdexService.LoadManifest().GetAwaiter().GetResult();
            Check(pets.Count > 0, "live manifest");
            using var liveSheet = PetdexService.DownloadSpritesheet(pets[0]).GetAwaiter().GetResult();
            foreach (int width in new[] { 111, 120 })
            {
                var data = PetdexService.BuildGif(liveSheet, PetdexService.States[7], width, 120);
                Check(data?.Length > 0, "live WebP to GIF");
                using var liveStream = new MemoryStream(data);
                using var liveImage = Image.FromStream(liveStream);
                Check(liveImage.Width == width && liveImage.Height == 120 &&
                    liveImage.GetFrameCount(FrameDimension.Time) == 6, "live GIF size and frames");
            }
            Console.WriteLine($"Live petdex: {pets[0].Slug}");
        }
        Console.WriteLine($"PASS: {count} pet image/lifetime checks");
    }

    static FieldInfo Field(string name) => typeof(PetPickerForm).GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic);
    static void Pump(int ms)
    {
        var until = Environment.TickCount64 + ms;
        do { Application.DoEvents(); Thread.Sleep(1); } while (Environment.TickCount64 < until);
    }
    static void Validate(MagickImage sheet, PetdexAnimState state, int width)
    {
        var bytes = PetdexService.BuildGif(sheet, state, width, 120);
        Check(bytes != null, "GIF generated");
        using var stream = new MemoryStream(bytes);
        // 使用 Windows GDI+ 獨立解碼，避免只用同一函式庫自我驗證。
        using var gif = Image.FromStream(stream);
        int frames = Math.Min(8, state.Frames);
        Check(gif.Width == width && gif.Height == 120, "slot dimensions");
        Check(gif.GetFrameCount(FrameDimension.Time) == frames, "frame count");
        var delays = gif.GetPropertyItem(0x5100).Value;
        int expectedDelay = Math.Max(5, state.DurationMs / state.Frames / 10);
        Check(Enumerable.Range(0, frames).All(i =>
            BitConverter.ToInt32(delays, i * 4) == expectedDelay), "frame delays and duration");
        Check(BitConverter.ToUInt16(gif.GetPropertyItem(0x5101).Value) == 0, "infinite loop");
        for (int i = 0; i < frames; i++)
        {
            gif.SelectActiveFrame(FrameDimension.Time, i);
            using var bitmap = new Bitmap(gif);
            var corner = bitmap.GetPixel(0, 0);
            Check(corner.A == 255 && corner.R == 0 && corner.G == 0 && corner.B == 0, "opaque black background");
            var center = bitmap.GetPixel(width / 2, 60);
            Check(Math.Abs(center.R - (30 + i * 25)) <= 3 &&
                Math.Abs(center.G - (30 + state.Row * 22)) <= 3, "correct row and frame crop");
        }
    }
}

namespace AIClockBridge
{
    sealed class DeviceException(string message) : Exception(message);
    static class DeviceClient
    {
        public static int UploadCount;
        public static string LastSlot;
        public static byte[] LastGif;
        public static Task UploadGif(byte[] data, string slot)
        {
            UploadCount++;
            LastSlot = slot;
            LastGif = data;
            return Task.CompletedTask;
        }
    }
}
