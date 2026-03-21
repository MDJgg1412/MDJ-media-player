using System;
using System.Windows;

namespace MDJMediaPlayer
{
    public partial class App : Application
    {
        public string[] StartupArguments { get; private set; } = Array.Empty<string>();

        protected override void OnStartup(StartupEventArgs e)
        {
            StartupArguments = e.Args ?? Array.Empty<string>();
            base.OnStartup(e);
        }
    }
}
