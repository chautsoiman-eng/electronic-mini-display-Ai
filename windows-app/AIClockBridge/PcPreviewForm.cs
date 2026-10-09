using System.Drawing;

namespace AIClockBridge;

// 不需要電子鐘、不讀 OAuth 帳號，也不把模擬資料冒充實機畫面。
sealed class PcPreviewForm : Form
{
    readonly PcMonitor _monitor;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    public PcPreviewForm(PcMonitor monitor)
    {
        _monitor = monitor;
        Text = "PC 監控 — 本機即時預覽（非實機）";
        ClientSize = new Size(480, 480);
        BackColor = Color.Black;
        DoubleBuffered = true;
        MinimumSize = new Size(280, 300);
        _timer.Tick += (_, _) => Invalidate();
        _timer.Start();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float scale = Math.Min(ClientSize.Width, ClientSize.Height) / 240f;
        e.Graphics.TranslateTransform((ClientSize.Width - 240 * scale) / 2, (ClientSize.Height - 240 * scale) / 2);
        e.Graphics.ScaleTransform(scale, scale);
        PcMonitorScene.Draw(e.Graphics, _monitor.Snapshot(), true);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
