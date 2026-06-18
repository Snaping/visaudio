using System.ComponentModel;
using NAudio.Dsp;
using NAudio.Wave;

namespace VisAudio.Services;

public class FftAnalyzer : ISampleProvider, INotifyPropertyChanged
{
    public const int FftSize = 2048;

    private readonly ISampleProvider _source;
    private readonly float[] _waveformBuffer;
    private readonly Complex[] _fftBuffer;
    private int _waveformWritePos;
    private int _fftAccumulated;

    public ISampleProvider Source => _source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float[] FftResults { get; }

    public float[] WaveformData { get; }

    public event EventHandler? FftCalculated;
    public event PropertyChangedEventHandler? PropertyChanged;

    public FftAnalyzer(ISampleProvider source)
    {
        _source = source;
        FftResults = new float[FftSize / 2];
        WaveformData = new float[1024];
        _waveformBuffer = new float[1024];
        _fftBuffer = new Complex[FftSize];
        _waveformWritePos = 0;
        _fftAccumulated = 0;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);

        for (int i = 0; i < samplesRead; i++)
        {
            float sample = buffer[offset + i];

            _waveformBuffer[_waveformWritePos] = sample;
            _waveformWritePos = (_waveformWritePos + 1) % _waveformBuffer.Length;

            if (_fftAccumulated < FftSize)
            {
                _fftBuffer[_fftAccumulated].X = sample;
                _fftBuffer[_fftAccumulated].Y = 0f;
                _fftAccumulated++;
            }

            if (_fftAccumulated >= FftSize)
            {
                PerformFft();
                _fftAccumulated = 0;
            }
        }

        Array.Copy(_waveformBuffer, _waveformWritePos, WaveformData, 0, _waveformBuffer.Length - _waveformWritePos);
        Array.Copy(_waveformBuffer, 0, WaveformData, _waveformBuffer.Length - _waveformWritePos, _waveformWritePos);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WaveformData)));

        return samplesRead;
    }

    private void PerformFft()
    {
        FastFourierTransform.FFT(true, (int)Math.Log2(FftSize), _fftBuffer);

        float normalization = 2f / FftSize;
        for (int i = 0; i < FftResults.Length; i++)
        {
            double real = _fftBuffer[i].X;
            double imag = _fftBuffer[i].Y;
            FftResults[i] = (float)Math.Sqrt(real * real + imag * imag) * normalization;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FftResults)));
        FftCalculated?.Invoke(this, EventArgs.Empty);
    }
}
