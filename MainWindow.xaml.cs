using MDJMediaPlayer.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
                var version = "1.1.1";
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

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.Selected))
            {
                try
                {
                    if (ViewModel.Selected != null)
                    {
                        // Validate file exists before attempting to play
                        if (!System.IO.File.Exists(ViewModel.Selected.FilePath))
                        {
                            MessageBox.Show("File not found: " + ViewModel.Selected.FilePath, "Playback Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            DebugStatus.Text = "File not found: " + ViewModel.Selected.FilePath;
                            return;
                        }

                        mediaElement.Stop();
                        mediaElement.Source = new Uri(ViewModel.Selected.FilePath, UriKind.Absolute);
                        mediaElement.Position = TimeSpan.FromSeconds(ViewModel.Position);
                        mediaElement.Volume = ViewModel.Volume;
                        if (ViewModel.IsPlaying) mediaElement.Play();
                        DebugStatus.Text = "Source: " + ViewModel.Selected.FilePath;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Unable to play file: " + ex.Message, "Playback Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    DebugStatus.Text = "Error: " + ex.Message;
                }
            }

            if (e.PropertyName == nameof(ViewModel.IsPlaying))
            {
                if (ViewModel.IsPlaying) mediaElement.Play(); else mediaElement.Pause();
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

                    // If video size is zero, likely no video stream or unsupported codec
                    if (mediaElement.NaturalVideoWidth == 0 || mediaElement.NaturalVideoHeight == 0)
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

            // No fallback here; user can install codecs or try another file
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
