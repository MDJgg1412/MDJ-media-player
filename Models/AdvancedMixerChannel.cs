using System;
using System.ComponentModel;
using System.Windows.Media;

namespace MDJMediaPlayer.Models
{
    public sealed class AdvancedMixerChannel : INotifyPropertyChanged
    {
        private readonly Func<double> _getHigh;
        private readonly Action<double> _setHigh;
        private readonly Func<double> _getMid;
        private readonly Action<double> _setMid;
        private readonly Func<double> _getLow;
        private readonly Action<double> _setLow;
        private readonly Func<double> _getFader;
        private readonly Action<double> _setFader;
        private readonly Func<double> _getGain;
        private readonly Action<double> _setGain;

        public string DeckLabel { get; }
        public Brush AccentBrush { get; }

        public AdvancedMixerChannel(
            string deckLabel,
            Brush accentBrush,
            Func<double> getHigh,
            Action<double> setHigh,
            Func<double> getMid,
            Action<double> setMid,
            Func<double> getLow,
            Action<double> setLow,
            Func<double> getFader,
            Action<double> setFader,
            Func<double> getGain,
            Action<double> setGain)
        {
            DeckLabel = deckLabel;
            AccentBrush = accentBrush;
            _getHigh = getHigh;
            _setHigh = setHigh;
            _getMid = getMid;
            _setMid = setMid;
            _getLow = getLow;
            _setLow = setLow;
            _getFader = getFader;
            _setFader = setFader;
            _getGain = getGain;
            _setGain = setGain;
        }

        public double High
        {
            get => _getHigh();
            set
            {
                _setHigh(value);
                OnPropertyChanged(nameof(High));
            }
        }

        public double Mid
        {
            get => _getMid();
            set
            {
                _setMid(value);
                OnPropertyChanged(nameof(Mid));
            }
        }

        public double Low
        {
            get => _getLow();
            set
            {
                _setLow(value);
                OnPropertyChanged(nameof(Low));
            }
        }

        public double Fader
        {
            get => _getFader();
            set
            {
                _setFader(value);
                OnPropertyChanged(nameof(Fader));
            }
        }

        public double Gain
        {
            get => _getGain();
            set
            {
                _setGain(value);
                OnPropertyChanged(nameof(Gain));
            }
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(High));
            OnPropertyChanged(nameof(Mid));
            OnPropertyChanged(nameof(Low));
            OnPropertyChanged(nameof(Fader));
            OnPropertyChanged(nameof(Gain));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
