// Gif: an engine-free GIF decoder (GIF87a / GIF89a) for animated Twitch emotes. Reads the logical screen, global and
// local colour tables, LZW image data (interlaced or not) and the graphic control extension (delay, transparency,
// disposal), and composites every frame onto the full canvas. Frames come out as top-down RGBA bytes plus a delay
// in ms. No UnityEngine here, so tests/unit compiles it straight in.
using System.Collections.Generic;

namespace StreamRacerApi;

public sealed class GifFrame
{
    public byte[] Rgba;   // Width * Height * 4, rows top-down
    public int DelayMs;   // as stored in the file (centiseconds * 10); see Gif.PlaybackDelay
}

public sealed class GifImage
{
    public int Width;
    public int Height;
    public List<GifFrame> Frames = new();
}

public static class Gif
{
    const int MaxCodes = 4096;
    const int MinDelayMs = 20;        // browsers never go faster than this
    const int TooFastDelayMs = 10;    // 0 or 10 ms means "as fast as possible" -> browsers play it at...
    const int DefaultDelayMs = 100;   // ...100 ms

    // "GIF8" magic: an animated Twitch emote; a PNG starts with 0x89 'P' 'N' 'G'.
    public static bool IsGif(byte[] data) => data != null && data.Length >= 6
        && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8';

    // How long a frame really shows, the way browsers play it.
    public static int PlaybackDelay(int delayMs) => delayMs <= TooFastDelayMs
        ? DefaultDelayMs
        : System.Math.Max(MinDelayMs, delayMs);

    // The whole file -> canvas size + composited frames. Null when it isn't a GIF or has no readable frame; a file
    // cut short keeps the frames decoded so far.
    public static GifImage Decode(byte[] data)
    {
        if (!IsGif(data) || data.Length < 13)
        {
            return null;
        }
        var reader = new GifReader(data);
        try
        {
            reader.ReadAll();
        }
        catch (System.IndexOutOfRangeException)
        {
            // truncated file: keep what was decoded
        }
        return reader.Image.Frames.Count > 0 ? reader.Image : null;
    }

    // ---- the block walker ----

    sealed class GifReader
    {
        readonly byte[] _data;
        int _position;
        byte[] _globalColors;
        byte[] _canvas;
        public readonly GifImage Image = new();

        // The pending graphic control extension (applies to the next image only).
        int _delayMs;
        int _transparent = -1;
        int _disposal;

        public GifReader(byte[] data) => _data = data;

        public void ReadAll()
        {
            ReadScreen();
            while (_position < _data.Length)
            {
                byte block = _data[_position++];
                if (block == 0x3B)   // trailer
                {
                    return;
                }
                if (block == 0x21)
                {
                    ReadExtension();
                }
                else if (block == 0x2C)
                {
                    ReadImage();
                }
                else
                {
                    return;   // garbage: stop with what we have
                }
            }
        }

        void ReadScreen()
        {
            _position = 6;
            Image.Width = Short();
            Image.Height = Short();
            byte packed = _data[_position];
            _position += 3;   // packed, background index, aspect ratio
            if ((packed & 0x80) != 0)
            {
                _globalColors = Colors(packed & 7);
            }
            _canvas = new byte[Image.Width * Image.Height * 4];
        }

        void ReadExtension()
        {
            byte label = _data[_position++];
            if (label == 0xF9 && _data[_position] >= 4)
            {
                byte packed = _data[_position + 1];
                _disposal = (packed >> 2) & 7;
                _delayMs = (_data[_position + 2] | (_data[_position + 3] << 8)) * 10;
                _transparent = (packed & 1) != 0 ? _data[_position + 4] : -1;
            }
            SubBlocks();   // skips the extension's data either way
        }

        void ReadImage()
        {
            int left = Short();
            int top = Short();
            int width = Short();
            int height = Short();
            byte packed = _data[_position++];
            var colors = (packed & 0x80) != 0 ? Colors(packed & 7) : _globalColors;
            bool interlaced = (packed & 0x40) != 0;
            int minCodeSize = _data[_position++];
            var indices = Lzw(SubBlocks(), minCodeSize, width * height);
            var rect = new Rect(left, top, width, height);
            var before = _disposal == 3 ? (byte[])_canvas.Clone() : null;
            if (colors != null)
            {
                Draw(indices, colors, rect, interlaced);
            }
            Image.Frames.Add(new GifFrame
            {
                Rgba = (byte[])_canvas.Clone(),
                DelayMs = _delayMs,
            });
            Dispose(rect, before);
            _delayMs = 0;
            _transparent = -1;
            _disposal = 0;
        }

        // Disposal 2 = clear the frame's rectangle to transparent, 3 = put the canvas back; 0/1 = leave it.
        void Dispose(Rect rect, byte[] before)
        {
            if (_disposal == 3 && before != null)
            {
                _canvas = before;
            }
            else if (_disposal == 2)
            {
                Fill(rect);
            }
        }

        // ---- pixels ----

        void Draw(byte[] indices, byte[] colors, Rect rect, bool interlaced)
        {
            for (int row = 0; row < rect.Height; row++)
            {
                int y = rect.Top + (interlaced ? InterlacedRow(row, rect.Height) : row);
                if (y < Image.Height)
                {
                    DrawRow(indices, row * rect.Width, colors, rect, y);
                }
            }
        }

