using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MDJMediaPlayer
{
    public sealed class SeparatorsLevelsChangedEventArgs : EventArgs
    {
        public SeparatorsLevelsChangedEventArgs(int deckIndex, double vocal, double intrumental)
        {
            DeckIndex = deckIndex;
            Vocal = vocal;
            Intrumental = intrumental;
        }

        public int DeckIndex { get; }
        public double Vocal { get; }
        public double Intrumental { get; }
    }

    public partial class SeparatorsWindow : Window
    {
        private bool _allowClose;
        private bool _suppressLevelEvents;
        private bool _pendingStemLevelCommit;
        private readonly System.Windows.Threading.DispatcherTimer _dragCommitTimer;
        private int _deckIndex = -1;
        public event EventHandler<SeparatorsLevelsChangedEventArgs>? LevelsChanged;

        public SeparatorsWindow()
        {
            InitializeComponent();
            _dragCommitTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _dragCommitTimer.Tick += DragCommitTimer_Tick;
        }

        public void ConfigureDeck(int deckIndex, string deckLabel, double vocal, double intrumental)
        {
            _deckIndex = deckIndex;

            var safeDeckLabel = string.IsNullOrWhiteSpace(deckLabel)
                ? "Deck"
                : deckLabel;
            DeckLabelText.Text = safeDeckLabel;
            WindowTitleText.Text = "separators - " + safeDeckLabel;
            Title = WindowTitleText.Text;

            _suppressLevelEvents = true;
            try
            {
                VocalSlider.Value = Math.Clamp(vocal, 0d, 100d);
                IntrumentalSlider.Value = Math.Clamp(intrumental, 0d, 100d);
            }
            finally
            {
                _suppressLevelEvents = false;
            }
        }

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
                return;
            }

            base.OnClosing(e);
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void StemSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressLevelEvents || _deckIndex < 0)
            {
                return;
            }

            _pendingStemLevelCommit = true;
            if (!_dragCommitTimer.IsEnabled)
            {
                _dragCommitTimer.Start();
            }
        }

        private void DragCommitTimer_Tick(object? sender, EventArgs e)
        {
            _dragCommitTimer.Stop();

            if (!_pendingStemLevelCommit || _suppressLevelEvents || _deckIndex < 0)
            {
                return;
            }

            _pendingStemLevelCommit = false;
            RaiseLevelsChanged();
        }

        private void RaiseLevelsChanged()
        {
            LevelsChanged?.Invoke(
                this,
                new SeparatorsLevelsChangedEventArgs(
                    _deckIndex,
                    VocalSlider.Value,
                    IntrumentalSlider.Value));
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
                    source is ToggleButton ||
                    source is Slider)
                {
                    return;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            try { DragMove(); } catch { }
        }
    }
}
