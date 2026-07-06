using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace HappyTrigger;

public sealed class HappyTriggerWindow : Window
{
    private enum TriggerListKind
    {
        Image,
        Text,
        FfxivLog,
    }

    private const int StyleColorCount = 12;
    private const int MaxVisibleLogRows = 30;

    private static readonly VoiceVoxSpeakerOption[] VoiceVoxSpeakerOptions =
    {
        new(3, "ずんだもん - ノーマル (3)"),
        new(1, "ずんだもん - あまあま (1)"),
        new(5, "ずんだもん - セクシー (5)"),
        new(7, "ずんだもん - ツンツン (7)"),
        new(22, "ずんだもん - ささやき (22)"),
        new(38, "ずんだもん - ヒソヒソ (38)"),
        new(2, "四国めたん - ノーマル (2)"),
        new(0, "四国めたん - あまあま (0)"),
        new(4, "四国めたん - セクシー (4)"),
        new(6, "四国めたん - ツンツン (6)"),
        new(36, "四国めたん - ささやき (36)"),
        new(37, "四国めたん - ヒソヒソ (37)"),
        new(8, "春日部つむぎ - ノーマル (8)"),
        new(10, "雨晴はう - ノーマル (10)"),
        new(9, "波音リツ - ノーマル (9)"),
        new(14, "冥鳴ひまり - ノーマル (14)"),
        new(13, "青山龍星 - ノーマル (13)"),
        new(11, "玄野武宏 - ノーマル (11)"),
        new(12, "白上虎太郎 - ノーマル (12)"),
        new(15, "九州そら - あまあま (15)"),
        new(16, "九州そら - ノーマル (16)"),
        new(17, "九州そら - セクシー (17)"),
        new(18, "九州そら - ツンツン (18)"),
        new(19, "九州そら - ささやき (19)"),
        new(20, "もち子さん - ノーマル (20)"),
        new(21, "剣崎雌雄 - ノーマル (21)"),
        new(23, "WhiteCUL - ノーマル (23)"),
        new(24, "WhiteCUL - たのしい (24)"),
        new(25, "WhiteCUL - かなしい (25)"),
        new(26, "WhiteCUL - びえーん (26)"),
        new(29, "No.7 - ノーマル (29)"),
        new(30, "No.7 - アナウンス (30)"),
        new(31, "No.7 - 読み聞かせ (31)"),
        new(46, "小夜/SAYO - ノーマル (46)"),
        new(47, "ナースロボ＿タイプＴ - ノーマル (47)"),
    };

    private sealed record VoiceVoxSpeakerOption(int Id, string Label);

    private readonly Configuration configuration;
    private readonly Action saveConfig;
    private readonly Action<HappyTriggerSetting> testTrigger;
    private readonly Action<HappyTriggerSetting> positionSettingTrigger;
    private readonly Action closePositionSettingPopup;
    private readonly Func<IReadOnlyList<FfxivLogEntry>> getBattleLogs;
    private readonly Func<IReadOnlyList<FfxivLogEntry>> getInternalLogs;
    private readonly Action clearFfxivLogs;

    private bool autoScrollBattleLog = true;
    private bool autoScrollInternalLog = true;
    private string battleLogSearchText = string.Empty;
    private string internalLogSearchText = string.Empty;
    private int selectedBattleLogIndex = -1;
    private int selectedInternalLogIndex = -1;
    private bool requestOpenTriggerEditTab = false;
    private string newTriggerBoxName = string.Empty;
    private string newTriggerLabelName = string.Empty;
    private string selectedManagementBoxId = string.Empty;
    private string selectedManagementLabelBoxId = string.Empty;
    private string selectedManagementTriggerKey = string.Empty;
    private string triggerBoxExportMessage = string.Empty;
    private string importCsvPath = string.Empty;
    private string triggerImportMessage = string.Empty;
    private string manualTriggerIdEditKey = string.Empty;
    private string manualTriggerIdInput = string.Empty;
    private string manualTriggerIdMessage = string.Empty;

    private HappyTriggerSetting editTrigger = new();
    private int editingIndex = -1;
    private TriggerListKind editingKind = TriggerListKind.Image;
    private string imageSelectMessage = string.Empty;

    public HappyTriggerWindow(
        Configuration configuration,
        Action saveConfig,
        Action<HappyTriggerSetting> testTrigger,
        Action<HappyTriggerSetting> positionSettingTrigger,
        Action closePositionSettingPopup,
        Func<IReadOnlyList<FfxivLogEntry>> getBattleLogs,
        Func<IReadOnlyList<FfxivLogEntry>> getInternalLogs,
        Action clearFfxivLogs)
        : base("HappyTrigger###HappyTriggerConfigWindow")
    {
        this.configuration = configuration;
        this.saveConfig = saveConfig;
        this.testTrigger = testTrigger;
        this.positionSettingTrigger = positionSettingTrigger;
        this.closePositionSettingPopup = closePositionSettingPopup;
        this.getBattleLogs = getBattleLogs;
        this.getInternalLogs = getInternalLogs;
        this.clearFfxivLogs = clearFfxivLogs;

        this.Size = new Vector2(1050, 700);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        // ウィンドウ右上の折りたたみ（最小化）ボタンを表示するため、NoCollapse は指定しません。
        this.Flags = ImGuiWindowFlags.None;
    }

