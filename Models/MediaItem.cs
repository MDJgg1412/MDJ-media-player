using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MDJMediaPlayer.Models
{
    // Represents a media file in the playlist
    public class MediaItem : INotifyPropertyChanged
    {
        private string _filePath = string.Empty;
        private string _title = string.Empty;
        private TimeSpan _duration;
        private string? _albumArtPath;

        public string FilePath
        {
            get => _filePath;
            set => SetField(ref _filePath, value);
        }

        public string Title
        {
            get => _title;
            set => SetField(ref _title, value);
        }

        public TimeSpan Duration
        {
            get => _duration;
            set => SetField(ref _duration, value);
        }

        public string? AlbumArtPath
        {
            get => _albumArtPath;
            set => SetField(ref _albumArtPath, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
