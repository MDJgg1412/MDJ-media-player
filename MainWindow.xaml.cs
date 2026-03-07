using MDJMediaPlayer.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.IO;
using System.Collections.Generic;
using System.Buffers;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using VlcMedia = LibVLCSharp.Shared.Media;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace MDJMediaPlayer
{
    public partial class MainWindow : Window
    {
        private MainViewModel ViewModel => (MainViewModel)DataContext!;
        private System.Windows.Threading.DispatcherTimer? _timer;
        private bool _isUserDraggingPosition = false;
        private System.Windows.Threading.DispatcherTimer? _hideControlsTimer;
        private bool _controlsVisible = true;
        private int _controlsAnimationVersion = 0;
        private Point _lastMousePosition;
        private bool _hasLastMousePosition = false;
        private ExtendedModeWindow? _extendedModeWindow;
        private bool _isExtendedMode = false;
        private SFXWindow? _sfxWindow;
        private readonly HashSet<string> _audioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".aac", ".flac", ".m4a", ".mp3", ".ogg", ".wav", ".wma"
        };
        private System.Windows.Threading.DispatcherTimer? _audioWaveTimer;
        private double[] _audioWaveEnvelope = Array.Empty<double>();
        private bool _isCurrentTrackAudioOnly = false;
        private const int AudioWavePointCount = 180;
        private const int AudioEnergyHistorySize = 4096;
        private const int AudioSampleHistorySize = 16384;
        private const int SpectrumBarCount = 96;
        private const double SpectrumBarGap = 0.6d;
        private const double SpectrumTailProfile = 0.38d;
        private const uint ProbeSampleRate = 44100;
        private const uint ProbeChannels = 2;
        private const int ProbeSampleDecimation = 8;
        private const int WaveSmoothingPasses = 1;
        private const long WaveProbeMaxFileBytes = 768L * 1024L * 1024L;
        private const int EnvelopeBytesPerSample = 2048;
        private const int WaveProbeDriftThresholdMs = 750;
        private const int WaveProbeSoftSyncIntervalMs = 700;
        private const double WaveVisibleHistoryScale = 0.38d;
        private const double WaveSyncCompensationMs = 0d;
        private readonly object _audioEnergyLock = new();
        private readonly double[] _audioEnergyHistory = new double[AudioEnergyHistorySize];
        private readonly double[] _audioSampleHistory = new double[AudioSampleHistorySize];
        private readonly double[] _audioSampleTimeHistory = new double[AudioSampleHistorySize];
        private readonly double[] _spectrumBarLevels = new double[SpectrumBarCount];
        private int _audioEnergyWriteIndex = 0;
        private bool _audioEnergyFilled = false;
        private int _audioSampleWriteIndex = 0;
        private bool _audioSampleFilled = false;
        private double _waveAmplitudeSmoother = 0.18d;
        private int _waveProbeSampleDecimationCounter = 0;
        private bool _waveProbeReady = false;
        private bool _waveProbeFailed = false;
        private string? _waveProbeSourcePath;
        private LibVLC? _waveProbeLibVlc;
        private VlcMediaPlayer? _waveProbePlayer;
        private VlcMedia? _waveProbeMedia;
        private VlcMediaPlayer.LibVLCAudioPlayCb? _waveProbePlayCb;
        private VlcMediaPlayer.LibVLCAudioPauseCb? _waveProbePauseCb;
        private VlcMediaPlayer.LibVLCAudioResumeCb? _waveProbeResumeCb;
        private VlcMediaPlayer.LibVLCAudioFlushCb? _waveProbeFlushCb;
        private VlcMediaPlayer.LibVLCAudioDrainCb? _waveProbeDrainCb;
        private DateTime _lastWaveProbeSoftSyncUtc = DateTime.MinValue;
        private double _waveProbeToMediaOffsetMs = 0d;
        private bool _waveProbeOffsetInitialized = false;
        // (No external fallback initialized) - keep MediaElement primary

        public MainWindow()
        {
            InitializeComponent();

            // Fade-in on startup
            this.Opacity = 0;
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450));
            this.BeginAnimation(OpacityProperty, fade);

            // Wire commands for track change
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Setup a timer to update playback position
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            _audioWaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _audioWaveTimer.Tick += AudioWaveTimer_Tick;

            // Auto-hide controls timer
            _hideControlsTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
            _hideControlsTimer.Tick += (s, e) => HideControls();
            _hideControlsTimer.Start();

            // Auto-hide cursor timer
            _hideCursorTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _hideCursorTimer.Tick += (s, e) => HideCursor();
            _hideCursorTimer.Start();


            mediaElement.MediaOpened += MediaElement_MediaOpened;
            mediaElement.MediaEnded += MediaElement_MediaEnded;
            mediaElement.MediaFailed += MediaElement_MediaFailed;
            this.StateChanged += MainWindow_StateChanged;
            this.Deactivated += MainWindow_Deactivated;
            // Ensure theme combo has default selection applied
            ApplyTheme("Dark");

            try { LoadPersistedMainPlaylist(); } catch { }
        }

        private void ThemeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ComboBox combo && combo.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Content is string s)
            {
                ApplyTheme(s);
            }
        }

        private void ApplyTheme(string theme)
        {
            try
            {
                if (theme == "Light")
                {
                    Application.Current.Resources["WindowBackgroundBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFFFF"));
                    Application.Current.Resources["PanelBackgroundBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFF0F0F0"));
                    Application.Current.Resources["CardBackgroundBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFFFFFF"));
                    Application.Current.Resources["CardAltBackgroundBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EEEEEE"));
                    Application.Current.Resources["PrimaryBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D7"));
                }
                else // Dark
                {
                    Application.Current.Resources["WindowBackgroundBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF0F1115"));
                    Application.Current.Resources["PanelBackgroundBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0F1115"));
                    Application.Current.Resources["CardBackgroundBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#141518"));
                    Application.Current.Resources["CardAltBackgroundBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#222222"));
                    Application.Current.Resources["PrimaryBrush"] = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00B4FF"));
                }
            }
            catch { }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                FullscreenButton.Content = "Exit Fullscreen";
            }
            else
            {
                FullscreenButton.Content = "Fullscreen";
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var asm = System.Reflection.Assembly.GetEntryAssembly() ?? System.Reflection.Assembly.GetExecutingAssembly();
                var name = asm.GetName().Name ?? "MDJ Media Player";
                var version = "1.2.0";
                var msg = $"{name}\nVersion: {version}\n\nA simple WPF media player.";
                MessageBox.Show(msg, "About", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
                MessageBox.Show("MDJ Media Player\nVersion: unknown", "About", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SFXButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sfxWindow == null)
            {
                _sfxWindow = new SFXWindow();
                _sfxWindow.Owner = this;
            }
            if (_sfxWindow.IsPlaying)
            {
                _sfxWindow.StopPlayback();
                return;
            }
            if (!_sfxWindow.IsVisible)
            {
                _sfxWindow.Show();
            }
            if (_sfxWindow.WindowState == WindowState.Minimized)
            {
                _sfxWindow.WindowState = WindowState.Normal;
            }
            _sfxWindow.Activate();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try { _audioWaveTimer?.Stop(); } catch { }
            try { StopWaveProbe(); } catch { }
            try { DisposeWaveProbe(); } catch { }
            try { SavePersistedMainPlaylist(); } catch { }
            try { _sfxWindow?.SavePersisted(); } catch { }
            try { _sfxWindow?.ForceClose(); } catch { }
            try { _extendedModeWindow?.Close(); } catch { }
            try
            {
                var others = new System.Collections.Generic.List<Window>();
                foreach (Window w in Application.Current.Windows)
                {
                    if (w != this && w is not SFXWindow)
                    {
                        others.Add(w);
                    }
                }
                foreach (var w in others)
                {
                    try { w.Close(); } catch { }
                }
            }
            catch { }
            base.OnClosing(e);
        }

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void FilesListButton_Click(object sender, RoutedEventArgs e)
        {
            var filesListWindow = new FilesListWindow(ViewModel);
            filesListWindow.Show();
        }

        private void ExtendedModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isExtendedMode)
            {
                ExitExtendedMode();
            }
            else
            {
                EnterExtendedMode();
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private static string GetAppDataDir()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MDJMediaPlayer");
            return dir;
        }

        private static string GetVideoPlaylistPath()
        {
            return Path.Combine(GetAppDataDir(), "video-playlist.m3u");
        }

        private void LoadPersistedMainPlaylist()
        {
            var path = GetVideoPlaylistPath();
            if (!File.Exists(path)) return;

            var baseDir = Path.GetDirectoryName(path) ?? string.Empty;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#")) continue;
                var mediaPath = line;
                if (!Path.IsPathRooted(mediaPath))
                {
                    mediaPath = Path.GetFullPath(Path.Combine(baseDir, mediaPath));
                }
                if (!File.Exists(mediaPath)) continue;
                if (ViewModel.Playlist.Any(p => string.Equals(p.FilePath, mediaPath, StringComparison.OrdinalIgnoreCase))) continue;
                ViewModel.Playlist.Add(new Models.MediaItem
                {
                    FilePath = mediaPath,
                    Title = System.IO.Path.GetFileName(mediaPath)
                });
            }
            if (ViewModel.Selected == null && ViewModel.Playlist.Count > 0)
            {
                ViewModel.Selected = ViewModel.Playlist[0];
            }
        }

        private void SavePersistedMainPlaylist()
        {
            var path = GetVideoPlaylistPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var writer = new StreamWriter(path, false);
            writer.WriteLine("#EXTM3U");
            foreach (var item in ViewModel.Playlist)
            {
                var title = string.IsNullOrWhiteSpace(item.Title) ? System.IO.Path.GetFileName(item.FilePath) : item.Title;
                writer.WriteLine("#EXTINF:-1," + title);
                writer.WriteLine(item.FilePath);
            }
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.Selected))
            {
                try
                {
                    if (ViewModel.Selected != null)
                    {
                        var selectedPath = ViewModel.Selected.FilePath;

                        // Validate file exists before attempting to play
                        if (!File.Exists(selectedPath))
                        {
                            MessageBox.Show("File not found: " + selectedPath, "Playback Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            DebugStatus.Text = "File not found: " + selectedPath;
                            _isCurrentTrackAudioOnly = false;
                            _audioWaveEnvelope = Array.Empty<double>();
                            StopWaveProbe();
                            UpdateAudioWaveVisibility();
                            return;
                        }

                        _isCurrentTrackAudioOnly = IsKnownAudioFile(selectedPath);
                        if (_isCurrentTrackAudioOnly)
                        {
                            PrepareAudioWaveEnvelope(selectedPath);
                            if (CanUseWaveProbe(selectedPath))
                            {
                                StartWaveProbe(selectedPath, ViewModel.Position, ViewModel.IsPlaying);
                            }
                            else
                            {
                                StopWaveProbe();
                            }
                        }
                        else
                        {
                            _audioWaveEnvelope = Array.Empty<double>();
                            StopWaveProbe();
                        }

                        mediaElement.Stop();
                        mediaElement.Source = new Uri(selectedPath, UriKind.Absolute);
                        mediaElement.Position = TimeSpan.FromSeconds(ViewModel.Position);
                        mediaElement.Volume = ViewModel.Volume;
                        if (ViewModel.IsPlaying) mediaElement.Play();
                        DebugStatus.Text = "Source: " + selectedPath;
                    }
                    else
                    {
                        _isCurrentTrackAudioOnly = false;
                        _audioWaveEnvelope = Array.Empty<double>();
                        StopWaveProbe();
                    }

                    UpdateAudioWaveVisibility();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Unable to play file: " + ex.Message, "Playback Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    DebugStatus.Text = "Error: " + ex.Message;
                    _isCurrentTrackAudioOnly = false;
                    _audioWaveEnvelope = Array.Empty<double>();
                    StopWaveProbe();
                    UpdateAudioWaveVisibility();
                }
            }

            if (e.PropertyName == nameof(ViewModel.IsPlaying))
            {
                if (ViewModel.IsPlaying) mediaElement.Play(); else mediaElement.Pause();
                SyncWaveProbePlayState(ViewModel.IsPlaying);
                UpdateAudioWaveVisibility();
            }
            if (e.PropertyName == nameof(ViewModel.Volume))
            {
                mediaElement.Volume = ViewModel.Volume;
            }
            if (e.PropertyName == nameof(ViewModel.Position))
            {
                try
                {
                    var pos = TimeSpan.FromSeconds(ViewModel.Position);
                    if (Math.Abs((mediaElement.Position - pos).TotalSeconds) > 0.5)
                    {
                        mediaElement.Position = pos;
                        SyncWaveProbePosition(true);
                    }
                }
                catch { }
            }
        }

        private void EnsureMediaElementCompatibility()
        {
            try
            {
                // Check if MediaElement is properly initialized
                if (mediaElement == null)
                {
                    MessageBox.Show("MediaElement is not initialized properly.", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Suggest codec installation if video is not showing
                if (mediaElement.NaturalVideoWidth == 0 || mediaElement.NaturalVideoHeight == 0)
                {
                    var codecMessage = "The video codec may not be supported. Please install the Media Feature Pack or necessary codecs for your system.\n\n" +
                                       "Visit: https://support.microsoft.com/en-us/help/3145500/media-feature-pack-list-for-windows-n-editions";
                    MessageBox.Show(codecMessage, "Codec Issue", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error occurred while ensuring MediaElement compatibility: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MediaElement_MediaOpened(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (mediaElement.NaturalDuration.HasTimeSpan)
                {
                    ViewModel.Duration = mediaElement.NaturalDuration.TimeSpan.TotalSeconds;
                    DebugStatus.Text = $"Source: {mediaElement.Source?.LocalPath}\nDuration: {mediaElement.NaturalDuration.TimeSpan}\nVideo: {mediaElement.NaturalVideoWidth}x{mediaElement.NaturalVideoHeight}";
                    mediaElement.Visibility = Visibility.Visible;
                    var isKnownAudio = IsKnownAudioFile(ViewModel.Selected?.FilePath);
                    _isCurrentTrackAudioOnly =
                        isKnownAudio ||
                        mediaElement.NaturalVideoWidth == 0 ||
                        mediaElement.NaturalVideoHeight == 0;
                    try
                    {
                        if (_isCurrentTrackAudioOnly && ViewModel.Selected != null)
                        {
                            if (CanUseWaveProbe(ViewModel.Selected.FilePath))
                            {
                                StartWaveProbe(ViewModel.Selected.FilePath, mediaElement.Position.TotalSeconds, ViewModel.IsPlaying);
                            }
                            else
                            {
                                StopWaveProbe();
                            }
                        }
                        else
                        {
                            StopWaveProbe();
                        }

                        UpdateAudioWaveVisibility();
                    }
                    catch (Exception waveEx)
                    {
                        // Keep media playback working even if visualization fails on edge cases.
                        _isCurrentTrackAudioOnly = false;
                        _audioWaveTimer?.Stop();
                        AudioWaveOverlay.Visibility = Visibility.Collapsed;
                        AudioWavePath.Data = null;
                        StopWaveProbe();
                        DebugStatus.Text += "\nWave disabled: " + waveEx.Message;
                    }

                    // If video size is zero, likely no video stream or unsupported codec
                    if ((mediaElement.NaturalVideoWidth == 0 || mediaElement.NaturalVideoHeight == 0) && !isKnownAudio)
                    {
                        var msg = "No video track detected or unsupported codec. Audio may play but video will not display.";
                        DebugStatus.Text += "\n" + msg;
                        MessageBox.Show(msg + "\n\nTry a different file or install media codecs (Media Feature Pack / media codecs).", "Video Not Detected", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error occurred while opening media: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DebugStatus.Text = "Error: " + ex.Message;
            }
        }

        private void MediaElement_MediaEnded(object? sender, RoutedEventArgs e)
        {
            // Auto-play next track
            ViewModel.PlayNext();
        }

        private void MediaElement_MediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            var msg = e.ErrorException?.ToString() ?? "Unknown media failure.";
            MessageBox.Show("Playback failed: " + msg, "Playback Error", MessageBoxButton.OK, MessageBoxImage.Error);
            DebugStatus.Text = "Playback failed: " + msg;
            _isCurrentTrackAudioOnly = false;
            _audioWaveEnvelope = Array.Empty<double>();
            StopWaveProbe();
            UpdateAudioWaveVisibility();

            // No fallback here; user can install codecs or try another file
        }

        private bool IsKnownAudioFile(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            return _audioExtensions.Contains(Path.GetExtension(filePath));
        }

        private static bool CanUseWaveProbe(string filePath)
        {
            try
            {
                return new FileInfo(filePath).Length <= WaveProbeMaxFileBytes;
            }
            catch
            {
                return false;
            }
        }

        private void PrepareAudioWaveEnvelope(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                _audioWaveEnvelope = Array.Empty<double>();
                return;
            }

            _audioWaveEnvelope = BuildAudioWaveEnvelope(filePath);
        }

        private void UpdateAudioWaveVisibility()
        {
            var shouldShow = ViewModel.IsPlaying && _isCurrentTrackAudioOnly && ViewModel.Selected != null;

            if (!shouldShow)
            {
                _audioWaveTimer?.Stop();
                AudioWaveOverlay.Visibility = Visibility.Collapsed;
                AudioWavePath.Data = null;
                return;
            }

            AudioWaveOverlay.Visibility = Visibility.Visible;
            RenderAudioWaveFrame();
            _audioWaveTimer?.Start();
        }

        private void AudioWaveTimer_Tick(object? sender, EventArgs e)
        {
            if (!ViewModel.IsPlaying || !_isCurrentTrackAudioOnly || ViewModel.Selected == null)
            {
                UpdateAudioWaveVisibility();
                return;
            }

            RenderAudioWaveFrame();
        }

        private void RenderAudioWaveFrame()
        {
            if (AudioWaveOverlay.ActualWidth <= 0 || AudioWaveOverlay.ActualHeight <= 0)
            {
                return;
            }

            var width = AudioWaveOverlay.ActualWidth;
            var height = AudioWaveOverlay.ActualHeight;
            var baselineY = Math.Max(8d, height - 6d);
            var maxBarHeight = Math.Max(18d, baselineY - 4d);
            var barCount = _spectrumBarLevels.Length;
            if (barCount < 2)
            {
                AudioWavePath.Data = null;
                return;
            }

            var (sampleValues, sampleTimes) = GetRecentAudioSamplesWithTimes(Math.Max(800, barCount * 10));
            var hasTimedSamples = sampleValues.Length >= 64;
            var mediaNowMs = Math.Max(0d, mediaElement.Position.TotalMilliseconds - WaveSyncCompensationMs);
            var probeNowMs = GetProbeAlignedTimeMs(mediaNowMs);
            var targetAmplitude = 0d;

            if (hasTimedSamples)
            {
                targetAmplitude = SampleAmplitudeNearTime(sampleValues, sampleTimes, probeNowMs, 4d);
                if (targetAmplitude < 0d)
                {
                    targetAmplitude = 0d;
                }
            }
            else
            {
                var realtimeEnergy = GetRecentAudioEnergy(24);
                if (realtimeEnergy.Length > 0)
                {
                    var avg = 0d;
                    for (var i = 0; i < realtimeEnergy.Length; i++)
                    {
                        avg += realtimeEnergy[i];
                    }

                    avg /= realtimeEnergy.Length;
                    targetAmplitude = Math.Clamp(avg * 2.8d, 0.03d, 1d);
                }
                else
                {
                    var progress = ViewModel.Duration > 0 ? ViewModel.Position / ViewModel.Duration : 0d;
                    var envelopeLength = _audioWaveEnvelope.Length;
                    var index = envelopeLength == 0 ? 0 : (int)(progress * (envelopeLength - 1));
                    var sample = envelopeLength == 0 ? 0.08d : _audioWaveEnvelope[index];
                    targetAmplitude = Math.Clamp(sample * 1.2d, 0.03d, 1d);
                }
            }

            if (targetAmplitude >= _waveAmplitudeSmoother)
            {
                _waveAmplitudeSmoother = (_waveAmplitudeSmoother * 0.03d) + (targetAmplitude * 0.97d);
            }
            else
            {
                _waveAmplitudeSmoother = (_waveAmplitudeSmoother * 0.5d) + (targetAmplitude * 0.5d);
            }

            // Higher user volume should visibly increase wave size.
            var volumeHeightFactor = 0.55d + (ViewModel.Volume * 1.25d);
            var dynamicMaxBarHeight = Math.Min(height - 4d, maxBarHeight * volumeHeightFactor);
            var volumeAmplitudeBoost = 0.75d + (ViewModel.Volume * 1.45d);

            var drawLevels = new double[barCount];
            if (hasTimedSamples)
            {
                var trailStepMs = 8d;
                var sampleWindowMs = 4d;
                for (var i = 0; i < barCount; i++)
                {
                    var barTimeMs = probeNowMs - (i * trailStepMs);
                    var amplitude = SampleAmplitudeNearTime(sampleValues, sampleTimes, barTimeMs, sampleWindowMs);
                    if (amplitude < 0d)
                    {
                        amplitude = _waveAmplitudeSmoother * 0.35d;
                    }

                    var profile = Math.Exp(-(double)i / (barCount * SpectrumTailProfile));
                    var normalized = Math.Clamp(amplitude * (1.0d + (profile * 1.7d)) * volumeAmplitudeBoost, 0d, 1.2d);
                    var targetPx = dynamicMaxBarHeight * Math.Clamp(normalized, 0d, 1d);

                    var current = _spectrumBarLevels[i];
                    if (targetPx >= current)
                    {
                        current = (current * 0.02d) + (targetPx * 0.98d);
                    }
                    else
                    {
                        current = (current * 0.65d) + (targetPx * 0.35d);
                    }

                    _spectrumBarLevels[i] = current;
                    var tailVolumeBoost = 0.85d + (ViewModel.Volume * 0.9d);
                    drawLevels[i] = Math.Clamp(current * (0.35d + (0.65d * profile)) * tailVolumeBoost, 0d, dynamicMaxBarHeight);
                }
            }
            else
            {
                var headTargetPx = dynamicMaxBarHeight * Math.Clamp(_waveAmplitudeSmoother * volumeAmplitudeBoost, 0d, 1.22d);
                for (var i = barCount - 1; i >= 1; i--)
                {
                    var shifted = _spectrumBarLevels[i - 1];
                    var travelDecay = Math.Max(0.86d, 0.988d - (i * 0.0012d));
                    shifted *= travelDecay;
                    var current = _spectrumBarLevels[i] * 0.95d;
                    _spectrumBarLevels[i] = Math.Max(current, shifted);
                }

                _spectrumBarLevels[0] = (_spectrumBarLevels[0] * 0.18d) + (headTargetPx * 0.82d);
                for (var i = 0; i < barCount; i++)
                {
                    var profile = Math.Exp(-(double)i / (barCount * SpectrumTailProfile));
                    var tailVolumeBoost = 0.85d + (ViewModel.Volume * 0.9d);
                    drawLevels[i] = Math.Clamp(_spectrumBarLevels[i] * (0.35d + (0.65d * profile)) * tailVolumeBoost, 0d, dynamicMaxBarHeight);
                }
            }

            AudioWavePath.Data = BuildSpectrumGeometry(width, baselineY, drawLevels);
        }

        private static Geometry BuildSpectrumGeometry(double width, double baselineY, IReadOnlyList<double> heights)
        {
            if (width <= 1d || baselineY <= 1d || heights.Count == 0)
            {
                return Geometry.Empty;
            }

            var barCount = heights.Count;
            var gap = SpectrumBarGap;
            var barWidth = (width - ((barCount - 1) * gap)) / barCount;
            if (barWidth < 0.6d)
            {
                barWidth = 0.6d;
                gap = barCount > 1 ? Math.Max(0d, (width - (barCount * barWidth)) / (barCount - 1)) : 0d;
            }

            var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var context = geometry.Open())
            {
                var x = 0d;
                for (var i = 0; i < barCount; i++)
                {
                    var barHeight = heights[i];
                    if (barHeight > 1d)
                    {
                        var top = Math.Max(0d, baselineY - barHeight);
                        var right = Math.Min(width, x + barWidth);
                        if (right > x)
                        {
                            context.BeginFigure(new Point(x, top), true, true);
                            context.LineTo(new Point(right, top), true, false);
                            context.LineTo(new Point(right, baselineY), true, false);
                            context.LineTo(new Point(x, baselineY), true, false);
                        }
                    }

                    x += barWidth + gap;
                    if (x > width)
                    {
                        break;
                    }
                }
            }

            geometry.Freeze();
            return geometry;
        }

        private double GetProbeAlignedTimeMs(double mediaTimeMs)
        {
            if (_waveProbePlayer == null || string.IsNullOrWhiteSpace(_waveProbeSourcePath))
            {
                return mediaTimeMs;
            }

            try
            {
                var probeTimeMs = Math.Max(0d, _waveProbePlayer.Time);
                var observedOffset = mediaElement.Position.TotalMilliseconds - probeTimeMs;
                if (!_waveProbeOffsetInitialized)
                {
                    _waveProbeToMediaOffsetMs = observedOffset;
                    _waveProbeOffsetInitialized = true;
                }
                else
                {
                    _waveProbeToMediaOffsetMs = (_waveProbeToMediaOffsetMs * 0.4d) + (observedOffset * 0.6d);
                }

                return Math.Max(0d, mediaTimeMs - _waveProbeToMediaOffsetMs);
            }
            catch
            {
                return mediaTimeMs;
            }
        }

        private static double[] BuildAudioWaveEnvelope(string filePath, int sampleCount = 1200)
        {
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists || info.Length <= 0)
                {
                    return Array.Empty<double>();
                }

                var envelope = new double[sampleCount];
                var buffer = new byte[EnvelopeBytesPerSample];
                var stride = Math.Max(1L, info.Length / sampleCount);
                var maxOffset = Math.Max(0L, info.Length - EnvelopeBytesPerSample);
                double previous = 0.22d;

                using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    bufferSize: 8192,
                    options: FileOptions.SequentialScan);

                for (var i = 0; i < sampleCount; i++)
                {
                    var offset = Math.Min(maxOffset, i * stride);
                    if (stream.Position != offset)
                    {
                        stream.Seek(offset, SeekOrigin.Begin);
                    }

                    var read = stream.Read(buffer, 0, buffer.Length);
                    long total = 0;
                    var count = read;
                    if (count == 0)
                    {
                        envelope[i] = previous;
                        continue;
                    }

                    for (var j = 0; j < read; j++)
                    {
                        total += Math.Abs(buffer[j] - 127);
                    }

                    var normalized = total / (count * 128d);
                    previous = (previous * 0.62d) + (normalized * 0.38d);
                    envelope[i] = Math.Clamp(previous * 1.9d, 0.08d, 1d);
                }

                return envelope;
            }
            catch
            {
                return Array.Empty<double>();
            }
        }

        private bool EnsureWaveProbe()
        {
            if (_waveProbeFailed)
            {
                return false;
            }

            if (_waveProbeReady && _waveProbePlayer != null && _waveProbeLibVlc != null)
            {
                return true;
            }

            try
            {
                Core.Initialize();
                _waveProbeLibVlc = new LibVLC("--no-video", "--quiet");
                _waveProbePlayer = new VlcMediaPlayer(_waveProbeLibVlc);
                _waveProbePlayCb = WaveProbePlayCallback;
                _waveProbePauseCb = WaveProbePauseCallback;
                _waveProbeResumeCb = WaveProbeResumeCallback;
                _waveProbeFlushCb = WaveProbeFlushCallback;
                _waveProbeDrainCb = WaveProbeDrainCallback;

                _waveProbePlayer.SetAudioFormat("S16N", ProbeSampleRate, ProbeChannels);
                _waveProbePlayer.SetAudioCallbacks(
                    _waveProbePlayCb,
                    _waveProbePauseCb,
                    _waveProbeResumeCb,
                    _waveProbeFlushCb,
                    _waveProbeDrainCb);

                _waveProbeReady = true;
                return true;
            }
            catch
            {
                _waveProbeFailed = true;
                DisposeWaveProbe();
                return false;
            }
        }

        private void StartWaveProbe(string? filePath, double startSeconds, bool shouldPlay)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                StopWaveProbe();
                return;
            }

            if (!EnsureWaveProbe() || _waveProbePlayer == null || _waveProbeLibVlc == null)
            {
                return;
            }

            var normalizedPath = Path.GetFullPath(filePath);

            try
            {
                if (string.Equals(_waveProbeSourcePath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    SyncWaveProbePosition(true);
                    SyncWaveProbePlayState(shouldPlay);
                    return;
                }

                _waveProbePlayer.Stop();
                _waveProbeMedia?.Dispose();
                _waveProbeMedia = null;
                ResetAudioEnergyHistory();
                _waveProbeOffsetInitialized = false;

                _waveProbeMedia = new VlcMedia(_waveProbeLibVlc, new Uri(normalizedPath));
                _waveProbePlayer.Media = _waveProbeMedia;
                _waveProbeSourcePath = normalizedPath;

                _waveProbePlayer.Play();
                var startMs = Math.Max(0L, (long)(startSeconds * 1000d));
                _waveProbePlayer.Time = startMs;
                _waveProbePlayer.SetPause(!shouldPlay);
            }
            catch
            {
                StopWaveProbe();
            }
        }

        private void StopWaveProbe()
        {
            try { _waveProbePlayer?.Stop(); } catch { }
            _waveProbeSourcePath = null;
            _waveProbeOffsetInitialized = false;
            ResetAudioEnergyHistory();
        }

        private void DisposeWaveProbe()
        {
            try { _waveProbePlayer?.Stop(); } catch { }
            try { _waveProbeMedia?.Dispose(); } catch { }
            try { _waveProbePlayer?.Dispose(); } catch { }
            try { _waveProbeLibVlc?.Dispose(); } catch { }

            _waveProbeMedia = null;
            _waveProbePlayer = null;
            _waveProbeLibVlc = null;
            _waveProbeSourcePath = null;
            _waveProbeOffsetInitialized = false;
            _waveProbeReady = false;
            _waveProbePlayCb = null;
            _waveProbePauseCb = null;
            _waveProbeResumeCb = null;
            _waveProbeFlushCb = null;
            _waveProbeDrainCb = null;
            ResetAudioEnergyHistory();
        }

        private void SyncWaveProbePlayState(bool shouldPlay)
        {
            if (_waveProbePlayer == null || string.IsNullOrWhiteSpace(_waveProbeSourcePath))
            {
                return;
            }

            try
            {
                if (shouldPlay)
                {
                    if (!_waveProbePlayer.IsPlaying)
                    {
                        _waveProbePlayer.Play();
                    }
                    _waveProbePlayer.SetPause(false);
                }
                else
                {
                    _waveProbePlayer.SetPause(true);
                }
            }
            catch { }
        }

        private void SyncWaveProbePosition(bool force)
        {
            if (_waveProbePlayer == null || string.IsNullOrWhiteSpace(_waveProbeSourcePath))
            {
                return;
            }

            try
            {
                var targetMs = Math.Max(0L, (long)mediaElement.Position.TotalMilliseconds);
                var currentMs = Math.Max(0L, _waveProbePlayer.Time);
                if (force || Math.Abs(targetMs - currentMs) > WaveProbeDriftThresholdMs)
                {
                    _waveProbePlayer.Time = targetMs;
                    _waveProbeOffsetInitialized = false;
                }
            }
            catch { }
        }

        private void WaveProbePlayCallback(IntPtr data, IntPtr samples, uint count, long pts)
        {
            if (count == 0 || samples == IntPtr.Zero)
            {
                return;
            }

            var channelCount = (int)ProbeChannels;
            var frameCount = (int)count;
            var sampleCount = frameCount * channelCount;
            var probeTimeMs = _waveProbePlayer?.Time ?? 0L;
            var sampleStepMs = 1000d / ProbeSampleRate;
            short[]? rented = null;

            try
            {
                rented = ArrayPool<short>.Shared.Rent(sampleCount);
                Marshal.Copy(samples, rented, 0, sampleCount);

                var chunkSize = Math.Max(64, frameCount / 24);
                var chunkSamples = 0;
                var chunkEnergy = 0d;

                for (var frame = 0; frame < frameCount; frame++)
                {
                    var baseIndex = frame * channelCount;
                    var mono = 0d;
                    for (var ch = 0; ch < channelCount; ch++)
                    {
                        mono += rented[baseIndex + ch] / 32768d;
                    }

                    mono /= channelCount;
                    chunkEnergy += mono * mono;
                    chunkSamples++;
                    if (++_waveProbeSampleDecimationCounter >= ProbeSampleDecimation)
                    {
                        _waveProbeSampleDecimationCounter = 0;
                        var frameOffsetFromEnd = frameCount - 1 - frame;
                        var sampleTimeMs = probeTimeMs - (frameOffsetFromEnd * sampleStepMs);
                        PushAudioSample(mono, sampleTimeMs);
                    }

                    if (chunkSamples >= chunkSize)
                    {
                        var rms = Math.Sqrt(chunkEnergy / chunkSamples);
                        PushAudioEnergy(rms);
                        chunkSamples = 0;
                        chunkEnergy = 0d;
                    }
                }

                if (chunkSamples > 0)
                {
                    var rms = Math.Sqrt(chunkEnergy / chunkSamples);
                    PushAudioEnergy(rms);
                }
            }
            catch
            {
                // Keep waveform alive even if a callback frame cannot be processed.
            }
            finally
            {
                if (rented != null)
                {
                    ArrayPool<short>.Shared.Return(rented);
                }
            }
        }

        private void WaveProbePauseCallback(IntPtr data, long pts)
        {
        }

        private void WaveProbeResumeCallback(IntPtr data, long pts)
        {
        }

        private void WaveProbeFlushCallback(IntPtr data, long pts)
        {
            // Flush can occur on internal probe timing changes; preserve visual bars to avoid visible hard-refresh.
            ResetAudioProbeBuffers(clearSpectrumBars: false);
        }

        private void WaveProbeDrainCallback(IntPtr data)
        {
        }

        private void PushAudioEnergy(double value)
        {
            var clamped = Math.Clamp(value * 3.8d, 0d, 1d);

            lock (_audioEnergyLock)
            {
                _audioEnergyHistory[_audioEnergyWriteIndex] = clamped;
                _audioEnergyWriteIndex = (_audioEnergyWriteIndex + 1) % AudioEnergyHistorySize;
                if (_audioEnergyWriteIndex == 0)
                {
                    _audioEnergyFilled = true;
                }
            }
        }

        private void PushAudioSample(double sample, double mediaTimeMs)
        {
            var clamped = Math.Clamp(sample, -1d, 1d);

            lock (_audioEnergyLock)
            {
                _audioSampleHistory[_audioSampleWriteIndex] = clamped;
                _audioSampleTimeHistory[_audioSampleWriteIndex] = mediaTimeMs;
                _audioSampleWriteIndex = (_audioSampleWriteIndex + 1) % AudioSampleHistorySize;
                if (_audioSampleWriteIndex == 0)
                {
                    _audioSampleFilled = true;
                }
            }
        }

        private double[] GetRecentAudioEnergy(int requestedCount)
        {
            if (requestedCount <= 0)
            {
                return Array.Empty<double>();
            }

            lock (_audioEnergyLock)
            {
                var available = _audioEnergyFilled ? AudioEnergyHistorySize : _audioEnergyWriteIndex;
                if (available <= 0)
                {
                    return Array.Empty<double>();
                }

                var count = Math.Min(requestedCount, available);
                var result = new double[count];
                var start = (_audioEnergyWriteIndex - count + AudioEnergyHistorySize) % AudioEnergyHistorySize;
                for (var i = 0; i < count; i++)
                {
                    result[i] = _audioEnergyHistory[(start + i) % AudioEnergyHistorySize];
                }

                return result;
            }
        }

        private double[] GetRecentAudioSamples(int requestedCount)
        {
            if (requestedCount <= 0)
            {
                return Array.Empty<double>();
            }

            lock (_audioEnergyLock)
            {
                var available = _audioSampleFilled ? AudioSampleHistorySize : _audioSampleWriteIndex;
                if (available <= 0)
                {
                    return Array.Empty<double>();
                }

                var count = Math.Min(requestedCount, available);
                var result = new double[count];
                var start = (_audioSampleWriteIndex - count + AudioSampleHistorySize) % AudioSampleHistorySize;
                for (var i = 0; i < count; i++)
                {
                    result[i] = _audioSampleHistory[(start + i) % AudioSampleHistorySize];
                }

                return result;
            }
        }

        private (double[] values, double[] times) GetRecentAudioSamplesWithTimes(int requestedCount)
        {
            if (requestedCount <= 0)
            {
                return (Array.Empty<double>(), Array.Empty<double>());
            }

            lock (_audioEnergyLock)
            {
                var available = _audioSampleFilled ? AudioSampleHistorySize : _audioSampleWriteIndex;
                if (available <= 0)
                {
                    return (Array.Empty<double>(), Array.Empty<double>());
                }

                var count = Math.Min(requestedCount, available);
                var values = new double[count];
                var times = new double[count];
                var start = (_audioSampleWriteIndex - count + AudioSampleHistorySize) % AudioSampleHistorySize;
                for (var i = 0; i < count; i++)
                {
                    var index = (start + i) % AudioSampleHistorySize;
                    values[i] = _audioSampleHistory[index];
                    times[i] = _audioSampleTimeHistory[index];
                }

                return (values, times);
            }
        }

        private static double SampleAmplitudeNearTime(double[] values, double[] times, double targetTimeMs, double windowMs)
        {
            if (values.Length == 0 || times.Length == 0 || values.Length != times.Length)
            {
                return -1d;
            }

            var peakInWindow = 0d;
            var nearestAbs = 0d;
            var nearestDelta = double.MaxValue;

            for (var i = 0; i < values.Length; i++)
            {
                var dt = Math.Abs(times[i] - targetTimeMs);
                if (dt < nearestDelta)
                {
                    nearestDelta = dt;
                    nearestAbs = Math.Abs(values[i]);
                }

                if (dt > windowMs)
                {
                    continue;
                }

                var abs = Math.Abs(values[i]);
                if (abs > peakInWindow)
                {
                    peakInWindow = abs;
                }
            }

            if (peakInWindow > 0d) return peakInWindow;
            return nearestAbs;
        }

        private void ResetAudioEnergyHistory()
        {
            ResetAudioProbeBuffers(clearSpectrumBars: true);
        }

        private void ResetAudioProbeBuffers(bool clearSpectrumBars)
        {
            lock (_audioEnergyLock)
            {
                Array.Clear(_audioEnergyHistory, 0, _audioEnergyHistory.Length);
                Array.Clear(_audioSampleHistory, 0, _audioSampleHistory.Length);
                Array.Clear(_audioSampleTimeHistory, 0, _audioSampleTimeHistory.Length);
                if (clearSpectrumBars)
                {
                    Array.Clear(_spectrumBarLevels, 0, _spectrumBarLevels.Length);
                }
                _audioEnergyWriteIndex = 0;
                _audioEnergyFilled = false;
                _audioSampleWriteIndex = 0;
                _audioSampleFilled = false;
            }

            if (clearSpectrumBars)
            {
                _waveAmplitudeSmoother = 0.18d;
            }
            _waveProbeSampleDecimationCounter = 0;
            _waveProbeOffsetInitialized = false;
        }

        private static void SmoothArray(double[] values, int passes)
        {
            if (values.Length < 3 || passes <= 0)
            {
                return;
            }

            for (var pass = 0; pass < passes; pass++)
            {
                var previous = values[0];
                for (var i = 1; i < values.Length - 1; i++)
                {
                    var current = values[i];
                    var next = values[i + 1];
                    values[i] = (previous + (current * 2d) + next) * 0.25d;
                    previous = current;
                }
            }
        }

        private static Geometry BuildSmoothWaveGeometry(IReadOnlyList<Point> points)
        {
            if (points.Count < 2)
            {
                return Geometry.Empty;
            }

            if (points.Count < 3)
            {
                var lineFigure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
                lineFigure.Segments.Add(new LineSegment(points[^1], true));
                return new PathGeometry(new[] { lineFigure });
            }

            var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
            for (var i = 1; i < points.Count - 1; i++)
            {
                var control = points[i];
                var next = points[i + 1];
                var midpoint = new Point((control.X + next.X) / 2d, (control.Y + next.Y) / 2d);
                figure.Segments.Add(new QuadraticBezierSegment(control, midpoint, true));
            }

            figure.Segments.Add(new LineSegment(points[^1], true));
            return new PathGeometry(new[] { figure });
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (mediaElement.NaturalDuration.HasTimeSpan)
                {
                    ViewModel.Duration = mediaElement.NaturalDuration.TimeSpan.TotalSeconds;
                    // Don't overwrite user's dragging position while they interact with the slider
                    if (!_isUserDraggingPosition)
                    {
                        ViewModel.Position = mediaElement.Position.TotalSeconds;
                    }

                    // Keep timeline alignment through sampled offset, avoid periodic hard re-seeks.
                }
            }
            catch { }
        }

        private void PositionSlider_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isUserDraggingPosition = true;
        }

        private void PositionSlider_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                _isUserDraggingPosition = false;
                // Apply the position immediately to mediaElement
                mediaElement.Position = TimeSpan.FromSeconds(ViewModel.Position);
                SyncWaveProbePosition(true);
            }
            catch { }
        }

        private System.Windows.Threading.DispatcherTimer? _hideCursorTimer;

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            _hasLastMousePosition = false;
            ShowCursor();
        }

        private void HideCursor()
        {
            _hideCursorTimer?.Stop();
            this.Cursor = Cursors.None;
        }

        private void ShowCursor()
        {
            _hideCursorTimer?.Stop();
            this.Cursor = Cursors.Arrow;
            _hideCursorTimer?.Start();
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            _hideCursorTimer?.Stop();
            this.Cursor = Cursors.Arrow;
        }

        private void MediaArea_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            HandleMouseActivity(e.GetPosition(this));
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            HandleMouseActivity(e.GetPosition(this));
        }

        private void HandleMouseActivity(Point currentPosition)
        {
            if (_hasLastMousePosition)
            {
                var dx = Math.Abs(currentPosition.X - _lastMousePosition.X);
                var dy = Math.Abs(currentPosition.Y - _lastMousePosition.Y);

                // Ignore synthetic mouse-move events caused by layout/animation changes.
                if (dx < 0.5 && dy < 0.5)
                {
                    return;
                }
            }

            _lastMousePosition = currentPosition;
            _hasLastMousePosition = true;
            ShowControls();
            ShowCursor();
        }


        private void ControlPanel_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ShowControls();
        }

        private void ControlPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ShowControls();
        }

        private void TopBar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ShowControls();
        }

        private void TopBar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ShowControls();
        }

        private void ShowControls()
        {
            try
            {
                _hideControlsTimer?.Stop();
                _controlsAnimationVersion++;

                // Cancel any pending fade-out/fade-in and force a clean visible state.
                ControlPanel.BeginAnimation(OpacityProperty, null);
                TopBar.BeginAnimation(OpacityProperty, null);

                TopBarRow.Height = new GridLength(38);
                ControlPanel.Visibility = Visibility.Visible;
                TopBar.Visibility = Visibility.Visible;
                ControlPanel.Opacity = 1;
                TopBar.Opacity = 1;
                _controlsVisible = true;
                _hideControlsTimer?.Start();
            }
            catch { }
        }

        private void HideControls()
        {
            try
            {
                _hideControlsTimer?.Stop();
                if (!_controlsVisible)
                {
                    return;
                }

                var animationVersion = ++_controlsAnimationVersion;
                _controlsVisible = false;

                var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                fadeOut.Completed += (s, e) =>
                {
                    try
                    {
                        // Ignore stale completion callbacks from prior hide requests.
                        if (animationVersion != _controlsAnimationVersion || _controlsVisible)
                        {
                            return;
                        }

                        ControlPanel.Visibility = Visibility.Collapsed;
                        TopBar.Visibility = Visibility.Collapsed;
                        TopBarRow.Height = new GridLength(0);
                    }
                    catch { }
                };
                ControlPanel.BeginAnimation(OpacityProperty, fadeOut);
                TopBar.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch { }
        }

        

        // Allow dragging the window from anywhere
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            // Otherwise allow dragging
            try { this.DragMove(); } catch { }
        }

        // Titlebar-specific mouse handler (used by the titlebar Border)
        private void Titlebar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Prevent dragging when clicking on a control inside the titlebar
            var source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is System.Windows.Controls.Button || source is System.Windows.Controls.Primitives.ToggleButton || source is System.Windows.Controls.ComboBox)
                {
                    return; // clicked a control; don't drag
                }
                source = System.Windows.Media.VisualTreeHelper.GetParent(source);
            }

            Window_MouseDown(sender, e);
        }

        private void EnterExtendedMode()
        {
            try
            {
                if (_extendedModeWindow == null)
                {
                    _extendedModeWindow = new ExtendedModeWindow
                    {
                        Owner = this
                    };
                    _extendedModeWindow.Closed += ExtendedModeWindow_Closed;
                }

                MoveMediaElementToHost(_extendedModeWindow.HostPanel);
                _extendedModeWindow.Show();
                _extendedModeWindow.Activate();
                _isExtendedMode = true;
                ExtendedModeButton.Content = "Exit Extended";
                ExtendedModeStatusLabel.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to enter extended mode: " + ex.Message, "Extended Mode", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExitExtendedMode()
        {
            try
            {
                MoveMediaElementToHost(MediaHost);

                if (_extendedModeWindow != null)
                {
                    _extendedModeWindow.Closed -= ExtendedModeWindow_Closed;
                    _extendedModeWindow.Close();
                    _extendedModeWindow = null;
                }

                _isExtendedMode = false;
                ExtendedModeButton.Content = "Extended Mode";
                ExtendedModeStatusLabel.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to exit extended mode: " + ex.Message, "Extended Mode", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExtendedModeWindow_Closed(object? sender, EventArgs e)
        {
            MoveMediaElementToHost(MediaHost);
            _extendedModeWindow = null;
            _isExtendedMode = false;
            ExtendedModeButton.Content = "Extended Mode";
            ExtendedModeStatusLabel.Visibility = Visibility.Collapsed;
        }

        private void MoveMediaElementToHost(Panel targetHost)
        {
            if (mediaElement.Parent == targetHost)
            {
                return;
            }

            if (mediaElement.Parent is Panel currentParent)
            {
                currentParent.Children.Remove(mediaElement);
            }

            targetHost.Children.Add(mediaElement);
        }
    }
}
