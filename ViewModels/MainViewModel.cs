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
        private static readonly (string Label, string Accent)[] DeckDefinitions =
        {
            ("Deck A", "#55C9FF"),
            ("Deck B", "#FF6B6B"),
            ("Deck C", "#62E0A1"),
            ("Deck D", "#F6C15A")
        };
        private readonly MediaItem?[] _djDeckAssignments = new MediaItem?[DeckDefinitions.Length];
        private readonly bool[] _djDeckPlaybackStates = new bool[DeckDefinitions.Length];
        private readonly double[] _djDeckPositions = new double[DeckDefinitions.Length];
        private readonly double[] _djDeckDurations = new double[DeckDefinitions.Length];
        private readonly HashSet<int> _selectedDjDeckIndexes = new();
        private int _focusedDjDeckIndex = -1;

        public ObservableCollection<MediaItem> Playlist { get; } = new();
        public ObservableCollection<DjDeckSlot> DjDeckSlots { get; } = new();
        public int FocusedDjDeckIndex => _focusedDjDeckIndex;

        private MediaItem? _selected;
        public MediaItem? Selected
        {
            get => _selected;
            set 
            { 
                _selected = value; 
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); 
                RaiseMixerDerivedProperties();
                RefreshDjDeckSlots();
            }
        }

        public bool NoVideoFound => Playlist.Count == 0;

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                _isPlaying = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
                RefreshDjDeckSlots();
            }
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
            set
            {
                _volume = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Volume)));
                RaiseMixerDerivedProperties();
            }
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

        private bool _checkForUpdateAtStartup = true;
        public bool CheckForUpdateAtStartup
        {
            get => _checkForUpdateAtStartup;
            set
            {
                if (_checkForUpdateAtStartup == value) return;
                _checkForUpdateAtStartup = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CheckForUpdateAtStartup)));
            }
        }

        private bool _allowPreReleaseUpdate;
        public bool AllowPreReleaseUpdate
        {
            get => _allowPreReleaseUpdate;
            set
            {
                if (_allowPreReleaseUpdate == value) return;
                _allowPreReleaseUpdate = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllowPreReleaseUpdate)));
            }
        }

        private string _deckLayout = "Default";
        public string DeckLayout
        {
            get => _deckLayout;
            set
            {
                var normalized = NormalizeDeckLayout(value);
                if (string.Equals(_deckLayout, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _deckLayout = normalized;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeckLayout)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDefaultDeckLayout)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTwoDeckLayout)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFourDeckLayout)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDjDeckLayout)));
                CommandManager.InvalidateRequerySuggested();
                RaiseMixerDerivedProperties();
                if (NormalizeDjDeckSelectionToCurrentLayout() && IsDjDeckLayout)
                {
                    RaiseDjSelectionProperties();
                }
                RefreshDjDeckSlots();
            }
        }

        public bool IsDefaultDeckLayout => string.Equals(DeckLayout, "Default", StringComparison.OrdinalIgnoreCase);
        public bool IsTwoDeckLayout => string.Equals(DeckLayout, "2 Decks", StringComparison.OrdinalIgnoreCase);
        public bool IsFourDeckLayout => string.Equals(DeckLayout, "4 Decks", StringComparison.OrdinalIgnoreCase);
        public bool IsDjDeckLayout => !IsDefaultDeckLayout;

        private double _deckAFader = 80d;
        public double DeckAFader
        {
            get => _deckAFader;
            set => SetMixerLevel(ref _deckAFader, value, nameof(DeckAFader));
        }

        private double _deckBFader = 80d;
        public double DeckBFader
        {
            get => _deckBFader;
            set => SetMixerLevel(ref _deckBFader, value, nameof(DeckBFader));
        }

        private double _deckCFader = 80d;
        public double DeckCFader
        {
            get => _deckCFader;
            set => SetMixerLevel(ref _deckCFader, value, nameof(DeckCFader));
        }

        private double _deckDFader = 80d;
        public double DeckDFader
        {
            get => _deckDFader;
            set => SetMixerLevel(ref _deckDFader, value, nameof(DeckDFader));
        }

        private double _crossfader = 50d;
        public double Crossfader
        {
            get => _crossfader;
            set => SetMixerLevel(ref _crossfader, value, nameof(Crossfader));
        }

        public double EffectivePlaybackVolume =>
            Math.Clamp(IsDjDeckLayout ? Volume * GetSelectedDeckEffectiveLevelNormalized() : Volume, 0d, 1d);

        public string MasterOutputText => $"{Math.Round(Volume * 100d):0}%";
        public string MixerMonitorText => BuildMixerMonitorText();


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
            set
            {
                _position = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Position)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTimeText)));
                RefreshDjDeckSlots();
            }
        }

        private double _duration;
        public double Duration
        {
            get => _duration;
            set
            {
                _duration = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Duration)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalTimeText)));
                RefreshDjDeckSlots();
            }
        }

        public string CurrentTimeText => TimeSpan.FromSeconds(Position).ToString(@"m\:ss");
        public string TotalTimeText => TimeSpan.FromSeconds(Duration).ToString(@"m\:ss");
        public string CurrentDjSelectionText => BuildCurrentDjSelectionText();

        public ICommand AddFilesCommand { get; }
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PrevCommand { get; }
        public ICommand SeekCommand { get; }

        public MainViewModel()
        {
            InitializeDjDeckSlots();

            Playlist.CollectionChanged += (_, _) =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoVideoFound)));
                RefreshDjDeckSlots();
            };

            AddFilesCommand = new RelayCommand(_ => AddFiles(), _ => !IsDjDeckLayout);
            PlayPauseCommand = new RelayCommand(_ => TogglePlayPause());
            StopCommand = new RelayCommand(_ => Stop());
            NextCommand = new RelayCommand(_ => PlayNext());
            PrevCommand = new RelayCommand(_ => PlayPrevious());
            SeekCommand = new RelayCommand(p => Seek(p));
        }

        private static string NormalizeDeckLayout(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Default";
            }

            if (string.Equals(value, "2 Decks", StringComparison.OrdinalIgnoreCase))
            {
                return "2 Decks";
            }

            if (string.Equals(value, "4 Decks", StringComparison.OrdinalIgnoreCase))
            {
                return "4 Decks";
            }

            if (string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase))
            {
                return "Default";
            }

            // Backward-compatible typo handling from older settings values.
            if (string.Equals(value, "Dafault", StringComparison.OrdinalIgnoreCase))
            {
                return "Default";
            }

            return "Default";
        }

        private void RaiseMixerDerivedProperties()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectivePlaybackVolume)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MasterOutputText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MixerMonitorText)));
        }

        private void SetMixerLevel(ref double field, double value, string propertyName)
        {
            var clamped = Math.Clamp(value, 0d, 100d);
            if (Math.Abs(field - clamped) < 0.001d)
            {
                return;
            }

            field = clamped;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            RaiseMixerDerivedProperties();
            RefreshDjDeckSlots();
        }

        private double GetDeckFaderPercent(int deckIndex) => deckIndex switch
        {
            0 => DeckAFader,
            1 => DeckBFader,
            2 => DeckCFader,
            3 => DeckDFader,
            _ => 0d
        };

        private double GetDeckSideWeight(int deckIndex)
        {
            var cross = Math.Clamp(Crossfader, 0d, 100d);
            var isLeftDeck = deckIndex == 0 || deckIndex == 2;

            if (isLeftDeck)
            {
                return cross <= 50d ? 1d : 1d - ((cross - 50d) / 50d);
            }

            return cross >= 50d ? 1d : cross / 50d;
        }

        private double GetDeckEffectivePercent(int deckIndex)
        {
            return Math.Clamp(GetDeckFaderPercent(deckIndex) * GetDeckSideWeight(deckIndex), 0d, 100d);
        }

        private double GetDeckEffectiveLevelNormalized(int deckIndex)
        {
            return GetDeckEffectivePercent(deckIndex) / 100d;
        }

        public double GetDjDeckPlaybackVolume(int deckIndex)
        {
            if (deckIndex < 0 || deckIndex >= DeckDefinitions.Length)
            {
                return 0d;
            }

            return Math.Clamp(Volume * GetDeckEffectiveLevelNormalized(deckIndex), 0d, 1d);
        }

        private int GetSelectedDjDeckIndex()
        {
            if (Selected == null)
            {
                return -1;
            }

            for (var i = 0; i < _djDeckAssignments.Length; i++)
            {
                if (ReferenceEquals(_djDeckAssignments[i], Selected))
                {
                    return i;
                }
            }

            var previewItems = GetDeckPreviewItems();
            for (var i = 0; i < previewItems.Count; i++)
            {
                if (ReferenceEquals(previewItems[i], Selected))
                {
                    return i;
                }
            }

            return -1;
        }

        private double GetSelectedDeckEffectiveLevelNormalized()
        {
            var selectedDeckIndex = GetSelectedDjDeckIndex();
            if (selectedDeckIndex < 0)
            {
                return 0d;
            }

            return GetDeckEffectiveLevelNormalized(selectedDeckIndex);
        }

        private string BuildMixerMonitorText()
        {
            if (!IsDjDeckLayout)
            {
                return "Deck mixer offline";
            }

            var output = Math.Round(EffectivePlaybackVolume * 100d);

            if (IsFourDeckLayout)
            {
                var left = Math.Round(GetDeckSideWeight(0) * 100d);
                var right = Math.Round(GetDeckSideWeight(1) * 100d);
                return $"L {left:0}%  R {right:0}%  OUT {output:0}%";
            }

            return $"A {GetDeckEffectivePercent(0):0}%  B {GetDeckEffectivePercent(1):0}%  OUT {output:0}%";
        }

        private int GetVisibleDjDeckCount()
        {
            if (!IsDjDeckLayout)
            {
                return 0;
            }

            var targetCount = IsFourDeckLayout ? 4 : 2;
            return Math.Min(targetCount, DjDeckSlots.Count);
        }

        private bool NormalizeDjDeckSelectionToCurrentLayout()
        {
            var visibleCount = GetVisibleDjDeckCount();
            var hasChanged = false;

            var outOfRangeIndexes = _selectedDjDeckIndexes
                .Where(index => index < 0 || index >= visibleCount)
                .ToList();
            foreach (var index in outOfRangeIndexes)
            {
                hasChanged |= _selectedDjDeckIndexes.Remove(index);
            }

            if (visibleCount > 0 && _selectedDjDeckIndexes.Count == 0)
            {
                var defaultIndex = _focusedDjDeckIndex >= 0 && _focusedDjDeckIndex < visibleCount
                    ? _focusedDjDeckIndex
                    : 0;
                _selectedDjDeckIndexes.Add(defaultIndex);
                hasChanged = true;
            }

            return hasChanged;
        }

        private string BuildCurrentDjSelectionText()
        {
            var visibleCount = GetVisibleDjDeckCount();
            if (visibleCount <= 0)
            {
                return "No decks selected";
            }

            var selectedLabels = _selectedDjDeckIndexes
                .Where(index => index >= 0 && index < visibleCount && index < DjDeckSlots.Count)
                .OrderBy(index => index)
                .Select(index => DjDeckSlots[index].Label)
                .ToList();

            return selectedLabels.Count > 0
                ? string.Join(", ", selectedLabels)
                : "No decks selected";
        }

        private void RaiseDjSelectionProperties()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentDjSelectionText)));
        }

        private void InitializeDjDeckSlots()
        {
            if (DjDeckSlots.Count > 0)
            {
                return;
            }

            foreach (var (label, accent) in DeckDefinitions)
            {
                DjDeckSlots.Add(new DjDeckSlot
                {
                    Label = label,
                    Accent = accent
                });
            }

            NormalizeDjDeckSelectionToCurrentLayout();
            RaiseDjSelectionProperties();
            RefreshDjDeckSlots();
        }

        private void RefreshDjDeckSlots()
        {
            if (DjDeckSlots.Count == 0)
            {
                return;
            }

            var selectionChanged = NormalizeDjDeckSelectionToCurrentLayout();
            if (selectionChanged)
            {
                RaiseDjSelectionProperties();
            }

            var previewItems = GetDeckPreviewItems();

            for (var i = 0; i < DjDeckSlots.Count; i++)
            {
                var slot = DjDeckSlots[i];
                var item = i < previewItems.Count ? previewItems[i] : null;
                var isPrimaryDeck = item != null && i == _focusedDjDeckIndex;
                var isSelected = _selectedDjDeckIndexes.Contains(i);
                var isPlaying = item != null && _djDeckPlaybackStates[i];
                var faderPercent = GetDeckFaderPercent(i);
                var outputPercent = GetDeckEffectivePercent(i);
                var durationSeconds = Math.Max(_djDeckDurations[i], item?.Duration.TotalSeconds ?? 0d);
                var positionSeconds = Math.Clamp(_djDeckPositions[i], 0d, durationSeconds > 0d ? durationSeconds : double.MaxValue);

                slot.IsActive = isPrimaryDeck;
                slot.IsSelected = isSelected;
                slot.IsPlaying = isPlaying;
                slot.CanInteract = item != null;
                slot.TrackTitle = item?.Title ?? $"Load {slot.Label}";
                slot.TrackSubtitle = item?.FilePath ?? "Use Add Files to build your DJ set.";
                slot.TransportText = $"Fader {faderPercent:0}%  Out {outputPercent:0}%";
                slot.VisualOpacity = Math.Clamp(
                    0.46d +
                    (outputPercent / 100d * 0.48d) +
                    (isPrimaryDeck ? 0.12d : 0d) +
                    (isSelected ? 0.1d : 0d) +
                    (isPlaying ? 0.08d : 0d),
                    0.46d,
                    1d);

                if (item == null)
                {
                    slot.Status = "EMPTY";
                    slot.Meta = "No track loaded";
                    slot.ProgressPercent = 0d;
                    continue;
                }

                var queueNumber = Playlist.IndexOf(item) + 1;
                slot.Status = isPlaying ? "LIVE" : isPrimaryDeck ? "READY" : "CUE";
                slot.Meta = durationSeconds > 0d
                    ? $"{FormatDeckTime(positionSeconds)} / {FormatDeckTime(durationSeconds)}"
                    : $"Queue {queueNumber:00}";
                if (!slot.IsSeekDragging)
                {
                    slot.ProgressPercent = durationSeconds > 0d
                        ? Math.Clamp((positionSeconds / durationSeconds) * 100d, 0d, 100d)
                        : 0d;
                }
            }
        }

        private static string FormatDeckTime(double seconds)
        {
            var safeSeconds = Math.Max(0d, seconds);
            return TimeSpan.FromSeconds(safeSeconds).ToString(@"m\:ss");
        }

        private List<MediaItem?> GetDeckPreviewItems()
        {
            var items = Enumerable.Repeat<MediaItem?>(null, DjDeckSlots.Count).ToList();
            var usedItems = new HashSet<MediaItem>();

            for (var i = 0; i < items.Count && i < _djDeckAssignments.Length; i++)
            {
                var assignedItem = _djDeckAssignments[i];
                if (assignedItem == null)
                {
                    continue;
                }

                items[i] = assignedItem;
                usedItems.Add(assignedItem);
            }

            // In DJ mode, decks should only show files explicitly assigned to each deck.
            // Do not auto-fill empty slots from the shared playlist.
            if (IsDjDeckLayout)
            {
                return items;
            }

            if (Selected != null)
            {
                var selectedIndex = Playlist.IndexOf(Selected);
                if (selectedIndex >= 0)
                {
                    for (var offset = 0; offset < Playlist.Count; offset++)
                    {
                        var item = Playlist[(selectedIndex + offset) % Playlist.Count];
                        if (item == null || usedItems.Contains(item))
                        {
                            continue;
                        }

                        var openIndex = items.FindIndex(entry => entry == null);
                        if (openIndex < 0)
                        {
                            break;
                        }

                        items[openIndex] = item;
                        usedItems.Add(item);
                    }
                }
                else
                {
                    var openIndex = items.FindIndex(entry => entry == null);
                    if (openIndex >= 0)
                    {
                        items[openIndex] = Selected;
                        usedItems.Add(Selected);
                    }
                }
            }

            foreach (var item in Playlist)
            {
                if (items.All(entry => entry != null))
                {
                    break;
                }

                if (item == null || usedItems.Contains(item))
                {
                    continue;
                }

                var openIndex = items.FindIndex(entry => entry == null);
                if (openIndex < 0)
                {
                    break;
                }

                items[openIndex] = item;
                usedItems.Add(item);
            }

            return items;
        }

        public int GetDjDeckSlotIndex(DjDeckSlot? slot)
        {
            if (slot == null)
            {
                return -1;
            }

            return DjDeckSlots.IndexOf(slot);
        }

        public bool SelectDjDeckSlot(int slotIndex, bool toggleSelection)
        {
            var visibleDeckCount = GetVisibleDjDeckCount();
            if (slotIndex < 0 || slotIndex >= visibleDeckCount || slotIndex >= DjDeckSlots.Count)
            {
                return false;
            }

            var hasChanged = false;
            if (toggleSelection)
            {
                if (_selectedDjDeckIndexes.Contains(slotIndex))
                {
                    hasChanged = _selectedDjDeckIndexes.Remove(slotIndex);
                }
                else
                {
                    hasChanged = _selectedDjDeckIndexes.Add(slotIndex);
                }
            }
            else if (_selectedDjDeckIndexes.Count != 1 || !_selectedDjDeckIndexes.Contains(slotIndex))
            {
                _selectedDjDeckIndexes.Clear();
                _selectedDjDeckIndexes.Add(slotIndex);
                hasChanged = true;
            }

            if (hasChanged)
            {
                RaiseDjSelectionProperties();
            }

            var shouldFocus = _selectedDjDeckIndexes.Contains(slotIndex);
            var hasMedia = GetMediaItemForDjDeckSlot(slotIndex) != null;
            if (shouldFocus && hasMedia)
            {
                FocusDjDeckSlot(slotIndex);
            }
            else
            {
                RefreshDjDeckSlots();
            }

            return true;
        }

        public MediaItem? GetMediaItemForDjDeckSlot(int slotIndex)
        {
            if (slotIndex < 0)
            {
                return null;
            }

            var previewItems = GetDeckPreviewItems();
            return slotIndex < previewItems.Count ? previewItems[slotIndex] : null;
        }

        public MediaItem? GetMediaItemForDjDeckSlot(DjDeckSlot? slot)
        {
            var slotIndex = GetDjDeckSlotIndex(slot);
            if (slotIndex < 0)
            {
                return null;
            }

            return GetMediaItemForDjDeckSlot(slotIndex);
        }

        public bool FocusDjDeckSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= DjDeckSlots.Count)
            {
                return false;
            }

            var item = GetMediaItemForDjDeckSlot(slotIndex);
            if (item == null)
            {
                return false;
            }

            _focusedDjDeckIndex = slotIndex;

            if (!ReferenceEquals(Selected, item))
            {
                Selected = item;
            }

            var durationSeconds = Math.Max(_djDeckDurations[slotIndex], item.Duration.TotalSeconds);
            if (Math.Abs(Duration - durationSeconds) > 0.001d)
            {
                Duration = durationSeconds;
            }

            var positionSeconds = Math.Clamp(_djDeckPositions[slotIndex], 0d, durationSeconds > 0d ? durationSeconds : double.MaxValue);
            if (Math.Abs(Position - positionSeconds) > 0.001d)
            {
                Position = positionSeconds;
            }

            var isPlaying = _djDeckPlaybackStates[slotIndex];
            if (IsPlaying != isPlaying)
            {
                IsPlaying = isPlaying;
            }

            RefreshDjDeckSlots();
            return true;
        }

        public void SetDjDeckPlaybackState(int slotIndex, bool isPlaying)
        {
            if (slotIndex < 0 || slotIndex >= _djDeckPlaybackStates.Length)
            {
                return;
            }

            _djDeckPlaybackStates[slotIndex] = isPlaying;
            if (_focusedDjDeckIndex == slotIndex && IsPlaying != isPlaying)
            {
                IsPlaying = isPlaying;
            }

            RefreshDjDeckSlots();
        }

        public bool IsDjDeckPlaying(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _djDeckPlaybackStates.Length)
            {
                return false;
            }

            return _djDeckPlaybackStates[slotIndex];
        }

        public void SetDjDeckTimeline(int slotIndex, double positionSeconds, double durationSeconds)
        {
            if (slotIndex < 0 || slotIndex >= _djDeckPositions.Length)
            {
                return;
            }

            var safeDuration = Math.Max(0d, durationSeconds);
            var safePosition = Math.Clamp(positionSeconds, 0d, safeDuration > 0d ? safeDuration : double.MaxValue);

            _djDeckDurations[slotIndex] = safeDuration;
            _djDeckPositions[slotIndex] = safePosition;

            var item = GetMediaItemForDjDeckSlot(slotIndex);
            if (item != null && safeDuration > 0d)
            {
                item.Duration = TimeSpan.FromSeconds(safeDuration);
            }

            if (_focusedDjDeckIndex == slotIndex)
            {
                if (Math.Abs(Duration - safeDuration) > 0.001d)
                {
                    Duration = safeDuration;
                }

                if (Math.Abs(Position - safePosition) > 0.001d)
                {
                    Position = safePosition;
                }
            }

            RefreshDjDeckSlots();
        }

        public void ResetDjDeckRuntimeState(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _djDeckAssignments.Length)
            {
                return;
            }

            _djDeckPlaybackStates[slotIndex] = false;
            _djDeckPositions[slotIndex] = 0d;
            _djDeckDurations[slotIndex] = 0d;

            if (_focusedDjDeckIndex == slotIndex)
            {
                Position = 0d;
                Duration = 0d;
                IsPlaying = false;
            }

            RefreshDjDeckSlots();
        }

        public void StopAllDjDeckPlayback()
        {
            for (var i = 0; i < _djDeckPlaybackStates.Length; i++)
            {
                _djDeckPlaybackStates[i] = false;
            }

            if (_focusedDjDeckIndex >= 0)
            {
                IsPlaying = false;
            }

            RefreshDjDeckSlots();
        }

        private void AssignMediaItemToDjDeckSlot(int slotIndex, MediaItem item)
        {
            if (slotIndex < 0 || slotIndex >= _djDeckAssignments.Length)
            {
                return;
            }

            for (var i = 0; i < _djDeckAssignments.Length; i++)
            {
                if (i == slotIndex)
                {
                    continue;
                }

                if (ReferenceEquals(_djDeckAssignments[i], item))
                {
                    _djDeckAssignments[i] = null;
                    ResetDjDeckRuntimeState(i);
                }
            }

            var hasChangedAssignment = !ReferenceEquals(_djDeckAssignments[slotIndex], item);
            _djDeckAssignments[slotIndex] = item;
            if (hasChangedAssignment)
            {
                ResetDjDeckRuntimeState(slotIndex);
                _djDeckDurations[slotIndex] = Math.Max(0d, item.Duration.TotalSeconds);
            }

            if (_focusedDjDeckIndex < 0)
            {
                _focusedDjDeckIndex = slotIndex;
            }

            RaiseMixerDerivedProperties();
        }

        public bool LoadMediaIntoDjDeckSlot(DjDeckSlot? slot)
        {
            var slotIndex = GetDjDeckSlotIndex(slot);
            if (slotIndex < 0)
            {
                return false;
            }

            var dlg = new OpenFileDialog
            {
                Multiselect = false,
                Filter = "Media Files|*.mp3;*.wav;*.mp4;*.wma;*.aac|All Files|*.*"
            };

            if (dlg.ShowDialog() != true)
            {
                return false;
            }

            var file = dlg.FileName;
            if (string.IsNullOrWhiteSpace(file))
            {
                return false;
            }

            var item = Playlist.FirstOrDefault(existingItem =>
                string.Equals(existingItem.FilePath, file, StringComparison.OrdinalIgnoreCase));

            if (item == null)
            {
                item = new MediaItem
                {
                    FilePath = file,
                    Title = System.IO.Path.GetFileName(file)
                };
                Playlist.Add(item);
            }

            AssignMediaItemToDjDeckSlot(slotIndex, item);

            if (Selected == null || (slot?.IsActive ?? false) || _focusedDjDeckIndex < 0)
            {
                FocusDjDeckSlot(slotIndex);
                IsPlaying = false;
            }

            RefreshDjDeckSlots();
            return true;
        }

        public bool ToggleDjDeckSlotPlayback(DjDeckSlot? slot)
        {
            var slotIndex = GetDjDeckSlotIndex(slot);
            if (slotIndex < 0)
            {
                return false;
            }

            var item = GetMediaItemForDjDeckSlot(slot);
            if (item == null)
            {
                return false;
            }

            AssignMediaItemToDjDeckSlot(slotIndex, item);

            if (ReferenceEquals(Selected, item))
            {
                IsPlaying = !IsPlaying;
                return true;
            }

            Selected = item;
            IsPlaying = true;
            return true;
        }

        public bool TrySeekDjDeckSlot(DjDeckSlot? slot, double progressPercent)
        {
            var slotIndex = GetDjDeckSlotIndex(slot);
            if (slotIndex < 0)
            {
                return false;
            }

            var item = GetMediaItemForDjDeckSlot(slot);
            if (item == null)
            {
                return false;
            }

            AssignMediaItemToDjDeckSlot(slotIndex, item);

            var targetDuration = item.Duration.TotalSeconds;
            if (!ReferenceEquals(Selected, item))
            {
                Selected = item;
            }
            else
            {
                targetDuration = Duration;
            }

            var clampedPercent = Math.Clamp(progressPercent, 0d, 100d);
            if (targetDuration > 0d)
            {
                Position = targetDuration * (clampedPercent / 100d);
            }

            return true;
        }

        private void AddFiles()
        {
            if (IsDjDeckLayout)
            {
                return;
            }

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
                    IsPlaying = false;
                    Position = 0;
                    if (!ReferenceEquals(Selected, itemToPlay))
                    {
                        Selected = null;
                    }
                    Selected = itemToPlay;
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
