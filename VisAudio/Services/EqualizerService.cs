using System.Collections.ObjectModel;
using NAudio.Dsp;
using NAudio.Wave;
using VisAudio.Models;

namespace VisAudio.Services;

public class EqualizerService : ISampleProvider
{
    public ISampleProvider Source { get; }

    public WaveFormat WaveFormat => Source.WaveFormat;

    public ObservableCollection<EqualizerBand> Bands { get; }

    private BiQuadFilter[] _filters;

    public EqualizerService(ISampleProvider source)
    {
        Source = source;

        Bands =
        [
            new EqualizerBand("31Hz", 31),
            new EqualizerBand("62Hz", 62),
            new EqualizerBand("125Hz", 125),
            new EqualizerBand("250Hz", 250),
            new EqualizerBand("500Hz", 500),
            new EqualizerBand("1kHz", 1000),
            new EqualizerBand("2kHz", 2000),
            new EqualizerBand("4kHz", 4000),
            new EqualizerBand("8kHz", 8000),
            new EqualizerBand("16kHz", 16000)
        ];

        _filters = new BiQuadFilter[Bands.Count];
        RebuildFilters();
    }

    public void RebuildFilters()
    {
        int sampleRate = Source.WaveFormat.SampleRate;
        for (int i = 0; i < Bands.Count; i++)
        {
            var band = Bands[i];
            _filters[i] = BiQuadFilter.PeakingEQ(sampleRate, (float)band.CenterFrequency, (float)band.Bandwidth, (float)band.GainDb);
        }
    }

    public void UpdateBandGain(int bandIndex, double gainDb)
    {
        if (bandIndex < 0 || bandIndex >= Bands.Count)
            return;

        gainDb = Math.Clamp(gainDb, -12, 12);
        Bands[bandIndex].GainDb = gainDb;

        int sampleRate = Source.WaveFormat.SampleRate;
        var band = Bands[bandIndex];
        _filters[bandIndex] = BiQuadFilter.PeakingEQ(sampleRate, (float)band.CenterFrequency, (float)band.Bandwidth, (float)band.GainDb);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = Source.Read(buffer, offset, count);

        for (int i = 0; i < samplesRead; i++)
        {
            float sample = buffer[offset + i];
            for (int b = 0; b < _filters.Length; b++)
            {
                sample = _filters[b].Transform(sample);
            }
            buffer[offset + i] = sample;
        }

        return samplesRead;
    }
}
