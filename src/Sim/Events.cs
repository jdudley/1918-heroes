namespace Sim;

/// <summary>
/// Transient per-tick output for renderers, loggers, and future AI senses.
/// Events are derived output and are deliberately excluded from state hashing.
/// </summary>
public readonly record struct ShotEvent(int ShooterId, int TargetId, bool Hit);
