// GifTests: the engine-free GIF decoder (src/Gif.cs) on tiny hand-made files: colour tables, LZW, frame delays,
// transparency, disposal methods, interlacing, and the browser-style playback delay.
using System;
using Xunit;

namespace StreamRacerApi.Tests;

public class GifTests
{
    // 2x2, palette red/green/blue/white, three frames:
    //  1. full canvas red, green / blue, white; 50 ms; disposal 1 (keep)
    //  2. 1x1 red at (1,1); 0 ms; disposal 2 (clear its rectangle afterwards)
    //  3. full canvas, index 3 transparent, only (0,1) green; 120 ms
    const string ThreeFrames = "47494638396102000200910000ff000000ff000000ffffffff21f90404050000002c0000000002000200"
        + "0002034434050021f90408000000002c010001000100010000020244010021f904010c0003002c0000000002000200000203dc"
        + "3205003b";

    // 16x16 interlaced, one frame, 100 ms; pixel (x, y) = odd row: (y*16+x)%3, even row: ((y+x)/5)%4.
    const string Interlaced = "47494638396110001000910000ff000000ff000000ffffffff21f904000a0000002c00000000100010004002"
        + "37841d99c23cdbc498f32015a2a4a3f16101ca81651185699fa17261b65a65260af54d07b89df3b6bfdbf582392290764cd686"
        + "4c9e315800003b";

    // The classic 1x1 transparent GIF89a.
    const string Pixel = "47494638396101000100800000000000ffffff21f90401000000002c000000000100010000020144003b";

    static readonly byte[][] Palette =
    {
        new byte[] { 255, 0, 0, 255 },
        new byte[] { 0, 255, 0, 255 },
        new byte[] { 0, 0, 255, 255 },
        new byte[] { 255, 255, 255, 255 },
    };

    static readonly byte[] Clear = { 0, 0, 0, 0 };

    static byte[] PixelAt(GifFrame frame, int width, int x, int y) =>
        frame.Rgba.AsSpan((y * width + x) * 4, 4).ToArray();

    [Fact]
    public void Recognises_gif_magic_and_rejects_png()
    {
        Assert.True(Gif.IsGif(Convert.FromHexString(Pixel)));
        Assert.False(Gif.IsGif(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 }));
        Assert.False(Gif.IsGif(null));
        Assert.Null(Gif.Decode(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void First_frame_has_palette_colours_and_delay()
    {
        var image = Gif.Decode(Convert.FromHexString(ThreeFrames));
        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(3, image.Frames.Count);
        var first = image.Frames[0];
        Assert.Equal(50, first.DelayMs);
        Assert.Equal(Palette[0], PixelAt(first, 2, 0, 0));
        Assert.Equal(Palette[1], PixelAt(first, 2, 1, 0));
        Assert.Equal(Palette[2], PixelAt(first, 2, 0, 1));
        Assert.Equal(Palette[3], PixelAt(first, 2, 1, 1));
    }

    [Fact]
    public void Partial_frame_composites_over_the_kept_canvas()
    {
        var second = Gif.Decode(Convert.FromHexString(ThreeFrames)).Frames[1];
        Assert.Equal(0, second.DelayMs);
        Assert.Equal(Palette[0], PixelAt(second, 2, 0, 0));
        Assert.Equal(Palette[1], PixelAt(second, 2, 1, 0));
        Assert.Equal(Palette[2], PixelAt(second, 2, 0, 1));
        Assert.Equal(Palette[0], PixelAt(second, 2, 1, 1));   // the new red pixel
    }

    [Fact]
    public void Transparent_pixels_keep_the_canvas_and_disposal_2_clears()
    {
        var third = Gif.Decode(Convert.FromHexString(ThreeFrames)).Frames[2];
        Assert.Equal(120, third.DelayMs);
        Assert.Equal(Palette[0], PixelAt(third, 2, 0, 0));   // transparent index: frame 1 shows through
        Assert.Equal(Palette[1], PixelAt(third, 2, 1, 0));
        Assert.Equal(Palette[1], PixelAt(third, 2, 0, 1));   // drawn green
        Assert.Equal(Clear, PixelAt(third, 2, 1, 1));        // frame 2's rectangle was cleared, nothing drawn on it
    }

    [Fact]
    public void Interlaced_rows_land_in_place()
    {
        var image = Gif.Decode(Convert.FromHexString(Interlaced));
        Assert.Single(image.Frames);
        Assert.Equal(100, image.Frames[0].DelayMs);
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int index = y % 2 == 1 ? (y * 16 + x) % 3 : (y + x) / 5 % 4;
                Assert.Equal(Palette[index], PixelAt(image.Frames[0], 16, x, y));
            }
        }
    }

    [Fact]
    public void Transparent_single_pixel_decodes_clear()
    {
        var image = Gif.Decode(Convert.FromHexString(Pixel));
        Assert.Single(image.Frames);
        Assert.Equal(Clear, PixelAt(image.Frames[0], 1, 0, 0));
    }

    [Fact]
    public void Truncated_file_keeps_the_frames_read_so_far()
    {
        var bytes = Convert.FromHexString(ThreeFrames);
        var image = Gif.Decode(bytes.AsSpan(0, bytes.Length - 12).ToArray());
        Assert.Equal(2, image.Frames.Count);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(10, 100)]
    [InlineData(20, 20)]
    [InlineData(30, 30)]
    [InlineData(500, 500)]
    public void Playback_delay_follows_browsers(int stored, int played)
    {
        Assert.Equal(played, Gif.PlaybackDelay(stored));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 8)]
    [InlineData(2, 4)]
    [InlineData(3, 12)]
    [InlineData(4, 2)]
    [InlineData(8, 1)]
    [InlineData(15, 15)]
    public void Interlaced_row_order_for_16_rows(int stored, int screen)
    {
        Assert.Equal(screen, Gif.InterlacedRow(stored, 16));
    }
}
