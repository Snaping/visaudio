using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VisAudio.Services;

public class ExportService
{
    public void ExportToMp3(string inputPath, string outputPath, TimeSpan? startTime = null, TimeSpan? endTime = null, int bitrate = 192)
    {
        using var reader = CreateReader(inputPath);

        if (startTime.HasValue)
        {
            reader.CurrentTime = startTime.Value;
        }

        IWaveProvider source = EnsurePcm(reader);

        if (endTime.HasValue)
        {
            TimeSpan actualStart = startTime ?? TimeSpan.Zero;
            TimeSpan duration = endTime.Value - actualStart;
            source = new TimeLimitedWaveProvider(source, duration);
        }

        MediaFoundationEncoder.EncodeToMp3(source, outputPath, bitrate * 1000);
    }

    public void ExportToMp3(ISampleProvider source, string outputPath, TimeSpan duration, int bitrate = 192)
    {
        var pcmProvider = new SampleToWaveProvider16(source);
        var limitedProvider = new TimeLimitedWaveProvider(pcmProvider, duration);
        MediaFoundationEncoder.EncodeToMp3(limitedProvider, outputPath, bitrate * 1000);
    }

    private static WaveStream CreateReader(string inputPath)
    {
        string ext = Path.GetExtension(inputPath).ToLowerInvariant();
        if (ext == ".flac")
        {
            return new MediaFoundationReader(inputPath);
        }
        return new AudioFileReader(inputPath);
    }

    private static IWaveProvider EnsurePcm(IWaveProvider source)
    {
        if (source.WaveFormat.Encoding == WaveFormatEncoding.Pcm)
            return source;

        if (source is ISampleProvider sampleProvider)
            return new SampleToWaveProvider16(sampleProvider);

        return new SampleToWaveProvider16(new WaveToSampleProvider(source));
    }

    private class TimeLimitedWaveProvider : IWaveProvider
    {
        private readonly IWaveProvider _source;
        private readonly long _maxBytes;
        private long _totalBytesRead;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public TimeLimitedWaveProvider(IWaveProvider source, TimeSpan maxDuration)
        {
            _source = source;
            _maxBytes = (long)(maxDuration.TotalSeconds * source.WaveFormat.AverageBytesPerSecond);
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            long remaining = _maxBytes - _totalBytesRead;
            if (remaining <= 0)
                return 0;

            int bytesToRead = (int)Math.Min(count, remaining);
            int bytesRead = _source.Read(buffer, offset, bytesToRead);
            _totalBytesRead += bytesRead;
            return bytesRead;
        }
    }
}
