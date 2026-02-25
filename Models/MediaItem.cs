using System;
using System.ComponentModel;

namespace MDJMediaPlayer.Models
{
    // Represents a media file in the playlist
    public class MediaItem : INotifyPropertyChanged
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public string? AlbumArtPath { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
