using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace HappyTrigger;

public sealed class HappyTriggerWindow : Window
{
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

    private HappyTriggerSetting editTrigger = new();
    private int editingIndex = -1;
    private bool editingTextList = false;
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
            if (ImGui.BeginTabItem("ログトリガー"))
            {
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

        var keyword = this.editTrigger.Keyword ?? string.Empty;
        ImGui.SetNextItemWidth(700.0f);
        if (ImGui.InputText("トリガー文字", ref keyword, 512))
        {
            this.editTrigger.Keyword = keyword;
        }

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
            var listName = this.editingTextList ? "テキスト表示用" : "画像表示用";
            ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.3f, 1.0f), $"編集中: {listName} {this.editingIndex + 1}");
        }
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

        if (ImGui.BeginTable("HappyTriggerImageTriggerTable", 11, tableFlags))
        {
            ImGui.TableSetupColumn("有効", ImGuiTableColumnFlags.WidthFixed, 50.0f);
            ImGui.TableSetupColumn("判定", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("トリガー文字");
            ImGui.TableSetupColumn("画像");
            ImGui.TableSetupColumn("サイズ", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("色", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("枠線", ImGuiTableColumnFlags.WidthFixed, 60.0f);
            ImGui.TableSetupColumn("Fade", ImGuiTableColumnFlags.WidthFixed, 60.0f);
            ImGui.TableSetupColumn("X", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("Y", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("幅", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("高さ", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("倍率", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("秒", ImGuiTableColumnFlags.WidthFixed, 60.0f);
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
                ImGui.Text(trigger.ExactMatch ? "完全一致" : "部分一致");

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(trigger.Keyword);

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(trigger.ImagePath);

                ImGui.TableSetColumnIndex(4);
                ImGui.Text($"{trigger.PositionX:0}");

                ImGui.TableSetColumnIndex(5);
                ImGui.Text($"{trigger.PositionY:0}");

                ImGui.TableSetColumnIndex(6);
                ImGui.Text(trigger.UseOriginalImageSize ? "元" : $"{trigger.ImageWidth:0}");

                ImGui.TableSetColumnIndex(7);
                ImGui.Text(trigger.UseOriginalImageSize ? "元" : $"{trigger.ImageHeight:0}");

                ImGui.TableSetColumnIndex(8);
                ImGui.Text($"{(trigger.ScalePercent <= 0.0f ? 100.0f : trigger.ScalePercent):0}%");

                ImGui.TableSetColumnIndex(9);
                ImGui.Text($"{trigger.DisplaySeconds:0.0}");

                ImGui.TableSetColumnIndex(10);
                this.DrawListOperationButtons(i, false, trigger);
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

        if (ImGui.BeginTable("HappyTriggerTextTriggerTable", 11, tableFlags))
        {
            ImGui.TableSetupColumn("有効", ImGuiTableColumnFlags.WidthFixed, 50.0f);
            ImGui.TableSetupColumn("判定", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("トリガー文字");
            ImGui.TableSetupColumn("表示テキスト");
            ImGui.TableSetupColumn("サイズ", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("色", ImGuiTableColumnFlags.WidthFixed, 70.0f);
            ImGui.TableSetupColumn("枠線", ImGuiTableColumnFlags.WidthFixed, 60.0f);
            ImGui.TableSetupColumn("Fade", ImGuiTableColumnFlags.WidthFixed, 60.0f);
            ImGui.TableSetupColumn("X/Y", ImGuiTableColumnFlags.WidthFixed, 120.0f);
            ImGui.TableSetupColumn("秒", ImGuiTableColumnFlags.WidthFixed, 60.0f);
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
                ImGui.Text(trigger.ExactMatch ? "完全一致" : "部分一致");

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(trigger.Keyword);

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(trigger.DisplayText);

                ImGui.TableSetColumnIndex(4);
                ImGui.Text($"{trigger.TextSize:0}");

                ImGui.TableSetColumnIndex(5);
                ImGui.Text($"RGBA");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip($"R:{trigger.TextColorR:0.00} G:{trigger.TextColorG:0.00} B:{trigger.TextColorB:0.00} A:{trigger.TextColorA:0.00}");
                }

                ImGui.TableSetColumnIndex(6);
                ImGui.Text(trigger.EnableTextOutline ? $"ON({trigger.TextOutlineThickness:0.#})" : "OFF");

                ImGui.TableSetColumnIndex(7);
                ImGui.Text(trigger.EnableTextFadeIn ? $"ON({trigger.TextFadeInSeconds:0.##})" : "OFF");

                ImGui.TableSetColumnIndex(8);
                ImGui.Text($"{trigger.PositionX:0} / {trigger.PositionY:0}");

                ImGui.TableSetColumnIndex(9);
                ImGui.Text($"{trigger.DisplaySeconds:0.0}");

                ImGui.TableSetColumnIndex(10);
                this.DrawListOperationButtons(i, true, trigger);
            }

            ImGui.EndTable();
        }
    }

    private void DrawListOperationButtons(int index, bool isTextList, HappyTriggerSetting trigger)
    {
        if (ImGui.SmallButton($"テスト##{(isTextList ? "text" : "image")}_test_{index}"))
        {
            this.testTrigger(trigger);
        }

        ImGui.SameLine();

        if (ImGui.SmallButton($"編集##{(isTextList ? "text" : "image")}_edit_{index}"))
        {
            this.editingIndex = index;
            this.editingTextList = isTextList;
            this.editTrigger = trigger.Clone();
            this.editTrigger.DisplayTextMode = isTextList;
            this.imageSelectMessage = string.Empty;
        }

        ImGui.SameLine();

        if (ImGui.SmallButton($"削除##{(isTextList ? "text" : "image")}_delete_{index}"))
        {
            if (isTextList)
            {
                this.configuration.TextTriggers.RemoveAt(index);
            }
            else
            {
                this.configuration.Triggers.RemoveAt(index);
            }

            this.saveConfig();

            if (this.editingIndex == index && this.editingTextList == isTextList)
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

        this.DrawLogBox("バトルログ", "HappyTriggerBattleLogChild", this.getBattleLogs(), battleHeight, this.autoScrollBattleLog);
        ImGui.Spacing();
        this.DrawLogBox("内部ログ", "HappyTriggerInternalLogChild", this.getInternalLogs(), internalHeight, this.autoScrollInternalLog);
    }

    private void DrawLogBox(string title, string childId, IReadOnlyList<FfxivLogEntry> logs, float height, bool autoScroll)
    {
        ImGui.Text(title);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.28f, 0.30f, 0.28f, 0.96f));
        ImGui.BeginChild(childId, new Vector2(-1.0f, height), true, ImGuiWindowFlags.HorizontalScrollbar);

        if (logs.Count == 0)
        {
            ImGui.TextDisabled("ログはまだありません。");
        }
        else
        {
            foreach (var log in logs)
            {
                ImGui.TextUnformatted(log.DisplayText);
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
        var saveTargetIsText = this.editTrigger.DisplayTextMode;
        this.editTrigger.DisplayTextMode = saveTargetIsText;

        if (this.editingIndex >= 0)
        {
            if (this.editingTextList == saveTargetIsText)
            {
                if (saveTargetIsText && this.editingIndex < this.configuration.TextTriggers.Count)
                {
                    this.configuration.TextTriggers[this.editingIndex] = this.editTrigger.Clone();
                }
                else if (!saveTargetIsText && this.editingIndex < this.configuration.Triggers.Count)
                {
                    this.configuration.Triggers[this.editingIndex] = this.editTrigger.Clone();
                }
            }
            else
            {
                if (this.editingTextList && this.editingIndex < this.configuration.TextTriggers.Count)
                {
                    this.configuration.TextTriggers.RemoveAt(this.editingIndex);
                }
                else if (!this.editingTextList && this.editingIndex < this.configuration.Triggers.Count)
                {
                    this.configuration.Triggers.RemoveAt(this.editingIndex);
                }

                if (saveTargetIsText)
                {
                    this.configuration.TextTriggers.Add(this.editTrigger.Clone());
                }
                else
                {
                    this.configuration.Triggers.Add(this.editTrigger.Clone());
                }
            }
        }
        else
        {
            if (saveTargetIsText)
            {
                this.configuration.TextTriggers.Add(this.editTrigger.Clone());
            }
            else
            {
                this.configuration.Triggers.Add(this.editTrigger.Clone());
            }
        }

        this.saveConfig();
        this.closePositionSettingPopup();
        this.ResetEditing();
    }

    private void ResetEditing()
    {
        this.editingIndex = -1;
        this.editingTextList = false;
        this.editTrigger = new HappyTriggerSetting();
        this.imageSelectMessage = string.Empty;
    }
}
