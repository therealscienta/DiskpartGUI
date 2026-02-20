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
}
