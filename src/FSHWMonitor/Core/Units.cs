namespace FSHWMonitor.Core;

/// <summary>Readings and settings are kept in °C; Fahrenheit is only for display and input.</summary>
public static class Units
{
    public static double ToFahrenheit(double celsius) => celsius * 9 / 5 + 32;

    public static double ToCelsius(double fahrenheit) => (fahrenheit - 32) * 5 / 9;
}
