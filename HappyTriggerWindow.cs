using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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
    private int selectedBattleLogIndex = -1;
    private int selectedInternalLogIndex = -1;
    private bool requestOpenTriggerEditTab = false;

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
        this.Flags = ImGuiWindowFlags.NoCollapse;
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
            if (ImGui.InputText("トリガー文字", ref keyword, 512))
            {
                this.editTrigger.Keyword = keyword;
            }
        }

        var triggerIdText = string.IsNullOrWhiteSpace(this.editTrigger.TriggerId)
            ? "ID: 未採番（保存時に自動採番）"
            : $"ID: {this.editTrigger.TriggerId}";
        ImGui.TextDisabled(triggerIdText);

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
        if (ImGui.InputText("バトルログ", ref battleLogKeyword, 2048))
        {
            this.editTrigger.BattleLogKeyword = RemoveLineBreaks(battleLogKeyword);
        }

        var internalLogKeywords = this.GetEditingInternalLogKeywordsForUi();

        for (var i = 0; i < internalLogKeywords.Count; i++)
        {
            var internalLogKeyword = RemoveLineBreaks(internalLogKeywords[i] ?? string.Empty);
            ImGui.SetNextItemWidth(1300.0f);
            if (ImGui.InputText($"内部ログ {i + 1}", ref internalLogKeyword, 2048))
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
        if (ImGui.InputText("表示対象テキスト", ref displayText, 1024))
        {
            this.editTrigger.DisplayText = displayText;
        }

        var textSize = this.editTrigger.TextSize;
        ImGui.SetNextItemWidth(180.0f);
        if (ImGui.InputFloat("テキストサイズ", ref textSize, 1.0f, 10.0f))
        {
            this.editTrigger.TextSize = Math.Clamp(textSize, 8.0f, 256.0f);
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

        ImGui.TextDisabled("トリガー文字にヒットした場合、ここで指定した文字を画面位置X/Yに表示します。色・枠線・フェードインもこの設定が反映されます。");
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
        if (ImGui.InputText("画像のパス / URL", ref imagePath, 2048))
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

        if (ImGui.BeginTable("HappyTriggerTextTriggerTable", 12, tableFlags))
        {
            ImGui.TableSetupColumn("有効", ImGuiTableColumnFlags.WidthFixed, 50.0f);
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("判定", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("トリガー文字");
            ImGui.TableSetupColumn("表示テキスト");
            ImGui.TableSetupColumn("サイズ", ImGuiTableColumnFlags.WidthFixed, 70.0f);
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
                ImGui.Text($"RGBA");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"R:{trigger.TextColorR:0.00} G:{trigger.TextColorG:0.00} B:{trigger.TextColorB:0.00} A:{trigger.TextColorA:0.00}");
                }

                ImGui.TableSetColumnIndex(7);
                ImGui.Text(trigger.EnableTextOutline ? $"ON({trigger.TextOutlineThickness:0.#})" : "OFF");

                ImGui.TableSetColumnIndex(8);
                ImGui.Text(trigger.EnableTextFadeIn ? $"ON({trigger.TextFadeInSeconds:0.##})" : "OFF");

                ImGui.TableSetColumnIndex(9);
                ImGui.Text($"{trigger.PositionX:0} / {trigger.PositionY:0}");

                ImGui.TableSetColumnIndex(10);
                ImGui.Text($"{trigger.WaitSeconds:0.0}/{trigger.DisplaySeconds:0.0}");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"待機時間: {trigger.WaitSeconds:0.0}秒 / 表示時間: {trigger.DisplaySeconds:0.0}秒");
                }

                ImGui.TableSetColumnIndex(11);
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

        if (ImGui.BeginTable("HappyTriggerFfxivLogTriggerTable", 12, tableFlags))
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
                this.DrawListOperationButtons(i, TriggerListKind.FfxivLog, trigger);
            }

            ImGui.EndTable();
        }
    }

    private void DrawListOperationButtons(int index, TriggerListKind listKind, HappyTriggerSetting trigger)
    {
        var key = listKind switch
        {
            TriggerListKind.FfxivLog => "ffxivlog",
            TriggerListKind.Text => "text",
            _ => "image",
        };

        if (ImGui.SmallButton($"テスト##{key}_test_{index}"))
        {
            this.testTrigger(trigger);
        }

        ImGui.SameLine();

        if (ImGui.SmallButton($"編集##{key}_edit_{index}"))
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

        ImGui.SameLine();

        if (ImGui.SmallButton($"削除##{key}_delete_{index}"))
        {
            if (listKind == TriggerListKind.FfxivLog)
            {
                this.configuration.FfxivLogTriggers.RemoveAt(index);
            }
            else if (listKind == TriggerListKind.Text)
            {
                this.configuration.TextTriggers.RemoveAt(index);
            }
            else
            {
                this.configuration.Triggers.RemoveAt(index);
            }

            this.saveConfig();

            if (this.editingIndex == index && this.editingKind == listKind)
            {
                this.ResetEditing();
            }
        }
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

        ImGui.TextDisabled("上段はFFXIVのチャットログ由来のログ、下段はHappyTrigger内部処理ログです。");
        ImGui.Spacing();

        var availableHeight = ImGui.GetContentRegionAvail().Y;
        var battleHeight = Math.Max(160.0f, availableHeight * 0.48f);
        var internalHeight = Math.Max(160.0f, availableHeight - battleHeight - 48.0f);

        this.DrawLogBox("バトルログ", "HappyTriggerBattleLogChild", this.getBattleLogs(), battleHeight, this.autoScrollBattleLog, ref this.selectedBattleLogIndex);
        ImGui.Spacing();
        this.DrawLogBox("内部ログ", "HappyTriggerInternalLogChild", this.getInternalLogs(), internalHeight, this.autoScrollInternalLog, ref this.selectedInternalLogIndex);
    }

    private void DrawLogBox(string title, string childId, IReadOnlyList<FfxivLogEntry> logs, float height, bool autoScroll, ref int selectedIndex)
    {
        ImGui.Text(title);

        if (selectedIndex >= logs.Count)
        {
            selectedIndex = -1;
        }

        if (selectedIndex >= 0 && selectedIndex < logs.Count)
        {
            ImGui.SameLine();

            if (ImGui.SmallButton($"選択ログをコピー##{childId}_copy"))
            {
                ImGui.SetClipboardText(logs[selectedIndex].DisplayText);
            }

            ImGui.SameLine();
            ImGui.TextDisabled("行をクリックで選択、ダブルクリックでコピーできます。");
        }
        else
        {
            ImGui.SameLine();
            ImGui.TextDisabled("行をクリックで選択、ダブルクリックでコピーできます。");
        }

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.28f, 0.30f, 0.28f, 0.96f));
        ImGui.BeginChild(childId, new Vector2(-1.0f, height), true, ImGuiWindowFlags.HorizontalScrollbar);

        if (logs.Count == 0)
        {
            selectedIndex = -1;
            ImGui.TextDisabled("ログはまだありません。");
        }
        else
        {
            for (var i = 0; i < logs.Count; i++)
            {
                var log = logs[i];
                var selected = selectedIndex == i;

                ImGui.PushID($"{childId}_{i}");
                if (ImGui.Selectable(log.DisplayText, selected))
                {
                    selectedIndex = i;

                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        ImGui.SetClipboardText(log.DisplayText);
                    }
                }

                ImGui.PopID();
            }

            if (autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 24.0f)
            {
                ImGui.SetScrollHereY(1.0f);
            }
        }

        ImGui.EndChild();
        ImGui.PopStyleColor();
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

    private void EnsureEditingTriggerId(TriggerListKind saveTargetKind)
    {
        var prefix = saveTargetKind switch
        {
            TriggerListKind.FfxivLog => this.editTrigger.UsePrerequisite ? "X" : "F",
            TriggerListKind.Text => "T",
            _ => "I",
        };
        var currentId = this.editTrigger.TriggerId;

        if (!HappyTriggerSetting.IsValidTriggerId(currentId, prefix) || this.IsDuplicateTriggerId(currentId, saveTargetKind))
        {
            this.editTrigger.TriggerId = this.GenerateNextTriggerId(prefix);
        }
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
