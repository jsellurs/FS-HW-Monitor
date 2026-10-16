namespace FSHWMonitor.Core;

/// <summary>Running high / average / low since the session started (or was reset).</summary>
public sealed class SessionStats
{
    double _sum;

    public double Min { get; private set; } = double.NaN;
    public double Max { get; private set; } = double.NaN;
    public int Count { get; private set; }
    public double Average => Count == 0 ? double.NaN : _sum / Count;

    public void Add(double value)
    {
        if (Count == 0 || value < Min) Min = value;
        if (Count == 0 || value > Max) Max = value;
        _sum += value;
        Count++;
    }

    public void Reset()
    {
        _sum = 0;
        Count = 0;
        Min = double.NaN;
        Max = double.NaN;
    }
}
