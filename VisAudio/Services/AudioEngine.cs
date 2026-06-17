using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VisAudio.Services;

public class AudioEngine : INotifyPropertyChanged
{
    private WaveOutEvent? _outputDevice;
    private ISampleProvider? _audioReader;
    private WaveStream? _waveStream;
    private float _volume = 1f;
    private float _preMuteVolume = 1f;
    private bool _isMuted;
    private TimeSpan _currentTime;
    private TimeSpan _totalDuration;
    private PlaybackState _playbackState = PlaybackState.Stopped;
    private TimeSpan? _loopA;
    private TimeSpan? _loopB;
    private readonly DispatcherTimer _positionTimer;

    public event PropertyChangedEventHandler? PropertyChanged;

    public WaveOutEvent? OutputDevice
    {
        get => _outputDevice;
        private set { _outputDevice = value; OnPropertyChanged(nameof(OutputDevice)); }
    }

    public ISampleProvider? AudioReader
    {
        get => _audioReader;
        private set { _audioReader = value; OnPropertyChanged(nameof(AudioReader)); }
    }

    public WaveStream? WaveStreamRef
    {
        get => _waveStream;
        private set { _waveStream = value; OnPropertyChanged(nameof(WaveStreamRef)); }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0f, 1f);
            if (_volume.Equals(clamped)) return;
            _volume = clamped;
            if (_outputDevice is not null)
                _outputDevice.Volume = _volume;
            OnPropertyChanged(nameof(Volume));
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value) return;
            if (value) { _preMuteVolume = _volume; Volume = 0f; }
            else { Volume = _preMuteVolume; }
            _isMuted = value;
            OnPropertyChanged(nameof(IsMuted));
        }
    }

    public TimeSpan CurrentTime
    {
        get => _currentTime;
        set
        {
            if (_waveStream is not null)
                _waveStream.CurrentTime = value;
            _currentTime = value;
            OnPropertyChanged(nameof(CurrentTime));
        }
    }

    public TimeSpan TotalDuration
    {
        get => _totalDuration;
        private set { if (_totalDuration.Equals(value)) return; _totalDuration = value; OnPropertyChanged(nameof(TotalDuration)); }
    }

    public PlaybackState PlaybackState
    {
        get => _playbackState;
        private set { if (_playbackState == value) return; _playbackState = value; OnPropertyChanged(nameof(PlaybackState)); }
    }

    public TimeSpan? LoopA { get => _loopA; set { _loopA = value; OnPropertyChanged(nameof(LoopA)); } }
    public TimeSpan? LoopB { get => _loopB; set { _loopB = value; OnPropertyChanged(nameof(LoopB)); } }

    public AudioEngine()
    {
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _positionTimer.Tick += OnPositionTimerTick;
    }

    public void LoadFile(string path)
    {
        Stop();

        var extension = Path.GetExtension(path).ToLowerInvariant();
        ISampleProvider reader;
        WaveStream waveStream;

        if (extension == ".flac")
        {
            var mfr = new MediaFoundationReader(path);
            waveStream = mfr;
            reader = new WaveToSampleProvider(mfr);
        }
        else
        {
            var afe = new AudioFileReader(path);
            waveStream = afe;
            reader = afe;
        }

        AudioReader = reader;
        WaveStreamRef = waveStream;
        TotalDuration = waveStream.TotalTime;

        _outputDevice = new WaveOutEvent();
        _outputDevice.Volume = _volume;
        _outputDevice.Init(_audioReader!);
        _outputDevice.PlaybackStopped += OnPlaybackStopped;
        OnPropertyChanged(nameof(OutputDevice));
    }

    public void InitWithProvider(ISampleProvider provider)
    {
        if (_outputDevice is not null)
        {
            _outputDevice.Stop();
            _outputDevice.PlaybackStopped -= OnPlaybackStopped;
            _outputDevice.Dispose();
        }

        _outputDevice = new WaveOutEvent();
        _outputDevice.Volume = _volume;
        _outputDevice.Init(provider);
        _outputDevice.PlaybackStopped += OnPlaybackStopped;
        OnPropertyChanged(nameof(OutputDevice));
    }

    public void Play()
    {
        if (_outputDevice is null || _audioReader is null) return;
        _outputDevice.Play();
        PlaybackState = PlaybackState.Playing;
        _positionTimer.Start();
    }

    public void Pause()
    {
        if (_outputDevice is null) return;
        _outputDevice.Pause();
        PlaybackState = PlaybackState.Paused;
        _positionTimer.Stop();
    }

    public void Stop()
    {
        if (_outputDevice is not null)
        {
            _outputDevice.Stop();
            _outputDevice.PlaybackStopped -= OnPlaybackStopped;
            _outputDevice.Dispose();
        }

        if (_waveStream is not null)
            _waveStream.Dispose();

        _outputDevice = null;
        _audioReader = null;
        _waveStream = null;
        PlaybackState = PlaybackState.Stopped;
        _positionTimer.Stop();

        CurrentTime = TimeSpan.Zero;
        TotalDuration = TimeSpan.Zero;
        LoopA = null;
        LoopB = null;

        OnPropertyChanged(nameof(OutputDevice));
        OnPropertyChanged(nameof(AudioReader));
        OnPropertyChanged(nameof(WaveStreamRef));
    }

    public void Resume()
    {
        if (_outputDevice is null || _audioReader is null) return;
        if (PlaybackState == PlaybackState.Paused)
        {
            _outputDevice.Play();
            PlaybackState = PlaybackState.Playing;
            _positionTimer.Start();
        }
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        UpdateCurrentTime();
        CheckAbLoop();
    }

    private void UpdateCurrentTime()
    {
        var newTime = _waveStream?.CurrentTime ?? TimeSpan.Zero;
        if (!_currentTime.Equals(newTime))
        {
            _currentTime = newTime;
            OnPropertyChanged(nameof(CurrentTime));
        }
    }

    private void CheckAbLoop()
    {
        if (_loopA.HasValue && _loopB.HasValue && _currentTime >= _loopB.Value)
            CurrentTime = _loopA.Value;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (PlaybackState != PlaybackState.Paused)
        {
            PlaybackState = PlaybackState.Stopped;
            _positionTimer.Stop();
        }
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
