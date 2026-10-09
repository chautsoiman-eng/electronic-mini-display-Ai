using System.Diagnostics;
using System.Globalization;
using System.Management;

namespace AIClockBridge;

sealed class PcMonitor : IDisposable
{
    readonly object _lock = new();
    readonly CancellationTokenSource _stop = new();
    readonly PcHistory _history = new();
    PcTelemetry _snapshot = new();
    (double? Cpu, double? Gpu, double? Load, long Ts) _sensors;
    bool _started;

    public PcTelemetry Snapshot()
    {
        lock (_lock) return _snapshot.At(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }
    public void Start()
    {
        if (_started) return;
        _started = true;
        _ = Task.Run(SampleLoop);
        _ = Task.Run(SensorLoop);
    }
    async Task SampleLoop()
    {
        using var gpu = new GpuStatsReader();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var stats = SystemStatsMonitor.Snapshot();
                double? load = gpu.Read();
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                lock (_lock)
                {
                    _history.Add(now, stats.Cpu);
                    bool fresh = now - _sensors.Ts <= 15 && now >= _sensors.Ts;
                    _snapshot = new PcTelemetry
                    {
                        Ts = now, Seq = _snapshot.Seq + 1, Stale = false,
                        CpuPct = PcTelemetry.Percent(stats.Cpu), MemPct = PcTelemetry.Percent(stats.Mem),
                        GpuPct = PcTelemetry.Percent(load ?? (fresh ? _sensors.Load : null)),
                        CpuTempC = fresh ? _sensors.Cpu : null, GpuTempC = fresh ? _sensors.Gpu : null,
                        CpuHistory = _history.Snapshot(),
                    };
                }
                if (!await timer.WaitForNextTickAsync(_stop.Token)) break;
            }
        }
        catch (OperationCanceledException) { }
    }

    async Task SensorLoop()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var nvidia = await ReadNvidia(_stop.Token);
                var cpu = ReadCpuTemperature();
                lock (_lock) _sensors = (cpu, nvidia.Temp, nvidia.Load, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                await Task.Delay(5000, _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
    }

    internal static (double? Load, double? Temp) ParseNvidia(string csv)
    {
        double? load = null, temp = null;
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(',');
            if (fields.Length != 3) continue;
            if (double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var l)
                && PcTelemetry.Percent(l) is double validLoad) load = Math.Max(load ?? 0, validLoad);
            if (double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)
                && PcTelemetry.Temperature(t) is double validTemp) temp = Math.Max(temp ?? -20, validTemp);
        }
        return (load, temp);
    }

    static async Task<(double? Load, double? Temp)> ReadNvidia(CancellationToken stop)
    {
        var path = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
        if (!File.Exists(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
        if (!File.Exists(path)) return (null, null);
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo(path,
                "--query-gpu=index,utilization.gpu,temperature.gpu --format=csv,noheader,nounits")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
            timeout.CancelAfter(2000);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { process.Kill(true); } catch { } return (null, null); }
            await error;
            return process.ExitCode == 0 ? ParseNvidia(await output) : (null, null);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        { return (null, null); }
    }

    static double? ReadCpuTemperature()
    {
        // 僅讀已運行的公開感測 provider。ACPI thermal zone 不是 CPU 核心溫度，不混用。
        foreach (var scope in new[] { @"root\LibreHardwareMonitor", @"root\OpenHardwareMonitor" })
        {
            try
            {
                using var query = new ManagementObjectSearcher(scope,
                    "SELECT Identifier, Value FROM Sensor WHERE SensorType='Temperature'");
                query.Options.Timeout = TimeSpan.FromSeconds(2);
                using var results = query.Get();
                double? highest = null;
                foreach (ManagementObject sensor in results)
                {
                    using (sensor)
                    {
                        string id = sensor["Identifier"]?.ToString() ?? "";
                        if (!(id.StartsWith("/intelcpu/", StringComparison.OrdinalIgnoreCase)
                            || id.StartsWith("/amdcpu/", StringComparison.OrdinalIgnoreCase))) continue;
                        if (sensor["Value"] == null) continue;
                        var value = PcTelemetry.Temperature(Convert.ToDouble(sensor["Value"], CultureInfo.InvariantCulture));
                        if (value.HasValue) highest = Math.Max(highest ?? -20, value.Value);
                    }
                }
                if (highest.HasValue) return highest;
            }
            catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
        }
        return null;
    }

    public void Dispose() => _stop.Cancel();
}
