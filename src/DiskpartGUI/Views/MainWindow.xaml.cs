using System.Windows;
using System.Windows.Media.Imaging;

namespace DiskpartGUI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Resources/favicon_filled.ico"));
    }
}
