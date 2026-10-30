namespace BogiHub.App.Controls;

/// <summary>
/// Critically-damped-ish spring used for every hologram animation: it follows a moving target smoothly,
/// never overshoots wildly, and can be interrupted at any moment (unlike fixed-duration tweens).
/// </summary>
public struct Spring(float stiffness = 420f, float dampingRatio = 0.86f)
{
    public float Value;
    public float Velocity;
    public float Target;
    private readonly float _k = stiffness;
    private readonly float _c = 2f * MathF.Sqrt(stiffness) * dampingRatio;

    public void Step(float dt)
    {
        // Sub-step so long frames (window dragged, system busy) stay stable.
        int steps = Math.Max(1, (int)MathF.Ceiling(dt / (1f / 120f)));
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            float a = _k * (Target - Value) - _c * Velocity;
            Velocity += a * h;
            Value += Velocity * h;
        }
    }

    public void Snap(float v)
    {
        Value = Target = v;
        Velocity = 0;
    }
}
