using System.Runtime.InteropServices;

namespace AIClockBridge;

// Windows GPU Engine 計數器跨 NVIDIA/AMD/Intel；不載入額外核心驅動。
sealed class GpuStatsReader : IDisposable
{
    IntPtr _query, _counter;
    public GpuStatsReader()
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0) return;
        if (PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _counter) != 0)
        { Dispose(); return; }
        PdhCollectQueryData(_query); // 百分比需要前後兩筆採樣。
    }

    public double? Read()
    {
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0) return null;
        uint bytes = 0, count;
        if (PdhGetFormattedCounterArrayW(_counter, 0x200, ref bytes, out count, IntPtr.Zero) != 0x800007D2 || bytes > 16 * 1024 * 1024)
            return null;
        var buffer = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (PdhGetFormattedCounterArrayW(_counter, 0x200, ref bytes, out count, buffer) != 0) return null;
            var samples = new List<(string Name, double Value)>();
            int stride = Marshal.SizeOf<CounterItem>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, i * stride));
                if (item.Value.Status <= 1 && double.IsFinite(item.Value.Number))
                    samples.Add((Marshal.PtrToStringUni(item.Name) ?? "", item.Value.Number));
            }
            return Aggregate(samples);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static double? Aggregate(IEnumerable<(string Name, double Value)> samples)
    {
        // 同一實體 engine 的不同 process 相加，再取最忙 engine，避免引擎間重複累加。
        var groups = new Dictionary<string, double>();
        foreach (var (name, value) in samples)
        {
            int start = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            if (start < 0 || !double.IsFinite(value) || value < 0) continue;
            var key = name[start..];
            groups[key] = groups.GetValueOrDefault(key) + value;
        }
        return groups.Count == 0 ? null : Math.Clamp(groups.Values.Max(), 0, 100);
    }

    public void Dispose() { if (_query != IntPtr.Zero) PdhCloseQuery(_query); _query = IntPtr.Zero; }
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    struct CounterValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Number; }
    [StructLayout(LayoutKind.Sequential)]
    struct CounterItem { public IntPtr Name; public CounterValue Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQueryW(string source, IntPtr data, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr data, out IntPtr counter);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr buffer);
    [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr query);
}
