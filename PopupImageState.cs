using System;

namespace HappyTrigger;

public sealed class PopupImageState
{
    public HappyTriggerSetting Trigger { get; }

    public DateTime StartTimeUtc { get; }

    public DateTime EndTimeUtc { get; }

    public bool IsPositionSetting { get; }

    public bool IsDragging { get; set; }

    public bool PositionChanged { get; set; }

    public bool IsClosed { get; set; }

    public PopupImageState(HappyTriggerSetting trigger, bool isPositionSetting = false)
    {
        this.Trigger = trigger;
        this.IsPositionSetting = isPositionSetting;
        this.StartTimeUtc = DateTime.UtcNow;

        if (isPositionSetting)
        {
            this.EndTimeUtc = DateTime.MaxValue;
        }
        else
        {
            var seconds = Math.Max(0.1f, trigger.DisplaySeconds);
            this.EndTimeUtc = this.StartTimeUtc.AddSeconds(seconds);
        }
    }

    public bool IsExpired => DateTime.UtcNow >= this.EndTimeUtc;
}
