using System.Drawing;

namespace AIClockBridge;

sealed class ClockPreviewForm : Form
{
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    readonly IClockWeatherSource _weather;
    long _lastMinute = long.MinValue;

    public ClockPreviewForm(IClockWeatherSource weather = null)
    {
        _weather = weather ?? new NullClockWeatherSource();
        Text = "Clock — 台北時間即時預覽（非實機）";
        ClientSize = new Size(480, 480);
        MinimumSize = new Size(320, 320);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.Black;
        DoubleBuffered = true;
        _timer.Tick += (_, _) => RefreshMinute();
        Shown += (_, _) => { _timer.Start(); RefreshMinute(force: true); };
        FormClosed += (_, _) => _timer.Dispose();
    }

    void RefreshMinute(bool force = false)
    {
        var snapshot = ClockScene.FromUtc(DateTimeOffset.UtcNow, _weather.Snapshot);
        if (!force && snapshot.MinuteKey == _lastMinute) return;
        _lastMinute = snapshot.MinuteKey;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var scale = Math.Min(ClientSize.Width, ClientSize.Height) / 240f;
        var state = e.Graphics.Save();
        e.Graphics.TranslateTransform((ClientSize.Width - 240 * scale) / 2,
                                      (ClientSize.Height - 240 * scale) / 2);
        e.Graphics.ScaleTransform(scale, scale);
        ClockScene.Draw(e.Graphics, ClockScene.FromUtc(DateTimeOffset.UtcNow, _weather.Snapshot));
        e.Graphics.Restore(state);
    }
}
