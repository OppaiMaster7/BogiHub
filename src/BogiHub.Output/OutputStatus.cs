namespace BogiHub.Output;

public enum OutputReadiness
{
    /// <summary>Not needed by the current profile, or the Hub is not controlling the device.</summary>
    Idle,
    Ready,
    /// <summary>The virtual-controller driver (ViGEmBus) is not installed.</summary>
    DriverMissing,
    /// <summary>Windows refused the input, e.g. the focused game runs as administrator.</summary>
    Blocked,
    Error,
}

public readonly record struct OutputStatus(OutputReadiness Readiness, string Message)
{
    public static readonly OutputStatus Idle = new(OutputReadiness.Idle, "Not in use");
}
