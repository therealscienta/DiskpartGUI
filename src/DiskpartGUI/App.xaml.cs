using System.Windows;
using DiskpartGUI.Services;
using DiskpartGUI.ViewModels;
using DiskpartGUI.Views;

namespace DiskpartGUI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(
                $"Unhandled error:\n\n{ex.Exception.GetType().Name}: {ex.Exception.Message}\n\n{ex.Exception.StackTrace}",
                "Unexpected Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        // Manual composition root — no DI container
        Func<IScriptBuilder> scriptBuilderFactory = () => new DiskpartScriptBuilder();
        var partitionService = new DiskpartService(scriptBuilderFactory);
        var diskService      = new WmiDiskService();
        var moveService      = new RawDiskMoveService();
        var dialogService    = new WpfDialogService();

        var mainVm = new MainViewModel(diskService, partitionService, moveService, dialogService);

        var mainWindow = new MainWindow { DataContext = mainVm };
        MainWindow = mainWindow;

        // Trigger initial disk load after window is visible
        mainWindow.Loaded += async (_, _) =>
        {
            try { await mainVm.RefreshAsync(); }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to load disks:\n\n{ex.GetType().Name}: {ex.Message}",
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        mainWindow.Show();
    }
}
