using BogiHub.Core.Output;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace BogiHub.Output;

/// <summary>
/// Virtual Xbox 360 controller through ViGEmBus 1.22 (retired upstream; see docs/feasibility.md for the risk note).
/// Only touched from the input thread, except <see cref="Rumble"/> which ViGEm raises on its own thread.
/// </summary>
public sealed class ViGEmGamepadSink : IDisposable
{
    private ViGEmClient? _client;
    private IXbox360Controller? _pad;
    private GamepadReport _last;
    private long _lastSubmitTicks;

    /// <summary>Game asked for rumble: (large/low-frequency motor, small/high-frequency motor), 0..255.</summary>
    public event Action<byte, byte>? Rumble;

    public bool IsConnected => _pad is not null;
    public OutputStatus Status { get; private set; } = OutputStatus.Idle;

    /// <summary>Plugs in the virtual controller. Safe to call repeatedly.</summary>
    public bool Connect()
    {
        if (_pad is not null) return true;
        try
        {
            _client ??= new ViGEmClient();
            var pad = _client.CreateXbox360Controller();
            pad.AutoSubmitReport = false;
            pad.FeedbackReceived += OnFeedback;
            pad.Connect();
            _pad = pad;
            _last = GamepadReport.Neutral;
            Submit(GamepadReport.Neutral, force: true);
            Status = new OutputStatus(OutputReadiness.Ready, "Virtual Xbox controller connected");
            return true;
        }
        catch (Exception ex) when (ex.GetType().Name == "VigemBusNotFoundException")
        {
            DisposeClient();
            Status = new OutputStatus(OutputReadiness.DriverMissing, "ViGEmBus driver is not installed. Install it from Settings to enable controller output.");
            return false;
        }
        catch (Exception ex)
        {
            DisposeClient();
            Status = new OutputStatus(OutputReadiness.Error, $"Virtual controller failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Sends the report if it changed, plus a periodic refresh so a dropped report can never leave a stuck value.</summary>
    public void Submit(in GamepadReport r, bool force = false)
    {
        var pad = _pad;
        if (pad is null) return;
        long now = Environment.TickCount64;
        if (!force && r.Equals(_last) && now - _lastSubmitTicks < 250) return;

        try
        {
            pad.SetButtonsFull((ushort)r.Buttons);
            pad.SetSliderValue(Xbox360Slider.LeftTrigger, r.LeftTrigger);
            pad.SetSliderValue(Xbox360Slider.RightTrigger, r.RightTrigger);
            pad.SetAxisValue(Xbox360Axis.LeftThumbX, r.LeftX);
            pad.SetAxisValue(Xbox360Axis.LeftThumbY, r.LeftY);
            pad.SetAxisValue(Xbox360Axis.RightThumbX, r.RightX);
            pad.SetAxisValue(Xbox360Axis.RightThumbY, r.RightY);
            pad.SubmitReport();
            _last = r;
            _lastSubmitTicks = now;
        }
        catch (Exception ex)
        {
            Status = new OutputStatus(OutputReadiness.Error, $"Virtual controller stopped responding: {ex.Message}");
            Disconnect();
        }
    }

    /// <summary>Centre sticks, release triggers and buttons. Keeps the device plugged in.</summary>
    public void Neutralize() => Submit(GamepadReport.Neutral, force: true);

    /// <summary>Neutralises, then unplugs the virtual controller.</summary>
    public void Disconnect()
    {
        var pad = _pad;
        if (pad is null) return;
        _pad = null;
        try
        {
            pad.ResetReport();
            pad.SubmitReport();
        }
        catch { /* already gone */ }
        try
        {
            pad.FeedbackReceived -= OnFeedback;
            pad.Disconnect();
        }
        catch { /* already gone */ }
        Rumble?.Invoke(0, 0);
        if (Status.Readiness == OutputReadiness.Ready) Status = OutputStatus.Idle;
    }

    /// <summary>Checks whether the driver is present without plugging anything in.</summary>
    public static OutputStatus ProbeDriver()
    {
        try
        {
            using var client = new ViGEmClient();
            return new OutputStatus(OutputReadiness.Ready, "ViGEmBus driver installed");
        }
        catch (Exception ex) when (ex.GetType().Name == "VigemBusNotFoundException")
        {
            return new OutputStatus(OutputReadiness.DriverMissing, "ViGEmBus driver is not installed");
        }
        catch (Exception ex)
        {
            return new OutputStatus(OutputReadiness.Error, $"ViGEmBus check failed: {ex.Message}");
        }
    }

    private void OnFeedback(object? sender, Xbox360FeedbackReceivedEventArgs e) => Rumble?.Invoke(e.LargeMotor, e.SmallMotor);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
    }

    public void Dispose()
    {
        Disconnect();
        DisposeClient();
    }
}
