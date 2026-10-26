using System.Windows;
using System.Windows.Controls;
using Pakban.ViewModels;

namespace Pakban.Views;

public partial class CleanerPage : UserControl
{
    public CleanerPage() => InitializeComponent();

    private void OnStandardTab(object sender, RoutedEventArgs e)
    {
        if (StandardList is not null) StandardList.Visibility = Visibility.Visible;
        if (OrphansPanel is not null) OrphansPanel.Visibility = Visibility.Collapsed;
        if (DataContext is CleanerViewModel vm) vm.Tab = CleanerTab.Standard;
    }

    private void OnOrphansTab(object sender, RoutedEventArgs e)
    {
        if (StandardList is not null) StandardList.Visibility = Visibility.Collapsed;
        if (OrphansPanel is not null) OrphansPanel.Visibility = Visibility.Visible;
        if (DataContext is CleanerViewModel vm) vm.Tab = CleanerTab.Orphans;
    }
}
