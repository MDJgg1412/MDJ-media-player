using MDJMediaPlayer.Helpers;
using MDJMediaPlayer.Models;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;

namespace MDJMediaPlayer.ViewModels
{
    // Main ViewModel for the player
    public class MainViewModel : INotifyPropertyChanged
    {
        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".aac", ".flac", ".m4a", ".mp3", ".ogg", ".wav", ".wma"
        };

        public ObservableCollection<MediaItem> Playlist { get; } = new();

        private MediaItem? _selected;
        public MediaItem? Selected
        {
            get => _selected;
            set 
            { 
                _selected = value; 
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); 
            }
        }

        public bool NoVideoFound => Playlist.Count == 0;

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set { _isPlaying = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying))); }
        }

        private bool _isNewVideoAvailable;
        public bool IsNewVideoAvailable
        {
            get => _isNewVideoAvailable;
            set
            {
                if (_isNewVideoAvailable == value) return;
                _isNewVideoAvailable = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNewVideoAvailable)));

                if (value)
                {
                    Task.Delay(4000).ContinueWith(_ => IsNewVideoAvailable = false);
                }
            }
        }

        private double _volume = 0.8;
        public double Volume
        {
            get => _volume;
            set { _volume = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Volume))); }
        }

        private string _theme = "Zune";
        public string Theme
        {
            get => _theme;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(normalized, "Dark", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = "Zune";
                }
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    normalized = "Aero";
                }
                if (string.Equals(_theme, normalized, StringComparison.OrdinalIgnoreCase)) return;
                _theme = normalized;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Theme)));
            }
        }

        private bool _loopMedia;
        public bool LoopMedia
        {
            get => _loopMedia;
            set
            {
                if (_loopMedia == value) return;
                _loopMedia = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoopMedia)));
            }
        }

        private bool _autoplayMedia = true;
        public bool AutoplayMedia
        {
            get => _autoplayMedia;
            set
            {
                if (_autoplayMedia == value) return;
                _autoplayMedia = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoplayMedia)));
            }
        }

        private bool _autoNext = false;
        public bool AutoNext
        {
            get => _autoNext;
            set
            {
                if (_autoNext == value) return;
                _autoNext = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoNext)));
            }
        }

        private bool _loopPlaylist;
        public bool LoopPlaylist
        {
            get => _loopPlaylist;
            set
            {
                if (_loopPlaylist == value) return;
                _loopPlaylist = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoopPlaylist)));
            }
        }

        private bool _autoHideControls = false;
        public bool AutoHideControls
        {
            get => _autoHideControls;
            set
            {
                if (_autoHideControls == value) return;
                _autoHideControls = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoHideControls)));
            }
        }


        private double _aeroColorLevel = 100d;
        public double AeroColorLevel
        {
            get => _aeroColorLevel;
            set
            {
                var clamped = Math.Clamp(value, 0d, 100d);
                if (Math.Abs(_aeroColorLevel - clamped) < 0.001d) return;
                _aeroColorLevel = clamped;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AeroColorLevel)));
            }
        }

        private double _aeroTransparency = 0d;
        public double AeroTransparency
        {
            get => _aeroTransparency;
            set
            {
                var clamped = Math.Clamp(value, 0d, 100d);
                if (Math.Abs(_aeroTransparency - clamped) < 0.001d) return;
                _aeroTransparency = clamped;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AeroTransparency)));
            }
        }

        private double _position;
        // Position in seconds for binding to seek slider
        public double Position
        {
            get => _position;
            set { _position = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Position))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTimeText))); }
        }

        private double _duration;
        public double Duration
        {
            get => _duration;
            set { _duration = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Duration))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalTimeText))); }
        }

        public string CurrentTimeText => TimeSpan.FromSeconds(Position).ToString(@"m\:ss");
        public string TotalTimeText => TimeSpan.FromSeconds(Duration).ToString(@"m\:ss");

        public ICommand AddFilesCommand { get; }
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PrevCommand { get; }
        public ICommand SeekCommand { get; }

        public MainViewModel()
        {
            Playlist.CollectionChanged += (_, _) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoVideoFound)));

            AddFilesCommand = new RelayCommand(_ => AddFiles());
            PlayPauseCommand = new RelayCommand(_ => TogglePlayPause());
            StopCommand = new RelayCommand(_ => Stop());
            NextCommand = new RelayCommand(_ => PlayNext());
            PrevCommand = new RelayCommand(_ => PlayPrevious());
            SeekCommand = new RelayCommand(p => Seek(p));
        }

        private void AddFiles()
        {
            var dlg = new OpenFileDialog()
            {
                Multiselect = true,
                Filter = "Media Files|*.mp3;*.wav;*.mp4;*.wma;*.aac|All Files|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                MediaItem? fallbackSelectedItem = null;
                MediaItem? preferredSelectedItem = null;

                foreach (var file in dlg.FileNames)
                {
                    var existingItem = Playlist.FirstOrDefault(item =>
                        string.Equals(item.FilePath, file, StringComparison.OrdinalIgnoreCase));

                    if (existingItem != null)
                    {
                        fallbackSelectedItem ??= existingItem;
                        if (!IsAudioFile(existingItem.FilePath))
                        {
                            preferredSelectedItem ??= existingItem;
                        }
                        continue;
                    }

                    var item = new MediaItem
                    {
                        FilePath = file,
                        Title = System.IO.Path.GetFileName(file)
                    };

                    Playlist.Add(item);
                    fallbackSelectedItem ??= item;
                    if (!IsAudioFile(item.FilePath))
                    {
                        preferredSelectedItem ??= item;
                    }
                }

                IsNewVideoAvailable = true;

                var itemToPlay = preferredSelectedItem ?? fallbackSelectedItem;

                if (itemToPlay != null)
                {
                    Position = 0;
                    IsPlaying = true;
                    Selected = itemToPlay;
                }
            }
        }

        private void TogglePlayPause()
        {
            IsPlaying = !IsPlaying;
        }

        private void Stop()
        {
            IsPlaying = false;
            Position = 0;
        }

        public void PlayNext()
        {
            if (!Playlist.Any()) return;
            var idx = Selected == null ? -1 : Playlist.IndexOf(Selected);
            var next = (idx + 1) < Playlist.Count ? Playlist[idx + 1] : null;
            if (next != null) Selected = next; else { Selected = Playlist[0]; }
            IsPlaying = true;
            IsNewVideoAvailable = false;
        }

        public bool TryPlayNextValid(bool wrap)
        {
            if (!Playlist.Any()) return false;
            var startIndex = Selected == null ? -1 : Playlist.IndexOf(Selected);
            var index = startIndex;

            for (var attempts = 0; attempts < Playlist.Count; attempts++)
            {
                index++;
                if (index >= Playlist.Count)
                {
                    if (!wrap)
                    {
                        return false;
                    }
                    index = 0;
                }

                if (index == startIndex && startIndex >= 0)
                {
                    return false;
                }

                var item = Playlist[index];
                if (item == null || string.IsNullOrWhiteSpace(item.FilePath))
                {
                    continue;
                }

                if (!System.IO.File.Exists(item.FilePath))
                {
                    continue;
                }

                Selected = item;
                IsPlaying = true;
                IsNewVideoAvailable = false;
                return true;
            }

            return false;
        }

        public void PlayPrevious()
        {
            if (!Playlist.Any()) return;
            var idx = Selected == null ? 0 : Playlist.IndexOf(Selected);
            var prev = (idx - 1) >= 0 ? Playlist[idx - 1] : null;
            if (prev != null) Selected = prev; else { Selected = Playlist[^1]; }
            IsPlaying = true;
        }

        private void Seek(object? parameter)
        {
            if (parameter == null) return;
            if (double.TryParse(parameter.ToString(), out var seconds))
            {
                Position = seconds;
            }
        }

        private static bool IsAudioFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            return AudioExtensions.Contains(System.IO.Path.GetExtension(filePath));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
