using System.Windows;
using FSHWMonitor.Platform;
using FSHWMonitor.ViewModels;

namespace FSHWMonitor.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(IDashboardHost host)
    {
        InitializeComponent();
        DarkTitleBar.Enable(this);
        DataContext = new SettingsViewModel(host);

        // Open on the main monitor, not the small in-case screen the dashboard lives on.
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + (area.Height - Height) / 2;
    }

    public void SelectTab(string? header)
    {
        if (header is null) return;
        var tab = Tabs.Items.OfType<System.Windows.Controls.TabItem>()
            .FirstOrDefault(t => string.Equals(t.Header as string, header, StringComparison.OrdinalIgnoreCase)
                                 || (t.Header as string)?.StartsWith(header, StringComparison.OrdinalIgnoreCase) == true);
        if (tab != null)
            Tabs.SelectedItem = tab;
    }
}
