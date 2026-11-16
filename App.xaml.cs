using System.Windows;

namespace ForpakLauncher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show(ex.Exception.Message, "Forpak Launcher - erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };
        }
    }
}
