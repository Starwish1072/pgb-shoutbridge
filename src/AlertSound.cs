using System;
using System.IO;
using System.Text;

namespace PgbShoutBridge;

/// <summary>生成一段两音上行提示音（44.1kHz/16bit/立体声 WAV），并可直接播放。</summary>
public static class AlertSound
{
    private static byte[] _wav;

    public static byte[] Wav => _wav ??= Build();

    private static byte[] Build()
    {
        const int sr = 44100;
        const double dur = 0.55;
        var n = (int)(sr * dur);
        var samples = new short[n];
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / sr;
            double v = 0;
            if (t < 0.18)
            {
                var env = Math.Min(1.0, t / 0.008) * Math.Min(1.0, (0.18 - t) / 0.03);
                v += 0.55 * Math.Sin(2 * Math.PI * 880 * t) * env;
            }
            else if (t >= 0.22)
            {
                var tt = t - 0.22;
                var env = Math.Min(1.0, tt / 0.008) * Math.Min(1.0, Math.Max(0, (0.34 - tt)) / 0.10);
                v += 0.55 * Math.Sin(2 * Math.PI * 1320 * tt) * env;
            }
            samples[i] = (short)(Math.Max(-1, Math.Min(1, v)) * 32000);
        }

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        var dataSize = n * 4; // stereo 16-bit
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)2);
        bw.Write(sr);
        bw.Write(sr * 4);
        bw.Write((short)4);
        bw.Write((short)16);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        foreach (var s in samples) { bw.Write(s); bw.Write(s); }
        bw.Flush();
        return ms.ToArray();
    }
}
