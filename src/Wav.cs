// Wav: a 16-bit PCM .wav file into an AudioClip (the Uma race music). Unity can't load a file from disk into a clip
// without the web request module, and a wav is just a header plus samples. Decode is thread-safe (no Unity calls), so
// a 30 MB track can be read off the main thread; Clip makes the AudioClip on the main thread.
namespace StreamRacerApi;

static class Wav
{
    public class Samples
    {
        public float[] Data;
        public int Channels;
        public int Rate;
    }

    // Null when it isn't a RIFF/WAVE 16-bit PCM file.
    public static Samples Decode(byte[] bytes)
    {
        if (bytes.Length < 44 || System.Text.Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF")
        {
            return null;
        }
        int channels = 0, rate = 0, bits = 0, dataAt = -1, dataSize = 0;
        for (int at = 12; at + 8 <= bytes.Length;)
        {
            string chunk = System.Text.Encoding.ASCII.GetString(bytes, at, 4);
            int size = System.BitConverter.ToInt32(bytes, at + 4);
            if (chunk == "fmt ")
            {
                channels = System.BitConverter.ToInt16(bytes, at + 10);
                rate = System.BitConverter.ToInt32(bytes, at + 12);
                bits = System.BitConverter.ToInt16(bytes, at + 22);
            }
            else if (chunk == "data")
            {
                dataAt = at + 8;
                dataSize = System.Math.Min(size, bytes.Length - dataAt);
                break;
            }
            at += 8 + size + (size & 1);
        }
        if (bits != 16 || channels <= 0 || rate <= 0 || dataAt < 0)
        {
            return null;
        }
        var data = new float[dataSize / 2];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = System.BitConverter.ToInt16(bytes, dataAt + i * 2) / 32768f;
        }
        return new Samples
        {
            Data = data,
            Channels = channels,
            Rate = rate,
        };
    }

    public static AudioClip Clip(Samples samples, string name)
    {
        var clip = AudioClip.Create(name, samples.Data.Length / samples.Channels, samples.Channels, samples.Rate, false);
        clip.SetData(samples.Data, 0);
        return clip;
    }
}
