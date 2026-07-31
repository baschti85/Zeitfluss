using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using Zeitfluss.Models;
using Zeitfluss.Services;

namespace Zeitfluss;

public partial class AboutWindow : Window
{
    public const string GitHubUrl = "https://github.com/baschti85/Zeitfluss";
    public const string ContactEmail = "bastianwerner@bundeswehr.org";

    public AboutWindow(AppSettings settings)
    {
        InitializeComponent();
        WindowAppearance.Apply(this, settings);
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "Version 1.1.0" : $"Version {version.Major}.{version.Minor}.{version.Build}";
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(
                $"Der Link konnte nicht geöffnet werden.\n\n{e.Uri.AbsoluteUri}",
                "Zeitfluss",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
