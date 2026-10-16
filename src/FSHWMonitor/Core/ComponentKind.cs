namespace FSHWMonitor.Core;

/// <summary>The cards the dashboard can show.</summary>
public enum ComponentKind { Cpu, Gpu, Ram, Ssd, Cooling }

public static class ComponentKinds
{
    public static IReadOnlyList<ComponentKind> All { get; } = Enum.GetValues<ComponentKind>();

    public static string Title(this ComponentKind kind) => kind switch
    {
        ComponentKind.Cpu => "CPU",
        ComponentKind.Gpu => "GPU",
        ComponentKind.Ram => "RAM",
        ComponentKind.Ssd => "SSD",
        _ => "Cooling",
    };
}
