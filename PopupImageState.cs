using System;

namespace HappyTrigger;

public sealed class PopupImageState
{
    public HappyTriggerSetting Trigger { get; }

    // トリガーを検知した時刻です。
    public DateTime DetectedTimeUtc { get; }

    // 実際に表示を開始する時刻です。
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
        this.DetectedTimeUtc = DateTime.UtcNow;

        if (isPositionSetting)
        {
            this.StartTimeUtc = this.DetectedTimeUtc;
            this.EndTimeUtc = DateTime.MaxValue;
        }
        else
        {
            var waitSeconds = Math.Clamp(trigger.WaitSeconds, 0.0f, 600.0f);
            var displaySeconds = Math.Max(0.1f, trigger.DisplaySeconds);

            this.StartTimeUtc = this.DetectedTimeUtc.AddSeconds(waitSeconds);
            this.EndTimeUtc = this.StartTimeUtc.AddSeconds(displaySeconds);
        }
    }

    public bool IsReadyToDisplay => DateTime.UtcNow >= this.StartTimeUtc;

    public bool IsExpired => DateTime.UtcNow >= this.EndTimeUtc;
}
