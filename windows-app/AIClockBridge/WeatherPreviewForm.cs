using System.Drawing;

namespace AIClockBridge;

sealed class WeatherPreviewForm : Form
{
    readonly WeatherMonitor _weather;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    public WeatherPreviewForm(WeatherMonitor weather)
    {
        _weather = weather;
        Text = "Weather — 即時預覽（非實機）";
        ClientSize = new Size(480, 480);
        MinimumSize = new Size(320, 320);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        DoubleBuffered = true;
        _timer.Tick += (_, _) => Invalidate();
        Shown += (_, _) => { _timer.Start(); Invalidate(); };
        FormClosed += (_, _) => _timer.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var scale = Math.Min(ClientSize.Width, ClientSize.Height) / 240f;
        var state = e.Graphics.Save();
        e.Graphics.TranslateTransform((ClientSize.Width - 240 * scale) / 2,
                                      (ClientSize.Height - 240 * scale) / 2);
        e.Graphics.ScaleTransform(scale, scale);
        WeatherScene.Draw(e.Graphics, _weather.Snapshot);
        e.Graphics.Restore(state);
    }
}
