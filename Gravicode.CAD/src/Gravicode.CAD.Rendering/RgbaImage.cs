using System.Buffers.Binary;
using System.IO.Compression;

namespace Gravicode.CAD.Rendering;

/// <summary>8-bit RGBA image with a minimal PNG encoder (no external dependencies).</summary>
public sealed class RgbaImage(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = new byte[width * height * 4];

    public void Set(int x, int y, CadColor c)
    {
        int i = (y * Width + x) * 4;
        Pixels[i] = ToByte(c.R); Pixels[i + 1] = ToByte(c.G); Pixels[i + 2] = ToByte(c.B); Pixels[i + 3] = ToByte(c.A);
    }

    public CadColor Get(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return new(Pixels[i] / 255.0, Pixels[i + 1] / 255.0, Pixels[i + 2] / 255.0, Pixels[i + 3] / 255.0);
    }

    private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);

    public byte[] EncodePng()
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], Height);
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        Chunk(ms, "IHDR", ihdr);
        using (var raw = new MemoryStream())
        {
            using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            {
                var row = new byte[Width * 4 + 1];
                var prev = new byte[Width * 4];
                for (int y = 0; y < Height; y++)
                {
                    // filter type 2 (Up) compresses smooth renders well
                    row[0] = 2;
                    var src = Pixels.AsSpan(y * Width * 4, Width * 4);
                    for (int i = 0; i < src.Length; i++) row[i + 1] = (byte)(src[i] - prev[i]);
                    src.CopyTo(prev);
                    z.Write(row);
                }
            }
            Chunk(ms, "IDAT", raw.ToArray());
        }
        Chunk(ms, "tEXt", System.Text.Encoding.Latin1.GetBytes($"Software\0{GravicodeCad.ProductName} {GravicodeCad.Version} - {GravicodeCad.Credits}"));
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    public void SavePng(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, EncodePng());
    }

    private static void Chunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        uint crc = Crc(t, 0xFFFFFFFFu);
        crc = Crc(data, crc) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(len, crc);
        s.Write(len);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(ReadOnlySpan<byte> data, uint crc)
    {
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    /// <summary>Box-filter downsample (used for supersampling anti-aliasing).</summary>
    public RgbaImage Downsample(int factor)
    {
        if (factor <= 1) return this;
        var r = new RgbaImage(Width / factor, Height / factor);
        int n = factor * factor;
        Parallel.For(0, r.Height, y =>
        {
            for (int x = 0; x < r.Width; x++)
            {
                int s0 = 0, s1 = 0, s2 = 0, s3 = 0;
                for (int dy = 0; dy < factor; dy++)
                    for (int dx = 0; dx < factor; dx++)
                    {
                        int i = ((y * factor + dy) * Width + x * factor + dx) * 4;
                        s0 += Pixels[i]; s1 += Pixels[i + 1]; s2 += Pixels[i + 2]; s3 += Pixels[i + 3];
                    }
                int o = (y * r.Width + x) * 4;
                r.Pixels[o] = (byte)(s0 / n); r.Pixels[o + 1] = (byte)(s1 / n); r.Pixels[o + 2] = (byte)(s2 / n); r.Pixels[o + 3] = (byte)(s3 / n);
            }
        });
        return r;
    }
}
