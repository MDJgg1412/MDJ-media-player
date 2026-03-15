using MDJMediaPlayer.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.IO;
using System.Globalization;
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
        private bool _suppressPositionSync = false;
        private System.Windows.Threading.DispatcherTimer? _hideControlsTimer;
        private bool _controlsVisible = true;
        private int _controlsAnimationVersion = 0;
        private Point _lastMousePosition;
        private bool _hasLastMousePosition = false;
        private ExtendedModeWindow? _extendedModeWindow;
        private bool _isExtendedMode = false;
        private SFXWindow? _sfxWindow;
        private SettingsWindow? _settingsWindow;
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
        private const int SpectrumBarCount = 156;
        private const double CircularBarGapRatio = 0.38d;
        private const double CircularInwardRatio = 0.24d;
        private const int WaveRenderMinSampleCount = 512;
        private const int WaveRenderSampleCountFactor = 3;
        private const uint ProbeSampleRate = 44100;
        private const uint ProbeChannels = 2;
        private const int ProbeSampleDecimation = 8;
        private const int WaveSmoothingPasses = 1;
        private const long WaveProbeMaxFileBytes = 768L * 1024L * 1024L;
        private const int EnvelopeBytesPerSample = 2048;
        private const int WaveProbeDriftThresholdMs = 160;
        private const int WaveProbeSoftSyncIntervalMs = 250;
        private const double WaveVisibleHistoryScale = 0.38d;
        // Compensate output/device buffering so visuals align with what is actually heard.
        private const double WaveSyncCompensationMs = 180d;
        private const double WaveAmplitudeProbeWindowMs = 12d;
        private const double WaveBarAmplitudeWindowMs = 9d;
        private const double WaveSpinBaseDegPerSec = 14d;
        private const double WaveSpinBoostDegPerSec = 52d;
        private const int WaveOrbBaseCount = 30;
        private const int WaveOrbFullscreenExtraCount = 14;
        private const double WaveOrbSafeOffsetPx = 16d;
        private const double WaveOrbFullscreenSpeedBoost = 1.22d;
        private const double WaveOrbFullscreenSizeBoost = 1.10d;
        private readonly object _audioEnergyLock = new();
        private readonly double[] _audioEnergyHistory = new double[AudioEnergyHistorySize];
        private readonly double[] _audioSampleHistory = new double[AudioSampleHistorySize];
        private readonly double[] _audioSampleTimeHistory = new double[AudioSampleHistorySize];
        private readonly double[] _spectrumBarLevels = new double[SpectrumBarCount];
        private readonly double[] _waveRenderSampleValues = new double[AudioSampleHistorySize];
        private readonly double[] _waveRenderSampleTimes = new double[AudioSampleHistorySize];
        private readonly double[] _waveDrawLevels = new double[SpectrumBarCount];
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
        private DateTime _lastWaveSpinUpdateUtc = DateTime.MinValue;
        private double _waveProbeToMediaOffsetMs = 0d;
        private bool _waveProbeOffsetInitialized = false;
        private double _waveSpinAngle = 0d;
        private readonly List<WaveOrbState> _waveOrbs = new();
        private readonly Random _waveOrbRandom = new(942137);
        private DateTime _lastWaveOrbUpdateUtc = DateTime.MinValue;
        private double _waveOrbClockSeconds = 0d;
        private double _lastWaveOrbViewportWidth = 0d;
        private double _lastWaveOrbViewportHeight = 0d;
        private Panel? _audioWaveOverlayHomeParent;
        private int _audioWaveOverlayHomeIndex = -1;
        private bool _isLoadingSettings = false;
        // (No external fallback initialized) - keep MediaElement primary

        private sealed class WaveOrbState
        {
            public required System.Windows.Shapes.Ellipse Shape { get; init; }
            public required GradientStop CoreStop { get; init; }
            public required GradientStop MidStop { get; init; }
            public required GradientStop OuterStop { get; init; }
            public double PositionX;
            public double PositionY;
            public double VelocityX;
            public double VelocityY;
            public double WanderX;
            public double WanderY;
            public double WanderTimer;
            public double BaseSizePx;
            public double PulsePhase;
            public double PulseSpeed;
            public double HueOffset;
        }

        public MainWindow()
        {
            InitializeComponent();
            InitializeWaveOrbs();
            CachePlaybackVisualHomeParents();

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

            _audioWaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            _audioWaveTimer.Tick += AudioWaveTimer_Tick;

            // Auto-hide controls timer
            _hideControlsTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
            _hideControlsTimer.Tick += (s, e) => HideControls();
            if (ViewModel.AutoHideControls)
            {
                _hideControlsTimer.Start();
            }

            // Auto-hide cursor timer
            _hideCursorTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _hideCursorTimer.Tick += (s, e) => HideCursor();
            _hideCursorTimer.Start();


            mediaElement.MediaOpened += MediaElement_MediaOpened;
            mediaElement.MediaEnded += MediaElement_MediaEnded;
            mediaElement.MediaFailed += MediaElement_MediaFailed;
            this.StateChanged += MainWindow_StateChanged;
            this.Deactivated += MainWindow_Deactivated;
            try { LoadPersistedSettings(); } catch { }
            // Ensure theme has default selection applied
            ApplyTheme(ViewModel.Theme);
            UpdateAutoHideBehavior();

            try { LoadPersistedMainPlaylist(); } catch { }
            try { LoadStartupMediaFromArguments(); } catch { }
        }

        private void CachePlaybackVisualHomeParents()
        {
            if (AudioWaveOverlay.Parent is Panel parent)
            {
                _audioWaveOverlayHomeParent = parent;
                _audioWaveOverlayHomeIndex = parent.Children.IndexOf(AudioWaveOverlay);
            }
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
                System.Windows.Media.Color ParseColor(string hex)
                {
                    return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                }

                SolidColorBrush MakeSolid(string hex) => new SolidColorBrush(ParseColor(hex));

                SolidColorBrush MakeSolidColor(System.Windows.Media.Color color) => new SolidColorBrush(color);

                LinearGradientBrush MakeVerticalGradient(string topHex, string bottomHex) =>
                    MakeVerticalGradientColors(ParseColor(topHex), ParseColor(bottomHex));

                LinearGradientBrush MakeVerticalGradientColors(System.Windows.Media.Color top, System.Windows.Media.Color bottom) =>
                    new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));

                LinearGradientBrush MakeVerticalGradientStops(params (System.Windows.Media.Color color, double offset)[] stops)
                {
                    var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                    foreach (var (color, offset) in stops)
                    {
                        brush.GradientStops.Add(new GradientStop(color, offset));
                    }
                    return brush;
                }

                void SetGlobalBrush(string key, Brush brush)
                {
                    Application.Current.Resources[key] = brush;
                }

                void SetLocalBrush(string key, Brush brush)
                {
                    Resources[key] = brush;
                }

                void ClearLocalBrush(string key)
                {
                    if (Resources.Contains(key))
                    {
                        Resources.Remove(key);
                    }
                }

                void SetFont(string key, string fontFamily)
                {
                    Application.Current.Resources[key] = new FontFamily(fontFamily);
                }

                var isAeroTheme = string.Equals(theme, "Aero", StringComparison.OrdinalIgnoreCase);
                var hueShift = Math.Clamp(ViewModel.AeroColorLevel, 0d, 100d) * 3.3d;
                var transparencyLevel = Math.Clamp(ViewModel.AeroTransparency / 100d, 0d, 1d);
                var transparencyEnabled = isAeroTheme && transparencyLevel > 0d;
                var transparencyFactor = 1d;
                var controlPanelOpacity = 1d;
                var controlPanelShadowOpacity = 0.55d;
                var controlPanelHazeOpacity = 0d;

                if (transparencyEnabled)
                {
                    transparencyFactor = Math.Clamp(1d - (transparencyLevel * 0.75d), 0.2d, 1d);
                    controlPanelOpacity = 1d;
                    controlPanelShadowOpacity = Math.Clamp(0.55d - (transparencyLevel * 0.45d), 0.08d, 0.55d);
                    var hazeLevel = transparencyLevel;
                    controlPanelHazeOpacity = Math.Clamp(hazeLevel * 0.7d, 0d, 0.7d);
                }
                else
                {
                    controlPanelHazeOpacity = 0d;
                }

                Resources["ControlPanelOpacity"] = controlPanelOpacity;
                Resources["ControlPanelShadowOpacity"] = controlPanelShadowOpacity;
                Resources["ControlPanelHazeOpacity"] = controlPanelHazeOpacity;

                void ToHsv(System.Windows.Media.Color color, out double hue, out double saturation, out double value)
                {
                    var r = color.R / 255d;
                    var g = color.G / 255d;
                    var b = color.B / 255d;
                    var max = Math.Max(r, Math.Max(g, b));
                    var min = Math.Min(r, Math.Min(g, b));
                    var delta = max - min;

                    hue = 0d;
                    if (delta > 0d)
                    {
                        if (max == r)
                        {
                            hue = 60d * (((g - b) / delta) % 6d);
                        }
                        else if (max == g)
                        {
                            hue = 60d * (((b - r) / delta) + 2d);
                        }
                        else
                        {
                            hue = 60d * (((r - g) / delta) + 4d);
                        }
                    }
                    if (hue < 0d)
                    {
                        hue += 360d;
                    }

                    value = max;
                    saturation = max <= 0d ? 0d : delta / max;
                }

                System.Windows.Media.Color FromHsv(byte alpha, double hue, double saturation, double value)
                {
                    hue = (hue % 360d + 360d) % 360d;
                    saturation = Math.Clamp(saturation, 0d, 1d);
                    value = Math.Clamp(value, 0d, 1d);

                    var c = value * saturation;
                    var x = c * (1d - Math.Abs(((hue / 60d) % 2d) - 1d));
                    var m = value - c;

                    double rPrime;
                    double gPrime;
                    double bPrime;

                    if (hue < 60d)
                    {
                        rPrime = c;
                        gPrime = x;
                        bPrime = 0d;
                    }
                    else if (hue < 120d)
                    {
                        rPrime = x;
                        gPrime = c;
                        bPrime = 0d;
                    }
                    else if (hue < 180d)
                    {
                        rPrime = 0d;
                        gPrime = c;
                        bPrime = x;
                    }
                    else if (hue < 240d)
                    {
                        rPrime = 0d;
                        gPrime = x;
                        bPrime = c;
                    }
                    else if (hue < 300d)
                    {
                        rPrime = x;
                        gPrime = 0d;
                        bPrime = c;
                    }
                    else
                    {
                        rPrime = c;
                        gPrime = 0d;
                        bPrime = x;
                    }

                    var r = (byte)Math.Clamp(Math.Round((rPrime + m) * 255d), 0d, 255d);
                    var g = (byte)Math.Clamp(Math.Round((gPrime + m) * 255d), 0d, 255d);
                    var b = (byte)Math.Clamp(Math.Round((bPrime + m) * 255d), 0d, 255d);
                    return System.Windows.Media.Color.FromArgb(alpha, r, g, b);
                }

                System.Windows.Media.Color ApplyHueShift(System.Windows.Media.Color color)
                {
                    if (!isAeroTheme || Math.Abs(hueShift) < 0.01d)
                    {
                        return color;
                    }

                    ToHsv(color, out var h, out var s, out var v);
                    var shiftedHue = (h + hueShift) % 360d;
                    return FromHsv(color.A, shiftedHue, s, v);
                }

                System.Windows.Media.Color ApplyTransparency(System.Windows.Media.Color color)
                {
                    if (!transparencyEnabled)
                    {
                        return color;
                    }

                    var a = (byte)Math.Clamp(color.A * transparencyFactor, 0d, 255d);
                    return System.Windows.Media.Color.FromArgb(a, color.R, color.G, color.B);
                }

                System.Windows.Media.Color EnsureOpaque(System.Windows.Media.Color color)
                {
                    if (transparencyEnabled || color.A == 255)
                    {
                        return color;
                    }

                    return System.Windows.Media.Color.FromArgb(255, color.R, color.G, color.B);
                }

                System.Windows.Media.Color AdjustColor(string hex, bool applyTransparency = false)
                {
                    var color = ParseColor(hex);
                    color = ApplyHueShift(color);
                    color = EnsureOpaque(color);
                    if (applyTransparency)
                    {
                        color = ApplyTransparency(color);
                    }
                    return color;
                }

                System.Windows.Media.Color AdjustColorOpaque(string hex)
                {
                    var color = ParseColor(hex);
                    color = ApplyHueShift(color);
                    return System.Windows.Media.Color.FromArgb(255, color.R, color.G, color.B);
                }

                if (string.Equals(theme, "Zune", StringComparison.OrdinalIgnoreCase))
                {
                    SetFont("AppFontFamily", "Segoe UI");
                    SetFont("AppFontFamilyDisplay", "Segoe UI Semibold, Segoe UI");

                    SetGlobalBrush("WindowBackgroundBrush", MakeVerticalGradient("#14171C", "#0B0D11"));
                    SetGlobalBrush("PanelBackgroundBrush", MakeVerticalGradient("#181C22", "#0E1116"));
                    SetGlobalBrush("CardBackgroundBrush", MakeVerticalGradientColors(AdjustColor("#1C2128", applyTransparency: true), AdjustColor("#12161C", applyTransparency: true)));
                    SetGlobalBrush("CardAltBackgroundBrush", MakeVerticalGradientColors(AdjustColor("#222834", applyTransparency: true), AdjustColor("#151A22", applyTransparency: true)));
                    SetGlobalBrush("PrimaryBrush", MakeSolid("#4AA3FF"));

                    SetGlobalBrush("AeroBorderBrush", MakeSolid("#39424D"));
                    SetGlobalBrush("AeroBorderDarkBrush", MakeSolid("#2A313A"));
                    SetGlobalBrush("AeroTextBrush", MakeSolid("#E7EDF5"));
                    SetGlobalBrush("AeroSubTextBrush", MakeSolid("#B3BDC8"));
                    SetGlobalBrush("ControlOverlayBrush", MakeSolidColor(AdjustColor("#1F242C", applyTransparency: true)));
                    SetGlobalBrush("ControlBackgroundBrush", MakeSolidColor(AdjustColor("#20262F", applyTransparency: true)));
                    SetGlobalBrush("AeroButtonBrush", MakeSolid("#2A3038"));
                    SetGlobalBrush("AeroButtonHoverBrush", MakeSolid("#343C46"));
                    SetGlobalBrush("AeroButtonPressedBrush", MakeSolid("#21262D"));
                    SetGlobalBrush("AeroButtonGlossBrush", MakeSolid("#00FFFFFF"));
                    SetGlobalBrush("AeroSelectionBrush", MakeSolid("#2C4D6B"));
                    SetGlobalBrush("AeroItemHoverBrush", MakeSolid("#2B313A"));
                }
                else // Aero
                {
                    SetFont("AppFontFamily", "Segoe UI");
                    SetFont("AppFontFamilyDisplay", "Segoe UI Semibold, Segoe UI");

                    SetGlobalBrush("WindowBackgroundBrush", MakeVerticalGradientColors(AdjustColor("#263956"), AdjustColor("#101722")));
                    SetGlobalBrush("PanelBackgroundBrush", MakeVerticalGradientColors(AdjustColor("#2B4162"), AdjustColor("#111A27")));
                    SetGlobalBrush("CardBackgroundBrush", MakeVerticalGradientColors(AdjustColorOpaque("#304A6D"), AdjustColorOpaque("#162335")));
                    SetGlobalBrush("CardAltBackgroundBrush", MakeVerticalGradientColors(AdjustColorOpaque("#3A5A81"), AdjustColorOpaque("#1B2C43")));
                    SetGlobalBrush("PrimaryBrush", MakeSolidColor(AdjustColor("#4EC1FF")));

                    SetGlobalBrush("AeroBorderBrush", MakeSolidColor(AdjustColor("#7FB6E8")));
                    SetGlobalBrush("AeroBorderDarkBrush", MakeSolidColor(AdjustColor("#3E5C78")));
                    SetGlobalBrush("AeroTextBrush", MakeSolidColor(ParseColor("#F4FAFF")));
                    SetGlobalBrush("AeroSubTextBrush", MakeSolidColor(ParseColor("#D7E8F7")));
                    SetGlobalBrush("ControlOverlayBrush", MakeVerticalGradientStops((AdjustColorOpaque("#6B4C6A89"), 0.0), (AdjustColorOpaque("#5E3D536D"), 0.52), (AdjustColorOpaque("#4A25364C"), 1.0)));
                    SetGlobalBrush("ControlBackgroundBrush", MakeVerticalGradientStops((AdjustColorOpaque("#6E506D8E"), 0.0), (AdjustColorOpaque("#5A39506A"), 1.0)));
                    SetGlobalBrush("AeroButtonBrush", MakeVerticalGradientStops((AdjustColor("#3A5D86"), 0.0), (AdjustColor("#1E2F45"), 1.0)));
                    SetGlobalBrush("AeroButtonHoverBrush", MakeVerticalGradientStops((AdjustColor("#4E78A8"), 0.0), (AdjustColor("#24405C"), 1.0)));
                    SetGlobalBrush("AeroButtonPressedBrush", MakeVerticalGradientStops((AdjustColor("#1B2B3F"), 0.0), (AdjustColor("#142233"), 1.0)));
                    SetGlobalBrush("AeroButtonGlossBrush", MakeVerticalGradientStops((AdjustColorOpaque("#66FFFFFF"), 0.0), (AdjustColorOpaque("#22FFFFFF"), 0.45), (AdjustColorOpaque("#00FFFFFF"), 1.0)));
                    SetGlobalBrush("AeroSelectionBrush", MakeVerticalGradientStops((AdjustColor("#6BAED9"), 0.0), (AdjustColor("#34618C"), 1.0)));
                    SetGlobalBrush("AeroItemHoverBrush", MakeVerticalGradientStops((AdjustColor("#3D5F82"), 0.0), (AdjustColor("#2A3F57"), 1.0)));
                }

                ClearLocalBrush("CardBackgroundBrush");
                ClearLocalBrush("CardAltBackgroundBrush");
                ClearLocalBrush("ControlOverlayBrush");
                ClearLocalBrush("ControlBackgroundBrush");
                ClearLocalBrush("AeroButtonGlossBrush");

                if (isAeroTheme && transparencyEnabled)
                {
                    SetLocalBrush("CardBackgroundBrush", MakeVerticalGradientColors(AdjustColor("#304A6D", applyTransparency: true), AdjustColor("#162335", applyTransparency: true)));
                    SetLocalBrush("CardAltBackgroundBrush", MakeVerticalGradientColors(AdjustColor("#3A5A81", applyTransparency: true), AdjustColor("#1B2C43", applyTransparency: true)));
                    SetLocalBrush("ControlOverlayBrush", MakeVerticalGradientStops((AdjustColor("#6B4C6A89", applyTransparency: true), 0.0), (AdjustColor("#5E3D536D", applyTransparency: true), 0.52), (AdjustColor("#4A25364C", applyTransparency: true), 1.0)));
                    SetLocalBrush("ControlBackgroundBrush", MakeVerticalGradientStops((AdjustColor("#6E506D8E", applyTransparency: true), 0.0), (AdjustColor("#5A39506A", applyTransparency: true), 1.0)));
                    SetLocalBrush("AeroButtonGlossBrush", MakeVerticalGradientStops((AdjustColor("#66FFFFFF", applyTransparency: true), 0.0), (AdjustColor("#22FFFFFF", applyTransparency: true), 0.45), (AdjustColor("#00FFFFFF", applyTransparency: true), 1.0)));
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

            SyncWaveOrbsWithWindowState();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow == null)
            {
                _settingsWindow = new SettingsWindow
                {
                    Owner = this,
                    DataContext = DataContext,
                    ShowInTaskbar = false
                };
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            }

            if (!_settingsWindow.IsVisible)
            {
                _settingsWindow.Show();
            }

            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Activate();
        }

        private void SFXButton_Click(object sender, RoutedEventArgs e)
        {
            if (_sfxWindow == null)
            {
                _sfxWindow = new SFXWindow
                {
                    ShowInTaskbar = false
                };
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
            try { SavePersistedSettings(); } catch { }
            try { SavePersistedMainPlaylist(); } catch { }
            try { _sfxWindow?.SavePersisted(); } catch { }
            try { _sfxWindow?.ForceClose(); } catch { }
            try { _settingsWindow?.Close(); } catch { }
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
            filesListWindow.Owner = this;
            filesListWindow.ShowInTaskbar = false;
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

        private static string GetSettingsPath()
        {
            return Path.Combine(GetAppDataDir(), "settings.ini");
        }

        private void LoadPersistedSettings()
        {
            var path = GetSettingsPath();
            if (!File.Exists(path)) return;

            _isLoadingSettings = true;
            try
            {
                foreach (var raw in File.ReadLines(path))
                {
                    var line = raw.Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("#", StringComparison.Ordinal)) continue;

                    var splitIndex = line.IndexOf('=');
                    if (splitIndex <= 0) continue;

                    var key = line.Substring(0, splitIndex).Trim();
                    var value = line.Substring(splitIndex + 1).Trim();

                    if (string.Equals(key, "Theme", StringComparison.OrdinalIgnoreCase))
                    {
                        ViewModel.Theme = value;
                        continue;
                    }

                    if (string.Equals(key, "LoopMedia", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bool.TryParse(value, out var loopMedia))
                        {
                            ViewModel.LoopMedia = loopMedia;
                        }
                        continue;
                    }

                    if (string.Equals(key, "AutoplayMedia", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bool.TryParse(value, out var autoplayMedia))
                        {
                            ViewModel.AutoplayMedia = autoplayMedia;
                        }
                        continue;
                    }

                    if (string.Equals(key, "AutoNext", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bool.TryParse(value, out var autoNext))
                        {
                            ViewModel.AutoNext = autoNext;
                        }
                        continue;
                    }

                    if (string.Equals(key, "LoopPlaylist", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bool.TryParse(value, out var loopPlaylist))
                        {
                            ViewModel.LoopPlaylist = loopPlaylist;
                        }
                        continue;
                    }

                    if (string.Equals(key, "AutoHideControls", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bool.TryParse(value, out var autoHideControls))
                        {
                            ViewModel.AutoHideControls = autoHideControls;
                        }
                        continue;
                    }


                    if (string.Equals(key, "AeroColorLevel", StringComparison.OrdinalIgnoreCase))
                    {
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var colorLevel))
                        {
                            ViewModel.AeroColorLevel = colorLevel;
                        }
                        continue;
                    }

                    if (string.Equals(key, "AeroTransparency", StringComparison.OrdinalIgnoreCase))
                    {
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var transparency))
                        {
                            ViewModel.AeroTransparency = transparency;
                        }
                    }
                }
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        private void SavePersistedSettings()
        {
            var path = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var writer = new StreamWriter(path, false);
            writer.WriteLine("# MDJ Media Player settings");
            writer.WriteLine("Theme=" + ViewModel.Theme);
            writer.WriteLine("LoopMedia=" + ViewModel.LoopMedia);
            writer.WriteLine("AutoplayMedia=" + ViewModel.AutoplayMedia);
            writer.WriteLine("AutoNext=" + ViewModel.AutoNext);
            writer.WriteLine("LoopPlaylist=" + ViewModel.LoopPlaylist);
            writer.WriteLine("AutoHideControls=" + ViewModel.AutoHideControls);
            writer.WriteLine("AeroColorLevel=" + ViewModel.AeroColorLevel.ToString(CultureInfo.InvariantCulture));
            writer.WriteLine("AeroTransparency=" + ViewModel.AeroTransparency.ToString(CultureInfo.InvariantCulture));
        }

        private void LoadStartupMediaFromArguments()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length <= 1)
            {
                return;
            }

            var firstTarget = AddMediaFilesFromArguments(args, 1);
            if (firstTarget == null)
            {
                return;
            }

            if (ViewModel.AutoplayMedia)
            {
                ViewModel.Position = 0;
                ViewModel.Selected = firstTarget;
                ViewModel.IsPlaying = true;
            }
        }

        private Models.MediaItem? AddMediaFilesFromArguments(string[] args, int startIndex)
        {
            Models.MediaItem? firstItem = null;

            for (var i = startIndex; i < args.Length; i++)
            {
                var filePath = NormalizeArgumentAsFilePath(args[i]);
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    continue;
                }

                var existingItem = FindPlaylistItemByPath(filePath);
                if (existingItem != null)
                {
                    firstItem ??= existingItem;
                    continue;
                }

                var item = new Models.MediaItem
                {
                    FilePath = filePath,
                    Title = Path.GetFileName(filePath)
                };

                ViewModel.Playlist.Add(item);
                firstItem ??= item;
            }

            return firstItem;
        }

        private Models.MediaItem? FindPlaylistItemByPath(string filePath)
        {
            foreach (var item in ViewModel.Playlist)
            {
                if (string.Equals(item.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
        }

        private static string? NormalizeArgumentAsFilePath(string rawArg)
        {
            if (string.IsNullOrWhiteSpace(rawArg))
            {
                return null;
            }

            var candidate = rawArg.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return null;
            }

            // Ignore startup switches or activation flags passed by shell integrations.
            if (candidate.StartsWith("-", StringComparison.Ordinal))
            {
                return null;
            }

            if (candidate.StartsWith("/", StringComparison.Ordinal) && !candidate.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                if (!Path.IsPathRooted(candidate))
                {
                    candidate = Path.GetFullPath(candidate);
                }
            }
            catch
            {
                return null;
            }

            return candidate;
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
            if (ViewModel.AutoplayMedia && ViewModel.Selected == null && ViewModel.Playlist.Count > 0)
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
                if (_isUserDraggingPosition || _suppressPositionSync)
                {
                    return;
                }
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
            if (e.PropertyName == nameof(ViewModel.Theme))
            {
                ApplyTheme(ViewModel.Theme);
                if (!_isLoadingSettings)
                {
                    try { SavePersistedSettings(); } catch { }
                }
            }
            if (e.PropertyName == nameof(ViewModel.AeroColorLevel) || e.PropertyName == nameof(ViewModel.AeroTransparency))
            {
                ApplyTheme(ViewModel.Theme);
                if (!_isLoadingSettings)
                {
                    try { SavePersistedSettings(); } catch { }
                }
            }
            if (e.PropertyName == nameof(ViewModel.LoopMedia) || e.PropertyName == nameof(ViewModel.AutoplayMedia) || e.PropertyName == nameof(ViewModel.AutoNext) || e.PropertyName == nameof(ViewModel.LoopPlaylist))
            {
                if (!_isLoadingSettings)
                {
                    try { SavePersistedSettings(); } catch { }
                }
            }
            if (e.PropertyName == nameof(ViewModel.AutoHideControls))
            {
                UpdateAutoHideBehavior();
                if (!_isLoadingSettings)
                {
                    try { SavePersistedSettings(); } catch { }
                }
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
            if (ViewModel.LoopMedia && ViewModel.Selected != null)
            {
                try
                {
                    ViewModel.Position = 0;
                    mediaElement.Position = TimeSpan.Zero;
                    mediaElement.Play();
                    ViewModel.IsPlaying = true;
                    SyncWaveProbePosition(true);
                }
                catch { }
                return;
            }

            if (ViewModel.AutoNext)
            {
                // Auto-play next track (wrap when loop playlist is enabled)
                if (ViewModel.TryPlayNextValid(wrap: ViewModel.LoopPlaylist))
                {
                    return;
                }
            }

            try
            {
                ViewModel.Position = 0;
                mediaElement.Position = TimeSpan.Zero;
            }
            catch { }

            ViewModel.IsPlaying = false;
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

            if (ViewModel.AutoNext && ViewModel.TryPlayNextValid(wrap: ViewModel.LoopPlaylist))
            {
                return;
            }

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
                _lastWaveSpinUpdateUtc = DateTime.MinValue;
                _lastWaveOrbUpdateUtc = DateTime.MinValue;
                _lastWaveOrbViewportWidth = 0d;
                _lastWaveOrbViewportHeight = 0d;
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
            if (AudioWaveSpinLayer.ActualWidth <= 0 || AudioWaveSpinLayer.ActualHeight <= 0)
            {
                return;
            }

            var width = AudioWaveSpinLayer.ActualWidth;
            var height = AudioWaveSpinLayer.ActualHeight;
            var minSide = Math.Min(width, height);
            var outerRingDiameter = Math.Clamp(minSide * 0.62d, 180d, Math.Max(180d, minSide - 24d));
            var innerRingDiameter = Math.Clamp(outerRingDiameter * 0.38d, 72d, outerRingDiameter * 0.52d);

            AudioWaveOuterRing.Width = outerRingDiameter;
            AudioWaveOuterRing.Height = outerRingDiameter;
            AudioWaveInnerRing.Width = innerRingDiameter;
            AudioWaveInnerRing.Height = innerRingDiameter;

            var barCount = _spectrumBarLevels.Length;
            var sampleCount = CopyRecentAudioSamplesWithTimes(
                Math.Max(WaveRenderMinSampleCount, barCount * WaveRenderSampleCountFactor),
                _waveRenderSampleValues,
                _waveRenderSampleTimes);
            var sampleValues = _waveRenderSampleValues.AsSpan(0, sampleCount);
            var sampleTimes = _waveRenderSampleTimes.AsSpan(0, sampleCount);
            var hasTimedSamples = sampleCount >= 42;
            var mediaNowMs = Math.Max(0d, mediaElement.Position.TotalMilliseconds - WaveSyncCompensationMs);
            var probeNowMs = GetProbeAlignedTimeMs(mediaNowMs);
            var targetAmplitude = 0d;

            if (hasTimedSamples)
            {
                targetAmplitude = SampleRmsNearTime(sampleValues, sampleTimes, probeNowMs, WaveAmplitudeProbeWindowMs);
                if (targetAmplitude < 0d)
                {
                    targetAmplitude = SampleAmplitudeNearTime(sampleValues, sampleTimes, probeNowMs, 6d);
                }
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
                    progress = Math.Clamp(progress, 0d, 1d);
                    var envelopeLength = _audioWaveEnvelope.Length;
                    var index = envelopeLength == 0 ? 0 : (int)(progress * (envelopeLength - 1));
                    if (envelopeLength > 0)
                    {
                        index = Math.Clamp(index, 0, envelopeLength - 1);
                    }
                    var sample = envelopeLength == 0 ? 0.08d : _audioWaveEnvelope[index];
                    targetAmplitude = Math.Clamp(sample * 1.2d, 0.03d, 1d);
                }
            }

            if (targetAmplitude >= _waveAmplitudeSmoother)
            {
                _waveAmplitudeSmoother = (_waveAmplitudeSmoother * 0.04d) + (targetAmplitude * 0.96d);
            }
            else
            {
                _waveAmplitudeSmoother = (_waveAmplitudeSmoother * 0.40d) + (targetAmplitude * 0.60d);
            }

            var renderWave = AudioWavePath.Visibility == Visibility.Visible;
            var renderOrbs = AudioWaveOrbCanvas.Visibility == Visibility.Visible;
            if (renderWave || renderOrbs)
            {
                if (barCount >= 12)
                {
                    var center = new Point(width * 0.5d, height * 0.5d);
                    var ringRadius = Math.Max(42d, minSide * 0.24d);
                    var maxBarHeight = Math.Max(14d, minSide * 0.17d);
                    var volumeHeightFactor = 0.44d + (ViewModel.Volume * 0.74d);
                    var dynamicMaxBarHeight = Math.Min(minSide * 0.30d, maxBarHeight * volumeHeightFactor);
                    var volumeAmplitudeBoost = 0.70d + (ViewModel.Volume * 0.95d);
                    var floorHeightPx = Math.Max(1.2d, dynamicMaxBarHeight * 0.055d);

                    var drawLevels = _waveDrawLevels;
                    if (hasTimedSamples)
                    {
                        var trailStepMs = Math.Max(0.9d, (WaveVisibleHistoryScale * 1000d) / barCount);
                        var sampleWindowMs = Math.Max(4.5d, WaveBarAmplitudeWindowMs);
                        for (var i = 0; i < barCount; i++)
                        {
                            var barTimeMs = probeNowMs - (i * trailStepMs);
                            var amplitude = SampleRmsNearTime(sampleValues, sampleTimes, barTimeMs, sampleWindowMs);
                            if (amplitude < 0d)
                            {
                                amplitude = SampleAmplitudeNearTime(sampleValues, sampleTimes, barTimeMs, sampleWindowMs * 0.5d);
                            }

                            if (amplitude < 0d)
                            {
                                amplitude = _waveAmplitudeSmoother * 0.52d;
                            }

                            var normalized = Math.Clamp(amplitude * volumeAmplitudeBoost, 0d, 1.3d);
                            var targetPx = dynamicMaxBarHeight * Math.Clamp(normalized, 0d, 1d);

                            var current = _spectrumBarLevels[i];
                            if (targetPx >= current)
                            {
                                current = (current * 0.07d) + (targetPx * 0.93d);
                            }
                            else
                            {
                                current = (current * 0.38d) + (targetPx * 0.62d);
                            }

                            _spectrumBarLevels[i] = current;
                            drawLevels[i] = Math.Clamp(Math.Max(floorHeightPx, current), floorHeightPx, dynamicMaxBarHeight);
                        }
                    }
                    else
                    {
                        var headTargetPx = dynamicMaxBarHeight * Math.Clamp(_waveAmplitudeSmoother * volumeAmplitudeBoost, 0d, 1.2d);
                        var wrapped = _spectrumBarLevels[barCount - 1];
                        for (var i = barCount - 1; i >= 1; i--)
                        {
                            var shifted = _spectrumBarLevels[i - 1] * 0.97d;
                            var current = _spectrumBarLevels[i] * 0.92d;
                            _spectrumBarLevels[i] = Math.Max(current, shifted);
                        }
                        _spectrumBarLevels[0] = Math.Max(_spectrumBarLevels[0] * 0.92d, wrapped * 0.97d);

                        var headIndex = (int)((DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond / 28d) % barCount);
                        if (headIndex < 0)
                        {
                            headIndex += barCount;
                        }

                        _spectrumBarLevels[headIndex] = (_spectrumBarLevels[headIndex] * 0.18d) + (headTargetPx * 0.82d);
                        for (var i = 0; i < barCount; i++)
                        {
                            var shaped = _spectrumBarLevels[i];
                            drawLevels[i] = Math.Clamp(Math.Max(floorHeightPx, shaped), floorHeightPx, dynamicMaxBarHeight);
                        }
                    }

                    if (renderOrbs)
                    {
                        UpdateWaveOrbPositions(center, ringRadius, dynamicMaxBarHeight, minSide, width, height);
                    }

                    if (renderWave)
                    {
                        AudioWavePath.Data = BuildCircularSpectrumGeometry(width, height, center, ringRadius, drawLevels);
                    }
                    else
                    {
                        AudioWavePath.Data = null;
                    }
                }
                else if (renderWave)
                {
                    AudioWavePath.Data = null;
                }
            }
            else
            {
                AudioWavePath.Data = null;
            }
            UpdateWaveSpinTransform();
        }

        private void UpdateWaveSpinTransform()
        {
            if (AudioWaveSpinTransform == null)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (_lastWaveSpinUpdateUtc == DateTime.MinValue)
            {
                _lastWaveSpinUpdateUtc = now;
                return;
            }

            var dt = (now - _lastWaveSpinUpdateUtc).TotalSeconds;
            _lastWaveSpinUpdateUtc = now;
            if (dt <= 0d || dt > 0.25d)
            {
                dt = 1d / 60d;
            }

            var spinSpeed = WaveSpinBaseDegPerSec + (_waveAmplitudeSmoother * WaveSpinBoostDegPerSec);
            _waveSpinAngle = (_waveSpinAngle + (spinSpeed * dt)) % 360d;
            if (_waveSpinAngle < 0d)
            {
                _waveSpinAngle += 360d;
            }

            AudioWaveSpinTransform.Angle = _waveSpinAngle;
        }

        private void InitializeWaveOrbs()
        {
            if (AudioWaveOrbCanvas == null || _waveOrbs.Count > 0)
            {
                return;
            }

            EnsureWaveOrbCount(GetTargetWaveOrbCount());
        }

        private int GetTargetWaveOrbCount()
        {
            return IsWindowFullscreenForOrbs()
                ? WaveOrbBaseCount + WaveOrbFullscreenExtraCount
                : WaveOrbBaseCount;
        }

        private bool IsWindowFullscreenForOrbs()
        {
            return this.WindowState == WindowState.Maximized;
        }

        private void SyncWaveOrbsWithWindowState()
        {
            if (AudioWaveOrbCanvas == null)
            {
                return;
            }

            EnsureWaveOrbCount(GetTargetWaveOrbCount());
            ResetWaveOrbLayout(randomizeVelocity: true);
        }

        private void ResetWaveOrbLayout(bool randomizeVelocity)
        {
            foreach (var orb in _waveOrbs)
            {
                orb.PositionX = -1d;
                orb.PositionY = -1d;
                orb.WanderTimer = 0d;
                if (randomizeVelocity)
                {
                    orb.VelocityX = (_waveOrbRandom.NextDouble() * 2d - 1d) * (12d + (_waveOrbRandom.NextDouble() * 18d));
                    orb.VelocityY = (_waveOrbRandom.NextDouble() * 2d - 1d) * (12d + (_waveOrbRandom.NextDouble() * 18d));
                }
            }
        }

        private void EnsureWaveOrbCount(int targetCount)
        {
            if (AudioWaveOrbCanvas == null || targetCount < 0)
            {
                return;
            }

            while (_waveOrbs.Count < targetCount)
            {
                _waveOrbs.Add(CreateWaveOrbState());
            }

            while (_waveOrbs.Count > targetCount)
            {
                var lastIndex = _waveOrbs.Count - 1;
                var orb = _waveOrbs[lastIndex];
                AudioWaveOrbCanvas.Children.Remove(orb.Shape);
                _waveOrbs.RemoveAt(lastIndex);
            }
        }

        private WaveOrbState CreateWaveOrbState()
        {
            if (AudioWaveOrbCanvas == null)
            {
                throw new InvalidOperationException("Orb canvas is not initialized.");
            }

            var coreStop = new GradientStop(Colors.White, 0d);
            var midStop = new GradientStop(Colors.White, 0.58d);
            var outerStop = new GradientStop(Color.FromArgb(0, 255, 255, 255), 1d);
            var fillBrush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.36d, 0.36d),
                Center = new Point(0.50d, 0.50d),
                RadiusX = 0.66d,
                RadiusY = 0.66d
            };
            fillBrush.GradientStops.Add(coreStop);
            fillBrush.GradientStops.Add(midStop);
            fillBrush.GradientStops.Add(outerStop);

            var baseSizePx = 8d + (_waveOrbRandom.NextDouble() * 10d);
            var orb = new System.Windows.Shapes.Ellipse
            {
                Width = baseSizePx,
                Height = baseSizePx,
                Opacity = 0.82d,
                Fill = fillBrush,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true,
                Effect = new BlurEffect
                {
                    Radius = 1.4d + (_waveOrbRandom.NextDouble() * 3.6d),
                    KernelType = KernelType.Gaussian,
                    RenderingBias = RenderingBias.Quality
                }
            };

            AudioWaveOrbCanvas.Children.Add(orb);
            var wanderAngle = _waveOrbRandom.NextDouble() * Math.PI * 2d;
            return new WaveOrbState
            {
                Shape = orb,
                CoreStop = coreStop,
                MidStop = midStop,
                OuterStop = outerStop,
                PositionX = -1d,
                PositionY = -1d,
                VelocityX = (_waveOrbRandom.NextDouble() * 2d - 1d) * (12d + (_waveOrbRandom.NextDouble() * 18d)),
                VelocityY = (_waveOrbRandom.NextDouble() * 2d - 1d) * (12d + (_waveOrbRandom.NextDouble() * 18d)),
                WanderX = Math.Cos(wanderAngle),
                WanderY = Math.Sin(wanderAngle),
                WanderTimer = 0.20d + (_waveOrbRandom.NextDouble() * 1.40d),
                BaseSizePx = baseSizePx,
                PulsePhase = _waveOrbRandom.NextDouble() * Math.PI * 2d,
                PulseSpeed = 0.42d + (_waveOrbRandom.NextDouble() * 0.90d),
                HueOffset = _waveOrbRandom.NextDouble() * 360d,
            };
        }

        private void UpdateWaveOrbPositions(Point center, double ringRadius, double maxBarHeight, double minSide, double width, double height)
        {
            if (AudioWaveOrbCanvas == null)
            {
                return;
            }

            EnsureWaveOrbCount(GetTargetWaveOrbCount());
            if (_waveOrbs.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (_lastWaveOrbUpdateUtc == DateTime.MinValue)
            {
                _lastWaveOrbUpdateUtc = now;
            }

            var dt = (now - _lastWaveOrbUpdateUtc).TotalSeconds;
            _lastWaveOrbUpdateUtc = now;
            if (dt <= 0d || dt > 0.25d)
            {
                dt = 1d / 60d;
            }

            _waveOrbClockSeconds += dt;
            var viewportChanged =
                Math.Abs(width - _lastWaveOrbViewportWidth) > 56d ||
                Math.Abs(height - _lastWaveOrbViewportHeight) > 56d;
            if (viewportChanged)
            {
                _lastWaveOrbViewportWidth = width;
                _lastWaveOrbViewportHeight = height;
                ResetWaveOrbLayout(randomizeVelocity: false);
            }

            var isFullscreen = IsWindowFullscreenForOrbs();
            var amplitude = Math.Clamp(_waveAmplitudeSmoother, 0d, 1d);
            var safeRadius = ringRadius + maxBarHeight + WaveOrbSafeOffsetPx;
            var fullscreenSpeedScale = isFullscreen ? WaveOrbFullscreenSpeedBoost : 1d;
            var fullscreenSizeScale = isFullscreen ? WaveOrbFullscreenSizeBoost : 1d;
            var driftForce = (5d + (amplitude * 8d)) * fullscreenSpeedScale;
            var randomForce = (18d + (amplitude * 22d)) * fullscreenSpeedScale;
            var damping = 0.972d;
            var maxSpeed = (15d + (amplitude * 20d)) * fullscreenSpeedScale;
            var boundsMargin = 6d;
            var leftBound = boundsMargin;
            var topBound = boundsMargin;
            var rightBound = Math.Max(leftBound + 1d, width - boundsMargin);
            var bottomBound = Math.Max(topBound + 1d, height - boundsMargin);

            foreach (var orb in _waveOrbs)
            {
                if (orb.PositionX < 0d || orb.PositionY < 0d)
                {
                    PlaceOrbOutsideWave(orb, center, safeRadius, width, height);
                }

                orb.WanderTimer -= dt;
                if (orb.WanderTimer <= 0d)
                {
                    var wanderAngle = _waveOrbRandom.NextDouble() * Math.PI * 2d;
                    orb.WanderX = Math.Cos(wanderAngle);
                    orb.WanderY = Math.Sin(wanderAngle);
                    orb.WanderTimer = 0.20d + (_waveOrbRandom.NextDouble() * 1.60d);
                }

                var jitterX = (_waveOrbRandom.NextDouble() * 2d) - 1d;
                var jitterY = (_waveOrbRandom.NextDouble() * 2d) - 1d;
                orb.VelocityX += ((orb.WanderX * driftForce) + (jitterX * randomForce)) * dt;
                orb.VelocityY += ((orb.WanderY * driftForce) + (jitterY * randomForce)) * dt;
                orb.VelocityX *= damping;
                orb.VelocityY *= damping;
                var speed = Math.Sqrt((orb.VelocityX * orb.VelocityX) + (orb.VelocityY * orb.VelocityY));
                if (speed > maxSpeed)
                {
                    var clamp = maxSpeed / speed;
                    orb.VelocityX *= clamp;
                    orb.VelocityY *= clamp;
                }

                orb.PositionX += orb.VelocityX * dt;
                orb.PositionY += orb.VelocityY * dt;

                var pulse = 0.78d + (0.24d * Math.Sin((_waveOrbClockSeconds * orb.PulseSpeed) + orb.PulsePhase));
                var size = Math.Clamp(
                    orb.BaseSizePx * fullscreenSizeScale * (0.86d + (pulse * 0.30d) + (amplitude * 0.16d)),
                    7d,
                    Math.Max(11d, minSide * (isFullscreen ? 0.07d : 0.06d)));
                var half = size * 0.5d;

                if (orb.PositionX < leftBound + half)
                {
                    orb.PositionX = leftBound + half;
                    orb.VelocityX = Math.Abs(orb.VelocityX) * 0.84d;
                }
                else if (orb.PositionX > rightBound - half)
                {
                    orb.PositionX = rightBound - half;
                    orb.VelocityX = -Math.Abs(orb.VelocityX) * 0.84d;
                }

                if (orb.PositionY < topBound + half)
                {
                    orb.PositionY = topBound + half;
                    orb.VelocityY = Math.Abs(orb.VelocityY) * 0.84d;
                }
                else if (orb.PositionY > bottomBound - half)
                {
                    orb.PositionY = bottomBound - half;
                    orb.VelocityY = -Math.Abs(orb.VelocityY) * 0.84d;
                }

                var dx = orb.PositionX - center.X;
                var dy = orb.PositionY - center.Y;
                var minDistance = safeRadius + (half * 0.55d);
                var distance = Math.Sqrt((dx * dx) + (dy * dy));
                if (distance < minDistance && distance > 0.001d)
                {
                    var nx = dx / distance;
                    var ny = dy / distance;
                    orb.PositionX = center.X + (nx * minDistance);
                    orb.PositionY = center.Y + (ny * minDistance);

                    var inward = (orb.VelocityX * nx) + (orb.VelocityY * ny);
                    if (inward < 0d)
                    {
                        orb.VelocityX -= inward * 1.6d * nx;
                        orb.VelocityY -= inward * 1.6d * ny;
                    }
                }

                orb.Shape.Width = size;
                orb.Shape.Height = size;
                Canvas.SetLeft(orb.Shape, orb.PositionX - half);
                Canvas.SetTop(orb.Shape, orb.PositionY - half);

                var hue = (orb.HueOffset + (_waveOrbClockSeconds * 27d) + (Math.Abs(orb.VelocityX) * 0.5d)) % 360d;
                var coreColor = HsvToColor((hue + 16d) % 360d, 0.28d, 1d);
                var midColor = HsvToColor(hue, 0.74d, 0.97d);
                var glowStrength = Math.Clamp(0.70d + (pulse * 0.22d) + (amplitude * 0.16d), 0d, 1d);

                orb.CoreStop.Color = Color.FromArgb((byte)(178 + (56d * glowStrength)), coreColor.R, coreColor.G, coreColor.B);
                orb.MidStop.Color = Color.FromArgb((byte)(116 + (88d * glowStrength)), midColor.R, midColor.G, midColor.B);
                orb.OuterStop.Color = Color.FromArgb(0, midColor.R, midColor.G, midColor.B);
                orb.Shape.Opacity = Math.Clamp(0.58d + (pulse * 0.18d) + (amplitude * 0.10d), 0.56d, 0.90d);
            }
        }

        private void PlaceOrbOutsideWave(WaveOrbState orb, Point center, double safeRadius, double width, double height)
        {
            if (width <= 1d || height <= 1d)
            {
                orb.PositionX = Math.Max(0.5d, width * 0.5d);
                orb.PositionY = Math.Max(0.5d, height * 0.5d);
                orb.WanderTimer = 0d;
                return;
            }

            var margin = 8d;
            var minDistance = safeRadius + orb.BaseSizePx;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var x = margin + (_waveOrbRandom.NextDouble() * Math.Max(1d, width - (margin * 2d)));
                var y = margin + (_waveOrbRandom.NextDouble() * Math.Max(1d, height - (margin * 2d)));
                var dx = x - center.X;
                var dy = y - center.Y;
                if ((dx * dx) + (dy * dy) >= (minDistance * minDistance))
                {
                    orb.PositionX = x;
                    orb.PositionY = y;
                    orb.WanderTimer = 0d;
                    return;
                }
            }

            var angle = _waveOrbRandom.NextDouble() * Math.PI * 2d;
            var radius = Math.Min(
                Math.Max(minDistance + 8d, 10d),
                Math.Max(10d, (Math.Min(width, height) * 0.5d) - 10d));
            var fallbackX = center.X + (Math.Cos(angle) * radius);
            var fallbackY = center.Y + (Math.Sin(angle) * radius);
            orb.PositionX = Math.Clamp(fallbackX, margin, width - margin);
            orb.PositionY = Math.Clamp(fallbackY, margin, height - margin);
            orb.WanderTimer = 0d;
        }

        private static Color HsvToColor(double hue, double saturation, double value)
        {
            hue %= 360d;
            if (hue < 0d)
            {
                hue += 360d;
            }

            saturation = Math.Clamp(saturation, 0d, 1d);
            value = Math.Clamp(value, 0d, 1d);
            if (saturation <= 0d)
            {
                var gray = (byte)Math.Round(value * 255d);
                return Color.FromRgb(gray, gray, gray);
            }

            var chroma = value * saturation;
            var hueSector = hue / 60d;
            var x = chroma * (1d - Math.Abs((hueSector % 2d) - 1d));
            var match = value - chroma;
            double r1;
            double g1;
            double b1;
            if (hueSector < 1d)
            {
                r1 = chroma; g1 = x; b1 = 0d;
            }
            else if (hueSector < 2d)
            {
                r1 = x; g1 = chroma; b1 = 0d;
            }
            else if (hueSector < 3d)
            {
                r1 = 0d; g1 = chroma; b1 = x;
            }
            else if (hueSector < 4d)
            {
                r1 = 0d; g1 = x; b1 = chroma;
            }
            else if (hueSector < 5d)
            {
                r1 = x; g1 = 0d; b1 = chroma;
            }
            else
            {
                r1 = chroma; g1 = 0d; b1 = x;
            }

            var r = (byte)Math.Round((r1 + match) * 255d);
            var g = (byte)Math.Round((g1 + match) * 255d);
            var b = (byte)Math.Round((b1 + match) * 255d);
            return Color.FromRgb(r, g, b);
        }

        private static Geometry BuildCircularSpectrumGeometry(double width, double height, Point center, double baseRadius, IReadOnlyList<double> lengths)
        {
            if (width <= 1d || height <= 1d || baseRadius <= 1d || lengths.Count < 12)
            {
                return Geometry.Empty;
            }

            var barCount = lengths.Count;
            var maxOuterRadius = (Math.Min(width, height) * 0.5d) - 2d;
            if (maxOuterRadius <= baseRadius)
            {
                return Geometry.Empty;
            }

            var angleStep = (Math.PI * 2d) / barCount;
            var halfSweep = angleStep * (0.5d - (CircularBarGapRatio * 0.5d));
            if (halfSweep < angleStep * 0.06d)
            {
                halfSweep = angleStep * 0.06d;
            }

            var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var context = geometry.Open())
            {
                for (var i = 0; i < barCount; i++)
                {
                    var length = lengths[i];
                    if (length > 0.05d)
                    {
                        var angle = (-Math.PI / 2d) + (i * angleStep);
                        var leftAngle = angle - halfSweep;
                        var rightAngle = angle + halfSweep;
                        var innerRadius = Math.Max(2d, baseRadius - (length * CircularInwardRatio));
                        var outerRadius = Math.Min(maxOuterRadius, baseRadius + length);
                        if (outerRadius > innerRadius + 0.2d)
                        {
                            var p1 = PolarPoint(center, innerRadius, leftAngle);
                            var p2 = PolarPoint(center, outerRadius, leftAngle);
                            var p3 = PolarPoint(center, outerRadius, rightAngle);
                            var p4 = PolarPoint(center, innerRadius, rightAngle);

                            context.BeginFigure(p1, true, true);
                            context.LineTo(p2, true, false);
                            context.LineTo(p3, true, false);
                            context.LineTo(p4, true, false);
                        }
                    }
                }
            }

            geometry.Freeze();
            return geometry;
        }

        private static Point PolarPoint(Point center, double radius, double angle)
        {
            return new Point(
                center.X + (Math.Cos(angle) * radius),
                center.Y + (Math.Sin(angle) * radius));
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
                // Ignore extreme outliers so a single timing blip does not push visuals ahead.
                observedOffset = Math.Clamp(observedOffset, -220d, 1500d);
                if (!_waveProbeOffsetInitialized)
                {
                    _waveProbeToMediaOffsetMs = observedOffset;
                    _waveProbeOffsetInitialized = true;
                }
                else
                {
                    _waveProbeToMediaOffsetMs = (_waveProbeToMediaOffsetMs * 0.4d) + (observedOffset * 0.6d);
                }

                var alignedTimeMs = mediaTimeMs - _waveProbeToMediaOffsetMs;
                // Never sample a probe time newer than the compensated media clock.
                if (alignedTimeMs > mediaTimeMs)
                {
                    alignedTimeMs = mediaTimeMs;
                }

                return Math.Max(0d, alignedTimeMs);
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

        private int CopyRecentAudioSamplesWithTimes(int requestedCount, double[] valueBuffer, double[] timeBuffer)
        {
            if (requestedCount <= 0 || valueBuffer.Length == 0 || timeBuffer.Length == 0)
            {
                return 0;
            }

            var capacity = Math.Min(valueBuffer.Length, timeBuffer.Length);
            var desiredCount = Math.Min(requestedCount, capacity);

            lock (_audioEnergyLock)
            {
                var available = _audioSampleFilled ? AudioSampleHistorySize : _audioSampleWriteIndex;
                if (available <= 0)
                {
                    return 0;
                }

                var count = Math.Min(desiredCount, available);
                var start = (_audioSampleWriteIndex - count + AudioSampleHistorySize) % AudioSampleHistorySize;
                for (var i = 0; i < count; i++)
                {
                    var index = (start + i) % AudioSampleHistorySize;
                    valueBuffer[i] = _audioSampleHistory[index];
                    timeBuffer[i] = _audioSampleTimeHistory[index];
                }

                return count;
            }
        }

        private static double SampleAmplitudeNearTime(ReadOnlySpan<double> values, ReadOnlySpan<double> times, double targetTimeMs, double windowMs)
        {
            var count = Math.Min(values.Length, times.Length);
            if (count == 0)
            {
                return -1d;
            }

            var nearestAbs = 0d;
            var nearestDelta = double.MaxValue;
            var peakInWindow = 0d;

            var low = 0;
            var high = count - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) / 2);
                if (times[mid] < targetTimeMs)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            var left = low - 1;
            var right = low;

            if (left >= 0)
            {
                nearestDelta = Math.Abs(times[left] - targetTimeMs);
                nearestAbs = Math.Abs(values[left]);
            }

            if (right < count)
            {
                var rightDelta = Math.Abs(times[right] - targetTimeMs);
                if (rightDelta < nearestDelta)
                {
                    nearestDelta = rightDelta;
                    nearestAbs = Math.Abs(values[right]);
                }
            }

            for (var i = left; i >= 0; i--)
            {
                var delta = targetTimeMs - times[i];
                if (delta > windowMs)
                {
                    break;
                }

                var abs = Math.Abs(values[i]);
                if (abs > peakInWindow)
                {
                    peakInWindow = abs;
                }
            }

            for (var i = right; i < count; i++)
            {
                var delta = times[i] - targetTimeMs;
                if (delta > windowMs)
                {
                    break;
                }

                var abs = Math.Abs(values[i]);
                if (abs > peakInWindow)
                {
                    peakInWindow = abs;
                }
            }

            if (peakInWindow > 0d)
            {
                return peakInWindow;
            }

            return nearestAbs;
        }

        private static double SampleRmsNearTime(ReadOnlySpan<double> values, ReadOnlySpan<double> times, double targetTimeMs, double windowMs)
        {
            var count = Math.Min(values.Length, times.Length);
            if (count == 0 || windowMs <= 0d)
            {
                return -1d;
            }

            var low = 0;
            var high = count - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) / 2);
                if (times[mid] < targetTimeMs)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            var left = low - 1;
            var right = low;
            var sum = 0d;
            var hit = 0;

            for (var i = left; i >= 0; i--)
            {
                var delta = targetTimeMs - times[i];
                if (delta > windowMs)
                {
                    break;
                }

                var sample = values[i];
                sum += sample * sample;
                hit++;
            }

            for (var i = right; i < count; i++)
            {
                var delta = times[i] - targetTimeMs;
                if (delta > windowMs)
                {
                    break;
                }

                var sample = values[i];
                sum += sample * sample;
                hit++;
            }

            if (hit == 0)
            {
                return -1d;
            }

            var rms = Math.Sqrt(sum / hit);
            return Math.Clamp(rms * 2.9d, 0d, 1d);
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

                if (ViewModel.IsPlaying && _isCurrentTrackAudioOnly && ViewModel.Selected != null)
                {
                    var nowUtc = DateTime.UtcNow;
                    if ((nowUtc - _lastWaveProbeSoftSyncUtc).TotalMilliseconds >= WaveProbeSoftSyncIntervalMs)
                    {
                        _lastWaveProbeSoftSyncUtc = nowUtc;
                        SyncWaveProbePosition(force: false);
                    }
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
                var targetSeconds = ViewModel.Position;
                if (sender is System.Windows.Controls.Slider slider)
                {
                    targetSeconds = slider.Value;
                    _suppressPositionSync = true;
                    ViewModel.Position = targetSeconds;
                    _suppressPositionSync = false;
                }
                // Apply the position immediately to mediaElement
                mediaElement.Position = TimeSpan.FromSeconds(targetSeconds);
                SyncWaveProbePosition(true);
            }
            catch
            {
                _suppressPositionSync = false;
            }
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
                WindowOuterBorder.BeginAnimation(OpacityProperty, null);

                TopBarRow.Height = new GridLength(38);
                ControlPanel.Visibility = Visibility.Visible;
                TopBar.Visibility = Visibility.Visible;
                ControlPanel.Opacity = 1;
                TopBar.Opacity = 1;
                WindowOuterBorder.Visibility = Visibility.Visible;
                WindowOuterBorder.Opacity = 1;
                _controlsVisible = true;
                if (ViewModel.AutoHideControls)
                {
                    _hideControlsTimer?.Start();
                }
            }
            catch { }
        }

        private void HideControls()
        {
            try
            {
                if (!ViewModel.AutoHideControls)
                {
                    ShowControls();
                    return;
                }
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
                        WindowOuterBorder.Opacity = 0;
                    }
                    catch { }
                };
                ControlPanel.BeginAnimation(OpacityProperty, fadeOut);
                TopBar.BeginAnimation(OpacityProperty, fadeOut);
                WindowOuterBorder.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch { }
        }

        private void UpdateAutoHideBehavior()
        {
            if (ViewModel.AutoHideControls)
            {
                ShowControls();
                return;
            }

            _hideControlsTimer?.Stop();
            _controlsAnimationVersion++;
            ControlPanel.BeginAnimation(OpacityProperty, null);
            TopBar.BeginAnimation(OpacityProperty, null);
            WindowOuterBorder.BeginAnimation(OpacityProperty, null);
            TopBarRow.Height = new GridLength(38);
            ControlPanel.Visibility = Visibility.Visible;
            TopBar.Visibility = Visibility.Visible;
            ControlPanel.Opacity = 1;
            TopBar.Opacity = 1;
            WindowOuterBorder.Visibility = Visibility.Visible;
            WindowOuterBorder.Opacity = 1;
            _controlsVisible = true;
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
                        Owner = this,
                        ShowInTaskbar = false
                    };
                    _extendedModeWindow.Closed += ExtendedModeWindow_Closed;
                }
                _extendedModeWindow.DataContext = DataContext;

                MovePlaybackVisualsToHost(_extendedModeWindow.HostPanel);
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
                MovePlaybackVisualsToHost(MediaHost);

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
            MovePlaybackVisualsToHost(MediaHost);
            _extendedModeWindow = null;
            _isExtendedMode = false;
            ExtendedModeButton.Content = "Extended Mode";
            ExtendedModeStatusLabel.Visibility = Visibility.Collapsed;
        }

        private void MovePlaybackVisualsToHost(Panel targetHost)
        {
            if (targetHost == null)
            {
                return;
            }

            MoveElementToPanel(mediaElement, targetHost);

            if (ReferenceEquals(targetHost, MediaHost))
            {
                MoveAudioWaveOverlayHome();
            }
            else
            {
                MoveElementToPanel(AudioWaveOverlay, targetHost);
            }
        }

        private static void MoveElementToPanel(FrameworkElement element, Panel targetHost)
        {
            if (element.Parent == targetHost)
            {
                return;
            }

            if (element.Parent is Panel currentParent)
            {
                currentParent.Children.Remove(element);
            }

            targetHost.Children.Add(element);
        }

        private void MoveAudioWaveOverlayHome()
        {
            if (_audioWaveOverlayHomeParent == null)
            {
                return;
            }

            if (AudioWaveOverlay.Parent == _audioWaveOverlayHomeParent)
            {
                return;
            }

            if (AudioWaveOverlay.Parent is Panel currentParent)
            {
                currentParent.Children.Remove(AudioWaveOverlay);
            }

            var insertIndex = _audioWaveOverlayHomeIndex;
            if (insertIndex < 0 || insertIndex > _audioWaveOverlayHomeParent.Children.Count)
            {
                _audioWaveOverlayHomeParent.Children.Add(AudioWaveOverlay);
            }
            else
            {
                _audioWaveOverlayHomeParent.Children.Insert(insertIndex, AudioWaveOverlay);
            }
        }
    }
}
