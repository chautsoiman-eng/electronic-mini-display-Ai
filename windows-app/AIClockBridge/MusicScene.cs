using System.Drawing;
using System.Drawing.Drawing2D;

namespace AIClockBridge;

static class MusicScene
{
    internal static readonly Color Green = Color.FromArgb(0, 217, 51);

    public static void Draw(Graphics g, string title, string artist, double elapsed,
                            double duration, bool playing, Image cover, int[] spectrum)
    {
        g.Clear(Color.Black);
        var coverRect = new Rectangle(56, 16, 128, 128);
        if (cover != null)
        {
            var state = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(cover, coverRect);
            g.Restore(state);
        }
        else
        {
            using var dark = new SolidBrush(Color.FromArgb(64, 64, 64));
            g.FillRectangle(dark, coverRect);
            using var font = new Font("Consolas", 13, FontStyle.Bold, GraphicsUnit.Pixel);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center };
            g.DrawString("No Art", font, Brushes.LightGray, new RectangleF(56, 72, 128, 20), fmt);
        }

        using var titleFmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        using (var font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold, GraphicsUnit.Pixel))
            g.DrawString(string.IsNullOrEmpty(title) ? "No Music" : title, font, Brushes.White,
                         new RectangleF(12, 154, 216, 24), titleFmt);
        using (var font = new Font("Microsoft YaHei UI", 12, FontStyle.Regular, GraphicsUnit.Pixel))
            g.DrawString(artist ?? "", font, Brushes.LightGray,
                         new RectangleF(12, 178, 216, 20), titleFmt);

        spectrum ??= new int[SpectrumAnalyzer.BarCount];
        using var barBg = new SolidBrush(Color.FromArgb(22, 54, 62));
        using var spectrumBrush = new SolidBrush(Green);
        for (var i = 0; i < SpectrumAnalyzer.BarCount; i++)
        {
            var value = i < spectrum.Length ? Math.Clamp(spectrum[i], 0, 100) : 0;
            var height = value * 28f / 100f;
            var x = 8 + i * 9.5f;
            g.FillRectangle(barBg, x, 201, 6, 28);
            if (height > 0) g.FillRectangle(spectrumBrush, x, 229 - height, 6, height);
        }
        var progress = new RectangleF(8, 233, 224, 3);
        using var progressBg = new SolidBrush(Color.FromArgb(64, 64, 64));
        g.FillRectangle(progressBg, progress);
        var fraction = duration > 0 ? (float)Math.Clamp(elapsed / duration, 0, 1) : 0;
        using var fill = new SolidBrush(playing ? Green : Color.Gray);
        g.FillRectangle(fill, progress.X, progress.Y, progress.Width * fraction, progress.Height);
    }
}
