using MDJMediaPlayer.Models;
using MDJMediaPlayer.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MDJMediaPlayer
{
    public partial class AdvancedMixerWindow : Window, INotifyPropertyChanged
    {
        private const double KnobStep = 2d;
        private const double KnobDefault = 50d;
        private const double GainKnobDefault = 100d;
        private readonly MainViewModel _mixerViewModel;

        public ObservableCollection<AdvancedMixerChannel> Channels { get; } = new();

        public MainViewModel Mixer => _mixerViewModel;

        public double Crossfader
        {
            get => _mixerViewModel.Crossfader;
            set
            {
                _mixerViewModel.Crossfader = value;
                OnPropertyChanged(nameof(Crossfader));
            }
        }

        public AdvancedMixerWindow(MainViewModel mixerViewModel)
        {
            _mixerViewModel = mixerViewModel ?? throw new ArgumentNullException(nameof(mixerViewModel));
            InitializeComponent();
            BuildChannels();
            DataContext = this;

            _mixerViewModel.PropertyChanged += MixerViewModel_PropertyChanged;
            Closed += AdvancedMixerWindow_Closed;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void BuildChannels()
        {
            Channels.Clear();
            Channels.Add(new AdvancedMixerChannel(
                deckLabel: "Deck A",
                accentBrush: CreateBrush("#55C9FF"),
                getHigh: () => _mixerViewModel.DeckAHigh,
                setHigh: value => _mixerViewModel.DeckAHigh = value,
                getMid: () => _mixerViewModel.DeckAMid,
                setMid: value => _mixerViewModel.DeckAMid = value,
                getLow: () => _mixerViewModel.DeckALow,
                setLow: value => _mixerViewModel.DeckALow = value,
                getFader: () => _mixerViewModel.DeckAFader,
                setFader: value => _mixerViewModel.DeckAFader = value,
                getGain: () => _mixerViewModel.DeckAGain,
                setGain: value => _mixerViewModel.DeckAGain = value));

            Channels.Add(new AdvancedMixerChannel(
                deckLabel: "Deck B",
                accentBrush: CreateBrush("#FF6B6B"),
                getHigh: () => _mixerViewModel.DeckBHigh,
                setHigh: value => _mixerViewModel.DeckBHigh = value,
                getMid: () => _mixerViewModel.DeckBMid,
                setMid: value => _mixerViewModel.DeckBMid = value,
                getLow: () => _mixerViewModel.DeckBLow,
                setLow: value => _mixerViewModel.DeckBLow = value,
                getFader: () => _mixerViewModel.DeckBFader,
                setFader: value => _mixerViewModel.DeckBFader = value,
                getGain: () => _mixerViewModel.DeckBGain,
                setGain: value => _mixerViewModel.DeckBGain = value));

            Channels.Add(new AdvancedMixerChannel(
                deckLabel: "Deck C",
                accentBrush: CreateBrush("#62E0A1"),
                getHigh: () => _mixerViewModel.DeckCHigh,
                setHigh: value => _mixerViewModel.DeckCHigh = value,
                getMid: () => _mixerViewModel.DeckCMid,
                setMid: value => _mixerViewModel.DeckCMid = value,
                getLow: () => _mixerViewModel.DeckCLow,
                setLow: value => _mixerViewModel.DeckCLow = value,
                getFader: () => _mixerViewModel.DeckCFader,
                setFader: value => _mixerViewModel.DeckCFader = value,
                getGain: () => _mixerViewModel.DeckCGain,
                setGain: value => _mixerViewModel.DeckCGain = value));

            Channels.Add(new AdvancedMixerChannel(
                deckLabel: "Deck D",
                accentBrush: CreateBrush("#F6C15A"),
                getHigh: () => _mixerViewModel.DeckDHigh,
                setHigh: value => _mixerViewModel.DeckDHigh = value,
                getMid: () => _mixerViewModel.DeckDMid,
                setMid: value => _mixerViewModel.DeckDMid = value,
                getLow: () => _mixerViewModel.DeckDLow,
                setLow: value => _mixerViewModel.DeckDLow = value,
                getFader: () => _mixerViewModel.DeckDFader,
                setFader: value => _mixerViewModel.DeckDFader = value,
                getGain: () => _mixerViewModel.DeckDGain,
                setGain: value => _mixerViewModel.DeckDGain = value));
        }

        private static SolidColorBrush CreateBrush(string hex)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private void AdvancedMixerWindow_Closed(object? sender, EventArgs e)
        {
            _mixerViewModel.PropertyChanged -= MixerViewModel_PropertyChanged;
            Closed -= AdvancedMixerWindow_Closed;
        }

        private void MixerViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.Crossfader))
            {
                OnPropertyChanged(nameof(Crossfader));
                return;
            }

            if (!IsChannelMixerProperty(e.PropertyName))
            {
                return;
            }

            foreach (var channel in Channels)
            {
                channel.Refresh();
            }
        }

        private static bool IsChannelMixerProperty(string? propertyName)
        {
            return propertyName == nameof(MainViewModel.DeckAFader) ||
                   propertyName == nameof(MainViewModel.DeckBFader) ||
                   propertyName == nameof(MainViewModel.DeckCFader) ||
                   propertyName == nameof(MainViewModel.DeckDFader) ||
                   propertyName == nameof(MainViewModel.DeckAGain) ||
                   propertyName == nameof(MainViewModel.DeckBGain) ||
                   propertyName == nameof(MainViewModel.DeckCGain) ||
                   propertyName == nameof(MainViewModel.DeckDGain) ||
                   propertyName == nameof(MainViewModel.DeckAHigh) ||
                   propertyName == nameof(MainViewModel.DeckAMid) ||
                   propertyName == nameof(MainViewModel.DeckALow) ||
                   propertyName == nameof(MainViewModel.DeckBHigh) ||
                   propertyName == nameof(MainViewModel.DeckBMid) ||
                   propertyName == nameof(MainViewModel.DeckBLow) ||
                   propertyName == nameof(MainViewModel.DeckCHigh) ||
                   propertyName == nameof(MainViewModel.DeckCMid) ||
                   propertyName == nameof(MainViewModel.DeckCLow) ||
                   propertyName == nameof(MainViewModel.DeckDHigh) ||
                   propertyName == nameof(MainViewModel.DeckDMid) ||
                   propertyName == nameof(MainViewModel.DeckDLow);
        }

        private static bool TryAdjustKnob(FrameworkElement? source, double delta)
        {
            if (source?.DataContext is not AdvancedMixerChannel channel)
            {
                return false;
            }

            if (source.Tag is not string band)
            {
                return false;
            }

            switch (band)
            {
                case "High":
                    channel.High = Math.Clamp(channel.High + delta, 0d, 100d);
                    return true;
                case "Mid":
                    channel.Mid = Math.Clamp(channel.Mid + delta, 0d, 100d);
                    return true;
                case "Low":
                    channel.Low = Math.Clamp(channel.Low + delta, 0d, 100d);
                    return true;
                case "Gain":
                    channel.Gain = Math.Clamp(channel.Gain + delta, 0d, 200d);
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryResetKnob(FrameworkElement? source)
        {
            if (source?.DataContext is not AdvancedMixerChannel channel)
            {
                return false;
            }

            if (source.Tag is not string band)
            {
                return false;
            }

            switch (band)
            {
                case "High":
                    channel.High = KnobDefault;
                    return true;
                case "Mid":
                    channel.Mid = KnobDefault;
                    return true;
                case "Low":
                    channel.Low = KnobDefault;
                    return true;
                case "Gain":
                    channel.Gain = GainKnobDefault;
                    return true;
                default:
                    return false;
            }
        }

        private void KnobDownButton_Click(object sender, RoutedEventArgs e)
        {
            _ = TryAdjustKnob(sender as FrameworkElement, -KnobStep);
        }

        private void KnobUpButton_Click(object sender, RoutedEventArgs e)
        {
            _ = TryAdjustKnob(sender as FrameworkElement, KnobStep);
        }

        private void Knob_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var delta = e.Delta > 0 ? KnobStep : -KnobStep;
            if (TryAdjustKnob(sender as FrameworkElement, delta))
            {
                e.Handled = true;
            }
        }

        private void Knob_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2)
            {
                return;
            }

            if (TryResetKnob(sender as FrameworkElement))
            {
                e.Handled = true;
            }
        }

        private void MixerSlider_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Slider slider)
            {
                return;
            }

            if (slider.Tag is null)
            {
                return;
            }

            if (!double.TryParse(Convert.ToString(slider.Tag, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var defaultValue))
            {
                return;
            }

            slider.Value = Math.Clamp(defaultValue, slider.Minimum, slider.Maximum);
            e.Handled = true;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Titlebar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            var source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is Button ||
                    source is System.Windows.Controls.Primitives.ToggleButton ||
                    source is ComboBox)
                {
                    return;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            try { DragMove(); } catch { }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
