using System.Windows;

namespace DiskpartGUI.Views.Dialogs;

public partial class AddPartitionDialog : Window
{
    public AddPartitionDialog()
    {
        InitializeComponent();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
