using System.Windows;
using System.Windows.Controls;

namespace FSHWMonitor.Controls;

/// <summary>
/// WPF has no letter-spacing, so this attached property sets a TextBlock's text in capitals with a
/// thin space between characters. Used for the small all-caps labels.
/// </summary>
public static class Tracking
{
    const string ThinSpace = " ";

    public static readonly DependencyProperty WideTextProperty = DependencyProperty.RegisterAttached(
        "WideText", typeof(string), typeof(Tracking), new PropertyMetadata(null, OnWideTextChanged));

    public static string? GetWideText(DependencyObject o) => (string?)o.GetValue(WideTextProperty);
    public static void SetWideText(DependencyObject o, string? value) => o.SetValue(WideTextProperty, value);

    static void OnWideTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
            textBlock.Text = e.NewValue is string { Length: > 0 } text
                ? string.Join(ThinSpace, text.ToUpperInvariant().ToCharArray())
                : "";
    }
}
