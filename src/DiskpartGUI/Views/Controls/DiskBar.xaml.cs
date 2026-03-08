using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using DiskpartGUI.ViewModels;

namespace DiskpartGUI.Views.Controls;

public partial class DiskBar : UserControl
{
    public static readonly DependencyProperty TotalSizeBytesProperty =
        DependencyProperty.Register(nameof(TotalSizeBytes), typeof(long), typeof(DiskBar),
            new PropertyMetadata(0L, OnLayoutChanged));

    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(IEnumerable<IDiskBarItem>), typeof(DiskBar),
            new PropertyMetadata(null, OnItemsChanged));

    public long TotalSizeBytes
    {
        get => (long)GetValue(TotalSizeBytesProperty);
        set => SetValue(TotalSizeBytesProperty, value);
    }

    public IEnumerable<IDiskBarItem>? Items
    {
        get => (IEnumerable<IDiskBarItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public DiskBar()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateRelativeWidths();
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((DiskBar)d).UpdateRelativeWidths();

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (DiskBar)d;

        if (e.OldValue is INotifyCollectionChanged oldColl)
            oldColl.CollectionChanged -= bar.OnCollectionChanged;

        if (e.NewValue is INotifyCollectionChanged newColl)
            newColl.CollectionChanged += bar.OnCollectionChanged;

        bar.PartitionsControl.ItemsSource = e.NewValue as IEnumerable;
        bar.UpdateRelativeWidths();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => UpdateRelativeWidths();

    private void UpdateRelativeWidths()
    {
        if (TotalSizeBytes <= 0 || ActualWidth <= 0) return;
        if (PartitionsControl.ItemsSource is not IEnumerable<IDiskBarItem> items) return;

        var totalWidth = ActualWidth - 4; // account for margins
        foreach (var item in items)
        {
            item.RelativeWidth = (double)item.SizeBytes / TotalSizeBytes * totalWidth;
        }
    }
}
