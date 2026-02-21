using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;

namespace MDJMediaPlayer
{
    public partial class SFXWindow : Window
    {
        public ObservableCollection<SfxItem> SfxItems { get; } = new();
        private readonly MediaPlayer _player = new();

        public SFXWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private bool _allowClose = false;

        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnClosing(e);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void Titlebar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is System.Windows.Controls.Button
                    || source is System.Windows.Controls.Primitives.ToggleButton
                    || source is System.Windows.Controls.ComboBox
                    || source is System.Windows.Controls.Menu
                    || source is System.Windows.Controls.MenuItem)
                {
                    return;
                }
                source = VisualTreeHelper.GetParent(source);
            }
            try { DragMove(); } catch { }
        }

        private void AddSfxButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Add SFX",
                Filter = "Audio Files|*.mp3;*.wav;*.ogg;*.flac;*.aac;*.wma|All Files|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            foreach (var filePath in dialog.FileNames)
            {
                if (SfxItems.Any(item => item.FilePath == filePath))
                {
                    continue;
                }

                SfxItems.Add(new SfxItem(filePath, Path.GetFileName(filePath)));
            }
        }

        private void SfxItemButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not SfxItem item)
            {
                return;
            }

            if (!File.Exists(item.FilePath))
            {
                MessageBox.Show("File not found: " + item.FilePath, "SFX", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _player.Open(new Uri(item.FilePath, UriKind.Absolute));
            _player.Position = TimeSpan.Zero;
            _player.Play();
        }

        private void SaveSfxPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (SfxItems.Count == 0)
            {
                MessageBox.Show("There are no SFX items to save.", "Save SFX Playlist", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save SFX Playlist",
                Filter = "SFX Playlist|*.sfx|Text File|*.txt|All Files|*.*",
                DefaultExt = "sfx",
                FileName = "sfx-playlist.sfx"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                using var writer = new StreamWriter(dialog.FileName, false);
                foreach (var item in SfxItems)
                {
                    writer.WriteLine(item.FilePath);
                }

                MessageBox.Show("SFX playlist saved successfully.", "Save SFX Playlist", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (IOException ex)
            {
                MessageBox.Show("Failed to save SFX playlist: " + ex.Message, "Save SFX Playlist", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenSfxPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Open SFX Playlist",
                Filter = "SFX Playlist|*.sfx;*.txt|All Files|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var baseDir = Path.GetDirectoryName(dialog.FileName) ?? Directory.GetCurrentDirectory();
                int added = 0;
                foreach (var raw in File.ReadLines(dialog.FileName))
                {
                    var line = raw.Trim();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }
                    if (line.StartsWith("#"))
                    {
                        continue;
                    }

                    var path = line;
                    if (!Path.IsPathRooted(path))
                    {
                        path = Path.GetFullPath(Path.Combine(baseDir, path));
                    }

                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    if (SfxItems.Any(i => string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    SfxItems.Add(new SfxItem(path, Path.GetFileName(path)));
                    added++;
                }

                if (added == 0)
                {
                    MessageBox.Show("No SFX items were added.", "Open SFX Playlist", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Added " + added + " item(s) from playlist.", "Open SFX Playlist", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (IOException ex)
            {
                MessageBox.Show("Failed to open SFX playlist: " + ex.Message, "Open SFX Playlist", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearSfxList_Click(object sender, RoutedEventArgs e)
        {
            if (SfxItems.Count == 0)
            {
                MessageBox.Show("SFX list is already empty.", "Clear SFX List", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            SfxItems.Clear();
            MessageBox.Show("SFX list cleared.", "Clear SFX List", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public sealed class SfxItem
        {
            public SfxItem(string filePath, string displayName)
            {
                FilePath = filePath;
                DisplayName = displayName;
            }

            public string FilePath { get; }
            public string DisplayName { get; }
        }
    }
}