    public override void PreDraw()
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.02f, 0.10f, 0.04f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(0.00f, 0.35f, 0.12f, 1.00f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(0.00f, 0.65f, 0.24f, 1.00f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, new Vector4(0.00f, 0.25f, 0.10f, 1.00f));
        ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.00f, 0.45f, 0.16f, 1.00f));
        ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.00f, 0.78f, 0.28f, 1.00f));
        ImGui.PushStyleColor(ImGuiCol.TabActive, new Vector4(0.00f, 0.62f, 0.22f, 1.00f));
        ImGui.PushStyleColor(ImGuiCol.TabUnfocused, new Vector4(0.00f, 0.32f, 0.12f, 1.00f));
        ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, new Vector4(0.00f, 0.48f, 0.17f, 1.00f));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.00f, 0.45f, 0.16f, 1.00f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.00f, 0.62f, 0.22f, 1.00f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0.00f, 0.78f, 0.28f, 1.00f));
    }

    public override void PostDraw()
    {
        ImGui.PopStyleColor(StyleColorCount);
    }

    public override void Draw()
    {
        this.DrawHeader();

        if (ImGui.BeginTabBar("HappyTriggerTabBar"))
        {
            var triggerEditTabFlags = this.requestOpenTriggerEditTab
                ? ImGuiTabItemFlags.SetSelected
                : ImGuiTabItemFlags.None;

            if (ImGui.BeginTabItem("ログトリガー", triggerEditTabFlags))
            {
                this.requestOpenTriggerEditTab = false;
                this.DrawTriggerEditTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("トリガー一覧"))
            {
                this.DrawTriggerListTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("トリガー管理"))
            {
                this.DrawTriggerManagementTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("FFXIV Log"))
            {
                this.DrawFfxivLogTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawHeader()
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        // const float height = 36.0f;
        var color = ImGui.GetColorU32(new Vector4(0.0f, 0.65f, 0.24f, 1.0f));

        // drawList.AddRectFilled(pos, new Vector2(pos.X + width, pos.Y + height), color);

        // ImGui.SetCursorScreenPos(new Vector2(pos.X + 8.0f, pos.Y + 7.0f));
        // ImGui.Text("HappyTrigger");
        // ImGui.SetCursorScreenPos(new Vector2(pos.X, pos.Y + height + 8.0f));
    }

    private void DrawTriggerEditTab()
    {
        ImGui.Spacing();
        ImGui.Text("基本設定");
        ImGui.Spacing();
        ImGui.Text("チャットログに表示される文言を設定してください。");
        ImGui.Spacing();

        var enabled = this.editTrigger.Enabled;
        if (ImGui.Checkbox("有効", ref enabled))
        {
            this.editTrigger.Enabled = enabled;
        }

        var exactMatch = this.editTrigger.ExactMatch;
        if (ImGui.Checkbox("完全一致", ref exactMatch))
        {
            this.editTrigger.ExactMatch = exactMatch;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("OFFの場合は部分一致で判定します。");

        var useFfxivLogReference = this.editTrigger.UseFfxivLogReference;
        if (ImGui.Checkbox("FFXIV Logを参照", ref useFfxivLogReference))
        {
            this.editTrigger.UseFfxivLogReference = useFfxivLogReference;
        }

        if (this.editTrigger.UseFfxivLogReference)
        {
            this.DrawFfxivLogReferenceSettingArea();
        }
        else
        {
            var keyword = this.editTrigger.Keyword ?? string.Empty;
            ImGui.SetNextItemWidth(700.0f);
            if (InputTextJapanese("トリガー文字", ref keyword, 512))
            {
                this.editTrigger.Keyword = keyword;
            }
        }

        var triggerIdText = string.IsNullOrWhiteSpace(this.editTrigger.TriggerId)
            ? "ID: 未採番（保存時に自動採番）"
            : $"ID: {this.editTrigger.TriggerId}";
        ImGui.TextDisabled(triggerIdText);

        var triggerName = RemoveLineBreaks(this.editTrigger.TriggerName ?? string.Empty);
        ImGui.SetNextItemWidth(420.0f);
        if (InputTextJapanese("トリガー名", ref triggerName, 256))
        {
            this.editTrigger.TriggerName = RemoveLineBreaks(triggerName);
        }
        ImGui.SameLine();
        ImGui.TextDisabled("空欄の場合は『名称未設定』として表示します。名称は重複しても問題ありません。");

        ImGui.Spacing();
        ImGui.Text("詳細設定");

        var x = this.editTrigger.PositionX;
        ImGui.SetNextItemWidth(180.0f);
        if (ImGui.InputFloat("画面位置 X", ref x, 1.0f, 10.0f))
        {
            this.editTrigger.PositionX = x;
        }

        var y = this.editTrigger.PositionY;
        ImGui.SetNextItemWidth(180.0f);
        if (ImGui.InputFloat("画面位置 Y", ref y, 1.0f, 10.0f))
        {
            this.editTrigger.PositionY = y;
        }

        var waitSeconds = this.editTrigger.WaitSeconds;
        ImGui.SetNextItemWidth(180.0f);
        if (ImGui.InputFloat("待機時間", ref waitSeconds, 0.1f, 1.0f))
        {
            this.editTrigger.WaitSeconds = Math.Clamp(waitSeconds, 0.0f, 600.0f);
        }

        ImGui.SameLine();
        ImGui.TextDisabled("トリガー文字を検知してから表示するまでの秒数です。0秒なら即時表示します。");

        ImGui.Spacing();
        ImGui.Text("管理設定");
        this.DrawTriggerManagementAssignmentArea();

        ImGui.Spacing();
        ImGui.Text("表示項目設定");

        var displayMode = this.editTrigger.DisplayTextMode ? 1 : 0;
        if (ImGui.RadioButton("トリガーで画像を表示", displayMode == 0))
        {
            displayMode = 0;
            this.editTrigger.DisplayTextMode = false;
        }

        if (ImGui.RadioButton("トリガーでテキストを表示", displayMode == 1))
        {
            displayMode = 1;
            this.editTrigger.DisplayTextMode = true;
        }

        ImGui.Spacing();

        if (displayMode == 1)
        {
            this.DrawTextSettingArea();
        }
        else
        {
            this.DrawImageSettingArea();
        }

        ImGui.Spacing();
        ImGui.Spacing();

        if (ImGui.Button("テスト", new Vector2(180.0f, 40.0f)))
        {
            this.testTrigger(this.editTrigger);
        }

        ImGui.SameLine();

        var positionButtonLabel = this.editTrigger.DisplayTextMode
            ? "テキスト表示位置をマウスで設定"
            : "画像表示位置をマウスで設定";

        if (ImGui.Button(positionButtonLabel, new Vector2(300.0f, 40.0f)))
        {
            this.positionSettingTrigger(this.editTrigger);
        }

        ImGui.SameLine();

        if (ImGui.Button("保存", new Vector2(180.0f, 40.0f)))
        {
            this.SaveEditingTrigger();
        }

        ImGui.SameLine();

        if (ImGui.Button("新規入力に戻す", new Vector2(180.0f, 40.0f)))
        {
            this.ResetEditing();
        }

        if (this.editingIndex >= 0)
        {
            ImGui.SameLine();
            var listName = this.editingKind switch
            {
                TriggerListKind.FfxivLog => "FFXIV Log参照用",
                TriggerListKind.Text => "テキスト表示用",
                _ => "画像表示用",
            };
            ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.3f, 1.0f), $"編集中: {listName} {this.editingIndex + 1}");
        }
    }

    private void DrawFfxivLogReferenceSettingArea()
    {
        var usePrerequisite = this.editTrigger.UsePrerequisite;
        if (ImGui.Checkbox("前提条件を使用する", ref usePrerequisite))
        {
            this.editTrigger.UsePrerequisite = usePrerequisite;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("ONの場合、指定した前提条件トリガーが表示時間中の間にログ条件が揃った場合だけ発火します。");

        if (this.editTrigger.UsePrerequisite)
        {
            this.DrawPrerequisiteTriggerCombo();
        }
        else
        {
            this.editTrigger.PrerequisiteTriggerId = string.Empty;
        }

        var battleLogKeyword = RemoveLineBreaks(this.editTrigger.BattleLogKeyword ?? string.Empty);
        ImGui.SetNextItemWidth(700.0f);
        if (InputTextJapanese("バトルログ", ref battleLogKeyword, 2048))
        {
            this.editTrigger.BattleLogKeyword = RemoveLineBreaks(battleLogKeyword);
        }

        var internalLogKeywords = this.GetEditingInternalLogKeywordsForUi();

        for (var i = 0; i < internalLogKeywords.Count; i++)
        {
            var internalLogKeyword = RemoveLineBreaks(internalLogKeywords[i] ?? string.Empty);
            ImGui.SetNextItemWidth(1300.0f);
            if (InputTextJapanese($"内部ログ {i + 1}", ref internalLogKeyword, 2048))
            {
                internalLogKeywords[i] = RemoveLineBreaks(internalLogKeyword);
                this.SetEditingInternalLogKeywords(internalLogKeywords);
            }

            if (internalLogKeywords.Count > 1)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton($"削除##internal_log_keyword_delete_{i}"))
                {
                    internalLogKeywords.RemoveAt(i);
                    this.SetEditingInternalLogKeywords(internalLogKeywords);
                    i--;
                }
            }
        }

        if (ImGui.Button("内部ログを追加", new Vector2(180.0f, 28.0f)))
        {
            internalLogKeywords.Add(string.Empty);
            this.SetEditingInternalLogKeywords(internalLogKeywords);
        }

        ImGui.Spacing();
        var enableStatusRemainingAppend = this.editTrigger.EnableStatusRemainingAppend;
        if (ImGui.Checkbox("ステータスの残り時間を取得する", ref enableStatusRemainingAppend))
        {
            this.editTrigger.EnableStatusRemainingAppend = enableStatusRemainingAppend;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("ONの場合、指定したジョブ/ステータス名の残り時間を表示テキスト末尾に付与します。");

        if (this.editTrigger.EnableStatusRemainingAppend)
        {
            var statusJob = RemoveLineBreaks(this.editTrigger.StatusRemainingJob ?? string.Empty);
            ImGui.SetNextItemWidth(180.0f);
            if (InputTextJapanese("ジョブ", ref statusJob, 64))
            {
                this.editTrigger.StatusRemainingJob = RemoveLineBreaks(statusJob).Trim();
            }

            ImGui.SameLine();
            ImGui.TextDisabled("例: PLD / 複数指定: <PLD|DRK|WAR>");
            ImGui.TextDisabled("Tips: 複数ジョブを対象にする場合は <PLD|DRK|WAR> のように | 区切りで指定します。");

            var statusName = RemoveLineBreaks(this.editTrigger.StatusRemainingStatusName ?? string.Empty);
            ImGui.SetNextItemWidth(360.0f);
            if (InputTextJapanese("ステータス名", ref statusName, 256))
            {
                this.editTrigger.StatusRemainingStatusName = RemoveLineBreaks(statusName).Trim();
            }

            ImGui.TextDisabled("例: job=PLD StatusName=水属性圧縮 のログがある場合、表示テキスト末尾に 水属性圧縮（75.99s） のように表示し、表示開始後にカウントダウンします。");
            ImGui.TextDisabled("例: ジョブ=<PLD|DRK|WAR> の場合、PLD/DRK/WAR のいずれかの MemberStatus ログでマッチします。");
        }

        ImGui.TextDisabled("保存時は 内部ログ1_@_内部ログ2_@_内部ログn の形式で保持します。編集時は _@_ で分割して各入力欄に戻します。");
        ImGui.TextDisabled("全角で ＿＠＿ と保存済みの場合も同じ区切り文字として扱います。入力欄内の改行は自動で除去します。");
        ImGui.TextDisabled("バトルログのみ、内部ログのみでも動作します。バトルログと内部ログを両方設定した場合は、すべての条件が揃った場合に発火します。");
    }

    private List<string> GetEditingInternalLogKeywordsForUi()
    {
        // UI編集中は空欄も保持します。
        // GetInternalLogKeywords() は空文字を除外するため、「内部ログを追加」で増やした空欄が次フレームで消えてしまいます。
        var internalLogKeywords = this.editTrigger.InternalLogKeywords?
            .Select(keyword => RemoveLineBreaks(keyword ?? string.Empty))
            .ToList()
            ?? new List<string>();

        if (internalLogKeywords.Count == 0)
        {
            internalLogKeywords = this.editTrigger.GetInternalLogKeywords()
                .Select(keyword => RemoveLineBreaks(keyword ?? string.Empty))
                .ToList();
        }

        if (internalLogKeywords.Count == 0)
        {
            internalLogKeywords.Add(string.Empty);
        }

        return internalLogKeywords;
    }

    private void SetEditingInternalLogKeywords(IReadOnlyList<string> internalLogKeywords)
    {
        var normalizedKeywords = internalLogKeywords
            .Select(keyword => RemoveLineBreaks(keyword ?? string.Empty).Trim())
            .ToList();

        this.editTrigger.InternalLogKeywords = normalizedKeywords;
        this.editTrigger.InternalLogKeyword = string.Join(
            HappyTriggerSetting.InternalLogKeywordDelimiter,
            normalizedKeywords.Where(keyword => !string.IsNullOrWhiteSpace(keyword)));
    }

    private static bool InputTextJapanese(string label, ref string value, int maxLength)
    {
        // Dalamud.Bindings.ImGui の string overload は第3引数を UTF-8 バイト長として扱うため、
        // 日本語のようなマルチバイト文字では指定値そのままだと入力できない/途中で欠けることがあります。
        // UI側で指定している長さは「文字数の上限」として扱い、UTF-8最大4byte分のバッファを確保します。
        var utf8BufferSize = Math.Max(maxLength * 4 + 1, 256);
        return ImGui.InputText(label, ref value, utf8BufferSize);
    }

    private static string RemoveLineBreaks(string value)
    {
        return value.Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

    private void DrawPrerequisiteTriggerCombo()
    {
        var availableTriggerIds = this.GetAvailablePrerequisiteTriggerIds();
        var selectedId = this.editTrigger.PrerequisiteTriggerId ?? string.Empty;

        if (availableTriggerIds.Count == 0)
        {
            this.editTrigger.PrerequisiteTriggerId = string.Empty;
            ImGui.TextDisabled("前提条件に指定できる保存済みトリガーIDがありません。");
            return;
        }

        if (string.IsNullOrWhiteSpace(selectedId) || !availableTriggerIds.Contains(selectedId, StringComparer.OrdinalIgnoreCase))
        {
            selectedId = availableTriggerIds[0];
            this.editTrigger.PrerequisiteTriggerId = selectedId;
        }

        ImGui.SetNextItemWidth(220.0f);
        if (ImGui.BeginCombo("前提条件トリガー", selectedId))
        {
            foreach (var triggerId in availableTriggerIds)
            {
                var isSelected = string.Equals(selectedId, triggerId, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(triggerId, isSelected))
                {
                    selectedId = triggerId;
                    this.editTrigger.PrerequisiteTriggerId = triggerId;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("選択したIDの表示時間中だけ、このFFXIV Log参照トリガーを判定します。");
    }

    private List<string> GetAvailablePrerequisiteTriggerIds()
    {
        var currentId = this.editTrigger.TriggerId ?? string.Empty;
        var ids = new List<string>();

        void AddId(HappyTriggerSetting trigger)
        {
            if (string.IsNullOrWhiteSpace(trigger.TriggerId))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(currentId) &&
                string.Equals(trigger.TriggerId, currentId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (ids.Contains(trigger.TriggerId, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            ids.Add(trigger.TriggerId);
        }

        foreach (var trigger in this.configuration.Triggers)
        {
            AddId(trigger);
        }

        foreach (var trigger in this.configuration.TextTriggers)
        {
            AddId(trigger);
        }

        foreach (var trigger in this.configuration.FfxivLogTriggers)
        {
            AddId(trigger);
        }

        return ids
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void DrawTextSettingArea()
    {
        ImGui.Text("テキスト項目設定 ※トリガーでテキストを表示を選択した場合");

        var displayText = this.editTrigger.DisplayText ?? string.Empty;
        ImGui.SetNextItemWidth(700.0f);
        if (InputTextJapanese("表示対象テキスト", ref displayText, 1024))
        {
            this.editTrigger.DisplayText = displayText;
        }

        ImGui.Spacing();
        var enableVoiceVox = this.editTrigger.EnableVoiceVox;
        if (ImGui.Checkbox("VOICEVOXで読み上げる", ref enableVoiceVox))
        {
            this.editTrigger.EnableVoiceVox = enableVoiceVox;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("ONの場合、表示対象テキストをVOICEVOX Engineで読み上げます。");

        if (this.editTrigger.EnableVoiceVox)
        {
            var endpoint = string.IsNullOrWhiteSpace(this.editTrigger.VoiceVoxEndpoint)
                ? "http://127.0.0.1:50021"
                : this.editTrigger.VoiceVoxEndpoint;
            ImGui.SetNextItemWidth(360.0f);
            if (InputTextJapanese("VOICEVOX URL", ref endpoint, 512))
            {
                this.editTrigger.VoiceVoxEndpoint = endpoint.Trim();
            }

            ImGui.SameLine();
            ImGui.TextDisabled("例: http://127.0.0.1:50021");

            var speakerId = this.editTrigger.VoiceVoxSpeakerId;
            var speakerLabels = VoiceVoxSpeakerOptions.Select(option => option.Label).ToArray();
            var selectedSpeakerIndex = Array.FindIndex(VoiceVoxSpeakerOptions, option => option.Id == speakerId);
            var speakerComboPreview = selectedSpeakerIndex >= 0
                ? VoiceVoxSpeakerOptions[selectedSpeakerIndex].Label
                : $"カスタム / 未登録 Speaker ID ({speakerId})";

            ImGui.SetNextItemWidth(360.0f);
            if (ImGui.BeginCombo("話者", speakerComboPreview))
            {
                for (var i = 0; i < VoiceVoxSpeakerOptions.Length; i++)
                {
                    var isSelected = i == selectedSpeakerIndex;
                    if (ImGui.Selectable(speakerLabels[i], isSelected))
                    {
                        this.editTrigger.VoiceVoxSpeakerId = VoiceVoxSpeakerOptions[i].Id;
                    }

                    if (isSelected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            speakerId = this.editTrigger.VoiceVoxSpeakerId;
            ImGui.SetNextItemWidth(180.0f);
            if (ImGui.InputInt("Speaker ID", ref speakerId))
            {
                this.editTrigger.VoiceVoxSpeakerId = Math.Max(0, speakerId);
            }

            ImGui.SameLine();
            ImGui.TextDisabled("一覧にない話者/スタイルはSpeaker IDを直接入力してください。");
        }

        ImGui.Spacing();
        var textSize = this.editTrigger.TextSize;
        ImGui.SetNextItemWidth(180.0f);
        if (ImGui.InputFloat("テキストサイズ", ref textSize, 1.0f, 10.0f))
        {
            this.editTrigger.TextSize = Math.Clamp(textSize, 8.0f, 256.0f);
        }

        var fontDesign = (int)this.editTrigger.TextFontDesign;
        var fontDesignLabels = new[] { "標準", "太字", "影付き", "黒縁強調", "ネオン風" };
        ImGui.SetNextItemWidth(220.0f);
        if (ImGui.Combo("フォントデザイン", ref fontDesign, fontDesignLabels, fontDesignLabels.Length))
        {
            this.editTrigger.TextFontDesign = (TextFontDesign)Math.Clamp(fontDesign, 0, fontDesignLabels.Length - 1);
        }

        var enablePixelSnap = this.editTrigger.EnableTextPixelSnap;
        if (ImGui.Checkbox("文字をきれいに表示する", ref enablePixelSnap))
        {
            this.editTrigger.EnableTextPixelSnap = enablePixelSnap;
        }
        ImGui.SameLine();
        ImGui.TextDisabled("ONの場合、描画位置を整数座標に丸めて文字のにじみを抑えます。");

        if (this.editTrigger.TextFontDesign == TextFontDesign.Shadow || this.editTrigger.TextFontDesign == TextFontDesign.Neon)
        {
            var shadowOffsetX = this.editTrigger.TextShadowOffsetX;
            ImGui.SetNextItemWidth(180.0f);
            if (ImGui.InputFloat("影の位置 X", ref shadowOffsetX, 0.5f, 1.0f))
            {
                this.editTrigger.TextShadowOffsetX = Math.Clamp(shadowOffsetX, -32.0f, 32.0f);
            }

            var shadowOffsetY = this.editTrigger.TextShadowOffsetY;
            ImGui.SetNextItemWidth(180.0f);
            if (ImGui.InputFloat("影の位置 Y", ref shadowOffsetY, 0.5f, 1.0f))
            {
                this.editTrigger.TextShadowOffsetY = Math.Clamp(shadowOffsetY, -32.0f, 32.0f);
            }

            var shadowColor = new Vector4(
                this.editTrigger.TextShadowColorR,
                this.editTrigger.TextShadowColorG,
                this.editTrigger.TextShadowColorB,
                this.editTrigger.TextShadowColorA);
            if (ImGui.ColorEdit4("影・発光色", ref shadowColor))
            {
                this.editTrigger.TextShadowColorR = shadowColor.X;
                this.editTrigger.TextShadowColorG = shadowColor.Y;
                this.editTrigger.TextShadowColorB = shadowColor.Z;
                this.editTrigger.TextShadowColorA = shadowColor.W;
            }
        }

        var textColor = new Vector4(
            this.editTrigger.TextColorR,
            this.editTrigger.TextColorG,
            this.editTrigger.TextColorB,
            this.editTrigger.TextColorA);
        if (ImGui.ColorEdit4("テキスト色", ref textColor))
        {
            this.editTrigger.TextColorR = textColor.X;
            this.editTrigger.TextColorG = textColor.Y;
            this.editTrigger.TextColorB = textColor.Z;
            this.editTrigger.TextColorA = textColor.W;
        }

        var enableOutline = this.editTrigger.EnableTextOutline;
        if (ImGui.Checkbox("枠線を表示", ref enableOutline))
        {
            this.editTrigger.EnableTextOutline = enableOutline;
        }

        if (this.editTrigger.EnableTextOutline)
        {
            var outlineThickness = this.editTrigger.TextOutlineThickness;
            ImGui.SetNextItemWidth(180.0f);
            if (ImGui.InputFloat("枠線の太さ", ref outlineThickness, 0.5f, 1.0f))
            {
                this.editTrigger.TextOutlineThickness = Math.Clamp(outlineThickness, 1.0f, 16.0f);
            }

            var outlineColor = new Vector4(
                this.editTrigger.TextOutlineColorR,
                this.editTrigger.TextOutlineColorG,
                this.editTrigger.TextOutlineColorB,
                this.editTrigger.TextOutlineColorA);
            if (ImGui.ColorEdit4("枠線色", ref outlineColor))
            {
                this.editTrigger.TextOutlineColorR = outlineColor.X;
                this.editTrigger.TextOutlineColorG = outlineColor.Y;
                this.editTrigger.TextOutlineColorB = outlineColor.Z;
                this.editTrigger.TextOutlineColorA = outlineColor.W;
            }
        }

        var enableFadeIn = this.editTrigger.EnableTextFadeIn;
        if (ImGui.Checkbox("フェードインを有効にする", ref enableFadeIn))
        {
            this.editTrigger.EnableTextFadeIn = enableFadeIn;
        }

        if (this.editTrigger.EnableTextFadeIn)
        {
            var fadeInSeconds = this.editTrigger.TextFadeInSeconds;
            ImGui.SetNextItemWidth(180.0f);
            if (ImGui.InputFloat("フェードイン時間", ref fadeInSeconds, 0.05f, 0.1f))
            {
                this.editTrigger.TextFadeInSeconds = Math.Clamp(fadeInSeconds, 0.01f, 10.0f);
            }
        }

        var seconds = this.editTrigger.DisplaySeconds;
        ImGui.SetNextItemWidth(180.0f);
        if (ImGui.InputFloat("表示時間", ref seconds, 0.1f, 1.0f))
        {
            this.editTrigger.DisplaySeconds = Math.Max(0.1f, seconds);
        }

        ImGui.TextDisabled("トリガー文字にヒットした場合、ここで指定した文字を画面位置X/Yに表示します。色・フォントデザイン・枠線・フェードインが反映されます。");
    }

    private void DrawImageSettingArea()
    {
        ImGui.Text("画像項目設定 ※トリガーで画像を表示を選択した場合");

        var useOriginalSize = this.editTrigger.UseOriginalImageSize;
        if (ImGui.Checkbox("画像を元サイズで表示", ref useOriginalSize))
        {
            this.editTrigger.UseOriginalImageSize = useOriginalSize;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("ONの場合、読み込んだ画像の実サイズを基準に表示します。");

        var width = this.editTrigger.ImageWidth;
        ImGui.SetNextItemWidth(180.0f);
        if (ImGui.InputFloat("画像幅", ref width, 1.0f, 10.0f))
        {
            this.editTrigger.ImageWidth = Math.Max(1.0f, width);
            this.editTrigger.ImageSize = Math.Max(1.0f, width);
        }

        var height = this.editTrigger.ImageHeight;
        ImGui.SetNextItemWidth(180.0f);
        if (ImGui.InputFloat("画像高さ", ref height, 1.0f, 10.0f))
        {
            this.editTrigger.ImageHeight = Math.Max(1.0f, height);
        }

        ImGui.Text("表示倍率");
        ImGui.SameLine();
        ImGui.TextDisabled("100%が等倍、50%が半分、200%が2倍です。倍率は常に反映されます。");

        var scalePercent = this.editTrigger.ScalePercent <= 0.0f ? 100.0f : this.editTrigger.ScalePercent;
        ImGui.SetNextItemWidth(180.0f);
        if (ImGui.InputFloat("表示倍率 %", ref scalePercent, 1.0f, 10.0f))
        {
            this.editTrigger.ScalePercent = Math.Clamp(scalePercent, 1.0f, 10000.0f);
            this.editTrigger.UsePercentScale = true;
        }

        var seconds = this.editTrigger.DisplaySeconds;
        ImGui.SetNextItemWidth(180.0f);
        if (ImGui.InputFloat("表示時間", ref seconds, 0.1f, 1.0f))
        {
            this.editTrigger.DisplaySeconds = Math.Max(0.1f, seconds);
        }

        var isWeb = this.editTrigger.IsWebImage;
        if (ImGui.Checkbox("Web画像URLを使用する", ref isWeb))
        {
            this.editTrigger.IsWebImage = isWeb;
        }

        var imagePath = this.editTrigger.ImagePath ?? string.Empty;
        ImGui.SetNextItemWidth(700.0f);
        if (InputTextJapanese("画像のパス / URL", ref imagePath, 2048))
        {
            this.editTrigger.ImagePath = imagePath;
            this.imageSelectMessage = string.Empty;
        }

        ImGui.SameLine();

        if (ImGui.Button("画像を選択", new Vector2(140.0f, 0.0f)))
        {
            this.SelectLocalImageFile();
        }

        if (this.editTrigger.IsWebImage)
        {
            ImGui.TextDisabled("例: https://example.com/image.png");
        }
        else
        {
            ImGui.TextDisabled(@"例: C:\Users\Rowsai\Pictures\sample.png");
        }

        if (!string.IsNullOrWhiteSpace(this.imageSelectMessage))
        {
            ImGui.TextDisabled(this.imageSelectMessage);
        }

        ImGui.TextDisabled("位置設定ボタンを押すと、現在のX/Y座標に表示します。左クリックドラッグで画面位置X/Yに反映、右クリックで閉じます。");
    }


    private void DrawTriggerManagementAssignmentArea()
    {
        var currentBoxName = this.GetTriggerBoxDisplayName(this.editTrigger.TriggerBoxId, "未分類");
        ImGui.SetNextItemWidth(300.0f);
        if (ImGui.BeginCombo("トリガーボックス", currentBoxName))
        {
            if (ImGui.Selectable("未分類", string.IsNullOrWhiteSpace(this.editTrigger.TriggerBoxId)))
            {
                this.editTrigger.TriggerBoxId = string.Empty;
                this.editTrigger.TriggerLabelId = string.Empty;
            }

            foreach (var box in this.configuration.TriggerBoxes)
            {
                var displayName = this.GetTriggerBoxDisplayName(box.BoxId, "名称未設定");
                var isSelected = string.Equals(this.editTrigger.TriggerBoxId, box.BoxId, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(displayName, isSelected))
                {
                    this.editTrigger.TriggerBoxId = box.BoxId;

                    if (!this.configuration.TriggerLabels.Any(label =>
                            string.Equals(label.BoxId, box.BoxId, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(label.LabelId, this.editTrigger.TriggerLabelId, StringComparison.OrdinalIgnoreCase)))
                    {
                        this.editTrigger.TriggerLabelId = string.Empty;
                    }
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();

        var currentLabelName = this.GetTriggerLabelDisplayName(this.editTrigger.TriggerLabelId, "未分類");
        ImGui.SetNextItemWidth(300.0f);
        if (ImGui.BeginCombo("トリガーラベル", currentLabelName))
        {
            if (ImGui.Selectable("未分類", string.IsNullOrWhiteSpace(this.editTrigger.TriggerLabelId)))
            {
                this.editTrigger.TriggerLabelId = string.Empty;
            }

            foreach (var label in this.configuration.TriggerLabels
                         .Where(label => string.IsNullOrWhiteSpace(this.editTrigger.TriggerBoxId)
                             || string.Equals(label.BoxId, this.editTrigger.TriggerBoxId, StringComparison.OrdinalIgnoreCase)))
            {
                var displayName = this.GetTriggerLabelDisplayName(label.LabelId, "名称未設定");
                var isSelected = string.Equals(this.editTrigger.TriggerLabelId, label.LabelId, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(displayName, isSelected))
                {
                    this.editTrigger.TriggerLabelId = label.LabelId;
                    this.editTrigger.TriggerBoxId = label.BoxId;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextDisabled("トリガーボックス / トリガーラベルは『トリガー管理』タブで作成できます。未分類のままでも保存できます。");
    }

    private void DrawTriggerListTab()
    {
        ImGui.Spacing();
        this.DrawImageTriggerTable();
        ImGui.Spacing();
        ImGui.Spacing();
        this.DrawTextTriggerTable();
        ImGui.Spacing();
        ImGui.Spacing();
        this.DrawFfxivLogTriggerTable();
    }

    private void DrawImageTriggerTable()
    {
        ImGui.Text("画像表示用トリガー一覧");

        if (this.configuration.Triggers.Count == 0)
        {
            ImGui.TextDisabled("保存済みの画像表示用トリガーはありません。");
            return;
        }

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("HappyTriggerImageTriggerTable", 12, tableFlags))
        {
            ImGui.TableSetupColumn("有効", ImGuiTableColumnFlags.WidthFixed, 50.0f);
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("判定", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("トリガー文字");
            ImGui.TableSetupColumn("画像");
            ImGui.TableSetupColumn("X", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("Y", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("幅", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("高さ", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("倍率", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("待機/表示", ImGuiTableColumnFlags.WidthFixed, 90.0f);
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 220.0f);
            ImGui.TableHeadersRow();

            for (var i = 0; i < this.configuration.Triggers.Count; i++)
            {
                var trigger = this.configuration.Triggers[i];
                trigger.DisplayTextMode = false;
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                var enabled = trigger.Enabled;
                if (ImGui.Checkbox($"##image_enabled_{i}", ref enabled))
                {
                    trigger.Enabled = enabled;
                    this.saveConfig();
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(string.IsNullOrWhiteSpace(trigger.TriggerId) ? "未採番" : trigger.TriggerId);

                ImGui.TableSetColumnIndex(2);
                ImGui.Text(trigger.ExactMatch ? "完全一致" : "部分一致");

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(trigger.Keyword);

                ImGui.TableSetColumnIndex(4);
                ImGui.TextUnformatted(trigger.ImagePath);

                ImGui.TableSetColumnIndex(5);
                ImGui.Text($"{trigger.PositionX:0}");

                ImGui.TableSetColumnIndex(6);
                ImGui.Text($"{trigger.PositionY:0}");

                ImGui.TableSetColumnIndex(7);
                ImGui.Text(trigger.UseOriginalImageSize ? "元" : $"{trigger.ImageWidth:0}");

                ImGui.TableSetColumnIndex(8);
                ImGui.Text(trigger.UseOriginalImageSize ? "元" : $"{trigger.ImageHeight:0}");

                ImGui.TableSetColumnIndex(9);
                ImGui.Text($"{(trigger.ScalePercent <= 0.0f ? 100.0f : trigger.ScalePercent):0}%");

                ImGui.TableSetColumnIndex(10);
                ImGui.Text($"{trigger.WaitSeconds:0.0}/{trigger.DisplaySeconds:0.0}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"待機時間: {trigger.WaitSeconds:0.0}秒 / 表示時間: {trigger.DisplaySeconds:0.0}秒");
                }

                ImGui.TableSetColumnIndex(11);
                this.DrawListOperationButtons(i, TriggerListKind.Image, trigger);
            }

            ImGui.EndTable();
        }
    }

    private void DrawTextTriggerTable()
    {
        ImGui.Text("テキスト表示用トリガー一覧");

        if (this.configuration.TextTriggers.Count == 0)
        {
            ImGui.TextDisabled("保存済みのテキスト表示用トリガーはありません。");
            return;
        }

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("HappyTriggerTextTriggerTable", 13, tableFlags))
        {
            ImGui.TableSetupColumn("有効", ImGuiTableColumnFlags.WidthFixed, 50.0f);
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("判定", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("トリガー文字");
            ImGui.TableSetupColumn("表示テキスト");
            ImGui.TableSetupColumn("サイズ", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("フォント", ImGuiTableColumnFlags.WidthFixed, 90.0f);
            ImGui.TableSetupColumn("色", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("枠線", ImGuiTableColumnFlags.WidthFixed, 60.0f);
            ImGui.TableSetupColumn("Fade", ImGuiTableColumnFlags.WidthFixed, 60.0f);
            ImGui.TableSetupColumn("X/Y", ImGuiTableColumnFlags.WidthFixed, 120.0f);
            ImGui.TableSetupColumn("待機/表示", ImGuiTableColumnFlags.WidthFixed, 90.0f);
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 220.0f);
            ImGui.TableHeadersRow();

            for (var i = 0; i < this.configuration.TextTriggers.Count; i++)
            {
                var trigger = this.configuration.TextTriggers[i];
                trigger.DisplayTextMode = true;
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                var enabled = trigger.Enabled;
                if (ImGui.Checkbox($"##text_enabled_{i}", ref enabled))
                {
                    trigger.Enabled = enabled;
                    this.saveConfig();
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(string.IsNullOrWhiteSpace(trigger.TriggerId) ? "未採番" : trigger.TriggerId);

                ImGui.TableSetColumnIndex(2);
                ImGui.Text(trigger.ExactMatch ? "完全一致" : "部分一致");

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(trigger.Keyword);

                ImGui.TableSetColumnIndex(4);
                ImGui.TextUnformatted(trigger.DisplayText);

                ImGui.TableSetColumnIndex(5);
                ImGui.Text($"{trigger.TextSize:0}");

                ImGui.TableSetColumnIndex(6);
                ImGui.Text(GetTextFontDesignLabel(trigger.TextFontDesign));

                ImGui.TableSetColumnIndex(7);
                ImGui.Text($"RGBA");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"R:{trigger.TextColorR:0.00} G:{trigger.TextColorG:0.00} B:{trigger.TextColorB:0.00} A:{trigger.TextColorA:0.00}");
                }

                ImGui.TableSetColumnIndex(8);
                ImGui.Text(trigger.EnableTextOutline ? $"ON({trigger.TextOutlineThickness:0.#})" : "OFF");

                ImGui.TableSetColumnIndex(9);
                ImGui.Text(trigger.EnableTextFadeIn ? $"ON({trigger.TextFadeInSeconds:0.##})" : "OFF");

                ImGui.TableSetColumnIndex(10);
                ImGui.Text($"{trigger.PositionX:0} / {trigger.PositionY:0}");

                ImGui.TableSetColumnIndex(11);
                ImGui.Text($"{trigger.WaitSeconds:0.0}/{trigger.DisplaySeconds:0.0}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"待機時間: {trigger.WaitSeconds:0.0}秒 / 表示時間: {trigger.DisplaySeconds:0.0}秒");
                }

                ImGui.TableSetColumnIndex(12);
                this.DrawListOperationButtons(i, TriggerListKind.Text, trigger);
            }

            ImGui.EndTable();
        }
    }

    private void DrawFfxivLogTriggerTable()
    {
        ImGui.Text("FFXIV Log参照用トリガー一覧");

        if (this.configuration.FfxivLogTriggers.Count == 0)
        {
            ImGui.TextDisabled("保存済みのFFXIV Log参照用トリガーはありません。");
            return;
        }

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp;

        if (ImGui.BeginTable("HappyTriggerFfxivLogTriggerTable", 13, tableFlags))
        {
            ImGui.TableSetupColumn("有効", ImGuiTableColumnFlags.WidthFixed, 50.0f);
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("判定", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("前提", ImGuiTableColumnFlags.WidthFixed, 110.0f);
            ImGui.TableSetupColumn("バトルログ");
            ImGui.TableSetupColumn("内部ログ");
            ImGui.TableSetupColumn("表示種別", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("表示内容");
            ImGui.TableSetupColumn("X/Y", ImGuiTableColumnFlags.WidthFixed, 120.0f);
            ImGui.TableSetupColumn("倍率/サイズ", ImGuiTableColumnFlags.WidthFixed, 90.0f);
            ImGui.TableSetupColumn("待機/表示", ImGuiTableColumnFlags.WidthFixed, 90.0f);
            ImGui.TableSetupColumn("残り時間", ImGuiTableColumnFlags.WidthFixed, 150.0f);
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 220.0f);
            ImGui.TableHeadersRow();

            for (var i = 0; i < this.configuration.FfxivLogTriggers.Count; i++)
            {
                var trigger = this.configuration.FfxivLogTriggers[i];
                trigger.UseFfxivLogReference = true;
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                var enabled = trigger.Enabled;
                if (ImGui.Checkbox($"##ffxivlog_enabled_{i}", ref enabled))
                {
                    trigger.Enabled = enabled;
                    this.saveConfig();
                }

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(string.IsNullOrWhiteSpace(trigger.TriggerId) ? "未採番" : trigger.TriggerId);

                ImGui.TableSetColumnIndex(2);
                ImGui.Text(trigger.ExactMatch ? "完全一致" : "部分一致");

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(trigger.UsePrerequisite
                    ? (string.IsNullOrWhiteSpace(trigger.PrerequisiteTriggerId) ? "ON:未指定" : trigger.PrerequisiteTriggerId)
                    : "OFF");

                ImGui.TableSetColumnIndex(4);
                ImGui.TextUnformatted(trigger.BattleLogKeyword);

                ImGui.TableSetColumnIndex(5);
                ImGui.TextUnformatted(trigger.GetInternalLogKeywordText());

                ImGui.TableSetColumnIndex(6);
                ImGui.Text(trigger.DisplayTextMode ? "テキスト" : "画像");

                ImGui.TableSetColumnIndex(7);
                ImGui.TextUnformatted(trigger.DisplayTextMode ? trigger.DisplayText : trigger.ImagePath);

                ImGui.TableSetColumnIndex(8);
                ImGui.Text($"{trigger.PositionX:0} / {trigger.PositionY:0}");

                ImGui.TableSetColumnIndex(9);
                ImGui.Text(trigger.DisplayTextMode
                    ? $"{trigger.TextSize:0}px"
                    : $"{(trigger.ScalePercent <= 0.0f ? 100.0f : trigger.ScalePercent):0}%");

                ImGui.TableSetColumnIndex(10);
                ImGui.Text($"{trigger.WaitSeconds:0.0}/{trigger.DisplaySeconds:0.0}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"待機時間: {trigger.WaitSeconds:0.0}秒 / 表示時間: {trigger.DisplaySeconds:0.0}秒");
                }

                ImGui.TableSetColumnIndex(11);
                ImGui.TextUnformatted(trigger.EnableStatusRemainingAppend
                    ? $"{trigger.StatusRemainingJob}/{trigger.StatusRemainingStatusName}"
                    : "OFF");

                ImGui.TableSetColumnIndex(12);
                this.DrawListOperationButtons(i, TriggerListKind.FfxivLog, trigger);
            }

            ImGui.EndTable();
        }
    }

    private void DrawListOperationButtons(int index, TriggerListKind listKind, HappyTriggerSetting trigger)
    {
        var key = GetTriggerListKindKey(listKind);

        if (ImGui.SmallButton($"テスト##{key}_test_{index}"))
        {
            this.testTrigger(trigger);
        }

        ImGui.SameLine();

        if (ImGui.SmallButton($"編集##{key}_edit_{index}"))
        {
            this.BeginEditTrigger(index, listKind, trigger);
        }

        ImGui.SameLine();

        if (ImGui.SmallButton($"削除##{key}_delete_{index}"))
        {
            this.DeleteTrigger(index, listKind);
        }
    }

    private void BeginEditTrigger(int index, TriggerListKind listKind, HappyTriggerSetting trigger)
    {
        this.editingIndex = index;
        this.editingKind = listKind;
        this.editTrigger = trigger.Clone();
        this.editTrigger.UseFfxivLogReference = listKind == TriggerListKind.FfxivLog;

        if (listKind == TriggerListKind.Image)
        {
            this.editTrigger.DisplayTextMode = false;
        }
        else if (listKind == TriggerListKind.Text)
        {
            this.editTrigger.DisplayTextMode = true;
        }

        this.imageSelectMessage = string.Empty;
        this.requestOpenTriggerEditTab = true;
    }

    private void DeleteTrigger(int index, TriggerListKind listKind)
    {
        if (listKind == TriggerListKind.FfxivLog)
        {
            if (index < 0 || index >= this.configuration.FfxivLogTriggers.Count)
            {
                return;
            }

            var deletedKey = GetManagementTriggerKey(this.configuration.FfxivLogTriggers[index], listKind, index);
            this.configuration.FfxivLogTriggers.RemoveAt(index);
            this.ClearSelectedManagementTriggerIfMatched(deletedKey);
        }
        else if (listKind == TriggerListKind.Text)
        {
            if (index < 0 || index >= this.configuration.TextTriggers.Count)
            {
                return;
            }

            var deletedKey = GetManagementTriggerKey(this.configuration.TextTriggers[index], listKind, index);
            this.configuration.TextTriggers.RemoveAt(index);
            this.ClearSelectedManagementTriggerIfMatched(deletedKey);
        }
        else
        {
            if (index < 0 || index >= this.configuration.Triggers.Count)
            {
                return;
            }

            var deletedKey = GetManagementTriggerKey(this.configuration.Triggers[index], listKind, index);
            this.configuration.Triggers.RemoveAt(index);
            this.ClearSelectedManagementTriggerIfMatched(deletedKey);
        }

        this.saveConfig();

        if (this.editingIndex == index && this.editingKind == listKind)
        {
            this.ResetEditing();
        }
    }

    private void CopyTrigger(int index, TriggerListKind listKind)
    {
        HappyTriggerSetting copiedTrigger;
        int copiedIndex;

        if (listKind == TriggerListKind.FfxivLog)
        {
            if (index < 0 || index >= this.configuration.FfxivLogTriggers.Count)
            {
                return;
            }

            copiedTrigger = this.configuration.FfxivLogTriggers[index].Clone();
            copiedTrigger.UseFfxivLogReference = true;
            copiedTrigger.TriggerId = this.GenerateNextTriggerId(copiedTrigger.UsePrerequisite ? "X" : "F");
            this.configuration.FfxivLogTriggers.Add(copiedTrigger);
            copiedIndex = this.configuration.FfxivLogTriggers.Count - 1;
        }
        else if (listKind == TriggerListKind.Text)
        {
            if (index < 0 || index >= this.configuration.TextTriggers.Count)
            {
                return;
            }

            copiedTrigger = this.configuration.TextTriggers[index].Clone();
            copiedTrigger.DisplayTextMode = true;
            copiedTrigger.UseFfxivLogReference = false;
            copiedTrigger.UsePrerequisite = false;
            copiedTrigger.PrerequisiteTriggerId = string.Empty;
            copiedTrigger.TriggerId = this.GenerateNextTriggerId("T");
            this.configuration.TextTriggers.Add(copiedTrigger);
            copiedIndex = this.configuration.TextTriggers.Count - 1;
        }
        else
        {
            if (index < 0 || index >= this.configuration.Triggers.Count)
            {
                return;
            }

            copiedTrigger = this.configuration.Triggers[index].Clone();
            copiedTrigger.DisplayTextMode = false;
            copiedTrigger.UseFfxivLogReference = false;
            copiedTrigger.UsePrerequisite = false;
            copiedTrigger.PrerequisiteTriggerId = string.Empty;
            copiedTrigger.TriggerId = this.GenerateNextTriggerId("I");
            this.configuration.Triggers.Add(copiedTrigger);
            copiedIndex = this.configuration.Triggers.Count - 1;
        }

        this.selectedManagementTriggerKey = GetManagementTriggerKey(copiedTrigger, listKind, copiedIndex);
        this.saveConfig();
    }

    private void ClearSelectedManagementTriggerIfMatched(string deletedKey)
    {
        if (string.Equals(this.selectedManagementTriggerKey, deletedKey, StringComparison.OrdinalIgnoreCase))
        {
            this.selectedManagementTriggerKey = string.Empty;
        }
    }

    private static string GetTriggerListKindKey(TriggerListKind listKind)
    {
        return listKind switch
        {
            TriggerListKind.FfxivLog => "ffxivlog",
            TriggerListKind.Text => "text",
            _ => "image",
        };
    }

    private static string GetTextFontDesignLabel(TextFontDesign design)
    {
        return design switch
        {
            TextFontDesign.Bold => "太字",
            TextFontDesign.Shadow => "影付き",
            TextFontDesign.StrongOutline => "黒縁強調",
            TextFontDesign.Neon => "ネオン風",
            _ => "標準",
        };
    }


    private void DrawTriggerManagementTab()
    {
        ImGui.Spacing();
        ImGui.Text("トリガーボックス / トリガーラベル管理");
        ImGui.TextDisabled("トリガーボックス > トリガーラベル > 作成したトリガー の階層で整理できます。");
        ImGui.Separator();

        if (ImGui.BeginTabBar("HappyTriggerManagementTabBar"))
        {
            if (ImGui.BeginTabItem("ボックス・ラベル作成"))
            {
                this.DrawTriggerBoxAndLabelEditor();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("階層表示"))
            {
                this.DrawTriggerManagementTree();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Import"))
            {
                this.DrawTriggerImportTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }


    private void DrawTriggerImportTab()
    {
        ImGui.Spacing();
        ImGui.Text("CSV Import");
        ImGui.TextDisabled("exportで出力したCSVを指定して、トリガーボックス/ラベル/トリガーを取り込みます。");
        ImGui.TextDisabled("不正なCSV、既存IDとの重複、設定不可能な値がある場合はimportを中断します。");
        ImGui.Separator();

        if (ImGui.Button("CSVを指定", new Vector2(160.0f, 32.0f)))
        {
            if (NativeFileDialogService.TryOpenCsvFile(out var selectedPath))
            {
                this.importCsvPath = selectedPath;
                this.triggerImportMessage = string.Empty;
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Import実行", new Vector2(160.0f, 32.0f)))
        {
            this.ImportTriggerCsv(this.importCsvPath);
        }

        ImGui.Spacing();
        ImGui.Text("選択中CSV");
        ImGui.TextWrapped(string.IsNullOrWhiteSpace(this.importCsvPath) ? "未選択" : this.importCsvPath);

        if (!string.IsNullOrWhiteSpace(this.triggerImportMessage))
        {
            var color = this.triggerImportMessage.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase)
                ? new Vector4(1.0f, 0.25f, 0.25f, 1.0f)
                : new Vector4(0.25f, 1.0f, 0.45f, 1.0f);
            ImGui.TextColored(color, this.triggerImportMessage);
        }
    }

    private void DrawTriggerBoxAndLabelEditor()
    {
        ImGui.Spacing();
        ImGui.Text("トリガーボックス作成");
        ImGui.SetNextItemWidth(360.0f);
        if (InputTextJapanese("ボックス名", ref this.newTriggerBoxName, 256))
        {
            this.newTriggerBoxName = RemoveLineBreaks(this.newTriggerBoxName);
        }

        ImGui.SameLine();
        if (ImGui.Button("ボックスを追加", new Vector2(150.0f, 0.0f)))
        {
            var name = this.newTriggerBoxName.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                var boxId = this.GenerateNextManagementId("BOX");
                this.configuration.TriggerBoxes.Add(new TriggerBoxSetting
                {
                    BoxId = boxId,
                    Name = name,
                });

                this.selectedManagementBoxId = boxId;
                this.selectedManagementLabelBoxId = boxId;
                this.newTriggerBoxName = string.Empty;
                this.saveConfig();
            }
        }

        this.DrawTriggerBoxTable();

        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("トリガーラベル作成");

        this.DrawBoxComboForLabelCreate();

        ImGui.SetNextItemWidth(360.0f);
        if (InputTextJapanese("ラベル名", ref this.newTriggerLabelName, 256))
        {
            this.newTriggerLabelName = RemoveLineBreaks(this.newTriggerLabelName);
        }

        ImGui.SameLine();
        if (ImGui.Button("ラベルを追加", new Vector2(150.0f, 0.0f)))
        {
            var name = this.newTriggerLabelName.Trim();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(this.selectedManagementLabelBoxId))
            {
                this.configuration.TriggerLabels.Add(new TriggerLabelSetting
                {
                    LabelId = this.GenerateNextManagementId("Lab"),
                    BoxId = this.selectedManagementLabelBoxId,
                    Name = name,
                });

                this.newTriggerLabelName = string.Empty;
                this.saveConfig();
            }
        }

        this.DrawTriggerLabelTable();
    }

    private void DrawBoxComboForLabelCreate()
    {
        if (this.configuration.TriggerBoxes.Count == 0)
        {
            this.selectedManagementLabelBoxId = string.Empty;
            ImGui.TextDisabled("先にトリガーボックスを作成してください。");
            return;
        }

        if (string.IsNullOrWhiteSpace(this.selectedManagementLabelBoxId)
            || !this.configuration.TriggerBoxes.Any(box => string.Equals(box.BoxId, this.selectedManagementLabelBoxId, StringComparison.OrdinalIgnoreCase)))
        {
            this.selectedManagementLabelBoxId = this.configuration.TriggerBoxes[0].BoxId;
        }

        var selectedName = this.GetTriggerBoxDisplayName(this.selectedManagementLabelBoxId, "名称未設定");
        ImGui.SetNextItemWidth(360.0f);
        if (ImGui.BeginCombo("所属ボックス", selectedName))
        {
            foreach (var box in this.configuration.TriggerBoxes)
            {
                var isSelected = string.Equals(this.selectedManagementLabelBoxId, box.BoxId, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(this.GetTriggerBoxDisplayName(box.BoxId, "名称未設定"), isSelected))
                {
                    this.selectedManagementLabelBoxId = box.BoxId;
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawTriggerBoxTable()
    {
        if (this.configuration.TriggerBoxes.Count == 0)
        {
            ImGui.TextDisabled("保存済みのトリガーボックスはありません。");
            return;
        }

        if (ImGui.BeginTable("HappyTriggerBoxTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 90.0f);
            ImGui.TableSetupColumn("名称");
            ImGui.TableSetupColumn("ラベル数", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 150.0f);
            ImGui.TableHeadersRow();

            for (var i = 0; i < this.configuration.TriggerBoxes.Count; i++)
            {
                var box = this.configuration.TriggerBoxes[i];
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(box.BoxId);

                ImGui.TableSetColumnIndex(1);
                var name = box.Name ?? string.Empty;
                ImGui.SetNextItemWidth(-1.0f);
                if (InputTextJapanese($"##box_name_{i}", ref name, 256))
                {
                    box.Name = RemoveLineBreaks(name);
                    this.saveConfig();
                }

                ImGui.TableSetColumnIndex(2);
                var labelCount = this.configuration.TriggerLabels.Count(label => string.Equals(label.BoxId, box.BoxId, StringComparison.OrdinalIgnoreCase));
                ImGui.TextUnformatted(labelCount.ToString());

                ImGui.TableSetColumnIndex(3);
                if (ImGui.SmallButton($"削除##box_delete_{i}"))
                {
                    this.RemoveTriggerBox(box.BoxId);
                    ImGui.EndTable();
                    return;
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawTriggerLabelTable()
    {
        if (this.configuration.TriggerLabels.Count == 0)
        {
            ImGui.TextDisabled("保存済みのトリガーラベルはありません。");
            return;
        }

        if (string.IsNullOrWhiteSpace(this.selectedManagementLabelBoxId))
        {
            ImGui.TextDisabled("トリガーボックスを選択すると、そのボックスに所属するトリガーラベルだけを表示します。");
            return;
        }

        var visibleLabels = this.configuration.TriggerLabels
            .Select((label, index) => new { Label = label, Index = index })
            .Where(item => string.Equals(item.Label.BoxId, this.selectedManagementLabelBoxId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (visibleLabels.Count == 0)
        {
            ImGui.TextDisabled("選択中のトリガーボックスに所属するトリガーラベルはありません。");
            return;
        }

        if (ImGui.BeginTable("HappyTriggerLabelTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 90.0f);
            ImGui.TableSetupColumn("所属ボックス", ImGuiTableColumnFlags.WidthFixed, 220.0f);
            ImGui.TableSetupColumn("名称");
            ImGui.TableSetupColumn("トリガー数", ImGuiTableColumnFlags.WidthFixed, 90.0f);
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 150.0f);
            ImGui.TableHeadersRow();

            foreach (var item in visibleLabels)
            {
                var label = item.Label;
                var i = item.Index;
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted(label.LabelId);

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(this.GetTriggerBoxDisplayName(label.BoxId, "不明なBOX"));

                ImGui.TableSetColumnIndex(2);
                var name = label.Name ?? string.Empty;
                ImGui.SetNextItemWidth(-1.0f);
                if (InputTextJapanese($"##label_name_{i}", ref name, 256))
                {
                    label.Name = RemoveLineBreaks(name);
                    this.saveConfig();
                }

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(this.GetAllTriggers().Count(trigger => string.Equals(trigger.TriggerLabelId, label.LabelId, StringComparison.OrdinalIgnoreCase)).ToString());

                ImGui.TableSetColumnIndex(4);
                if (ImGui.SmallButton($"削除##label_delete_{i}"))
                {
                    this.RemoveTriggerLabel(label.LabelId);
                    ImGui.EndTable();
                    return;
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawTriggerManagementTree()
    {
        ImGui.TextDisabled("ボックス、ラベル、トリガーの階層を確認できます。トリガーの所属はログトリガータブの管理設定で変更できます。");
        ImGui.TextDisabled("トリガー名をクリックすると、保存済み設定の詳細を確認できます。各トリガーの右側からテスト・編集・コピー・削除もできます。");
        ImGui.TextDisabled("トリガーボックス右側の export ボタンで、そのボックス配下のトリガーをCSV出力できます。");

        if (!string.IsNullOrWhiteSpace(this.triggerBoxExportMessage))
        {
            ImGui.TextColored(new Vector4(1.0f, 0.92f, 0.25f, 1.0f), this.triggerBoxExportMessage);
        }

        if (!string.IsNullOrWhiteSpace(this.manualTriggerIdMessage))
        {
            var color = this.manualTriggerIdMessage.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase)
                ? new Vector4(1.0f, 0.35f, 0.35f, 1.0f)
                : new Vector4(1.0f, 0.92f, 0.25f, 1.0f);
            ImGui.TextColored(color, this.manualTriggerIdMessage);
        }

        ImGui.Separator();

        foreach (var box in this.configuration.TriggerBoxes)
        {
            var boxHeader = this.GetTriggerBoxDisplayName(box.BoxId, "名称未設定");
            var isOpen = ImGui.TreeNode(boxHeader);

            ImGui.SameLine();
            if (ImGui.SmallButton($"export##box_export_{box.BoxId}"))
            {
                this.ExportTriggerBoxToCsv(box);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("このトリガーボックス配下のトリガーをCSV形式で出力します。");
            }

            if (isOpen)
            {
                foreach (var label in this.configuration.TriggerLabels.Where(label => string.Equals(label.BoxId, box.BoxId, StringComparison.OrdinalIgnoreCase)))
                {
                    var labelHeader = this.GetTriggerLabelDisplayName(label.LabelId, "名称未設定");
                    if (ImGui.TreeNode(labelHeader))
                    {
                        if (this.DrawTriggerTreeRowsForLabel(label.LabelId))
                        {
                            ImGui.TreePop();
                            ImGui.TreePop();
                            return;
                        }

                        ImGui.TreePop();
                    }
                }

                var directTriggers = this.GetAllTriggers()
                    .Where(trigger => string.Equals(trigger.TriggerBoxId, box.BoxId, StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(trigger.TriggerLabelId))
                    .ToList();

                if (directTriggers.Count > 0 && ImGui.TreeNode("ラベル未設定"))
                {
                    foreach (var trigger in directTriggers)
                    {
                        if (this.DrawTriggerTreeRow(trigger))
                        {
                            ImGui.TreePop();
                            ImGui.TreePop();
                            return;
                        }
                    }

                    ImGui.TreePop();
                }

                ImGui.TreePop();
            }
        }

        var unassignedTriggers = this.GetAllTriggers()
            .Where(trigger => string.IsNullOrWhiteSpace(trigger.TriggerBoxId) && string.IsNullOrWhiteSpace(trigger.TriggerLabelId))
            .ToList();

        if (unassignedTriggers.Count > 0 && ImGui.TreeNode("未分類"))
        {
            foreach (var trigger in unassignedTriggers)
            {
                if (this.DrawTriggerTreeRow(trigger))
                {
                    ImGui.TreePop();
                    return;
                }
            }

            ImGui.TreePop();
        }
    }

    private void ExportTriggerBoxToCsv(TriggerBoxSetting box)
    {
        try
        {
            var boxId = box.BoxId ?? string.Empty;
            var boxName = string.IsNullOrWhiteSpace(box.Name) ? "名称未設定" : box.Name.Trim();
            var exportedAt = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var fileName = $"HappyTrigger_{MakeSafeFileName(boxId)}_{MakeSafeFileName(boxName)}_{exportedAt}.csv";
            var exportDirectory = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "exports");
            Directory.CreateDirectory(exportDirectory);

            var filePath = Path.Combine(exportDirectory, fileName);
            var triggers = this.GetAllTriggers()
                .Where(trigger => string.Equals(trigger.TriggerBoxId, boxId, StringComparison.OrdinalIgnoreCase))
                .Select(trigger => new
                {
                    Trigger = trigger,
                    Location = this.TryFindTriggerLocation(trigger, out var listKind, out var index)
                        ? new TriggerExportLocation(listKind, index)
                        : new TriggerExportLocation(TriggerListKind.Image, -1),
                })
                .OrderBy(item => item.Trigger.TriggerLabelId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Trigger.TriggerId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", ExportCsvHeaders.Select(EscapeCsv)));

            foreach (var item in triggers)
            {
                var trigger = item.Trigger;
                var values = this.GetTriggerExportCsvValues(trigger, item.Location.ListKind, item.Location.Index, boxId, boxName);
                csv.AppendLine(string.Join(",", values.Select(EscapeCsv)));
            }

            File.WriteAllText(filePath, csv.ToString(), new UTF8Encoding(true));
            this.triggerBoxExportMessage = $"CSV export完了: {filePath}";
        }
        catch (Exception ex)
        {
            this.triggerBoxExportMessage = $"CSV export失敗: {ex.Message}";
        }
    }

    private sealed class TriggerExportLocation
    {
        public TriggerExportLocation(TriggerListKind listKind, int index)
        {
            this.ListKind = listKind;
            this.Index = index;
        }

        public TriggerListKind ListKind { get; }

        public int Index { get; }
    }

    private static readonly string[] ExportCsvHeaders =
    {
        "TriggerListKind",
        "TriggerIndex",
        "TriggerId",
        "TriggerName",
        "Enabled",
        "TriggerBoxId",
        "TriggerBoxName",
        "TriggerLabelId",
        "TriggerLabelName",
        "Keyword",
        "ExactMatch",
        "UseFfxivLogReference",
        "UsePrerequisite",
        "PrerequisiteTriggerId",
        "EnableStatusRemainingAppend",
        "StatusRemainingJob",
        "StatusRemainingStatusName",
        "BattleLogKeyword",
        "InternalLogKeyword",
        "InternalLogKeywords",
        "DisplayTextMode",
        "DisplayText",
        "EnableVoiceVox",
        "VoiceVoxEndpoint",
        "VoiceVoxSpeakerId",
        "IsWebImage",
        "ImagePath",
        "PositionX",
        "PositionY",
        "WaitSeconds",
        "ImageSize",
        "UseOriginalImageSize",
        "ImageWidth",
        "ImageHeight",
        "UsePercentScale",
        "ScalePercent",
        "TextSize",
        "TextFontDesign",
        "EnableTextPixelSnap",
        "TextShadowOffsetX",
        "TextShadowOffsetY",
        "TextShadowColorR",
        "TextShadowColorG",
        "TextShadowColorB",
        "TextShadowColorA",
        "TextColorR",
        "TextColorG",
        "TextColorB",
        "TextColorA",
        "EnableTextOutline",
        "TextOutlineThickness",
        "TextOutlineColorR",
        "TextOutlineColorG",
        "TextOutlineColorB",
        "TextOutlineColorA",
        "EnableTextFadeIn",
        "TextFadeInSeconds",
        "DisplaySeconds",
    };

    private IEnumerable<string> GetTriggerExportCsvValues(
        HappyTriggerSetting trigger,
        TriggerListKind listKind,
        int index,
        string boxId,
        string boxName)
    {
        var labelName = string.IsNullOrWhiteSpace(trigger.TriggerLabelId)
            ? string.Empty
            : this.GetTriggerLabelNameOnly(trigger.TriggerLabelId, "名称未設定");

        return new[]
        {
            GetTriggerListKindKey(listKind),
            index.ToString(CultureInfo.InvariantCulture),
            trigger.TriggerId ?? string.Empty,
            string.IsNullOrWhiteSpace(trigger.TriggerName) ? "名称未設定" : trigger.TriggerName.Trim(),
            trigger.Enabled ? "Enabled" : "Disabled",
            boxId,
            boxName,
            trigger.TriggerLabelId ?? string.Empty,
            labelName,
            trigger.Keyword ?? string.Empty,
            BoolText(trigger.ExactMatch),
            BoolText(trigger.UseFfxivLogReference),
            BoolText(trigger.UsePrerequisite),
            trigger.PrerequisiteTriggerId ?? string.Empty,
            BoolText(trigger.EnableStatusRemainingAppend),
            trigger.StatusRemainingJob ?? string.Empty,
            trigger.StatusRemainingStatusName ?? string.Empty,
            trigger.BattleLogKeyword ?? string.Empty,
            trigger.InternalLogKeyword ?? string.Empty,
            string.Join(HappyTriggerSetting.InternalLogKeywordDelimiter, trigger.GetInternalLogKeywords()),
            BoolText(trigger.DisplayTextMode),
            trigger.DisplayText ?? string.Empty,
            BoolText(trigger.EnableVoiceVox),
            string.IsNullOrWhiteSpace(trigger.VoiceVoxEndpoint) ? "http://127.0.0.1:50021" : trigger.VoiceVoxEndpoint.Trim(),
            trigger.VoiceVoxSpeakerId.ToString(CultureInfo.InvariantCulture),
            BoolText(trigger.IsWebImage),
            trigger.ImagePath ?? string.Empty,
            FloatText(trigger.PositionX),
            FloatText(trigger.PositionY),
            FloatText(trigger.WaitSeconds),
            FloatText(trigger.ImageSize),
            BoolText(trigger.UseOriginalImageSize),
            FloatText(trigger.ImageWidth),
            FloatText(trigger.ImageHeight),
            BoolText(trigger.UsePercentScale),
            FloatText(trigger.ScalePercent),
            FloatText(trigger.TextSize),
            trigger.TextFontDesign.ToString(),
            BoolText(trigger.EnableTextPixelSnap),
            FloatText(trigger.TextShadowOffsetX),
            FloatText(trigger.TextShadowOffsetY),
            FloatText(trigger.TextShadowColorR),
            FloatText(trigger.TextShadowColorG),
            FloatText(trigger.TextShadowColorB),
            FloatText(trigger.TextShadowColorA),
            FloatText(trigger.TextColorR),
            FloatText(trigger.TextColorG),
            FloatText(trigger.TextColorB),
            FloatText(trigger.TextColorA),
            BoolText(trigger.EnableTextOutline),
            FloatText(trigger.TextOutlineThickness),
            FloatText(trigger.TextOutlineColorR),
            FloatText(trigger.TextOutlineColorG),
            FloatText(trigger.TextOutlineColorB),
            FloatText(trigger.TextOutlineColorA),
            BoolText(trigger.EnableTextFadeIn),
            FloatText(trigger.TextFadeInSeconds),
            FloatText(trigger.DisplaySeconds),
        };
    }

    private string GetTriggerLabelNameOnly(string? labelId, string fallback)
    {
        if (string.IsNullOrWhiteSpace(labelId))
        {
            return string.Empty;
        }

        var label = this.configuration.TriggerLabels.FirstOrDefault(label => string.Equals(label.LabelId, labelId, StringComparison.OrdinalIgnoreCase));
        if (label == null)
        {
            return "不明";
        }

        return string.IsNullOrWhiteSpace(label.Name) ? fallback : label.Name.Trim();
    }

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        var mustQuote = text.Contains(',') || text.Contains('"') || text.Contains('\r') || text.Contains('\n');
        text = text.Replace("\"", "\"\"");
        return mustQuote ? $"\"{text}\"" : text;
    }

    private static string MakeSafeFileName(string value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "名称未設定" : value.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            text = text.Replace(invalidChar, '_');
        }

        return text;
    }

    private static string BoolText(bool value)
    {
        return value ? "TRUE" : "FALSE";
    }

    private static string FloatText(float value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }


    private sealed class IllegalImportException : Exception
    {
        public IllegalImportException(string message)
            : base(message)
        {
        }
    }

    private sealed class ImportRow
    {
        public TriggerListKind ListKind { get; init; }

        public HappyTriggerSetting Trigger { get; init; } = new();

        public string BoxId { get; init; } = string.Empty;

        public string BoxName { get; init; } = string.Empty;

        public string LabelId { get; init; } = string.Empty;

        public string LabelName { get; init; } = string.Empty;
    }

    private void ImportTriggerCsv(string filePath)
    {
        try
        {
            var rows = this.LoadAndValidateImportCsv(filePath);

            var boxesToAdd = rows
                .GroupBy(row => row.BoxId, StringComparer.OrdinalIgnoreCase)
                .Select(group => new TriggerBoxSetting
                {
                    BoxId = group.Key,
                    Name = group.First().BoxName,
                })
                .ToList();

            var labelsToAdd = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.LabelId))
                .GroupBy(row => row.LabelId, StringComparer.OrdinalIgnoreCase)
                .Select(group => new TriggerLabelSetting
                {
                    LabelId = group.Key,
                    BoxId = group.First().BoxId,
                    Name = group.First().LabelName,
                })
                .ToList();

            foreach (var box in boxesToAdd)
            {
                this.configuration.TriggerBoxes.Add(box);
            }

            foreach (var label in labelsToAdd)
            {
                this.configuration.TriggerLabels.Add(label);
            }

            foreach (var row in rows)
            {
                var trigger = row.Trigger.Clone();
                if (row.ListKind == TriggerListKind.FfxivLog)
                {
                    this.configuration.FfxivLogTriggers.Add(trigger);
                }
                else if (row.ListKind == TriggerListKind.Text)
                {
                    this.configuration.TextTriggers.Add(trigger);
                }
                else
                {
                    this.configuration.Triggers.Add(trigger);
                }
            }

            this.saveConfig();
            this.selectedManagementTriggerKey = string.Empty;
            this.triggerImportMessage = $"CSV import完了: {rows.Count}件";
        }
        catch (IllegalImportException)
        {
            this.triggerImportMessage = "[ERROR]IllegalImportException : 不正なcsvファイルを指定しています。";
        }
        catch
        {
            this.triggerImportMessage = "[ERROR]IllegalImportException : 不正なcsvファイルを指定しています。";
        }
    }

    private List<ImportRow> LoadAndValidateImportCsv(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new IllegalImportException("CSVファイルが見つかりません。");
        }

        if (!string.Equals(Path.GetExtension(filePath), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new IllegalImportException("CSV以外のファイルが指定されています。");
        }

        var csvText = File.ReadAllText(filePath, Encoding.UTF8);
        var table = ParseCsv(csvText);
        if (table.Count < 2)
        {
            throw new IllegalImportException("CSVにデータ行がありません。");
        }

        var headers = table[0].Select(header => (header ?? string.Empty).Trim().TrimStart('\ufeff')).ToList();
        if (headers.Count != ExportCsvHeaders.Length)
        {
            throw new IllegalImportException("CSVヘッダー数が一致しません。");
        }

        var headerSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header) || !headerSet.Add(header))
            {
                throw new IllegalImportException("CSVヘッダーが不正です。");
            }
        }

        foreach (var requiredHeader in ExportCsvHeaders)
        {
            if (!headerSet.Contains(requiredHeader))
            {
                throw new IllegalImportException("CSVヘッダーが不足しています。");
            }
        }

        var savedTriggerIds = new HashSet<string>(this.GetAllTriggers().Select(trigger => trigger.TriggerId).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!), StringComparer.OrdinalIgnoreCase);
        var savedBoxIds = new HashSet<string>(this.configuration.TriggerBoxes.Select(box => box.BoxId).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!), StringComparer.OrdinalIgnoreCase);
        var savedLabelIds = new HashSet<string>(this.configuration.TriggerLabels.Select(label => label.LabelId).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!), StringComparer.OrdinalIgnoreCase);
        var importTriggerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var importBoxNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var importLabelRows = new Dictionary<string, ImportRow>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ImportRow>();

        for (var rowIndex = 1; rowIndex < table.Count; rowIndex++)
        {
            var columns = table[rowIndex];
            if (columns.Count == 1 && string.IsNullOrWhiteSpace(columns[0]))
            {
                continue;
            }

            if (columns.Count != headers.Count)
            {
                throw new IllegalImportException("CSV行の列数が一致しません。");
            }

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var columnIndex = 0; columnIndex < headers.Count; columnIndex++)
            {
                row[headers[columnIndex]] = columns[columnIndex] ?? string.Empty;
            }

            var listKind = ParseTriggerListKind(GetRequired(row, "TriggerListKind"));
            var triggerId = GetRequired(row, "TriggerId").Trim();
            var trigger = new HappyTriggerSetting
            {
                TriggerId = triggerId,
                TriggerName = NormalizeImportedName(GetRequired(row, "TriggerName")),
                Enabled = ParseEnabled(GetRequired(row, "Enabled")),
                TriggerBoxId = GetRequired(row, "TriggerBoxId").Trim(),
                TriggerLabelId = Get(row, "TriggerLabelId").Trim(),
                Keyword = Get(row, "Keyword"),
                ExactMatch = ParseBool(GetRequired(row, "ExactMatch")),
                UseFfxivLogReference = ParseBool(GetRequired(row, "UseFfxivLogReference")),
                UsePrerequisite = ParseBool(GetRequired(row, "UsePrerequisite")),
                PrerequisiteTriggerId = Get(row, "PrerequisiteTriggerId").Trim(),
                EnableStatusRemainingAppend = ParseBool(GetRequired(row, "EnableStatusRemainingAppend")),
                StatusRemainingJob = Get(row, "StatusRemainingJob").Trim(),
                StatusRemainingStatusName = Get(row, "StatusRemainingStatusName").Trim(),
                BattleLogKeyword = Get(row, "BattleLogKeyword"),
                InternalLogKeyword = Get(row, "InternalLogKeyword"),
                DisplayTextMode = ParseBool(GetRequired(row, "DisplayTextMode")),
                DisplayText = Get(row, "DisplayText"),
                EnableVoiceVox = ParseBool(GetRequired(row, "EnableVoiceVox")),
                VoiceVoxEndpoint = GetRequired(row, "VoiceVoxEndpoint").Trim(),
                VoiceVoxSpeakerId = ParseInt(GetRequired(row, "VoiceVoxSpeakerId")),
                IsWebImage = ParseBool(GetRequired(row, "IsWebImage")),
                ImagePath = Get(row, "ImagePath"),
                PositionX = ParseFloat(GetRequired(row, "PositionX")),
                PositionY = ParseFloat(GetRequired(row, "PositionY")),
                WaitSeconds = ParseFloat(GetRequired(row, "WaitSeconds")),
                ImageSize = ParseFloat(GetRequired(row, "ImageSize")),
                UseOriginalImageSize = ParseBool(GetRequired(row, "UseOriginalImageSize")),
                ImageWidth = ParseFloat(GetRequired(row, "ImageWidth")),
                ImageHeight = ParseFloat(GetRequired(row, "ImageHeight")),
                UsePercentScale = ParseBool(GetRequired(row, "UsePercentScale")),
                ScalePercent = ParseFloat(GetRequired(row, "ScalePercent")),
                TextSize = ParseFloat(GetRequired(row, "TextSize")),
                TextFontDesign = ParseTextFontDesign(GetRequired(row, "TextFontDesign")),
                EnableTextPixelSnap = ParseBool(GetRequired(row, "EnableTextPixelSnap")),
                TextShadowOffsetX = ParseFloat(GetRequired(row, "TextShadowOffsetX")),
                TextShadowOffsetY = ParseFloat(GetRequired(row, "TextShadowOffsetY")),
                TextShadowColorR = ParseFloat(GetRequired(row, "TextShadowColorR")),
                TextShadowColorG = ParseFloat(GetRequired(row, "TextShadowColorG")),
                TextShadowColorB = ParseFloat(GetRequired(row, "TextShadowColorB")),
                TextShadowColorA = ParseFloat(GetRequired(row, "TextShadowColorA")),
                TextColorR = ParseFloat(GetRequired(row, "TextColorR")),
                TextColorG = ParseFloat(GetRequired(row, "TextColorG")),
                TextColorB = ParseFloat(GetRequired(row, "TextColorB")),
                TextColorA = ParseFloat(GetRequired(row, "TextColorA")),
                EnableTextOutline = ParseBool(GetRequired(row, "EnableTextOutline")),
                TextOutlineThickness = ParseFloat(GetRequired(row, "TextOutlineThickness")),
                TextOutlineColorR = ParseFloat(GetRequired(row, "TextOutlineColorR")),
                TextOutlineColorG = ParseFloat(GetRequired(row, "TextOutlineColorG")),
                TextOutlineColorB = ParseFloat(GetRequired(row, "TextOutlineColorB")),
                TextOutlineColorA = ParseFloat(GetRequired(row, "TextOutlineColorA")),
                EnableTextFadeIn = ParseBool(GetRequired(row, "EnableTextFadeIn")),
                TextFadeInSeconds = ParseFloat(GetRequired(row, "TextFadeInSeconds")),
                DisplaySeconds = ParseFloat(GetRequired(row, "DisplaySeconds")),
            };

            trigger.SetInternalLogKeywordText(Get(row, "InternalLogKeywords"));
            if (trigger.GetInternalLogKeywords().Count == 0 && !string.IsNullOrWhiteSpace(trigger.InternalLogKeyword))
            {
                trigger.SetInternalLogKeywordText(trigger.InternalLogKeyword);
            }

            ValidateImportedTriggerKind(listKind, trigger);
            ValidateImportedTriggerValues(trigger);

            if (savedTriggerIds.Contains(trigger.TriggerId) || !importTriggerIds.Add(trigger.TriggerId))
            {
                throw new IllegalImportException("トリガーIDが重複しています。");
            }

            var boxId = trigger.TriggerBoxId.Trim();
            var boxName = NormalizeImportedName(GetRequired(row, "TriggerBoxName"));
            if (!HappyTriggerSetting.TryGetManagementIdNumber(boxId, "BOX", out _))
            {
                throw new IllegalImportException("トリガーボックスIDが不正です。");
            }

            if (savedBoxIds.Contains(boxId))
            {
                throw new IllegalImportException("トリガーボックスIDが既に存在します。");
            }

            if (importBoxNames.TryGetValue(boxId, out var existingBoxName))
            {
                if (!string.Equals(existingBoxName, boxName, StringComparison.Ordinal))
                {
                    throw new IllegalImportException("同一ボックスIDに異なる名称が設定されています。");
                }
            }
            else
            {
                importBoxNames[boxId] = boxName;
            }

            var labelId = trigger.TriggerLabelId.Trim();
            var labelName = NormalizeImportedName(Get(row, "TriggerLabelName"));
            if (!string.IsNullOrWhiteSpace(labelId))
            {
                if (!HappyTriggerSetting.TryGetManagementIdNumber(labelId, "Lab", out _))
                {
                    throw new IllegalImportException("トリガーラベルIDが不正です。");
                }

                if (savedLabelIds.Contains(labelId))
                {
                    throw new IllegalImportException("トリガーラベルIDが既に存在します。");
                }

            }

            var importRow = new ImportRow
            {
                ListKind = listKind,
                Trigger = trigger,
                BoxId = boxId,
                BoxName = boxName,
                LabelId = labelId,
                LabelName = labelName,
            };

            if (!string.IsNullOrWhiteSpace(labelId))
            {
                if (importLabelRows.TryGetValue(labelId, out var existingLabelRow))
                {
                    if (!string.Equals(existingLabelRow.BoxId, boxId, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(existingLabelRow.LabelName, labelName, StringComparison.Ordinal))
                    {
                        throw new IllegalImportException("同一ラベルIDに異なる情報が設定されています。");
                    }
                }
                else
                {
                    importLabelRows[labelId] = importRow;
                }
            }

            rows.Add(importRow);
        }

        if (rows.Count == 0)
        {
            throw new IllegalImportException("import対象のトリガーがありません。");
        }

        var allImportTriggerIds = rows.Select(row => row.Trigger.TriggerId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row.Trigger.UsePrerequisite && !string.IsNullOrWhiteSpace(row.Trigger.PrerequisiteTriggerId))
            {
                if (!savedTriggerIds.Contains(row.Trigger.PrerequisiteTriggerId) && !allImportTriggerIds.Contains(row.Trigger.PrerequisiteTriggerId))
                {
                    throw new IllegalImportException("前提トリガーIDが存在しません。");
                }
            }
        }

        return rows;
    }

    private static List<List<string>> ParseCsv(string csvText)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csvText.Length; i++)
        {
            var c = csvText[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csvText.Length && csvText[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\r')
                {
                    if (i + 1 < csvText.Length && csvText[i + 1] == '\n')
                    {
                        i++;
                    }

                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else if (c == '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else
                {
                    field.Append(c);
                }
            }
        }

        if (inQuotes)
        {
            throw new IllegalImportException("CSVのクォートが閉じられていません。");
        }

        row.Add(field.ToString());
        if (row.Count > 1 || !string.IsNullOrWhiteSpace(row[0]) || rows.Count == 0)
        {
            rows.Add(row);
        }

        return rows;
    }

    private static string GetRequired(IReadOnlyDictionary<string, string> row, string key)
    {
        var value = Get(row, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new IllegalImportException($"必須項目が不足しています: {key}");
        }

        return value;
    }

    private static string Get(IReadOnlyDictionary<string, string> row, string key)
    {
        return row.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
    }

    private static string NormalizeImportedName(string value)
    {
        var trimmed = RemoveLineBreaks(value ?? string.Empty).Trim();
        return string.Equals(trimmed, "名称未設定", StringComparison.OrdinalIgnoreCase) ? string.Empty : trimmed;
    }

    private static TriggerListKind ParseTriggerListKind(string value)
    {
        var normalized = value.Trim();
        if (string.Equals(normalized, "image", StringComparison.OrdinalIgnoreCase))
        {
            return TriggerListKind.Image;
        }

        if (string.Equals(normalized, "text", StringComparison.OrdinalIgnoreCase))
        {
            return TriggerListKind.Text;
        }

        if (string.Equals(normalized, "ffxivlog", StringComparison.OrdinalIgnoreCase))
        {
            return TriggerListKind.FfxivLog;
        }

        throw new IllegalImportException("TriggerListKindが不正です。");
    }

    private static bool ParseEnabled(string value)
    {
        if (string.Equals(value.Trim(), "Enabled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value.Trim(), "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new IllegalImportException("Enabledが不正です。");
    }

    private static bool ParseBool(string value)
    {
        var trimmed = value.Trim();
        if (string.Equals(trimmed, "TRUE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "ON", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(trimmed, "FALSE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "OFF", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new IllegalImportException("bool値が不正です。");
    }

    private static int ParseInt(string value)
    {
        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new IllegalImportException("整数値が不正です。");
        }

        return result;
    }

    private static float ParseFloat(string value)
    {
        if (!float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            throw new IllegalImportException("数値が不正です。");
        }

        if (float.IsNaN(result) || float.IsInfinity(result))
        {
            throw new IllegalImportException("数値が不正です。");
        }

        return result;
    }

    private static TextFontDesign ParseTextFontDesign(string value)
    {
        if (!Enum.TryParse<TextFontDesign>(value.Trim(), true, out var result) || !Enum.IsDefined(typeof(TextFontDesign), result))
        {
            throw new IllegalImportException("フォント設定が不正です。");
        }

        return result;
    }

    private static void ValidateImportedTriggerKind(TriggerListKind listKind, HappyTriggerSetting trigger)
    {
        if (listKind == TriggerListKind.Image)
        {
            if (!HappyTriggerSetting.IsValidTriggerId(trigger.TriggerId, "I") || trigger.DisplayTextMode || trigger.UseFfxivLogReference)
            {
                throw new IllegalImportException("画像トリガー情報が不正です。");
            }
        }
        else if (listKind == TriggerListKind.Text)
        {
            if (!HappyTriggerSetting.IsValidTriggerId(trigger.TriggerId, "T") || !trigger.DisplayTextMode || trigger.UseFfxivLogReference)
            {
                throw new IllegalImportException("テキストトリガー情報が不正です。");
            }
        }
        else
        {
            var isValidFfxivLogId = HappyTriggerSetting.IsValidFfxivLogTriggerId(trigger.TriggerId);
            if (!isValidFfxivLogId || !trigger.UseFfxivLogReference)
            {
                throw new IllegalImportException("FFXIV Logトリガー情報が不正です。");
            }
        }
    }

    private static void ValidateImportedTriggerValues(HappyTriggerSetting trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger.TriggerBoxId))
        {
            throw new IllegalImportException("所属ボックスIDが不足しています。");
        }

        if (trigger.WaitSeconds < 0.0f || trigger.WaitSeconds > 600.0f)
        {
            throw new IllegalImportException("待機時間が範囲外です。");
        }

        if (trigger.DisplaySeconds <= 0.0f || trigger.DisplaySeconds > 3600.0f)
        {
            throw new IllegalImportException("表示時間が範囲外です。");
        }

        if (trigger.ImageWidth <= 0.0f || trigger.ImageHeight <= 0.0f || trigger.ImageSize <= 0.0f || trigger.ScalePercent <= 0.0f)
        {
            throw new IllegalImportException("画像サイズ設定が不正です。");
        }

        if (trigger.TextSize < 8.0f || trigger.TextSize > 256.0f)
        {
            throw new IllegalImportException("テキストサイズが範囲外です。");
        }

        ValidateColor(trigger.TextShadowColorR, trigger.TextShadowColorG, trigger.TextShadowColorB, trigger.TextShadowColorA);
        ValidateColor(trigger.TextColorR, trigger.TextColorG, trigger.TextColorB, trigger.TextColorA);
        ValidateColor(trigger.TextOutlineColorR, trigger.TextOutlineColorG, trigger.TextOutlineColorB, trigger.TextOutlineColorA);

        if (trigger.UseFfxivLogReference)
        {
            if (string.IsNullOrWhiteSpace(trigger.BattleLogKeyword) && trigger.GetInternalLogKeywords().Count == 0)
            {
                throw new IllegalImportException("FFXIV Log参照条件が不足しています。");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(trigger.Keyword))
            {
                throw new IllegalImportException("トリガー文字が不足しています。");
            }
        }

        if (trigger.DisplayTextMode)
        {
            if (string.IsNullOrWhiteSpace(trigger.DisplayText))
            {
                throw new IllegalImportException("表示テキストが不足しています。");
            }

            if (trigger.EnableVoiceVox)
            {
                if (string.IsNullOrWhiteSpace(trigger.VoiceVoxEndpoint)
                    || !Uri.TryCreate(trigger.VoiceVoxEndpoint.Trim(), UriKind.Absolute, out var voiceVoxUri)
                    || (voiceVoxUri.Scheme != Uri.UriSchemeHttp && voiceVoxUri.Scheme != Uri.UriSchemeHttps))
                {
                    throw new IllegalImportException("VOICEVOX URLが不正です。");
                }

                if (trigger.VoiceVoxSpeakerId < 0)
                {
                    throw new IllegalImportException("VOICEVOX Speaker IDが不正です。");
                }
            }
        }
        else
        {
            if (trigger.EnableVoiceVox)
            {
                throw new IllegalImportException("画像トリガーでVOICEVOX読み上げは使用できません。");
            }

            if (string.IsNullOrWhiteSpace(trigger.ImagePath))
            {
                throw new IllegalImportException("画像パスが不足しています。");
            }

            if (trigger.IsWebImage)
            {
                if (!Uri.TryCreate(trigger.ImagePath, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    throw new IllegalImportException("Web画像URLが不正です。");
                }
            }
            else if (!File.Exists(trigger.ImagePath))
            {
                throw new IllegalImportException("ローカル画像ファイルが存在しません。");
            }
        }

        if (trigger.EnableStatusRemainingAppend && (string.IsNullOrWhiteSpace(trigger.StatusRemainingJob) || string.IsNullOrWhiteSpace(trigger.StatusRemainingStatusName)))
        {
            throw new IllegalImportException("残り時間参照設定が不足しています。");
        }
    }

    private static void ValidateColor(float r, float g, float b, float a)
    {
        if (r < 0.0f || r > 1.0f || g < 0.0f || g > 1.0f || b < 0.0f || b > 1.0f || a < 0.0f || a > 1.0f)
        {
            throw new IllegalImportException("色設定が範囲外です。");
        }
    }

    private bool DrawTriggerTreeRowsForLabel(string labelId)
    {
        var triggers = this.GetAllTriggers()
            .Where(trigger => string.Equals(trigger.TriggerLabelId, labelId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (triggers.Count == 0)
        {
            ImGui.TextDisabled("このラベルに所属しているトリガーはありません。");
            return false;
        }

        foreach (var trigger in triggers)
        {
            if (this.DrawTriggerTreeRow(trigger))
            {
                return true;
            }
        }

        return false;
    }

    private bool DrawTriggerTreeRow(HappyTriggerSetting trigger)
    {
        if (!this.TryFindTriggerLocation(trigger, out var listKind, out var index))
        {
            ImGui.BulletText(GetTriggerDisplayName(trigger));
            return false;
        }

        var rowKey = GetManagementTriggerKey(trigger, listKind, index);
        var isSelected = string.Equals(this.selectedManagementTriggerKey, rowKey, StringComparison.OrdinalIgnoreCase);
        var displayName = GetTriggerDisplayName(trigger);

        ImGui.Bullet();
        ImGui.SameLine();

        if (isSelected)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.92f, 0.25f, 1.0f));
        }

        ImGui.TextUnformatted(displayName);

        if (isSelected)
        {
            ImGui.PopStyleColor();
        }

        if (ImGui.IsItemClicked())
        {
            this.selectedManagementTriggerKey = isSelected ? string.Empty : rowKey;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(isSelected
                ? "クリックすると、このトリガーの詳細表示を閉じます。"
                : "クリックすると、このトリガーの保存済み設定を表示します。");
        }

        ImGui.SameLine();
        if (ImGui.SmallButton($"テスト##management_test_{rowKey}"))
        {
            this.testTrigger(trigger);
        }

        ImGui.SameLine();
        if (ImGui.SmallButton($"編集##management_edit_{rowKey}"))
        {
            this.BeginEditTrigger(index, listKind, trigger);
        }

        ImGui.SameLine();
        if (ImGui.SmallButton($"コピー##management_copy_{rowKey}"))
        {
            this.CopyTrigger(index, listKind);
        }

        if (listKind == TriggerListKind.FfxivLog)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"ID更新##management_id_update_{rowKey}"))
            {
                this.manualTriggerIdEditKey = rowKey;
                this.manualTriggerIdInput = trigger.TriggerId ?? string.Empty;
                this.manualTriggerIdMessage = "ログトリガーIDを半角英数字1〜6桁で入力してください。";
            }
        }

        ImGui.SameLine();
        if (ImGui.SmallButton($"削除##management_delete_{rowKey}"))
        {
            this.DeleteTrigger(index, listKind);
            return true;
        }

        if (listKind == TriggerListKind.FfxivLog && string.Equals(this.manualTriggerIdEditKey, rowKey, StringComparison.OrdinalIgnoreCase))
        {
            this.DrawManualTriggerIdEditor(trigger, listKind, index, rowKey);
        }

        if (isSelected)
        {
            this.DrawManagementTriggerDetail(trigger);
        }

        return false;
    }

    private void DrawManualTriggerIdEditor(HappyTriggerSetting trigger, TriggerListKind listKind, int index, string oldRowKey)
    {
        ImGui.Indent(24.0f);
        ImGui.TextDisabled("ログトリガーIDを手動更新します。使用できる文字は半角英数字のみ、最大6桁です。");

        var newId = this.manualTriggerIdInput ?? string.Empty;
        ImGui.SetNextItemWidth(220.0f);
        if (InputTextJapanese($"新しいID##manual_trigger_id_{oldRowKey}", ref newId, 64))
        {
            this.manualTriggerIdInput = newId;
        }

        ImGui.SameLine();
        if (ImGui.SmallButton($"更新##manual_trigger_id_apply_{oldRowKey}"))
        {
            this.TryUpdateManualFfxivLogTriggerId(trigger, listKind, index, oldRowKey);
        }

        ImGui.SameLine();
        if (ImGui.SmallButton($"キャンセル##manual_trigger_id_cancel_{oldRowKey}"))
        {
            this.manualTriggerIdEditKey = string.Empty;
            this.manualTriggerIdInput = string.Empty;
            this.manualTriggerIdMessage = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(this.manualTriggerIdMessage))
        {
            var color = this.manualTriggerIdMessage.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase)
                ? new Vector4(1.0f, 0.35f, 0.35f, 1.0f)
                : new Vector4(1.0f, 0.92f, 0.25f, 1.0f);
            ImGui.TextColored(color, this.manualTriggerIdMessage);
        }

        ImGui.Unindent(24.0f);
    }

    private void TryUpdateManualFfxivLogTriggerId(HappyTriggerSetting trigger, TriggerListKind listKind, int index, string oldRowKey)
    {
        if (listKind != TriggerListKind.FfxivLog)
        {
            this.manualTriggerIdMessage = "[ERROR]ログトリガー以外のIDは手動更新できません。";
            return;
        }

        if (index < 0 || index >= this.configuration.FfxivLogTriggers.Count)
        {
            this.manualTriggerIdMessage = "[ERROR]更新対象のログトリガーが見つかりません。";
            return;
        }

        var newId = (this.manualTriggerIdInput ?? string.Empty).Trim();
        if (!HappyTriggerSetting.IsValidManualTriggerId(newId))
        {
            this.manualTriggerIdMessage = "[ERROR]IDは半角英数字1〜6桁で入力してください。";
            return;
        }

        if (this.IsDuplicateTriggerIdExcept(newId, listKind, index))
        {
            this.manualTriggerIdMessage = "[ERROR]指定したIDはすでに保存済みトリガーで使用されています。";
            return;
        }

        var oldId = string.IsNullOrWhiteSpace(trigger.TriggerId) ? "未採番" : trigger.TriggerId.Trim();
        trigger.TriggerId = newId;

        if (!string.Equals(oldId, "未採番", StringComparison.OrdinalIgnoreCase))
        {
            this.UpdatePrerequisiteTriggerReferences(oldId, newId);
        }

        this.saveConfig();

        var newRowKey = GetManagementTriggerKey(trigger, listKind, index);
        if (string.Equals(this.selectedManagementTriggerKey, oldRowKey, StringComparison.OrdinalIgnoreCase))
        {
            this.selectedManagementTriggerKey = newRowKey;
        }

        this.manualTriggerIdEditKey = string.Empty;
        this.manualTriggerIdInput = string.Empty;
        this.manualTriggerIdMessage = $"ログトリガーIDを更新しました: {oldId} -> {newId}";
    }

    private void UpdatePrerequisiteTriggerReferences(string oldId, string newId)
    {
        foreach (var trigger in this.GetAllTriggers())
        {
            if (string.Equals(trigger.PrerequisiteTriggerId, oldId, StringComparison.OrdinalIgnoreCase))
            {
                trigger.PrerequisiteTriggerId = newId;
            }
        }
    }

    private void DrawManagementTriggerDetail(HappyTriggerSetting trigger)
    {
        ImGui.Indent(24.0f);

        if (ImGui.BeginTable($"ManagementTriggerDetail_{this.selectedManagementTriggerKey}", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("項目", ImGuiTableColumnFlags.WidthFixed, 180.0f);
            ImGui.TableSetupColumn("内容");

            this.DrawTriggerDetailRow("有効", trigger.Enabled ? "Enabled" : "Disabled");
            this.DrawTriggerDetailRow("ID", string.IsNullOrWhiteSpace(trigger.TriggerId) ? "未採番" : trigger.TriggerId);
            this.DrawTriggerDetailRow("名称", string.IsNullOrWhiteSpace(trigger.TriggerName) ? "名称未設定" : trigger.TriggerName);
            this.DrawTriggerDetailRow("種別", GetTriggerKindLabel(trigger));
            this.DrawTriggerDetailRow("所属ボックス", string.IsNullOrWhiteSpace(trigger.TriggerBoxId) ? "未分類" : this.GetTriggerBoxDisplayName(trigger.TriggerBoxId, "不明なBOX"));
            this.DrawTriggerDetailRow("所属ラベル", string.IsNullOrWhiteSpace(trigger.TriggerLabelId) ? "未設定" : this.GetTriggerLabelDisplayName(trigger.TriggerLabelId, "不明なラベル"));
            this.DrawTriggerDetailRow("判定", trigger.ExactMatch ? "完全一致" : "部分一致");
            this.DrawTriggerDetailRow("トリガー文字", ToDisplayValue(trigger.Keyword));
            this.DrawTriggerDetailRow("FFXIV Log参照", trigger.UseFfxivLogReference ? "ON" : "OFF");
            this.DrawTriggerDetailRow("前提", trigger.UsePrerequisite ? ToDisplayValue(trigger.PrerequisiteTriggerId, "ON：未指定") : "OFF");
            this.DrawTriggerDetailRow("バトルログ", ToDisplayValue(trigger.BattleLogKeyword));
            this.DrawTriggerDetailRow("内部ログ", ToDisplayValue(trigger.GetInternalLogKeywordText()));
            this.DrawTriggerDetailRow("ステータス残り時間", trigger.EnableStatusRemainingAppend ? "ON" : "OFF");
            this.DrawTriggerDetailRow("残り時間参照ジョブ", ToDisplayValue(trigger.StatusRemainingJob));
            this.DrawTriggerDetailRow("残り時間参照ステータス", ToDisplayValue(trigger.StatusRemainingStatusName));
            this.DrawTriggerDetailRow("表示種別", trigger.DisplayTextMode ? "テキスト" : "画像");
            this.DrawTriggerDetailRow("表示テキスト", ToDisplayValue(trigger.DisplayText));
            this.DrawTriggerDetailRow("VOICEVOX読み上げ", trigger.EnableVoiceVox ? "ON" : "OFF");
            this.DrawTriggerDetailRow("VOICEVOX URL", ToDisplayValue(trigger.VoiceVoxEndpoint));
            this.DrawTriggerDetailRow("VOICEVOX Speaker ID", trigger.VoiceVoxSpeakerId.ToString(CultureInfo.InvariantCulture));
            this.DrawTriggerDetailRow("画像種別", trigger.IsWebImage ? "Web画像" : "ローカル画像");
            this.DrawTriggerDetailRow("画像パス", ToDisplayValue(trigger.ImagePath));
            this.DrawTriggerDetailRow("画面位置", $"X={trigger.PositionX:0.##} / Y={trigger.PositionY:0.##}");
            this.DrawTriggerDetailRow("待機時間", $"{trigger.WaitSeconds:0.##} 秒");
            this.DrawTriggerDetailRow("表示時間", $"{trigger.DisplaySeconds:0.##} 秒");
            this.DrawTriggerDetailRow("元画像サイズ使用", trigger.UseOriginalImageSize ? "ON" : "OFF");
            this.DrawTriggerDetailRow("画像幅", $"{trigger.ImageWidth:0.##}");
            this.DrawTriggerDetailRow("画像高さ", $"{trigger.ImageHeight:0.##}");
            this.DrawTriggerDetailRow("画像サイズ旧設定", $"{trigger.ImageSize:0.##}");
            this.DrawTriggerDetailRow("倍率", $"{(trigger.ScalePercent <= 0.0f ? 100.0f : trigger.ScalePercent):0.##}%");
            this.DrawTriggerDetailRow("テキストサイズ", $"{trigger.TextSize:0.##}");
            this.DrawTriggerDetailRow("フォント", GetTextFontDesignLabel(trigger.TextFontDesign));
            this.DrawTriggerDetailRow("文字スナップ", trigger.EnableTextPixelSnap ? "ON" : "OFF");
            this.DrawTriggerDetailRow("影位置", $"X={trigger.TextShadowOffsetX:0.##} / Y={trigger.TextShadowOffsetY:0.##}");
            this.DrawTriggerDetailRow("影・発光色", $"R={trigger.TextShadowColorR:0.##} G={trigger.TextShadowColorG:0.##} B={trigger.TextShadowColorB:0.##} A={trigger.TextShadowColorA:0.##}");
            this.DrawTriggerDetailRow("テキスト色", $"R={trigger.TextColorR:0.##} G={trigger.TextColorG:0.##} B={trigger.TextColorB:0.##} A={trigger.TextColorA:0.##}");
            this.DrawTriggerDetailRow("枠線", trigger.EnableTextOutline ? "ON" : "OFF");
            this.DrawTriggerDetailRow("枠線太さ", $"{trigger.TextOutlineThickness:0.##}");
            this.DrawTriggerDetailRow("枠線色", $"R={trigger.TextOutlineColorR:0.##} G={trigger.TextOutlineColorG:0.##} B={trigger.TextOutlineColorB:0.##} A={trigger.TextOutlineColorA:0.##}");
            this.DrawTriggerDetailRow("フェードイン", trigger.EnableTextFadeIn ? "ON" : "OFF");
            this.DrawTriggerDetailRow("フェードイン時間", $"{trigger.TextFadeInSeconds:0.##} 秒");

            ImGui.EndTable();
        }

        ImGui.Unindent(24.0f);
    }

    private void DrawTriggerDetailRow(string label, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);
        ImGui.TableSetColumnIndex(1);
        ImGui.TextWrapped(value);
    }

    private bool TryFindTriggerLocation(HappyTriggerSetting trigger, out TriggerListKind listKind, out int index)
    {
        for (var i = 0; i < this.configuration.Triggers.Count; i++)
        {
            if (ReferenceEquals(this.configuration.Triggers[i], trigger) || IsSameTrigger(this.configuration.Triggers[i], trigger))
            {
                listKind = TriggerListKind.Image;
                index = i;
                return true;
            }
        }

        for (var i = 0; i < this.configuration.TextTriggers.Count; i++)
        {
            if (ReferenceEquals(this.configuration.TextTriggers[i], trigger) || IsSameTrigger(this.configuration.TextTriggers[i], trigger))
            {
                listKind = TriggerListKind.Text;
                index = i;
                return true;
            }
        }

        for (var i = 0; i < this.configuration.FfxivLogTriggers.Count; i++)
        {
            if (ReferenceEquals(this.configuration.FfxivLogTriggers[i], trigger) || IsSameTrigger(this.configuration.FfxivLogTriggers[i], trigger))
            {
                listKind = TriggerListKind.FfxivLog;
                index = i;
                return true;
            }
        }

        listKind = TriggerListKind.Image;
        index = -1;
        return false;
    }

    private static bool IsSameTrigger(HappyTriggerSetting left, HappyTriggerSetting right)
    {
        return !string.IsNullOrWhiteSpace(left.TriggerId)
            && !string.IsNullOrWhiteSpace(right.TriggerId)
            && string.Equals(left.TriggerId, right.TriggerId, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetManagementTriggerKey(HappyTriggerSetting trigger, TriggerListKind listKind, int index)
    {
        var triggerId = string.IsNullOrWhiteSpace(trigger.TriggerId) ? $"index{index}" : trigger.TriggerId.Trim();
        return $"{GetTriggerListKindKey(listKind)}_{triggerId}";
    }

    private static string GetTriggerKindLabel(HappyTriggerSetting trigger)
    {
        if (trigger.UseFfxivLogReference)
        {
            return "FFXIV Log参照用";
        }

        return trigger.DisplayTextMode ? "テキスト表示用" : "画像表示用";
    }

    private static string ToDisplayValue(string? value, string emptyText = "未設定")
    {
        return string.IsNullOrWhiteSpace(value) ? emptyText : value.Trim();
    }

    private static string GetTriggerDisplayName(HappyTriggerSetting trigger)
    {
        var triggerId = string.IsNullOrWhiteSpace(trigger.TriggerId)
            ? "未採番"
            : trigger.TriggerId.Trim();
        var triggerName = string.IsNullOrWhiteSpace(trigger.TriggerName)
            ? "名称未設定"
            : trigger.TriggerName.Trim();

        return $"{triggerId} : {triggerName}";
    }

    private List<HappyTriggerSetting> GetAllTriggers()
    {
        return this.configuration.Triggers
            .Concat(this.configuration.TextTriggers)
            .Concat(this.configuration.FfxivLogTriggers)
            .ToList();
    }

    private void RemoveTriggerBox(string boxId)
    {
        this.configuration.TriggerBoxes.RemoveAll(box => string.Equals(box.BoxId, boxId, StringComparison.OrdinalIgnoreCase));
        var removedLabelIds = this.configuration.TriggerLabels
            .Where(label => string.Equals(label.BoxId, boxId, StringComparison.OrdinalIgnoreCase))
            .Select(label => label.LabelId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        this.configuration.TriggerLabels.RemoveAll(label => string.Equals(label.BoxId, boxId, StringComparison.OrdinalIgnoreCase));

        foreach (var trigger in this.GetAllTriggers())
        {
            if (string.Equals(trigger.TriggerBoxId, boxId, StringComparison.OrdinalIgnoreCase))
            {
                trigger.TriggerBoxId = string.Empty;
            }

            if (removedLabelIds.Contains(trigger.TriggerLabelId))
            {
                trigger.TriggerLabelId = string.Empty;
            }
        }

        this.saveConfig();
    }

    private void RemoveTriggerLabel(string labelId)
    {
        this.configuration.TriggerLabels.RemoveAll(label => string.Equals(label.LabelId, labelId, StringComparison.OrdinalIgnoreCase));
        foreach (var trigger in this.GetAllTriggers())
        {
            if (string.Equals(trigger.TriggerLabelId, labelId, StringComparison.OrdinalIgnoreCase))
            {
                trigger.TriggerLabelId = string.Empty;
            }
        }

        this.saveConfig();
    }

    private string GetTriggerBoxDisplayName(string? boxId, string fallback)
    {
        if (string.IsNullOrWhiteSpace(boxId))
        {
            return fallback;
        }

        var box = this.configuration.TriggerBoxes.FirstOrDefault(box => string.Equals(box.BoxId, boxId, StringComparison.OrdinalIgnoreCase));
        if (box == null)
        {
            return $"{boxId} : 不明";
        }

        var boxName = string.IsNullOrWhiteSpace(box.Name) ? fallback : box.Name.Trim();
        return $"{box.BoxId} : {boxName}";
    }

    private string GetTriggerLabelDisplayName(string? labelId, string fallback)
    {
        if (string.IsNullOrWhiteSpace(labelId))
        {
            return fallback;
        }

        var label = this.configuration.TriggerLabels.FirstOrDefault(label => string.Equals(label.LabelId, labelId, StringComparison.OrdinalIgnoreCase));
        if (label == null)
        {
            return $"{labelId} : 不明";
        }

        var labelName = string.IsNullOrWhiteSpace(label.Name) ? fallback : label.Name.Trim();
        return $"{label.LabelId} : {labelName}";
    }

    private string GenerateNextManagementId(string prefix)
    {
        var maxNumber = 0;

        foreach (var box in this.configuration.TriggerBoxes)
        {
            if (HappyTriggerSetting.TryGetManagementIdNumber(box.BoxId, prefix, out var number))
            {
                maxNumber = Math.Max(maxNumber, number);
            }
        }

        foreach (var label in this.configuration.TriggerLabels)
        {
            if (HappyTriggerSetting.TryGetManagementIdNumber(label.LabelId, prefix, out var number))
            {
                maxNumber = Math.Max(maxNumber, number);
            }
        }

        return HappyTriggerSetting.FormatManagementId(prefix, maxNumber + 1);
    }

    private void DrawFfxivLogTab()
    {
        ImGui.Spacing();

        if (ImGui.Button("ログをクリア", new Vector2(140.0f, 32.0f)))
        {
            this.clearFfxivLogs();
        }

        ImGui.SameLine();
        ImGui.Checkbox("バトルログ自動スクロール", ref this.autoScrollBattleLog);
        ImGui.SameLine();
        ImGui.Checkbox("内部ログ自動スクロール", ref this.autoScrollInternalLog);

        var showDebugLogs = this.configuration.ShowFfxivLogReferenceDebugLogs;
        if (ImGui.Checkbox("FFXIV Log参照デバッグログを表示", ref showDebugLogs))
        {
            this.configuration.ShowFfxivLogReferenceDebugLogs = showDebugLogs;
            this.saveConfig();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("OFF推奨");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("ONにすると、FFXIV Log参照トリガーの Matched / Missing 状態を内部ログに出力します。デバッグログはONでもトリガー判定対象から除外されます。");
        }

        var pairWindowSeconds = this.configuration.FfxivLogReferencePairWindowSeconds;
        ImGui.SetNextItemWidth(160.0f);
        if (ImGui.InputFloat("組み合わせ判定猶予秒数", ref pairWindowSeconds, 1.0f, 5.0f))
        {
            this.configuration.FfxivLogReferencePairWindowSeconds = Math.Clamp(pairWindowSeconds, 1.0f, 120.0f);
            this.saveConfig();
        }

        ImGui.SameLine();
        ImGui.TextDisabled("1.0〜120.0秒。バトルログ/内部ログの全条件がこの秒数内に揃った場合だけ発火します。");

        ImGui.TextDisabled("上段はFFXIVのチャットログ由来のログ、下段はHappyTrigger内部処理ログです。");
        ImGui.Spacing();

        var availableHeight = ImGui.GetContentRegionAvail().Y;
        var battleHeight = Math.Max(160.0f, availableHeight * 0.48f);
        var internalHeight = Math.Max(160.0f, availableHeight - battleHeight - 48.0f);

        this.DrawLogBox("バトルログ", "HappyTriggerBattleLogChild", this.getBattleLogs(), battleHeight, this.autoScrollBattleLog, ref this.selectedBattleLogIndex, ref this.battleLogSearchText);
        ImGui.Spacing();
        this.DrawLogBox("内部ログ", "HappyTriggerInternalLogChild", this.getInternalLogs(), internalHeight, this.autoScrollInternalLog, ref this.selectedInternalLogIndex, ref this.internalLogSearchText);
    }

    private void DrawLogBox(
        string title,
        string childId,
        IReadOnlyList<FfxivLogEntry> logs,
        float height,
        bool autoScroll,
        ref int selectedIndex,
        ref string searchText)
    {
        ImGui.Text(title);

        ImGui.SameLine();
        ImGui.TextDisabled("検索:");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(320.0f);
        if (InputTextJapanese($"##{childId}_search", ref searchText, 512))
        {
            searchText = RemoveLineBreaks(searchText);
        }

        ImGui.SameLine();
        if (ImGui.SmallButton($"クリア##{childId}_search_clear"))
        {
            searchText = string.Empty;
        }

        if (selectedIndex >= logs.Count)
        {
            selectedIndex = -1;
        }

        var filteredLogIndexes = GetFilteredLogIndexes(logs, searchText);
        var hasSearchText = !string.IsNullOrWhiteSpace(searchText);

        var logText = BuildLogText(logs, filteredLogIndexes);

        ImGui.SameLine();
        if (ImGui.SmallButton($"表示中ログを全コピー##{childId}_copy_all"))
        {
            ImGui.SetClipboardText(logText);
        }

        ImGui.TextDisabled(hasSearchText
            ? $"検索結果: {filteredLogIndexes.Count} / {logs.Count} 件。テキストエリア内をドラッグして、任意の複数行をコピーできます。"
            : "テキストエリア内をドラッグして、任意の複数行をコピーできます。");

        var logBoxHeight = Math.Min(height, CalculateLogBoxHeight(MaxVisibleLogRows));

        if (logs.Count == 0)
        {
            selectedIndex = -1;
            var emptyText = "ログはまだありません。";
            ImGui.InputTextMultiline(
                $"##{childId}_text_area",
                ref emptyText,
                Math.Max(emptyText.Length + 1, 256),
                new Vector2(-1.0f, logBoxHeight),
                ImGuiInputTextFlags.ReadOnly);
        }
        else if (filteredLogIndexes.Count == 0)
        {
            selectedIndex = -1;
            var emptyText = "検索条件に一致するログはありません。";
            ImGui.InputTextMultiline(
                $"##{childId}_text_area",
                ref emptyText,
                Math.Max(emptyText.Length + 1, 256),
                new Vector2(-1.0f, logBoxHeight),
                ImGuiInputTextFlags.ReadOnly);
        }
        else
        {
            selectedIndex = -1;
            var textAreaBufferSize = Math.Max(logText.Length * 4 + 1, 4096);
            ImGui.InputTextMultiline(
                $"##{childId}_text_area",
                ref logText,
                textAreaBufferSize,
                new Vector2(-1.0f, logBoxHeight),
                ImGuiInputTextFlags.ReadOnly);

            if (!hasSearchText && autoScroll && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("ログの末尾へ移動する場合は、テキストエリア内をクリックして Ctrl+End を押してください。");
            }
        }
    }


    private static float CalculateLogBoxHeight(int maxRows)
    {
        var rowHeight = ImGui.GetTextLineHeightWithSpacing();
        var paddingY = ImGui.GetStyle().WindowPadding.Y * 2.0f;
        return Math.Max(120.0f, (rowHeight * maxRows) + paddingY);
    }

    private static string BuildLogText(IReadOnlyList<FfxivLogEntry> logs, IReadOnlyList<int> filteredLogIndexes)
    {
        if (logs.Count == 0 || filteredLogIndexes.Count == 0)
        {
            return string.Empty;
        }

        return string.Join("\n", filteredLogIndexes.Select(index => logs[index].DisplayText));
    }

    private static List<int> GetFilteredLogIndexes(IReadOnlyList<FfxivLogEntry> logs, string searchText)
    {
        var results = new List<int>();
        var normalizedSearchText = searchText?.Trim() ?? string.Empty;

        for (var i = 0; i < logs.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(normalizedSearchText)
                || logs[i].DisplayText.Contains(normalizedSearchText, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(i);
            }
        }

        return results;
    }

    private void SelectLocalImageFile()
    {
        if (!NativeFileDialogService.TryOpenImageFile(out var selectedPath))
        {
            return;
        }

        this.editTrigger.IsWebImage = false;
        this.editTrigger.ImagePath = selectedPath;

        if (ImageDimensionReader.TryRead(selectedPath, out var width, out var height))
        {
            this.editTrigger.ImageWidth = width;
            this.editTrigger.ImageHeight = height;
            this.editTrigger.ImageSize = width;
            this.imageSelectMessage = $"選択画像の解像度: {width} x {height}";
        }
        else
        {
            this.imageSelectMessage = "画像を選択しましたが、解像度を取得できませんでした。";
        }
    }

    private void SaveEditingTrigger()
    {
        var saveTargetKind = this.GetSaveTargetKind();
        this.editTrigger.UseFfxivLogReference = saveTargetKind == TriggerListKind.FfxivLog;
        if (this.editTrigger.UseFfxivLogReference)
        {
            this.editTrigger.NormalizeInternalLogKeywords();

            if (!this.editTrigger.UsePrerequisite)
            {
                this.editTrigger.PrerequisiteTriggerId = string.Empty;
            }
        }
        else
        {
            this.editTrigger.UsePrerequisite = false;
            this.editTrigger.PrerequisiteTriggerId = string.Empty;
        }

        this.editTrigger.TriggerName = RemoveLineBreaks(this.editTrigger.TriggerName ?? string.Empty).Trim();
        this.NormalizeEditingManagementAssignment();
        this.EnsureEditingTriggerId(saveTargetKind);

        if (this.editingIndex >= 0)
        {
            this.RemoveEditingSourceIfNeeded(saveTargetKind);
            this.SaveTriggerToTargetList(saveTargetKind);
        }
        else
        {
            this.AddTriggerToTargetList(saveTargetKind);
        }

        this.saveConfig();
        this.closePositionSettingPopup();
        this.ResetEditing();
    }

    private TriggerListKind GetSaveTargetKind()
    {
        if (this.editTrigger.UseFfxivLogReference)
        {
            return TriggerListKind.FfxivLog;
        }

        return this.editTrigger.DisplayTextMode ? TriggerListKind.Text : TriggerListKind.Image;
    }

    private void RemoveEditingSourceIfNeeded(TriggerListKind saveTargetKind)
    {
        if (this.editingIndex < 0 || this.editingKind == saveTargetKind)
        {
            return;
        }

        if (this.editingKind == TriggerListKind.FfxivLog && this.editingIndex < this.configuration.FfxivLogTriggers.Count)
        {
            this.configuration.FfxivLogTriggers.RemoveAt(this.editingIndex);
        }
        else if (this.editingKind == TriggerListKind.Text && this.editingIndex < this.configuration.TextTriggers.Count)
        {
            this.configuration.TextTriggers.RemoveAt(this.editingIndex);
        }
        else if (this.editingKind == TriggerListKind.Image && this.editingIndex < this.configuration.Triggers.Count)
        {
            this.configuration.Triggers.RemoveAt(this.editingIndex);
        }
    }

    private void SaveTriggerToTargetList(TriggerListKind saveTargetKind)
    {
        if (this.editingKind != saveTargetKind)
        {
            this.AddTriggerToTargetList(saveTargetKind);
            return;
        }

        if (saveTargetKind == TriggerListKind.FfxivLog && this.editingIndex < this.configuration.FfxivLogTriggers.Count)
        {
            this.configuration.FfxivLogTriggers[this.editingIndex] = this.editTrigger.Clone();
        }
        else if (saveTargetKind == TriggerListKind.Text && this.editingIndex < this.configuration.TextTriggers.Count)
        {
            this.configuration.TextTriggers[this.editingIndex] = this.editTrigger.Clone();
        }
        else if (saveTargetKind == TriggerListKind.Image && this.editingIndex < this.configuration.Triggers.Count)
        {
            this.configuration.Triggers[this.editingIndex] = this.editTrigger.Clone();
        }
        else
        {
            this.AddTriggerToTargetList(saveTargetKind);
        }
    }

    private void AddTriggerToTargetList(TriggerListKind saveTargetKind)
    {
        if (saveTargetKind == TriggerListKind.FfxivLog)
        {
            this.configuration.FfxivLogTriggers.Add(this.editTrigger.Clone());
        }
        else if (saveTargetKind == TriggerListKind.Text)
        {
            this.configuration.TextTriggers.Add(this.editTrigger.Clone());
        }
        else
        {
            this.configuration.Triggers.Add(this.editTrigger.Clone());
        }
    }

    private void NormalizeEditingManagementAssignment()
    {
        if (!string.IsNullOrWhiteSpace(this.editTrigger.TriggerLabelId))
        {
            var label = this.configuration.TriggerLabels.FirstOrDefault(label =>
                string.Equals(label.LabelId, this.editTrigger.TriggerLabelId, StringComparison.OrdinalIgnoreCase));

            if (label == null)
            {
                this.editTrigger.TriggerLabelId = string.Empty;
            }
            else
            {
                this.editTrigger.TriggerBoxId = label.BoxId;
            }
        }

        if (!string.IsNullOrWhiteSpace(this.editTrigger.TriggerBoxId)
            && !this.configuration.TriggerBoxes.Any(box => string.Equals(box.BoxId, this.editTrigger.TriggerBoxId, StringComparison.OrdinalIgnoreCase)))
        {
            this.editTrigger.TriggerBoxId = string.Empty;
            this.editTrigger.TriggerLabelId = string.Empty;
        }
    }

    private void EnsureEditingTriggerId(TriggerListKind saveTargetKind)
    {
        var prefix = saveTargetKind switch
        {
            TriggerListKind.FfxivLog => this.editTrigger.UsePrerequisite ? "X" : "F",
            TriggerListKind.Text => "T",
            _ => "I",
        };
        var currentId = this.editTrigger.TriggerId;

        var isValidCurrentId = saveTargetKind == TriggerListKind.FfxivLog
            ? HappyTriggerSetting.IsValidFfxivLogTriggerId(currentId)
            : HappyTriggerSetting.IsValidTriggerId(currentId, prefix);

        if (!isValidCurrentId || this.IsDuplicateTriggerId(currentId, saveTargetKind))
        {
            this.editTrigger.TriggerId = this.GenerateNextTriggerId(prefix);
        }
    }

    private bool IsDuplicateTriggerIdExcept(string? triggerId, TriggerListKind targetKind, int targetIndex)
    {
        if (string.IsNullOrWhiteSpace(triggerId))
        {
            return false;
        }

        var normalizedId = triggerId.Trim();

        for (var i = 0; i < this.configuration.Triggers.Count; i++)
        {
            if (targetKind == TriggerListKind.Image && targetIndex == i)
            {
                continue;
            }

            if (string.Equals(this.configuration.Triggers[i].TriggerId, normalizedId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        for (var i = 0; i < this.configuration.TextTriggers.Count; i++)
        {
            if (targetKind == TriggerListKind.Text && targetIndex == i)
            {
                continue;
            }

            if (string.Equals(this.configuration.TextTriggers[i].TriggerId, normalizedId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        for (var i = 0; i < this.configuration.FfxivLogTriggers.Count; i++)
        {
            if (targetKind == TriggerListKind.FfxivLog && targetIndex == i)
            {
                continue;
            }

            if (string.Equals(this.configuration.FfxivLogTriggers[i].TriggerId, normalizedId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsDuplicateTriggerId(string? triggerId, TriggerListKind saveTargetKind)
    {
        if (string.IsNullOrWhiteSpace(triggerId))
        {
            return false;
        }

        for (var i = 0; i < this.configuration.Triggers.Count; i++)
        {
            if (saveTargetKind == TriggerListKind.Image && this.editingKind == TriggerListKind.Image && this.editingIndex == i)
            {
                continue;
            }

            if (string.Equals(this.configuration.Triggers[i].TriggerId, triggerId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        for (var i = 0; i < this.configuration.TextTriggers.Count; i++)
        {
            if (saveTargetKind == TriggerListKind.Text && this.editingKind == TriggerListKind.Text && this.editingIndex == i)
            {
                continue;
            }

            if (string.Equals(this.configuration.TextTriggers[i].TriggerId, triggerId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        for (var i = 0; i < this.configuration.FfxivLogTriggers.Count; i++)
        {
            if (saveTargetKind == TriggerListKind.FfxivLog && this.editingKind == TriggerListKind.FfxivLog && this.editingIndex == i)
            {
                continue;
            }

            if (string.Equals(this.configuration.FfxivLogTriggers[i].TriggerId, triggerId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string GenerateNextTriggerId(string prefix)
    {
        var maxNumber = 0;

        foreach (var trigger in this.configuration.Triggers)
        {
            if (HappyTriggerSetting.TryGetTriggerIdNumber(trigger.TriggerId, prefix, out var number))
            {
                maxNumber = Math.Max(maxNumber, number);
            }
        }

        foreach (var trigger in this.configuration.TextTriggers)
        {
            if (HappyTriggerSetting.TryGetTriggerIdNumber(trigger.TriggerId, prefix, out var number))
            {
                maxNumber = Math.Max(maxNumber, number);
            }
        }

        foreach (var trigger in this.configuration.FfxivLogTriggers)
        {
            if (HappyTriggerSetting.TryGetTriggerIdNumber(trigger.TriggerId, prefix, out var number))
            {
                maxNumber = Math.Max(maxNumber, number);
            }
        }

        return HappyTriggerSetting.FormatTriggerId(prefix, maxNumber + 1);
    }

    private void ResetEditing()
    {
        this.editingIndex = -1;
        this.editingKind = TriggerListKind.Image;
        this.editTrigger = new HappyTriggerSetting();
        this.imageSelectMessage = string.Empty;
    }

}
