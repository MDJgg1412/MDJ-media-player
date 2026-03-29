using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MDJMediaPlayer.Models
{
    public class DjDeckSlot : INotifyPropertyChanged
    {
        private string _label = string.Empty;
        private string _accent = "#4FC3FF";
        private string _trackTitle = string.Empty;
        private string _trackSubtitle = string.Empty;
        private string _status = string.Empty;
        private string _meta = string.Empty;
        private double _progressPercent;
        private bool _isActive;
        private string _transportText = string.Empty;
        private double _visualOpacity = 1d;
        private bool _isSeekDragging;
        private bool _isPlaying;
        private bool _canInteract;
        private bool _isSelected;

        public string Label
        {
            get => _label;
            set => SetField(ref _label, value);
        }

        public string Accent
        {
            get => _accent;
            set => SetField(ref _accent, value);
        }

        public string TrackTitle
        {
            get => _trackTitle;
            set => SetField(ref _trackTitle, value);
        }

        public string TrackSubtitle
        {
            get => _trackSubtitle;
            set => SetField(ref _trackSubtitle, value);
        }

        public string Status
        {
            get => _status;
            set => SetField(ref _status, value);
        }

        public string Meta
        {
            get => _meta;
            set => SetField(ref _meta, value);
        }

        public double ProgressPercent
        {
            get => _progressPercent;
            set => SetField(ref _progressPercent, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => SetField(ref _isActive, value);
        }

        public string TransportText
        {
            get => _transportText;
            set => SetField(ref _transportText, value);
        }

        public double VisualOpacity
        {
            get => _visualOpacity;
            set => SetField(ref _visualOpacity, value);
        }

        public bool IsSeekDragging
        {
            get => _isSeekDragging;
            set => SetField(ref _isSeekDragging, value);
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set => SetField(ref _isPlaying, value);
        }

        public bool CanInteract
        {
            get => _canInteract;
            set => SetField(ref _canInteract, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
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
