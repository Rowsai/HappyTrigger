using System;

namespace HappyTrigger;

public sealed class FfxivLogEntry
{
    public FfxivLogEntry(DateTime timestamp, string category, string text)
    {
        this.Timestamp = timestamp;
        this.Category = category;
        this.Text = text;
    }

    public DateTime Timestamp { get; }

    public string Category { get; }

    public string Text { get; }

    public string DisplayText => $"[{this.Timestamp:HH:mm:ss}][{this.Category}]{this.Text}";
}
