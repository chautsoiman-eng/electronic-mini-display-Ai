using System.Numerics;
using NAudio.Wave;

namespace AIClockBridge;

// 擷取 Windows 正在播放的系統音訊，轉成固定 24 條頻譜。
// 擷取裝置不存在或被占用時保持全零，不會影響音樂資訊或主程式。
sealed class AudioSpectrumMonitor : IDisposable
{
    readonly object _gate = new();
    readonly List<float> _samples = new(4096);
    readonly int[] _levels = new int[SpectrumAnalyzer.BarCount];
    WasapiLoopbackCapture _capture;
    DateTime _lastAudioAt = DateTime.MinValue;

    public int[] Levels
    {
        get
        {
            lock (_gate)
            {
                if (DateTime.UtcNow - _lastAudioAt > TimeSpan.FromSeconds(2))
                    return new int[SpectrumAnalyzer.BarCount];
                return (int[])_levels.Clone();
            }
        }
    }

    public void Start()
    {
        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += (_, _) => { };
            _capture.StartRecording();
        }
        catch
        {
            _capture?.Dispose();
            _capture = null;
        }
    }

    void OnData(object sender, WaveInEventArgs e)
    {
        var format = _capture?.WaveFormat;
        if (format == null || e.BytesRecorded <= 0) return;
        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = Math.Max(1, format.BitsPerSample / 8);
        var frameBytes = bytesPerSample * channels;
        lock (_gate)
        {
            for (var offset = 0; offset + frameBytes <= e.BytesRecorded; offset += frameBytes)
            {
                double sum = 0;
                for (var channel = 0; channel < channels; channel++)
                    sum += ReadSample(e.Buffer, offset + channel * bytesPerSample, format);
                _samples.Add((float)(sum / channels));
            }
            if (_samples.Count >= SpectrumAnalyzer.FftSize)
            {
                var start = _samples.Count - SpectrumAnalyzer.FftSize;
                var next = SpectrumAnalyzer.Analyze(
                    _samples.GetRange(start, SpectrumAnalyzer.FftSize).ToArray(), format.SampleRate);
                for (var i = 0; i < _levels.Length; i++)
                    _levels[i] = Math.Max(next[i], (int)Math.Round(_levels[i] * 0.68));
                if (_samples.Count > SpectrumAnalyzer.FftSize * 2)
                    _samples.RemoveRange(0, _samples.Count - SpectrumAnalyzer.FftSize);
                _lastAudioAt = DateTime.UtcNow;
            }
        }
    }

    static float ReadSample(byte[] data, int offset, WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            return BitConverter.ToSingle(data, offset);
        if (format.BitsPerSample == 16)
            return BitConverter.ToInt16(data, offset) / 32768f;
        if (format.BitsPerSample == 24)
        {
            var value = data[offset] | data[offset + 1] << 8 | data[offset + 2] << 16;
            if ((value & 0x800000) != 0) value |= unchecked((int)0xff000000);
            return value / 8388608f;
        }
        if (format.BitsPerSample == 32)
            return BitConverter.ToInt32(data, offset) / 2147483648f;
        return 0;
    }

    public void Dispose()
    {
        try { _capture?.StopRecording(); } catch { }
        _capture?.Dispose();
    }
}

static class SpectrumAnalyzer
{
    public const int BarCount = 24;
    public const int FftSize = 2048;

    internal static int[] Analyze(float[] input, int sampleRate)
    {
        if (input == null || input.Length < FftSize || sampleRate <= 0)
            return new int[BarCount];
        var bins = new Complex[FftSize];
        for (var i = 0; i < FftSize; i++)
        {
            var hann = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));
            bins[i] = new Complex(input[i] * hann, 0);
        }
        Fft(bins);
        var levels = new int[BarCount];
        const double minHz = 55;
        var maxHz = Math.Min(16000, sampleRate / 2.0);
        for (var bar = 0; bar < BarCount; bar++)
        {
            var low = minHz * Math.Pow(maxHz / minHz, bar / (double)BarCount);
            var high = minHz * Math.Pow(maxHz / minHz, (bar + 1) / (double)BarCount);
            var first = Math.Max(1, (int)Math.Floor(low * FftSize / sampleRate));
            var last = Math.Min(FftSize / 2 - 1, (int)Math.Ceiling(high * FftSize / sampleRate));
            double power = 0;
            for (var i = first; i <= last; i++) power = Math.Max(power, bins[i].Magnitude);
            var db = 20 * Math.Log10(Math.Max(power * 2 / FftSize, 1e-7));
            levels[bar] = (int)Math.Round(Math.Clamp((db + 70) / 60 * 100, 0, 100));
        }
        return levels;
    }

    static void Fft(Complex[] values)
    {
        for (int i = 1, j = 0; i < values.Length; i++)
        {
            var bit = values.Length >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (values[i], values[j]) = (values[j], values[i]);
        }
        for (var length = 2; length <= values.Length; length <<= 1)
        {
            var root = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
            for (var start = 0; start < values.Length; start += length)
            {
                var factor = Complex.One;
                for (var j = 0; j < length / 2; j++)
                {
                    var even = values[start + j];
                    var odd = values[start + j + length / 2] * factor;
                    values[start + j] = even + odd;
                    values[start + j + length / 2] = even - odd;
                    factor *= root;
                }
            }
        }
    }
}
