using System.Windows;
using FSHWMonitor.Platform;

namespace FSHWMonitor.Views;

public partial class AlreadyRunningWindow : Window
{
    public enum Choice { Cancel, Show, Restart }

    Choice _choice = Choice.Cancel;

    AlreadyRunningWindow()
    {
        InitializeComponent();
        DarkTitleBar.Enable(this);
    }

    public static Choice Ask()
    {
        var window = new AlreadyRunningWindow();
        window.ShowDialog();
        return window._choice;
    }

    void OnShow(object sender, RoutedEventArgs e) => Finish(Choice.Show);
    void OnRestart(object sender, RoutedEventArgs e) => Finish(Choice.Restart);
    void OnCancel(object sender, RoutedEventArgs e) => Finish(Choice.Cancel);

    void Finish(Choice choice)
    {
        _choice = choice;
        Close();
    }
}
