using System.Numerics;
using BogiHub.Core.Profiles;

namespace BogiHub.Core.Mapping;

/// <summary>
/// Raw gyro → aim velocity (rad/s, +X right, +Y up) for one sample. Owns the per-session state: the drift
/// estimate, the gravity estimate and the smoothing filter. Pure (time comes in as dt), so it is unit tested.
/// <list type="bullet">
/// <item><b>Calibration:</b> whenever the controller rests still for a moment, the gyro's resting reading is learned
/// and subtracted, so the aim stops creeping.</item>
/// <item><b>Turn axis:</b> yaw, roll, or "player space", which turns about the real world's up axis however the
/// controller is held (Jibb Smart's method: gravity from the accelerometer picks the axis).</item>
/// <item><b>Smoothing:</b> only below a small speed, so hand shake is calmed and real motion keeps full response.</item>
/// <item><b>Precision:</b> lower sensitivity at slow speeds for fine aim, full sensitivity for fast turns.</item>
/// </list>
/// </summary>
public sealed class GyroProcessor
{
    /// <summary>Below this speed (rad/s, about 3.4°/s) with a steady accelerometer the controller counts as resting.</summary>
    public const float StillGyro = 0.06f;
    /// <summary>Accelerometer change (m/s²) still counted as resting: hands are never perfectly steady.</summary>
    public const float StillAccel = 0.35f;
    /// <summary>Resting time before calibration starts learning, and the time constant it learns with.</summary>
    public const float StillSettleSeconds = 0.6f, CalibrationSeconds = 1.0f;
    /// <summary>Smoothing at 100 % works below this speed (rad/s, about 7°/s).</summary>
    public const float SmoothingMaxThreshold = 0.12f;
    /// <summary>Precision: the speed (rad/s, about 75°/s) at which full sensitivity is reached.</summary>
    public const float PrecisionFullSpeed = 1.3f;
    /// <summary>Player space: how far yaw may be boosted from the world axis before the local turn caps it.</summary>
    private const float YawRelax = 1.41f;
    private const float NoiseFloor = 0.004f;

    private Vector3 _bias;
    private bool _biasKnown;
    private float _stillFor;
    private Vector3 _gravity;
    private Vector3 _accelSmoothed;
    private Vector2 _smoothed;

    /// <summary>The learned resting reading (rad/s), for diagnostics.</summary>
    public Vector3 Bias => _bias;
    public bool Calibrated => _biasKnown;

    public void Reset()
    {
        _stillFor = 0;
        _smoothed = Vector2.Zero;
        _gravity = Vector3.Zero;
        _accelSmoothed = Vector3.Zero;
        // The bias belongs to the physical sensor, so it survives a profile switch or a pause.
    }

    /// <summary>Aiming is held off (hold-to-enable released): start the smoothing fresh when it comes back.</summary>
    public void Idle() => _smoothed = Vector2.Zero;

    /// <summary>Learns drift and gravity. Call on every motion sample, also while gyro aiming is held off.</summary>
    public void Observe(Vector3 gyro, Vector3 accel, float dt)
    {
        if (dt <= 0) return;
        if (accel != Vector3.Zero)
        {
            if (_accelSmoothed == Vector3.Zero) _accelSmoothed = _gravity = accel;
            float accelChange = (accel - _accelSmoothed).Length();
            _accelSmoothed = Vector3.Lerp(_accelSmoothed, accel, Blend(dt, 0.05f));
            // Gravity: a slow average, so quick hand movements don't tilt the turning axis.
            _gravity = Vector3.Lerp(_gravity, accel, Blend(dt, 0.25f));

            bool still = (gyro - (_biasKnown ? _bias : Vector3.Zero)).Length() < StillGyro && accelChange < StillAccel;
            _stillFor = still ? _stillFor + dt : 0;
        }
        else
        {
            _stillFor = 0; // no accelerometer: never guess that the controller is resting
        }

        if (_stillFor >= StillSettleSeconds)
        {
            _bias = _biasKnown ? Vector3.Lerp(_bias, gyro, Blend(dt, CalibrationSeconds)) : gyro;
            _biasKnown = true;
        }
    }

    /// <summary>Aim velocity for this sample (rad/s before sensitivity): X right, Y up.</summary>
    public Vector2 Aim(Vector3 gyro, GyroConfig c, float dt)
    {
        var w = gyro - (_biasKnown ? _bias : Vector3.Zero);
        if (MathF.Abs(w.X) < NoiseFloor) w.X = 0;
        if (MathF.Abs(w.Y) < NoiseFloor) w.Y = 0;
        if (MathF.Abs(w.Z) < NoiseFloor) w.Z = 0;

        // SDL axes: X pitch (nose up +), Y yaw (turn left +), Z roll (tilt left +).
        float turnLeft = c.TurnAxis switch
        {
            GyroTurnAxis.Yaw => w.Y,
            GyroTurnAxis.Roll => w.Z,
            _ => PlayerSpaceYaw(w),
        };
        var aim = new Vector2(-turnLeft, w.X * c.VerticalScale);
        if (c.InvertX) aim.X = -aim.X;
        if (c.InvertY) aim.Y = -aim.Y;

        aim = Smooth(aim, c.Smoothing, dt);

        if (c.Precision > 0)
        {
            float speed = aim.Length();
            float k = Math.Clamp(speed / PrecisionFullSpeed, 0f, 1f);
            aim *= 1f - c.Precision * (1f - k);
        }
        return aim;
    }

    private float PlayerSpaceYaw(Vector3 w)
    {
        if (_gravity.LengthSquared() < 1f) return w.Y; // no gravity estimate yet
        var up = Vector3.Normalize(_gravity);
        // Turn about the world's up axis. Pitch (X) is left out, so tilting forward never leaks into turning.
        float world = w.Y * up.Y + w.Z * up.Z;
        float local = MathF.Sqrt(w.Y * w.Y + w.Z * w.Z);
        return MathF.Sign(world) * MathF.Min(MathF.Abs(world) * YawRelax, local);
    }

    /// <summary>
    /// Tiered smoothing: each sample splits into a direct part and a smoothed part by speed (all smoothed below half
    /// the threshold, all direct above it). Only the smoothed part enters the average, so a fast turn leaves no tail.
    /// </summary>
    private Vector2 Smooth(Vector2 input, float amount, float dt)
    {
        if (amount <= 0)
        {
            _smoothed = Vector2.Zero;
            return input;
        }
        float threshold = amount * SmoothingMaxThreshold;
        float direct = Math.Clamp((input.Length() - threshold * 0.5f) / (threshold * 0.5f), 0f, 1f);
        _smoothed = Vector2.Lerp(_smoothed, input * (1f - direct), Blend(dt, 0.06f));
        return input * direct + _smoothed;
    }

    /// <summary>Fraction of the way to move towards a target this sample, for a given time constant.</summary>
    private static float Blend(float dt, float tau) => 1f - MathF.Exp(-dt / tau);
}
