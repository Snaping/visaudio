using System.Collections.ObjectModel;
using NAudio.Dsp;
using NAudio.Wave;
using VisAudio.Models;

namespace VisAudio.Services;

public class InstrumentSeparator : ISampleProvider
{
    public ISampleProvider Source { get; }
    public ObservableCollection<InstrumentChannel> Channels { get; } = new();
    public WaveFormat WaveFormat => Source.WaveFormat;

    private float[] _tempBuffer = [];

    public InstrumentSeparator(ISampleProvider source)
    {
        Source = source;

        Channels.Add(new InstrumentChannel("Drums", 20, 5000, "🥁"));
        Channels.Add(new InstrumentChannel("Bass", 20, 250, "🎸"));
        Channels.Add(new InstrumentChannel("Cello", 65, 1000, "🎻"));
        Channels.Add(new InstrumentChannel("Erhu", 300, 3500, "🎻"));
        Channels.Add(new InstrumentChannel("Violin", 196, 4500, "🎻"));
        Channels.Add(new InstrumentChannel("Piano", 27, 4200, "🎹"));
        Channels.Add(new InstrumentChannel("Guitar", 80, 1300, "🎸"));
        Channels.Add(new InstrumentChannel("Vocal", 300, 4000, "🎤"));

        CreateFilters(Source.WaveFormat.SampleRate);
    }

    public void CreateFilters(int sampleRate)
    {
        foreach (var channel in Channels)
        {
            channel.Filters.Clear();
            channel.Filters.Add(BiQuadFilter.LowPassFilter(sampleRate, (float)channel.HighFreq, 0.7f));
            channel.Filters.Add(BiQuadFilter.HighPassFilter(sampleRate, (float)channel.LowFreq, 0.7f));
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_tempBuffer.Length < count)
            _tempBuffer = new float[count];

        int samplesRead = Source.Read(_tempBuffer, 0, count);

        int enabledCount = 0;
        foreach (var ch in Channels)
            if (ch.IsEnabled) enabledCount++;

        if (enabledCount == 0)
        {
            for (int i = 0; i < samplesRead; i++)
                buffer[offset + i] = 0f;
            return samplesRead;
        }

        if (enabledCount == Channels.Count)
        {
            for (int i = 0; i < samplesRead; i++)
                buffer[offset + i] = _tempBuffer[i];
            return samplesRead;
        }

        for (int i = 0; i < samplesRead; i++)
        {
            float source = _tempBuffer[i];
            float sample = 0f;

            foreach (var channel in Channels)
            {
                if (!channel.IsEnabled) continue;
                float filtered = source;
                foreach (var filter in channel.Filters)
                    filtered = filter.Transform(filtered);
                sample += (float)(filtered * channel.Volume);
            }

            sample /= enabledCount;
            buffer[offset + i] = sample;
        }

        return samplesRead;
    }

    public void RebuildFilters()
    {
        CreateFilters(Source.WaveFormat.SampleRate);
    }
}
