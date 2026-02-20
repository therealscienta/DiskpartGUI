using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace DiskpartGUI.Views.Dialogs;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        var asm     = Assembly.GetExecutingAssembly();
        var version = asm.GetName().Version;
        var ver     = version is null ? "Unknown"
                    : $"{version.Major}.{version.Minor}.{version.Build}";

        AppNameText.Text   = "DiskpartGUI";
        VersionText.Text   = $"Version {ver}";
        CopyrightText.Text = $"© {DateTime.Now.Year} [therealscienta]"; // TODO: replace
    }

    private void GitHubLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
