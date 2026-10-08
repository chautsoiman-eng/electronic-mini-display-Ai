using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIClockBridge;

// 傳輸固定為 snake_case；未知用 null，歷史最多 60 秒，適合 ESP8266 記憶體。
sealed record PcTelemetry
{
    public long Ts { get; init; }
    public long Seq { get; init; }
    public int IntervalMs { get; init; } = 1000;
    public double? CpuPct { get; init; }
    public double? GpuPct { get; init; }
    public double? MemPct { get; init; }
    public double? CpuTempC { get; init; }
    public double? GpuTempC { get; init; }
    public double?[] CpuHistory { get; init; } = Array.Empty<double?>();
    public bool Stale { get; init; } = true;
    public static double? Percent(double? value) => Valid(value, 0, 100);
    public static double? Temperature(double? value) => Valid(value, -20, 150);
    static double? Valid(double? v, double min, double max) => v.HasValue && double.IsFinite(v.Value)
        && v >= min && v <= max ? Math.Round(v.Value, 1) : null;
    public PcTelemetry At(long now) => now - Ts > 5 || now < Ts
        ? this with { Stale = true, CpuPct = null, GpuPct = null, MemPct = null, CpuTempC = null, GpuTempC = null }
        : this;
    public byte[] ToJson() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

sealed class PcHistory
{
    readonly Queue<double?> _values = new();
    long _lastTs;
    public void Add(long ts, double? cpu)
    {
        // 休眠／採樣中斷留下空白，不把斷線前後畫成連續曲線。
        if (_lastTs != 0)
            for (long i = 1; i < Math.Min(60, ts - _lastTs); i++) Push(null);
        Push(PcTelemetry.Percent(cpu));
        _lastTs = ts;
    }
    void Push(double? value) { _values.Enqueue(value); while (_values.Count > 60) _values.Dequeue(); }
    public double?[] Snapshot() => _values.ToArray();
}
