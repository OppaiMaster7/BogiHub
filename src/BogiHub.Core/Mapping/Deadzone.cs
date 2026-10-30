using System.Numerics;

namespace BogiHub.Core.Mapping;

public static class Deadzone
{
    /// <summary>
    /// Radial deadzone with rescaling: inside <paramref name="inner"/> → zero; beyond 1 - <paramref name="outer"/> → full;
    /// in between the magnitude is stretched to 0..1 so there is no jump at the deadzone edge. Direction is preserved.
    /// </summary>
    public static Vector2 Radial(Vector2 v, float inner, float outer)
    {
        float mag = v.Length();
        if (mag <= inner || mag <= 0f) return Vector2.Zero;
        float usable = 1f - outer - inner;
        float scaled = usable <= 0f ? 1f : Math.Clamp((mag - inner) / usable, 0f, 1f);
        return v / mag * scaled;
    }

    /// <summary>One-dimensional version for triggers (0..1).</summary>
    public static float Linear(float v, float inner)
    {
        if (v <= inner) return 0f;
        return inner >= 1f ? 1f : Math.Clamp((v - inner) / (1f - inner), 0f, 1f);
    }

    /// <summary>Clamp each component and the length to the unit circle.</summary>
    public static Vector2 ClampUnit(Vector2 v)
    {
        float mag = v.Length();
        return mag > 1f ? v / mag : v;
    }
}
