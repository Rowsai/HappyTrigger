using System;

namespace HappyTrigger;

public sealed class TimelineBetaRecord
{
    public int SessionId { get; set; }

    public DateTime Timestamp { get; set; }

    public double ElapsedSeconds { get; set; }

    public string Source { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string EnemyName { get; set; } = string.Empty;

    public uint ActionId { get; set; }

    public string ActionName { get; set; } = string.Empty;

    public float CastTotalSeconds { get; set; }

    public string TargetName { get; set; } = string.Empty;

    public long Damage { get; set; }

    public string MaxDamageTargetName { get; set; } = string.Empty;

    public long MaxDamage { get; set; }

    public string TargetStatuses { get; set; } = string.Empty;

    public string TimelineText { get; set; } = string.Empty;

    public string RawLog { get; set; } = string.Empty;
}