        void DrawRow(byte[] indices, int start, byte[] colors, Rect rect, int y)
        {
            for (int column = 0; column < rect.Width; column++)
            {
                int x = rect.Left + column;
                int index = indices[start + column];
                if (x >= Image.Width || index == _transparent || index * 3 + 2 >= colors.Length)
                {
                    continue;
                }
                int pixel = (y * Image.Width + x) * 4;
                _canvas[pixel] = colors[index * 3];
                _canvas[pixel + 1] = colors[index * 3 + 1];
                _canvas[pixel + 2] = colors[index * 3 + 2];
                _canvas[pixel + 3] = 255;
            }
        }

        void Fill(Rect rect)
        {
            for (int y = rect.Top; y < System.Math.Min(Image.Height, rect.Top + rect.Height); y++)
            {
                for (int x = rect.Left; x < System.Math.Min(Image.Width, rect.Left + rect.Width); x++)
                {
                    System.Array.Clear(_canvas, (y * Image.Width + x) * 4, 4);
                }
            }
        }

        // ---- bytes ----

        int Short()
        {
            int value = _data[_position] | (_data[_position + 1] << 8);
            _position += 2;
            return value;
        }

        // A colour table of 2^(sizeBits+1) RGB entries.
        byte[] Colors(int sizeBits)
        {
            int length = 3 * (1 << (sizeBits + 1));
            var table = new byte[length];
            System.Array.Copy(_data, _position, table, 0, System.Math.Min(length, _data.Length - _position));
            _position += length;
            return table;
        }

        // Length-prefixed sub-blocks up to the 0 terminator, joined.
        byte[] SubBlocks()
        {
            var joined = new List<byte>();
            while (true)
            {
                int length = _data[_position++];
                if (length == 0)
                {
                    return joined.ToArray();
                }
                for (int offset = 0; offset < length; offset++)
                {
                    joined.Add(_data[_position + offset]);
                }
                _position += length;
            }
        }
    }

    readonly struct Rect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Width;
        public readonly int Height;

        public Rect(int left, int top, int width, int height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }
    }

    // Interlaced images store rows in four passes: every 8th from 0, every 8th from 4, every 4th from 2, every 2nd
    // from 1. `row` is the stored order; the result is the row on screen.
    public static int InterlacedRow(int row, int height)
    {
        int[] starts = { 0, 4, 2, 1 };
        int[] steps = { 8, 8, 4, 2 };
        for (int pass = 0; pass < 4; pass++)
        {
            int rows = (height - starts[pass] + steps[pass] - 1) / steps[pass];
            if (row < rows)
            {
                return starts[pass] + row * steps[pass];
            }
            row -= System.Math.Max(0, rows);
        }
        return 0;
    }

    // ---- LZW ----

    // Variable-width codes, least significant bit first.
    sealed class CodeReader
    {
        readonly byte[] _data;
        int _position;
        int _bits;
        int _bitCount;

        public CodeReader(byte[] data) => _data = data;

        // The next `size`-bit code, or -1 when the data runs out.
        public int Read(int size)
        {
            while (_bitCount < size)
            {
                if (_position >= _data.Length)
                {
                    return -1;
                }
                _bits |= _data[_position++] << _bitCount;
                _bitCount += 8;
            }
            int code = _bits & ((1 << size) - 1);
            _bits >>= size;
            _bitCount -= size;
            return code;
        }
    }

    // Colour indices for `pixelCount` pixels (short data leaves the rest 0).
    public static byte[] Lzw(byte[] data, int minCodeSize, int pixelCount)
    {
        var output = new byte[pixelCount];
        if (minCodeSize < 1 || minCodeSize > 11)
        {
            return output;
        }
        int clear = 1 << minCodeSize;
        int end = clear + 1;
        var prefix = new short[MaxCodes];
        var suffix = new byte[MaxCodes];
        var first = new byte[MaxCodes];   // the first index of each code's string
        var stack = new byte[MaxCodes + 1];
        for (int code = 0; code < clear; code++)
        {
            suffix[code] = (byte)code;
            first[code] = (byte)code;
        }
        var reader = new CodeReader(data);
        int codeSize = minCodeSize + 1;
        int available = clear + 2;
        int previous = -1;
        int written = 0;
        while (written < pixelCount)
        {
            int code = reader.Read(codeSize);
            if (code < 0 || code == end || code > available)
            {
                break;
            }
            if (code == clear)
            {
                codeSize = minCodeSize + 1;
                available = clear + 2;
                previous = -1;
                continue;
            }
            if (previous < 0)
            {
                if (code >= clear)
                {
                    break;
                }
                output[written++] = (byte)code;
                previous = code;
                continue;
            }
            // code == available is the KwKwK case: the previous string plus its own first index, which goes out last
            // so it sits at the bottom of the stack
            bool known = code < available;
            byte head = known ? first[code] : first[previous];
            int top = 0;
            if (!known)
            {
                stack[top++] = head;
            }
            top = Unwind(known ? code : previous, clear, prefix, suffix, stack, top);
            if (available < MaxCodes)
            {
                prefix[available] = (short)previous;
                suffix[available] = head;
                first[available] = first[previous];
                available++;
                codeSize = available == 1 << codeSize && codeSize < 12 ? codeSize + 1 : codeSize;
            }
            while (top > 0 && written < pixelCount)
            {
                output[written++] = stack[--top];
            }
            previous = code;
        }
        return output;
    }

    // Pushes `code`'s string onto `stack` back to front (its last index first), from height `top`. Returns the new
    // height.
    static int Unwind(int code, int clear, short[] prefix, byte[] suffix, byte[] stack, int top)
    {
        while (code >= clear && top < MaxCodes)
        {
            stack[top++] = suffix[code];
            code = prefix[code];
        }
        stack[top++] = (byte)code;
        return top;
    }
}
