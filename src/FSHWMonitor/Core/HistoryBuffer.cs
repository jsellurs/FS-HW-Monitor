namespace FSHWMonitor.Core;

/// <summary>Fixed-size ring buffer of samples; index 0 is the oldest.</summary>
public sealed class HistoryBuffer
{
    readonly double[] _items;
    int _next;

    public HistoryBuffer(int capacity) => _items = new double[Math.Max(2, capacity)];

    public int Capacity => _items.Length;
    public int Count { get; private set; }

    public double this[int index] => _items[(_next - Count + index + _items.Length) % _items.Length];

    public void Add(double value)
    {
        _items[_next] = value;
        _next = (_next + 1) % _items.Length;
        if (Count < _items.Length) Count++;
    }

    public void Clear()
    {
        Count = 0;
        _next = 0;
    }
}
