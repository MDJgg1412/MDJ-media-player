using System.Reflection;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MDJMediaPlayer.ViewModels;

namespace MDJMediaPlayer
{
    public partial class SettingsWindow : Window
    {
        private const string ReleasesPageUrl = "https://github.com/MDJgg1412/MDJ-media-player/releases";
        private static readonly Uri LatestReleaseApiUri = new("https://api.github.com/repos/MDJgg1412/MDJ-media-player/releases/latest");
        private static readonly Uri ReleasesApiUri = new("https://api.github.com/repos/MDJgg1412/MDJ-media-player/releases?per_page=25");
        private bool _isCheckingForUpdates;

        private sealed class GitHubLatestReleaseResponse
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }
        }

        private sealed class GitHubReleaseItem
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("prerelease")]
            public bool Prerelease { get; set; }

            [JsonPropertyName("draft")]
            public bool Draft { get; set; }
        }

        public SettingsWindow()
        {
            InitializeComponent();
            Loaded += SettingsWindow_Loaded;
        }

        private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var name = asm.GetName().Name ?? "MDJ Media Player";
                var versionInfo = asm.GetName().Version;
                var version = versionInfo == null ? "unknown" : versionInfo.ToString(3);
                AboutNameText.Text = name;
                AboutVersionText.Text = $"Version {version}";
            }
            catch
            {
                AboutVersionText.Text = "Version unknown";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Titlebar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

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

            try { DragMove(); } catch { }
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {

        }

        private async void CheckForUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isCheckingForUpdates)
            {
                return;
            }

            if (string.Equals(CheckForUpdateButton.Content?.ToString(), "Open", System.StringComparison.OrdinalIgnoreCase))
            {
                if (TryOpenReleasePage())
                {
                    UpdateCheckStatusText.Text = "Release page opened.";
                }
                else
                {
                    UpdateCheckStatusText.Text = $"Unable to open browser. Use: {ReleasesPageUrl}";
                }
                return;
            }

            _isCheckingForUpdates = true;
            try
            {
                CheckForUpdateButton.IsEnabled = false;
                CheckForUpdateButton.Content = "Check for update";
                UpdateCheckProgressBar.IsIndeterminate = true;
                UpdateCheckProgressBar.Value = 0d;
                UpdateCheckStatusText.Text = "Checking for updates...";

                var currentVersion = GetCurrentAppVersion();
                var allowPreRelease = (DataContext as MainViewModel)?.AllowPreReleaseUpdate == true;
                var latestVersion = await GetLatestReleaseVersionAsync(allowPreRelease);

                UpdateCheckProgressBar.IsIndeterminate = false;
                UpdateCheckProgressBar.Value = 0d;

                if (latestVersion == null)
                {
                    UpdateCheckStatusText.Text = "Update check failed. Could not read the latest release version.";
                    UpdateCheckProgressBar.Value = 0d;
                    CheckForUpdateButton.Content = "Check for update";
                    return;
                }

                if (latestVersion > currentVersion)
                {
                    const int openDelayMs = 5000;
                    const int tickMs = 100;
                    var steps = openDelayMs / tickMs;

                    UpdateCheckStatusText.Text = $"New version {latestVersion} found (current {currentVersion}).";

                    for (var step = 1; step <= steps; step++)
                    {
                        UpdateCheckProgressBar.Value = (100d * step) / steps;
                        await Task.Delay(tickMs);
                    }

                    CheckForUpdateButton.Content = "Open";
                    UpdateCheckStatusText.Text = $"New version {latestVersion} found. Click Open to view the release page.";
                    return;
                }

                CheckForUpdateButton.Content = "Check for update";
                UpdateCheckProgressBar.Value = 100d;
                UpdateCheckStatusText.Text = $"You are using the latest version ({currentVersion}).";
            }
            catch
            {
                UpdateCheckStatusText.Text = "Update check failed. Please try again.";
                UpdateCheckProgressBar.IsIndeterminate = false;
                UpdateCheckProgressBar.Value = 0d;
                CheckForUpdateButton.Content = "Check for update";
            }
            finally
            {
                CheckForUpdateButton.IsEnabled = true;
                _isCheckingForUpdates = false;
            }
        }

        private static Version GetCurrentAppVersion()
        {
            var versionInfo = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version;
            return NormalizeVersion(versionInfo ?? new Version(0, 0, 0, 0));
        }

        public static async Task<bool> IsUpdateAvailableAsync(bool includePreRelease)
        {
            var currentVersion = GetCurrentAppVersion();
            var latestVersion = await GetLatestReleaseVersionAsync(includePreRelease);
            return latestVersion != null && latestVersion > currentVersion;
        }

        public static bool OpenReleasePage()
        {
            return TryOpenReleasePage();
        }

        private static Version NormalizeVersion(Version version)
        {
            return new Version(
                version.Major,
                version.Minor,
                version.Build < 0 ? 0 : version.Build);
        }

        private static Version? ParseReleaseVersion(string? tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return null;
            }

            var value = tagName.Trim();
            if (value.StartsWith("v", System.StringComparison.OrdinalIgnoreCase))
            {
                value = value[1..];
            }

            var suffixIndex = value.IndexOf('-');
            if (suffixIndex >= 0)
            {
                value = value[..suffixIndex];
            }

            return Version.TryParse(value, out var parsed)
                ? NormalizeVersion(parsed)
                : null;
        }

        private static async Task<Version?> GetLatestReleaseVersionAsync(bool includePreRelease)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MDJMediaPlayer Update Checker");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            if (!includePreRelease)
            {
                using var latestResponse = await client.GetAsync(LatestReleaseApiUri);
                latestResponse.EnsureSuccessStatusCode();

                await using var latestStream = await latestResponse.Content.ReadAsStreamAsync();
                var latestPayload = await JsonSerializer.DeserializeAsync<GitHubLatestReleaseResponse>(latestStream);
                return ParseReleaseVersion(latestPayload?.TagName);
            }

            using var releasesResponse = await client.GetAsync(ReleasesApiUri);
            releasesResponse.EnsureSuccessStatusCode();

            await using var releasesStream = await releasesResponse.Content.ReadAsStreamAsync();
            var releases = await JsonSerializer.DeserializeAsync<GitHubReleaseItem[]>(releasesStream) ?? System.Array.Empty<GitHubReleaseItem>();

            foreach (var release in releases)
            {
                if (release == null || release.Draft)
                {
                    continue;
                }

                var parsedVersion = ParseReleaseVersion(release.TagName);
                if (parsedVersion != null)
                {
                    return parsedVersion;
                }
            }

            return null;
        }

        private static bool TryOpenReleasePage()
        {
            try
            {
                Process.Start(new ProcessStartInfo(ReleasesPageUrl)
                {
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
