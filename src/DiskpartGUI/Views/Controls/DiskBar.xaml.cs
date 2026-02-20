using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using DiskpartGUI.ViewModels;

namespace DiskpartGUI.Views.Controls;

public partial class DiskBar : UserControl
{
    public static readonly DependencyProperty TotalSizeBytesProperty =
        DependencyProperty.Register(nameof(TotalSizeBytes), typeof(long), typeof(DiskBar),
            new PropertyMetadata(0L, OnLayoutChanged));

    public long TotalSizeBytes
    {
        get => (long)GetValue(TotalSizeBytesProperty);
        set => SetValue(TotalSizeBytesProperty, value);
    }

    public DiskBar()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateRelativeWidths();
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((DiskBar)d).UpdateRelativeWidths();

    private void UpdateRelativeWidths()
    {
        if (TotalSizeBytes <= 0 || ActualWidth <= 0) return;
        if (PartitionsControl.ItemsSource is not IEnumerable<PartitionItemViewModel> partitions) return;

        var totalWidth = ActualWidth - 4; // account for margins
        foreach (var partition in partitions)
        {
            partition.RelativeWidth = (double)partition.SizeBytes / TotalSizeBytes * totalWidth;
        }
    }
}
