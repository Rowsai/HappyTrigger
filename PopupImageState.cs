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

    public string PositionSettingGroupId { get; } = string.Empty;

    public TriggerLabelSetting? LabelStack { get; }

    public bool HasLabelStack => this.LabelStack != null && !string.IsNullOrWhiteSpace(this.LabelStack.LabelId);

    public bool IsLabelPositionSetting => this.IsPositionSetting && this.HasLabelStack;

    public bool IsGroupedPositionSetting => this.IsPositionSetting && !string.IsNullOrWhiteSpace(this.PositionSettingGroupId);

    public bool IsDragging { get; set; }

    public bool PositionChanged { get; set; }

    public bool IsClosed { get; set; }

    public bool HasStatusRemainingDisplay { get; }

    public string StatusRemainingStatusName { get; } = string.Empty;

    public float StatusRemainingInitialSeconds { get; }

    public DateTime StatusRemainingCountdownStartUtc { get; }

    public PopupImageState(
        HappyTriggerSetting trigger,
        bool isPositionSetting = false,
        StatusRemainingDisplayState? statusRemainingDisplayState = null,
        string positionSettingGroupId = "",
        TriggerLabelSetting? labelStack = null)
    {
        this.Trigger = trigger;
        this.IsPositionSetting = isPositionSetting;
        this.PositionSettingGroupId = isPositionSetting ? positionSettingGroupId : string.Empty;
        this.LabelStack = labelStack;
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

        if (!isPositionSetting && statusRemainingDisplayState != null)
        {
            var elapsedUntilDisplay = (float)Math.Max(0.0, (this.StartTimeUtc - statusRemainingDisplayState.CapturedAtUtc).TotalSeconds);
            this.HasStatusRemainingDisplay = true;
            this.StatusRemainingStatusName = statusRemainingDisplayState.StatusName;
            this.StatusRemainingInitialSeconds = Math.Max(0.0f, statusRemainingDisplayState.RemainingSeconds - elapsedUntilDisplay);
            this.StatusRemainingCountdownStartUtc = this.StartTimeUtc;
        }
    }

    public bool IsReadyToDisplay => DateTime.UtcNow >= this.StartTimeUtc;

    public float CurrentStatusRemainingSeconds
    {
        get
        {
            if (!this.HasStatusRemainingDisplay)
            {
                return 0.0f;
            }

            var elapsed = (float)Math.Max(0.0, (DateTime.UtcNow - this.StatusRemainingCountdownStartUtc).TotalSeconds);
            return Math.Max(0.0f, this.StatusRemainingInitialSeconds - elapsed);
        }
    }

    public bool IsExpired
    {
        get
        {
            if (this.IsPositionSetting)
            {
                return false;
            }

            if (this.HasStatusRemainingDisplay)
            {
                // ステータス残り時間表示つきのテキストは、通常の表示時間設定を無視します。
                // 表示開始後、残り時間が0になるまで表示し続けます。
                return this.IsReadyToDisplay && this.CurrentStatusRemainingSeconds <= 0.0f;
            }

            return DateTime.UtcNow >= this.EndTimeUtc;
        }
    }
}


public sealed class StatusRemainingDisplayState
{
    public StatusRemainingDisplayState(string statusName, float remainingSeconds, DateTime capturedAtUtc)
    {
        this.StatusName = statusName;
        this.RemainingSeconds = remainingSeconds;
        this.CapturedAtUtc = capturedAtUtc;
    }

    public string StatusName { get; }

    public float RemainingSeconds { get; }

    public DateTime CapturedAtUtc { get; }
}
