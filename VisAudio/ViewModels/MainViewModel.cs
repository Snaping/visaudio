using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VisAudio.Models;
using VisAudio.Services;

namespace VisAudio.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class MainViewModel : INotifyPropertyChanged
{
    private readonly AudioEngine _engine;
    private readonly ExportService _exportService;
    private InstrumentSeparator? _instrumentSeparator;
    private EqualizerService? _equalizerService;
    private FftAnalyzer? _fftAnalyzer;
    private WaveOutEvent? _outputDevice;
    private ISampleProvider? _audioReader;
    private IDisposable? _readerDisposable;
    private string _currentFilePath = string.Empty;
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _visualizationTimer;
    private float _preMuteVolume = 1f;

    private TimeSpan _currentTime;
    private TimeSpan _totalDuration;
    private float _volume = 1f;
    private bool _isMuted;
    private PlaybackState _playbackState = PlaybackState.Stopped;
    private string _currentFileName = string.Empty;
    private int _selectedPlaylistIndex = -1;
    private string _currentPlaylistPath = string.Empty;
    private TimeSpan? _loopA;
    private TimeSpan? _loopB;
    private float[] _waveformData = [];
    private float[] _fftData = [];
    private float[]? _waveformBufferA;
    private float[]? _waveformBufferB;
    private float[]? _fftBufferA;
    private float[]? _fftBufferB;
    private bool _vizBufferToggle;
    private List<LrcLine> _lrcLines = [];
    private string _currentLyricText = string.Empty;
    private string _nextLyricText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AudioEngine Engine => _engine;

    public ObservableCollection<InstrumentChannel> InstrumentChannels => _instrumentSeparator?.Channels ?? [];

    public ObservableCollection<EqualizerBand> EqualizerBands => _equalizerService?.Bands ?? [];

    public TimeSpan CurrentTime
    {
        get => _currentTime;
        set
        {
            if (_currentTime == value) return;
            SetReaderTime(value);
            _currentTime = value;
            OnPropertyChanged(nameof(CurrentTime));
            OnPropertyChanged(nameof(CurrentTimeString));
            OnPropertyChanged(nameof(PlayPosition));
            UpdateLyrics();
        }
    }

