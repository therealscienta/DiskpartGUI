using System.ComponentModel;
using System.Windows;
using DiskpartGUI.ViewModels;

namespace DiskpartGUI.Views.Dialogs;

public partial class MovePartitionDialog : Window
{
    public MovePartitionDialog()
    {
        InitializeComponent();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        // Inject the close callback into the VM so it can close the dialog
        // when the user clicks "Close" after the operation finishes.
        if (DataContext is MovePartitionViewModel vm)
        {
            vm.RequestClose = () =>
            {
                // True = completed successfully; false/null = cancelled or not started
                DialogResult = vm.IsComplete;
                Close();
            };
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing mid-copy would leave the move running unseen and re-enable the main window,
        // so another operation could start on the same disk. Make the user cancel first.
        if (DataContext is MovePartitionViewModel { IsMoving: true })
        {
            e.Cancel = true;
            MessageBox.Show(this,
                "The partition is still being moved. Click Cancel to stop it safely, then close this window.",
                "Move in progress", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        base.OnClosing(e);
    }
}
