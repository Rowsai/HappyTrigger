using System;
using System.Collections.Generic;
using System.Linq;

namespace HappyTrigger;

[Serializable]
public sealed class HappyTriggerSetting
{
    public const string InternalLogKeywordDelimiter = "_@_";

    private static readonly string[] InternalLogKeywordDelimiters =
    {
        InternalLogKeywordDelimiter,
        "＿＠＿",
        "＿@＿",
        "_＠_",
    };

    public bool Enabled { get; set; } = true;

    // 画像表示用は I00001、テキスト表示用は T00001 の形式で採番します。
    public string TriggerId { get; set; } = string.Empty;

    public string Keyword { get; set; } = string.Empty;

    public bool ExactMatch { get; set; } = false;

    // true の場合、チャットログのトリガー文字ではなく、FFXIV Logタブに出力されたログを判定対象にします。
    public bool UseFfxivLogReference { get; set; } = false;

    // UseFfxivLogReference=true の場合に、指定した前提条件トリガーが表示中であることを要求するかどうかです。
    // true の場合、PrerequisiteTriggerId のトリガーが表示時間中の間にログ条件が揃った場合だけ発火します。
    public bool UsePrerequisite { get; set; } = false;

    // UsePrerequisite=true の場合に、前提条件として扱う保存済みトリガーIDです。
    public string PrerequisiteTriggerId { get; set; } = string.Empty;

    // UseFfxivLogReference=true の場合に、バトルログ側で判定する文字列です。
    public string BattleLogKeyword { get; set; } = string.Empty;

    // UseFfxivLogReference=true の場合に、内部ログ側で判定する文字列です。
    // 旧設定互換用です。新規設定は InternalLogKeywords を使用します。
    public string InternalLogKeyword { get; set; } = string.Empty;

    // UseFfxivLogReference=true の場合に、内部ログ側で判定する文字列リストです。
    // 複数設定されている場合は、すべての内部ログ条件が揃った場合に発火します。
    public List<string> InternalLogKeywords { get; set; } = new();

    // false = 画像表示用、true = テキスト表示用。
    // 既存設定は false 扱いになるため、今までのトリガーは画像表示用として残ります。
    public bool DisplayTextMode { get; set; } = false;

    // DisplayTextMode=true の場合に表示する任意テキストです。
    public string DisplayText { get; set; } = string.Empty;

    public bool IsWebImage { get; set; } = false;

    public string ImagePath { get; set; } = string.Empty;

    public float PositionX { get; set; } = 500.0f;

    public float PositionY { get; set; } = 300.0f;

    // トリガー文字を検知してから実際に表示するまでの待機時間です。
    // 0.0f の場合は即時表示します。
    public float WaitSeconds { get; set; } = 0.0f;

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
            TriggerId = this.TriggerId,
            Keyword = this.Keyword,
            ExactMatch = this.ExactMatch,
            UseFfxivLogReference = this.UseFfxivLogReference,
            UsePrerequisite = this.UsePrerequisite,
            PrerequisiteTriggerId = this.PrerequisiteTriggerId,
            BattleLogKeyword = this.BattleLogKeyword,
            InternalLogKeyword = this.InternalLogKeyword,
            InternalLogKeywords = this.GetInternalLogKeywords().ToList(),
            DisplayTextMode = this.DisplayTextMode,
            DisplayText = this.DisplayText,
            IsWebImage = this.IsWebImage,
            ImagePath = this.ImagePath,
            PositionX = this.PositionX,
            PositionY = this.PositionY,
            WaitSeconds = this.WaitSeconds,
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

    public static bool IsValidTriggerId(string? triggerId, string prefix)
    {
        return TryGetTriggerIdNumber(triggerId, prefix, out _);
    }

