using System.Windows;
using DiskpartGUI.ViewModels;
using DiskpartGUI.Views.Dialogs;

namespace DiskpartGUI.Services;

public sealed class WpfDialogService : IDialogService
{
    public bool? ShowAddPartitionDialog(AddPartitionViewModel vm)
    {
        var dialog = new AddPartitionDialog { DataContext = vm, Owner = Application.Current.MainWindow };
        return dialog.ShowDialog();
    }

    public bool? ShowDeletePartitionDialog(DeletePartitionViewModel vm)
    {
        var dialog = new DeletePartitionDialog { DataContext = vm, Owner = Application.Current.MainWindow };
        return dialog.ShowDialog();
    }

    public bool? ShowResizePartitionDialog(ResizePartitionViewModel vm)
    {
        var dialog = new ResizePartitionDialog { DataContext = vm, Owner = Application.Current.MainWindow };
        return dialog.ShowDialog();
    }

    public bool? ShowMovePartitionDialog(MovePartitionViewModel vm)
    {
        var dialog = new MovePartitionDialog { DataContext = vm, Owner = Application.Current.MainWindow };
        return dialog.ShowDialog();
    }

    public void ShowAboutDialog()
    {
        var dialog = new AboutDialog { Owner = Application.Current.MainWindow };
        dialog.ShowDialog();
    }

    public void ShowError(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
