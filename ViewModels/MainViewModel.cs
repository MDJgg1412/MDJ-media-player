using MDJMediaPlayer.Helpers;
using MDJMediaPlayer.Models;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;

namespace MDJMediaPlayer.ViewModels
{
    // Main ViewModel for the player
    public class MainViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<MediaItem> Playlist { get; } = new();

        private MediaItem? _selected;
        public MediaItem? Selected
        {
            get => _selected;
            set 
            { 
                _selected = value; 
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); 
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoVideoFound)));
            }
        }

        public bool NoVideoFound => Selected == null;

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
                foreach (var file in dlg.FileNames)
                {
                    Playlist.Add(new MediaItem { FilePath = file, Title = System.IO.Path.GetFileName(file) });
                }

                IsNewVideoAvailable = true;

                if (Selected == null && Playlist.Any())
                {
                    Selected = Playlist[0];
                    IsPlaying = true;
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

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