    public static bool TryGetTriggerIdNumber(string? triggerId, string prefix, out int number)
    {
        number = 0;

        if (string.IsNullOrWhiteSpace(triggerId) || string.IsNullOrWhiteSpace(prefix))
        {
            return false;
        }

        var trimmedId = triggerId.Trim();
        if (!trimmedId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var numberPart = trimmedId[prefix.Length..];
        if (numberPart.Length != 5)
        {
            return false;
        }

        foreach (var c in numberPart)
        {
            if (!char.IsDigit(c))
            {
                return false;
            }
        }

        return int.TryParse(numberPart, out number) && number > 0;
    }

    public static string FormatTriggerId(string prefix, int number)
    {
        return $"{prefix}{Math.Max(1, number):D5}";
    }

    public bool IsMatch(string chatText)
    {
        if (!this.Enabled || this.UseFfxivLogReference)
        {
            return false;
        }

        return this.IsTextMatched(chatText, this.Keyword);
    }

    public bool IsBattleLogReferenceMatch(FfxivLogEntry logEntry)
    {
        if (!this.Enabled || !this.UseFfxivLogReference)
        {
            return false;
        }

        return this.IsLogTextMatched(logEntry, this.BattleLogKeyword);
    }

    public IReadOnlyList<string> GetInternalLogKeywords()
    {
        var results = new List<string>();

        if (this.InternalLogKeywords != null)
        {
            foreach (var keyword in this.InternalLogKeywords)
            {
                AddInternalLogKeywordParts(results, keyword);
            }
        }

        AddInternalLogKeywordParts(results, this.InternalLogKeyword);

        return results;
    }

    public string GetInternalLogKeywordText()
    {
        return string.Join(InternalLogKeywordDelimiter, this.GetInternalLogKeywords());
    }

    public void SetInternalLogKeywordText(string value)
    {
        var results = new List<string>();
        AddInternalLogKeywordParts(results, value);

        this.InternalLogKeywords = results;
        this.InternalLogKeyword = string.Join(InternalLogKeywordDelimiter, results);
    }

    public void NormalizeInternalLogKeywords()
    {
        var keywords = this.GetInternalLogKeywords().ToList();
        this.InternalLogKeywords = keywords;
        this.InternalLogKeyword = string.Join(InternalLogKeywordDelimiter, keywords);
    }

    private static void AddInternalLogKeywordParts(List<string> results, string? keywordText)
    {
        if (string.IsNullOrWhiteSpace(keywordText))
        {
            return;
        }

        var parts = keywordText.Split(InternalLogKeywordDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            var trimmed = part.Trim();
            if (!results.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                results.Add(trimmed);
            }
        }
    }

    public bool IsInternalLogReferenceMatch(FfxivLogEntry logEntry)
    {
        if (!this.Enabled || !this.UseFfxivLogReference)
        {
            return false;
        }

        foreach (var internalLogKeyword in this.GetInternalLogKeywords())
        {
            if (this.IsLogTextMatched(logEntry, internalLogKeyword))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsInternalLogReferenceMatch(FfxivLogEntry logEntry, int internalLogKeywordIndex)
    {
        if (!this.Enabled || !this.UseFfxivLogReference)
        {
            return false;
        }

        var internalLogKeywords = this.GetInternalLogKeywords();
        if (internalLogKeywordIndex < 0 || internalLogKeywordIndex >= internalLogKeywords.Count)
        {
            return false;
        }

        return this.IsLogTextMatched(logEntry, internalLogKeywords[internalLogKeywordIndex]);
    }

    private bool IsLogTextMatched(FfxivLogEntry logEntry, string conditionText)
    {
        if (string.IsNullOrWhiteSpace(conditionText))
        {
            return false;
        }

        // FFXIV Logタブ上の表示文字列でも、ログ本文だけでもヒットできるようにします。
        // 完全一致の場合はログ本文の完全一致が主用途です。
        return this.IsTextMatched(logEntry.Text, conditionText)
            || this.IsTextMatched(logEntry.DisplayText, conditionText);
    }

    private bool IsTextMatched(string targetText, string conditionText)
    {
        if (string.IsNullOrWhiteSpace(conditionText))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetText))
        {
            return false;
        }

        if (this.ExactMatch)
        {
            return string.Equals(targetText.Trim(), conditionText.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return targetText.Contains(conditionText, StringComparison.OrdinalIgnoreCase);
    }
}
