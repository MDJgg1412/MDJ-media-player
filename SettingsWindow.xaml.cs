using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MDJMediaPlayer
{
    public partial class SettingsWindow : Window
    {
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
    }
}
