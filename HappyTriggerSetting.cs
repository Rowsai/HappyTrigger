using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HappyTrigger;

public enum TextFontDesign
{
    Normal = 0,
    Bold = 1,
    Shadow = 2,
    StrongOutline = 3,
    Neon = 4,
}

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

    // トリガー管理や一覧で表示する任意名称です。空欄の場合は「名称未設定」と表示します。
    public string TriggerName { get; set; } = string.Empty;

    // トリガー管理で所属するトリガーボックスIDです。未設定の場合は未分類扱いです。
    public string TriggerBoxId { get; set; } = string.Empty;

    // トリガー管理で所属するトリガーラベルIDです。未設定の場合は未分類扱いです。
    public string TriggerLabelId { get; set; } = string.Empty;

    // true の場合、所属トリガーラベルの基準座標からID順に縦並び表示します。
    // false の場合、このログトリガー自身の PositionX / PositionY で単独表示します。
    public bool UseTriggerLabelPosition { get; set; } = true;

    public string Keyword { get; set; } = string.Empty;

    public bool ExactMatch { get; set; } = false;

    // true の場合、チャットログのトリガー文字ではなく、FFXIV Logタブに出力されたログを判定対象にします。
    public bool UseFfxivLogReference { get; set; } = false;

    // UseFfxivLogReference=true の場合に、指定した前提条件トリガーが表示中であることを要求するかどうかです。
    // true の場合、PrerequisiteTriggerId のトリガーが表示時間中の間にログ条件が揃った場合だけ発火します。
    public bool UsePrerequisite { get; set; } = false;

    // UsePrerequisite=true の場合に、前提条件として扱う保存済みトリガーIDです。
    public string PrerequisiteTriggerId { get; set; } = string.Empty;


    // FFXIV Log参照用トリガーで、表示テキストの末尾にステータス残り時間を付与するかどうかです。
    public bool EnableStatusRemainingAppend { get; set; } = false;

    // EnableStatusRemainingAppend=true の場合に参照するジョブです。例: PLD / WHM / RDM。
    public string StatusRemainingJob { get; set; } = string.Empty;

    // EnableStatusRemainingAppend=true の場合に参照するステータス名です。例: 水属性圧縮。
    public string StatusRemainingStatusName { get; set; } = string.Empty;

    // trueの場合、同じステータス名がすでに表示中でも、別メンバーに付与された同名ステータスとして同時表示を許可します。
    public bool AllowDuplicateStatusRemainingDisplay { get; set; } = false;

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

    // DisplayTextMode=true の場合に、表示対象テキストをVOICEVOXで読み上げるかどうかです。
    public bool EnableVoiceVox { get; set; } = false;

    // VOICEVOX Engine の接続先です。通常は http://127.0.0.1:50021 です。
    public string VoiceVoxEndpoint { get; set; } = "http://127.0.0.1:50021";

    // VOICEVOX の話者IDです。
    public int VoiceVoxSpeakerId { get; set; } = 3;

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

    // 表示テキストのフォントデザインです。
    // Normal=標準、Bold=太字、Shadow=影付き、StrongOutline=黒縁強調、Neon=ネオン風。
    public TextFontDesign TextFontDesign { get; set; } = TextFontDesign.Normal;

    // true の場合、描画位置を整数座標に丸めて文字のにじみを抑えます。
    public bool EnableTextPixelSnap { get; set; } = true;

    // true の場合、大きい文字サイズでも見やすいように、Windowsの文字描画でテクスチャ化して表示します。
    // 秒数表示つきテキストでは、表示幅を固定してブレを抑えます。
    public bool EnableTextSharpRendering { get; set; } = true;

    // くっきり表示で使用するWindowsフォント名です。
    // 例: Meiryo / Yu Gothic UI / BIZ UDPGothic / Noto Sans JP。
    public string TextFontFamilyName { get; set; } = "Meiryo";

    // 任意の .ttf / .otf フォントファイルを指定する場合に使用します。
    // 空欄の場合は TextFontFamilyName で指定したインストール済みフォントを使用します。
    public string CustomTextFontPath { get; set; } = string.Empty;

    public float TextShadowOffsetX { get; set; } = 3.0f;

    public float TextShadowOffsetY { get; set; } = 3.0f;

    public float TextShadowColorR { get; set; } = 0.0f;

    public float TextShadowColorG { get; set; } = 0.0f;

    public float TextShadowColorB { get; set; } = 0.0f;

    public float TextShadowColorA { get; set; } = 0.70f;

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
            TriggerName = this.TriggerName,
            TriggerBoxId = this.TriggerBoxId,
            TriggerLabelId = this.TriggerLabelId,
            UseTriggerLabelPosition = this.UseTriggerLabelPosition,
            Keyword = this.Keyword,
            ExactMatch = this.ExactMatch,
            UseFfxivLogReference = this.UseFfxivLogReference,
            UsePrerequisite = this.UsePrerequisite,
            PrerequisiteTriggerId = this.PrerequisiteTriggerId,
            EnableStatusRemainingAppend = this.EnableStatusRemainingAppend,
            StatusRemainingJob = this.StatusRemainingJob,
            StatusRemainingStatusName = this.StatusRemainingStatusName,
            AllowDuplicateStatusRemainingDisplay = this.AllowDuplicateStatusRemainingDisplay,
            BattleLogKeyword = this.BattleLogKeyword,
            InternalLogKeyword = this.InternalLogKeyword,
            InternalLogKeywords = this.GetInternalLogKeywords().ToList(),
            DisplayTextMode = this.DisplayTextMode,
            DisplayText = this.DisplayText,
            EnableVoiceVox = this.EnableVoiceVox,
            VoiceVoxEndpoint = this.VoiceVoxEndpoint,
            VoiceVoxSpeakerId = this.VoiceVoxSpeakerId,
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
            TextFontDesign = this.TextFontDesign,
            EnableTextPixelSnap = this.EnableTextPixelSnap,
            EnableTextSharpRendering = this.EnableTextSharpRendering,
            TextFontFamilyName = this.TextFontFamilyName,
            CustomTextFontPath = this.CustomTextFontPath,
            TextShadowOffsetX = this.TextShadowOffsetX,
            TextShadowOffsetY = this.TextShadowOffsetY,
            TextShadowColorR = this.TextShadowColorR,
            TextShadowColorG = this.TextShadowColorG,
            TextShadowColorB = this.TextShadowColorB,
            TextShadowColorA = this.TextShadowColorA,
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

    public static bool IsValidManualTriggerId(string? triggerId)
    {
        if (string.IsNullOrWhiteSpace(triggerId))
        {
            return false;
        }

        var trimmedId = triggerId.Trim();
        if (trimmedId.Length == 0 || trimmedId.Length > 6)
        {
            return false;
        }

        foreach (var c in trimmedId)
        {
            var isAsciiDigit = c >= '0' && c <= '9';
            var isAsciiUpper = c >= 'A' && c <= 'Z';
            var isAsciiLower = c >= 'a' && c <= 'z';
            if (!isAsciiDigit && !isAsciiUpper && !isAsciiLower)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsValidFfxivLogTriggerId(string? triggerId)
    {
        return IsValidTriggerId(triggerId, "F")
            || IsValidTriggerId(triggerId, "X")
            || IsValidManualTriggerId(triggerId);
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

    public static bool TryGetManagementIdNumber(string? id, string prefix, out int number)
    {
        number = 0;

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(prefix))
        {
            return false;
        }

        var trimmedId = id.Trim();
        if (!trimmedId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var numberPart = trimmedId[prefix.Length..];
        if (numberPart.Length != 3)
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

    public static string FormatManagementId(string prefix, int number)
    {
        return $"{prefix}{Math.Max(1, number):D3}";
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

    public bool HasStatusRemainingAppendSetting()
    {
        return this.UseFfxivLogReference
            && this.DisplayTextMode
            && this.EnableStatusRemainingAppend
            && !string.IsNullOrWhiteSpace(this.StatusRemainingJob)
            && !string.IsNullOrWhiteSpace(this.StatusRemainingStatusName);
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
        // ログ側・条件側の両方から [19:42:52] のような先頭タイムスタンプを外した候補も作ります。
        // そのため、条件にタイムスタンプが混ざっていても、実ログ側の時刻に依存せず一致できます。
        // また、[Chat] / [HappyTrigger] などのカテゴリ付き・カテゴリなしのどちらでも一致できるようにします。
        var targetCandidates = GetLogTextMatchCandidates(logEntry).ToList();
        var conditionCandidates = GetLogConditionMatchCandidates(conditionText).ToList();

        foreach (var targetCandidate in targetCandidates)
        {
            foreach (var conditionCandidate in conditionCandidates)
            {
                if (this.IsTextMatched(targetCandidate, conditionCandidate))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> GetLogTextMatchCandidates(FfxivLogEntry logEntry)
    {
        foreach (var candidate in ExpandLogMatchCandidates(logEntry.Text))
        {
            yield return candidate;
        }

        foreach (var candidate in ExpandLogMatchCandidates(logEntry.DisplayText))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<string> GetLogConditionMatchCandidates(string conditionText)
    {
        foreach (var candidate in ExpandLogMatchCandidates(conditionText))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<string> ExpandLogMatchCandidates(string value)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var candidates = new List<string>();

        void Add(string candidate)
        {
            candidate = (candidate ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
            {
                candidates.Add(candidate);
            }
        }

        var raw = (value ?? string.Empty).Trim();
        var withoutTimestamp = StripLeadingTimestamp(raw);
        var withoutCategory = StripLeadingCategory(withoutTimestamp);

        Add(raw);
        Add(withoutTimestamp);
        Add(withoutCategory);

        foreach (var candidate in candidates)
        {
            yield return candidate;
        }
    }

    private static string StripLeadingTimestamp(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();

        // [HH:mm:ss] の10文字を想定します。
        if (trimmed.Length >= 10 &&
            trimmed[0] == '[' &&
            char.IsDigit(trimmed[1]) &&
            char.IsDigit(trimmed[2]) &&
            trimmed[3] == ':' &&
            char.IsDigit(trimmed[4]) &&
            char.IsDigit(trimmed[5]) &&
            trimmed[6] == ':' &&
            char.IsDigit(trimmed[7]) &&
            char.IsDigit(trimmed[8]) &&
            trimmed[9] == ']')
        {
            return trimmed[10..].TrimStart();
        }

        return trimmed;
    }

    private static string StripLeadingCategory(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();

        // [Chat] / [HappyTrigger] のような先頭カテゴリを外します。
        // [19:42:52] は StripLeadingTimestamp で先に外す想定です。
        if (trimmed.Length >= 3 && trimmed[0] == '[')
        {
            var closeIndex = trimmed.IndexOf(']');
            if (closeIndex > 0 && closeIndex + 1 < trimmed.Length)
            {
                return trimmed[(closeIndex + 1)..].TrimStart();
            }
        }

        return trimmed;
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

        var normalizedTargetText = NormalizeRemainingForMatch(targetText.Trim());
        var normalizedConditionText = NormalizeRemainingForMatch(conditionText.Trim());

        if (IsJobAllCondition(normalizedConditionText))
        {
            normalizedTargetText = NormalizeJobFieldForMatch(normalizedTargetText);
            normalizedConditionText = NormalizeJobFieldForMatch(normalizedConditionText);
        }

        if (this.ExactMatch)
        {
            return string.Equals(normalizedTargetText, normalizedConditionText, StringComparison.OrdinalIgnoreCase);
        }

        return normalizedTargetText.Contains(normalizedConditionText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJobAllCondition(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var jobValue = GetLogFieldValue(value, "job");
        return string.Equals(jobValue, "ALL", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeJobFieldForMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(
            value,
            @"\bjob\s*=\s*[^\s]+",
            "job=*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string GetLogFieldValue(string text, string fieldName)
    {
        var match = Regex.Match(
            text ?? string.Empty,
            $@"(?:^|\s){Regex.Escape(fieldName)}=(?<value>.*?)(?=\s[A-Za-z][A-Za-z0-9_]*=|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success ? match.Groups["value"].Value.Trim() : string.Empty;
    }

    private static string NormalizeRemainingForMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // ステータス残り時間ログは、登録時と実際の付与時で Remaining=xx.xx が必ず変動します。
        // 条件側に Remaining=75.99s のような秒数つきログをそのまま貼っていても、
        // 実ログ側の Remaining=42.99s と同じ条件として扱えるよう、秒数部分だけを判定対象から外します。
        return Regex.Replace(
            value,
            @"\bRemaining\s*=\s*[^\s]+",
            "Remaining=*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
