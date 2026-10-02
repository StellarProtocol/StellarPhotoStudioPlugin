using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using Stellar.Abstractions.Domain;

namespace Stellar.PhotoStudio;

/// <summary>Draws the frame guide (portrait-capture spec § 5) as a PNG: the area outside the shape's rectangle dimmed, a
/// light border on the rectangle and rule-of-thirds lines inside it; everything else transparent. Pure managed code
/// (no Unity, no camera — the game's URP renders the whole world through an offscreen camera, docs/
/// rendering-images-in-game.md), so it is unit-tested directly.</summary>
internal static class FrameGuidePainter
{
    private const byte DimAlpha = 140;       // ~55 % — the mockup's rgba(8,6,14,.55)
    private const byte BorderAlpha = 217;    // ~85 % white border
    private const byte ThirdsAlpha = 72;     // ~28 % white thirds lines

    /// <summary>RGBA pixels (row 0 = top) for a <paramref name="width"/> × <paramref name="height"/> guide around
    /// <paramref name="rect"/> (normalized, top-left origin). Border and lines are <paramref name="line"/> px wide.</summary>
    public static byte[] Paint(int width, int height, NormalizedRect rect, int line = 1)
    {
        var px = new byte[width * height * 4];
        var (x0, y0, x1, y1) = PixelBox(width, height, rect);
        var thirds = ShapeFrame.Thirds(rect);
        int tx1 = (int)MathF.Round(thirds.X1 * width), tx2 = (int)MathF.Round(thirds.X2 * width);
        int ty1 = (int)MathF.Round(thirds.Y1 * height), ty2 = (int)MathF.Round(thirds.Y2 * height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var inside = x >= x0 && x < x1 && y >= y0 && y < y1;
                if (!inside) { Set(px, width, x, y, 8, 6, 14, DimAlpha); continue; }
                if (x < x0 + line || x >= x1 - line || y < y0 + line || y >= y1 - line) Set(px, width, x, y, 255, 255, 255, BorderAlpha);
                else if (Near(x, tx1, line) || Near(x, tx2, line) || Near(y, ty1, line) || Near(y, ty2, line))
                    Set(px, width, x, y, 255, 255, 255, ThirdsAlpha);
            }
        return px;
    }

    /// <summary>The guide's clear rectangle in pixels: [x0, x1) × [y0, y1).</summary>
    public static (int X0, int Y0, int X1, int Y1) PixelBox(int width, int height, NormalizedRect r) =>
        ((int)MathF.Round(r.X * width), (int)MathF.Round(r.Y * height),
         (int)MathF.Round((r.X + r.Width) * width), (int)MathF.Round((r.Y + r.Height) * height));

    /// <summary>Encodes RGBA pixels (row 0 = top) as a PNG (8-bit RGBA, zlib-compressed, filter 0).</summary>
    public static byte[] EncodePng(byte[] rgba, int width, int height)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = width * 4;
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgba, y * row, row);
            }
        }
        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; ihdr[9] = 6;   // 8-bit, RGBA; compression/filter/interlace 0
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static bool Near(int v, int at, int line) => v >= at && v < at + line;

    private static void Set(byte[] px, int width, int x, int y, byte r, byte g, byte b, byte a)
    {
        var i = (y * width + x) * 4;
        px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = a;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = Crc32(typeBytes, data);
        var c = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(byte[] a, byte[] b)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in a) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in b) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
