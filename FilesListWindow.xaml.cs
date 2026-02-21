using MDJMediaPlayer.ViewModels;
using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MDJMediaPlayer
{
    public partial class FilesListWindow : Window
    {
        public FilesListWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Titlebar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            // Avoid dragging when clicking on interactive controls
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

            try { this.DragMove(); } catch { }
        }

        private void SavePlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel || viewModel.Playlist.Count == 0)
            {
                MessageBox.Show("There are no items in the playlist to save.", "Save Playlist", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save Playlist",
                Filter = "M3U Playlist|*.m3u|Text File|*.txt|All Files|*.*",
                DefaultExt = "m3u",
                FileName = "playlist.m3u"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                using var writer = new StreamWriter(dialog.FileName, false);
                writer.WriteLine("#EXTM3U");
                foreach (var item in viewModel.Playlist)
                {
                    if (!string.IsNullOrWhiteSpace(item.Title))
                    {
                        writer.WriteLine("#EXTINF:-1," + item.Title);
                    }

                    writer.WriteLine(item.FilePath);
                }

                MessageBox.Show("Playlist saved successfully.", "Save Playlist", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (IOException ex)
            {
                MessageBox.Show("Failed to save playlist: " + ex.Message, "Save Playlist", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel)
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Open Playlist",
                Filter = "Playlist Files|*.m3u;*.m3u8;*.txt|All Files|*.*"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var baseDirectory = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
                var addedCount = 0;

                foreach (var rawLine in File.ReadLines(dialog.FileName))
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    {
                        continue;
                    }

                    var mediaPath = line;
                    if (!Path.IsPathRooted(mediaPath))
                    {
                        mediaPath = Path.GetFullPath(Path.Combine(baseDirectory, mediaPath));
                    }

                    if (!File.Exists(mediaPath))
                    {
                        continue;
                    }

                    viewModel.Playlist.Add(new Models.MediaItem
                    {
                        FilePath = mediaPath,
                        Title = Path.GetFileName(mediaPath)
                    });
                    addedCount++;
                }

                if (viewModel.Selected == null && viewModel.Playlist.Count > 0)
                {
                    viewModel.Selected = viewModel.Playlist[0];
                }

                MessageBox.Show($"Added {addedCount} item(s) from playlist.", "Open Playlist", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open playlist: " + ex.Message, "Open Playlist", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel)
            {
                return;
            }

            if (viewModel.Playlist.Count == 0)
            {
                return;
            }

            var result = MessageBox.Show(
                "Clear all items from the playlist?",
                "Clear Playlist",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            viewModel.Playlist.Clear();
            viewModel.Selected = null;
            viewModel.IsPlaying = false;
            viewModel.Position = 0;
        }
    }
}