    public TimeSpan TotalDuration
    {
        get => _totalDuration;
        set
        {
            if (_totalDuration == value) return;
            _totalDuration = value;
            OnPropertyChanged(nameof(TotalDuration));
            OnPropertyChanged(nameof(TotalDurationString));
            OnPropertyChanged(nameof(PlayPosition));
            OnPropertyChanged(nameof(LoopStartPosition));
            OnPropertyChanged(nameof(LoopEndPosition));
        }
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
            _engine.Volume = _volume;
            OnPropertyChanged(nameof(Volume));
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted == value) return;
            if (value)
            {
                _preMuteVolume = _volume;
                Volume = 0f;
            }
            else
            {
                Volume = _preMuteVolume;
            }
            _isMuted = value;
            _engine.IsMuted = value;
            OnPropertyChanged(nameof(IsMuted));
        }
    }

    public PlaybackState PlaybackState
    {
        get => _playbackState;
        private set
        {
            if (_playbackState == value) return;
            _playbackState = value;
            OnPropertyChanged(nameof(PlaybackState));
            OnPropertyChanged(nameof(IsPlaying));
        }
    }

    public bool IsPlaying => _playbackState == PlaybackState.Playing;

    public string CurrentTimeString => _currentTime.ToString(@"mm\:ss");

    public string TotalDurationString => _totalDuration.ToString(@"mm\:ss");

    public string CurrentFileName
    {
        get => _currentFileName;
        set { if (_currentFileName != value) { _currentFileName = value; OnPropertyChanged(nameof(CurrentFileName)); } }
    }

    public ObservableCollection<PlaylistItem> PlaylistItems { get; }

    public int SelectedPlaylistIndex
    {
        get => _selectedPlaylistIndex;
        set
        {
            if (_selectedPlaylistIndex == value) return;
            _selectedPlaylistIndex = value;
            OnPropertyChanged(nameof(SelectedPlaylistIndex));
            RemoveFileCommand.RaiseCanExecuteChanged();
            MoveUpCommand.RaiseCanExecuteChanged();
            MoveDownCommand.RaiseCanExecuteChanged();
        }
    }

    public string CurrentPlaylistPath
    {
        get => _currentPlaylistPath;
        set { if (_currentPlaylistPath != value) { _currentPlaylistPath = value; OnPropertyChanged(nameof(CurrentPlaylistPath)); } }
    }

    public TimeSpan? LoopA
    {
        get => _loopA;
        set { if (_loopA != value) { _loopA = value; OnPropertyChanged(nameof(LoopA)); OnPropertyChanged(nameof(LoopStartPosition)); } }
    }

    public TimeSpan? LoopB
    {
        get => _loopB;
        set { if (_loopB != value) { _loopB = value; OnPropertyChanged(nameof(LoopB)); OnPropertyChanged(nameof(LoopEndPosition)); } }
    }

    public float[] WaveformData
    {
        get => _waveformData;
        set { _waveformData = value; OnPropertyChanged(nameof(WaveformData)); }
    }

    public float[] FftData
    {
        get => _fftData;
        set { _fftData = value; OnPropertyChanged(nameof(FftData)); }
    }

    public double PlayPosition => _totalDuration > TimeSpan.Zero
        ? _currentTime.TotalSeconds / _totalDuration.TotalSeconds
        : 0;

    public double? LoopStartPosition => _loopA.HasValue && _totalDuration > TimeSpan.Zero
        ? _loopA.Value.TotalSeconds / _totalDuration.TotalSeconds
        : null;

    public double? LoopEndPosition => _loopB.HasValue && _totalDuration > TimeSpan.Zero
        ? _loopB.Value.TotalSeconds / _totalDuration.TotalSeconds
        : null;

    public List<LrcLine> LrcLines
    {
        get => _lrcLines;
        set
        {
            _lrcLines = value;
            OnPropertyChanged(nameof(LrcLines));
            OnPropertyChanged(nameof(CurrentLyricText));
            OnPropertyChanged(nameof(NextLyricText));
        }
    }

    public string CurrentLyricText
    {
        get => _currentLyricText;
        set { if (_currentLyricText != value) { _currentLyricText = value; OnPropertyChanged(nameof(CurrentLyricText)); } }
    }

    public string NextLyricText
    {
        get => _nextLyricText;
        set { if (_nextLyricText != value) { _nextLyricText = value; OnPropertyChanged(nameof(NextLyricText)); } }
    }

    public RelayCommand AddFilesCommand { get; }
    public RelayCommand RemoveFileCommand { get; }
    public RelayCommand SavePlaylistCommand { get; }
    public RelayCommand LoadPlaylistCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand PlayCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand PlaySelectedCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand SetLoopACommand { get; }
    public RelayCommand SetLoopBCommand { get; }
    public RelayCommand ClearLoopCommand { get; }
    public RelayCommand ToggleChannelCommand { get; }
    public RelayCommand UpdateBandGainCommand { get; }
    public RelayCommand LoadLrcCommand { get; }
    public RelayCommand ExportCommand { get; }

    public MainViewModel()
    {
        _engine = new AudioEngine();
        _exportService = new ExportService();
        PlaylistItems = [];

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _positionTimer.Tick += OnPositionTimerTick;

        _visualizationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _visualizationTimer.Tick += OnVisualizationTimerTick;

        AddFilesCommand = new RelayCommand(ExecuteAddFiles);
        RemoveFileCommand = new RelayCommand(ExecuteRemoveFile, _ => SelectedPlaylistIndex >= 0);
        SavePlaylistCommand = new RelayCommand(ExecuteSavePlaylist);
        LoadPlaylistCommand = new RelayCommand(ExecuteLoadPlaylist);
        MoveUpCommand = new RelayCommand(ExecuteMoveUp, _ => SelectedPlaylistIndex > 0);
        MoveDownCommand = new RelayCommand(ExecuteMoveDown,
            _ => SelectedPlaylistIndex >= 0 && SelectedPlaylistIndex < PlaylistItems.Count - 1);
        PlayCommand = new RelayCommand(_ => Play());
        PauseCommand = new RelayCommand(_ => Pause());
        StopCommand = new RelayCommand(_ => Stop());
        ResumeCommand = new RelayCommand(_ => Resume());
        PlaySelectedCommand = new RelayCommand(ExecutePlaySelected, _ => SelectedPlaylistIndex >= 0);
        PreviousCommand = new RelayCommand(ExecutePrevious);
        NextCommand = new RelayCommand(ExecuteNext);
        SetLoopACommand = new RelayCommand(_ => LoopA = CurrentTime);
        SetLoopBCommand = new RelayCommand(_ => LoopB = CurrentTime);
        ClearLoopCommand = new RelayCommand(_ => { LoopA = null; LoopB = null; });
        ToggleChannelCommand = new RelayCommand(ExecuteToggleChannel);
        UpdateBandGainCommand = new RelayCommand(ExecuteUpdateBandGain);
        LoadLrcCommand = new RelayCommand(ExecuteLoadLrc);
        ExportCommand = new RelayCommand(ExecuteExport, _ => _audioReader != null);

        _engine.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName!);
    }

    public void LoadAndPlay(string path)
    {
        try
        {
            Stop();

            var extension = Path.GetExtension(path).ToLowerInvariant();
            ISampleProvider reader;

            if (extension == ".flac")
            {
                var mfr = new MediaFoundationReader(path);
                reader = new WaveToSampleProvider(mfr);
                _readerDisposable = mfr;
            }
            else
            {
                var afe = new AudioFileReader(path);
                reader = afe;
                _readerDisposable = afe;
            }

            _audioReader = reader;
            _currentFilePath = path;
            CurrentFileName = Path.GetFileName(path);
            TotalDuration = GetReaderDuration();

            _instrumentSeparator = new InstrumentSeparator(reader);
            _equalizerService = new EqualizerService(_instrumentSeparator);
            _fftAnalyzer = new FftAnalyzer(_equalizerService);

            _outputDevice = new WaveOutEvent { DesiredLatency = 200, NumberOfBuffers = 4 };
            _outputDevice.Volume = _volume;
            _outputDevice.Init(_fftAnalyzer);
            _outputDevice.PlaybackStopped += OnPlaybackStopped;

            SubscribeToFftEvents();
            OnPropertyChanged(nameof(InstrumentChannels));
            OnPropertyChanged(nameof(EqualizerBands));
            ExportCommand.RaiseCanExecuteChanged();

            _outputDevice.Play();
            PlaybackState = PlaybackState.Playing;
            _positionTimer.Start();
            _visualizationTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载或播放失败：{ex.Message}\n\n{ex.StackTrace}",
                "播放错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Stop();
        }
    }

    public void Play()
    {
        try
        {
            if (_outputDevice is null || _audioReader is null) return;
            _outputDevice.Play();
            PlaybackState = PlaybackState.Playing;
            _positionTimer.Start();
            _visualizationTimer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"播放失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Pause()
    {
        try
        {
            if (_outputDevice is null) return;
            _outputDevice.Pause();
            PlaybackState = PlaybackState.Paused;
            _positionTimer.Stop();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"暂停失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Resume()
    {
        try
        {
            if (_outputDevice is null || _audioReader is null)
            {
                if (PlaylistItems.Count > 0)
                {
                    int index = SelectedPlaylistIndex >= 0 ? SelectedPlaylistIndex : 0;
                    SelectedPlaylistIndex = index;
                    LoadAndPlay(PlaylistItems[index].FilePath);
                }
                return;
            }

            if (PlaybackState == PlaybackState.Paused)
            {
                _outputDevice.Play();
                PlaybackState = PlaybackState.Playing;
                _positionTimer.Start();
                _visualizationTimer.Start();
            }
            else if (PlaybackState == PlaybackState.Stopped)
            {
                Play();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"恢复播放失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Stop()
    {
        try
        {
            if (_outputDevice is not null)
            {
                _outputDevice.Stop();
                _outputDevice.PlaybackStopped -= OnPlaybackStopped;
                _outputDevice.Dispose();
                _outputDevice = null;
            }

            _readerDisposable?.Dispose();
            _readerDisposable = null;
            _audioReader = null;
            _instrumentSeparator = null;
            _equalizerService = null;
            _fftAnalyzer = null;

            PlaybackState = PlaybackState.Stopped;
            _positionTimer.Stop();
            _visualizationTimer.Stop();

            CurrentTime = TimeSpan.Zero;
            TotalDuration = TimeSpan.Zero;
            LoopA = null;
            LoopB = null;
            CurrentFileName = string.Empty;
            _currentFilePath = string.Empty;
            WaveformData = [];
            FftData = [];

            OnPropertyChanged(nameof(InstrumentChannels));
            OnPropertyChanged(nameof(EqualizerBands));
            ExportCommand.RaiseCanExecuteChanged();
        }
        catch { }
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        var newTime = GetReaderCurrentTime();
        if (_currentTime != newTime)
        {
            _currentTime = newTime;
            OnPropertyChanged(nameof(CurrentTime));
            OnPropertyChanged(nameof(CurrentTimeString));
            OnPropertyChanged(nameof(PlayPosition));
            UpdateLyrics();
        }

        if (_loopA.HasValue && _loopB.HasValue && _currentTime >= _loopB.Value)
        {
            CurrentTime = _loopA.Value;
        }
    }

    private void OnVisualizationTimerTick(object? sender, EventArgs e)
    {
        UpdateVisualization();
    }

    private void UpdateVisualization()
    {
        if (_fftAnalyzer is null) return;

        var srcWave = _fftAnalyzer.WaveformData;
        var srcFft = _fftAnalyzer.FftResults;

        if (_waveformBufferA is null || _waveformBufferA.Length != srcWave.Length)
        {
            _waveformBufferA = new float[srcWave.Length];
            _waveformBufferB = new float[srcWave.Length];
            _fftBufferA = new float[srcFft.Length];
            _fftBufferB = new float[srcFft.Length];
        }

        _vizBufferToggle = !_vizBufferToggle;

        if (_vizBufferToggle)
        {
            Array.Copy(srcWave, _waveformBufferA!, srcWave.Length);
            Array.Copy(srcFft, _fftBufferA!, srcFft.Length);
            WaveformData = _waveformBufferA!;
            FftData = _fftBufferA!;
        }
        else
        {
            Array.Copy(srcWave, _waveformBufferB!, srcWave.Length);
            Array.Copy(srcFft, _fftBufferB!, srcFft.Length);
            WaveformData = _waveformBufferB!;
            FftData = _fftBufferB!;
        }
    }

    private void SubscribeToFftEvents()
    {
        if (_fftAnalyzer is null) return;
        _fftAnalyzer.FftCalculated += (_, _) => UpdateVisualization();
    }

    private void UpdateLyrics()
    {
        if (_lrcLines.Count == 0)
        {
            CurrentLyricText = string.Empty;
            NextLyricText = string.Empty;
            return;
        }

        int currentIndex = -1;
        for (int i = _lrcLines.Count - 1; i >= 0; i--)
        {
            if (_currentTime >= _lrcLines[i].Timestamp)
            {
                currentIndex = i;
                break;
            }
        }

        CurrentLyricText = currentIndex >= 0 ? _lrcLines[currentIndex].Text : string.Empty;
        NextLyricText = currentIndex >= 0 && currentIndex + 1 < _lrcLines.Count
            ? _lrcLines[currentIndex + 1].Text
            : string.Empty;
    }

    private void SetReaderTime(TimeSpan time)
    {
        if (_audioReader is AudioFileReader afe)
            afe.CurrentTime = time;
        else if (_readerDisposable is MediaFoundationReader mfr)
            mfr.CurrentTime = time;
    }

    private TimeSpan GetReaderCurrentTime()
    {
        if (_audioReader is AudioFileReader afe)
            return afe.CurrentTime;
        if (_readerDisposable is MediaFoundationReader mfr)
            return mfr.CurrentTime;
        return TimeSpan.Zero;
    }

    private TimeSpan GetReaderDuration()
    {
        if (_audioReader is AudioFileReader afe)
            return afe.TotalTime;
        if (_readerDisposable is MediaFoundationReader mfr)
            return mfr.TotalTime;
        return TimeSpan.Zero;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (e.Exception != null)
            {
                MessageBox.Show($"播放错误：{e.Exception.Message}\n\n{e.Exception.StackTrace}",
                    "播放错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            if (PlaybackState != PlaybackState.Paused)
            {
                PlaybackState = PlaybackState.Stopped;
                _positionTimer.Stop();
                _visualizationTimer.Stop();
            }
        });
    }

    private void ExecuteAddFiles(object? _)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Audio Files|*.mp3;*.wav;*.flac|All Files|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
                PlaylistItems.Add(new PlaylistItem(file));
        }
    }

    private void ExecuteRemoveFile(object? _)
    {
        if (SelectedPlaylistIndex >= 0 && SelectedPlaylistIndex < PlaylistItems.Count)
            PlaylistItems.RemoveAt(SelectedPlaylistIndex);
    }

    private void ExecuteSavePlaylist(object? _)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Playlist Files|*.xml",
            DefaultExt = ".xml"
        };

        if (dialog.ShowDialog() != true) return;

        CurrentPlaylistPath = dialog.FileName;
        var doc = new XmlDocument();
        var root = doc.CreateElement("Playlist");
        doc.AppendChild(root);

        foreach (var item in PlaylistItems)
        {
            var elem = doc.CreateElement("Item");
            elem.SetAttribute("FilePath", item.FilePath);
            root.AppendChild(elem);
        }

        doc.Save(CurrentPlaylistPath);
    }

    private void ExecuteLoadPlaylist(object? _)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Playlist Files|*.xml",
            DefaultExt = ".xml"
        };

        if (dialog.ShowDialog() != true) return;

        CurrentPlaylistPath = dialog.FileName;
        PlaylistItems.Clear();

        var doc = new XmlDocument();
        doc.Load(CurrentPlaylistPath);

        var items = doc.SelectNodes("//Item");
        if (items is null) return;

        foreach (XmlNode node in items)
        {
            var path = node.Attributes?["FilePath"]?.Value;
            if (path is not null)
                PlaylistItems.Add(new PlaylistItem(path));
        }
    }

    private void ExecuteMoveUp(object? _)
    {
        if (SelectedPlaylistIndex <= 0) return;
        int i = SelectedPlaylistIndex;
        (PlaylistItems[i - 1], PlaylistItems[i]) = (PlaylistItems[i], PlaylistItems[i - 1]);
        SelectedPlaylistIndex = i - 1;
    }

    private void ExecuteMoveDown(object? _)
    {
        if (SelectedPlaylistIndex < 0 || SelectedPlaylistIndex >= PlaylistItems.Count - 1) return;
        int i = SelectedPlaylistIndex;
        (PlaylistItems[i], PlaylistItems[i + 1]) = (PlaylistItems[i + 1], PlaylistItems[i]);
        SelectedPlaylistIndex = i + 1;
    }

    private void ExecutePlaySelected(object? _)
    {
        if (SelectedPlaylistIndex >= 0 && SelectedPlaylistIndex < PlaylistItems.Count)
            LoadAndPlay(PlaylistItems[SelectedPlaylistIndex].FilePath);
    }

    private void ExecutePrevious(object? _)
    {
        if (PlaylistItems.Count == 0) return;
        int index = SelectedPlaylistIndex - 1;
        if (index < 0) index = PlaylistItems.Count - 1;
        SelectedPlaylistIndex = index;
        LoadAndPlay(PlaylistItems[index].FilePath);
    }

    private void ExecuteNext(object? _)
    {
        if (PlaylistItems.Count == 0) return;
        int index = SelectedPlaylistIndex + 1;
        if (index >= PlaylistItems.Count) index = 0;
        SelectedPlaylistIndex = index;
        LoadAndPlay(PlaylistItems[index].FilePath);
    }

    private void ExecuteToggleChannel(object? param)
    {
        if (param is InstrumentChannel channel)
            channel.IsEnabled = !channel.IsEnabled;
    }

    private void ExecuteUpdateBandGain(object? param)
    {
        if (param is ValueTuple<int, double> tuple)
            _equalizerService?.UpdateBandGain(tuple.Item1, tuple.Item2);
    }

    private void ExecuteLoadLrc(object? _)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "LRC Files|*.lrc|All Files|*.*"
        };

        if (dialog.ShowDialog() != true) return;
        LrcLines = LrcParser.Parse(dialog.FileName);
    }

    private void ExecuteExport(object? _)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "MP3 Files|*.mp3",
            DefaultExt = ".mp3"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            if (LoopA.HasValue && LoopB.HasValue)
            {
                _exportService.ExportToMp3(_currentFilePath, dialog.FileName, LoopA, LoopB);
            }
            else
            {
                _exportService.ExportToMp3(_currentFilePath, dialog.FileName);
            }
        }
        catch
        {
            MessageBox.Show("Export failed.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
