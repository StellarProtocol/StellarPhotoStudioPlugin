using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using Stellar.Abstractions.Domain;
using Stellar.PhotoStudio;
using Xunit;

namespace Stellar.PhotoStudio.Tests;

// Portrait-capture spec § 5 (approved mockup 2026-10-03): the frame guide dims OUTSIDE the shape, draws a border on it,
// thirds inside, and leaves the rest of the inside fully transparent (the photo area must not be tinted on screen).
public sealed class FrameGuidePainterTests
{
    private static byte Alpha(byte[] px, int w, int x, int y) => px[(y * w + x) * 4 + 3];

    [Fact]
    public void Portrait_guide_dims_outside_and_keeps_the_inside_clear()
    {
        const int w = 160, h = 90;
        var rect = ShapeFrame.GuideRect(PhotoShape.Portrait9x16, 1920, 1080);
        var px = FrameGuidePainter.Paint(w, h, rect);
        var (x0, y0, x1, y1) = FrameGuidePainter.PixelBox(w, h, rect);

        Assert.True(x0 > 0 && x1 < w);                       // portrait: side bands dimmed
        Assert.Equal(0, y0); Assert.Equal(h, y1);            // full height
        Assert.True(Alpha(px, w, 0, h / 2) > 100);           // left band dimmed
        Assert.True(Alpha(px, w, w - 1, h / 2) > 100);       // right band dimmed
        Assert.True(Alpha(px, w, x0, h / 2) > 200);          // border
        var midX = (x0 + x1) / 2 + 3;                        // off the thirds lines
        Assert.Equal(0, Alpha(px, w, midX, h / 2 + 5));      // inside stays transparent
    }

    [Fact]
    public void Wide_guide_letterboxes_top_and_bottom()
    {
        const int w = 160, h = 90;
        var rect = ShapeFrame.GuideRect(PhotoShape.Wide21x9, 1920, 1080);
        var (x0, y0, x1, y1) = FrameGuidePainter.PixelBox(w, h, rect);
        Assert.Equal(0, x0); Assert.Equal(w, x1);
        Assert.True(y0 > 0 && y1 < h);
        var px = FrameGuidePainter.Paint(w, h, rect);
        Assert.True(Alpha(px, w, w / 2, 0) > 100);
        Assert.True(Alpha(px, w, w / 2, h - 1) > 100);
    }

    [Fact]
    public void Thirds_lines_are_drawn_inside_the_frame()
    {
        const int w = 300, h = 300;
        var rect = ShapeFrame.GuideRect(PhotoShape.Square, w, h);   // square on a square screen = the whole canvas
        var px = FrameGuidePainter.Paint(w, h, rect);
        var (x1, _, _, _) = ShapeFrame.Thirds(rect);
        var lineX = (int)MathF.Round(x1 * w);
        Assert.InRange(Alpha(px, w, lineX, h / 2 + 7), 1, 199);     // faint line, not border, not clear
        Assert.Equal(0, Alpha(px, w, lineX + 5, h / 2 + 7));
    }

    [Fact]
    public void EncodePng_writes_a_valid_rgba_png_that_inflates_to_the_pixels()
    {
        const int w = 7, h = 3;
        var rgba = new byte[w * h * 4];
        for (var i = 0; i < rgba.Length; i++) rgba[i] = (byte)(i * 13);
        var png = FrameGuidePainter.EncodePng(rgba, w, h);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal(w, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(h, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        Assert.Equal(6, png[25]);                                    // colour type RGBA

        var idatLen = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33));
        Assert.Equal("IDAT", System.Text.Encoding.ASCII.GetString(png, 37, 4));
        using var z = new ZLibStream(new MemoryStream(png, 41, idatLen), CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        z.CopyTo(outMs);
        var raw = outMs.ToArray();
        Assert.Equal(h * (1 + w * 4), raw.Length);
        for (var y = 0; y < h; y++)
        {
            Assert.Equal(0, raw[y * (1 + w * 4)]);                   // filter 0 per row
            Assert.Equal(rgba.AsSpan(y * w * 4, w * 4).ToArray(), raw.AsSpan(y * (1 + w * 4) + 1, w * 4).ToArray());
        }

        // Review minor 7: the chunk CRC is pinned through the one chunk whose CRC is a known constant — an empty IEND
        // always ends 'AE 42 60 82' (CRC-32 of "IEND"); a broken table or a CRC over the wrong bytes changes it.
        Assert.Equal(new byte[] { 0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0xAE, 0x42, 0x60, 0x82 }, png[^12..]);
    }
}
