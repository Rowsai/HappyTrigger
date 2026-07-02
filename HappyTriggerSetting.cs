using System;

namespace HappyTrigger;

[Serializable]
public sealed class HappyTriggerSetting
{
    public bool Enabled { get; set; } = true;

    public string Keyword { get; set; } = string.Empty;

    public bool ExactMatch { get; set; } = false;

    // false = 画像表示用、true = テキスト表示用。
    // 既存設定は false 扱いになるため、今までのトリガーは画像表示用として残ります。
    public bool DisplayTextMode { get; set; } = false;

    // DisplayTextMode=true の場合に表示する任意テキストです。
    public string DisplayText { get; set; } = string.Empty;

    public bool IsWebImage { get; set; } = false;

    public string ImagePath { get; set; } = string.Empty;

    public float PositionX { get; set; } = 500.0f;

    public float PositionY { get; set; } = 300.0f;

    // 旧設定互換用。以前の「画像サイズ」設定値を残します。
    public float ImageSize { get; set; } = 300.0f;

    // trueの場合、読み込んだ画像の元サイズを基準に表示します。
    public bool UseOriginalImageSize { get; set; } = false;

    // UseOriginalImageSize=false の場合に使用する表示幅です。
    public float ImageWidth { get; set; } = 300.0f;

    // UseOriginalImageSize=false の場合に使用する表示高さです。
    public float ImageHeight { get; set; } = 300.0f;

    // 互換用に残します。現在は ScalePercent を常に反映します。
    public bool UsePercentScale { get; set; } = false;

    // 100.0f が等倍です。50.0f なら半分、200.0f なら2倍です。
    public float ScalePercent { get; set; } = 100.0f;



    public float TextSize { get; set; } = 32.0f;

    public float TextColorR { get; set; } = 1.0f;

    public float TextColorG { get; set; } = 1.0f;

    public float TextColorB { get; set; } = 1.0f;

    public float TextColorA { get; set; } = 1.0f;

    public bool EnableTextOutline { get; set; } = true;

    public float TextOutlineThickness { get; set; } = 2.0f;

    public float TextOutlineColorR { get; set; } = 0.0f;

    public float TextOutlineColorG { get; set; } = 0.0f;

    public float TextOutlineColorB { get; set; } = 0.0f;

    public float TextOutlineColorA { get; set; } = 1.0f;

    public bool EnableTextFadeIn { get; set; } = false;

    public float TextFadeInSeconds { get; set; } = 0.15f;

    public float DisplaySeconds { get; set; } = 3.0f;

    public HappyTriggerSetting Clone()
    {
        return new HappyTriggerSetting
        {
            Enabled = this.Enabled,
            Keyword = this.Keyword,
            ExactMatch = this.ExactMatch,
            DisplayTextMode = this.DisplayTextMode,
            DisplayText = this.DisplayText,
            IsWebImage = this.IsWebImage,
            ImagePath = this.ImagePath,
            PositionX = this.PositionX,
            PositionY = this.PositionY,
            ImageSize = this.ImageSize,
            UseOriginalImageSize = this.UseOriginalImageSize,
            ImageWidth = this.ImageWidth,
            ImageHeight = this.ImageHeight,
            UsePercentScale = this.UsePercentScale,
            ScalePercent = this.ScalePercent,

            TextSize = this.TextSize,
            TextColorR = this.TextColorR,
            TextColorG = this.TextColorG,
            TextColorB = this.TextColorB,
            TextColorA = this.TextColorA,
            EnableTextOutline = this.EnableTextOutline,
            TextOutlineThickness = this.TextOutlineThickness,
            TextOutlineColorR = this.TextOutlineColorR,
            TextOutlineColorG = this.TextOutlineColorG,
            TextOutlineColorB = this.TextOutlineColorB,
            TextOutlineColorA = this.TextOutlineColorA,
            EnableTextFadeIn = this.EnableTextFadeIn,
            TextFadeInSeconds = this.TextFadeInSeconds,
            DisplaySeconds = this.DisplaySeconds,
        };
    }

    public bool IsMatch(string chatText)
    {
        if (!this.Enabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(this.Keyword))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(chatText))
        {
            return false;
        }

        if (this.ExactMatch)
        {
            return string.Equals(chatText.Trim(), this.Keyword.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return chatText.Contains(this.Keyword, StringComparison.OrdinalIgnoreCase);
    }
}
