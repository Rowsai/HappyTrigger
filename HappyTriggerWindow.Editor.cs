using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace HappyTrigger;

public sealed partial class HappyTriggerWindow
{
    private int editorPage;
    private string editorNotice = string.Empty;

    private static void SetEditorFieldWidth(float preferred)
        => ImGui.SetNextItemWidth(Math.Max(100, Math.Min(preferred, ImGui.GetContentRegionAvail().X - 220)));

    private static void EditorSection(string title, string description)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.75f, 0.61f, 1, 1), title);
        ImGui.TextWrapped(description);
        ImGui.Spacing();
    }

    private void DrawTriggerEditTab()
    {
        var footerHeight = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetTextLineHeightWithSpacing() * 5 + 24;
        var bodyHeight = Math.Max(180, ImGui.GetContentRegionAvail().Y - footerHeight);
        if (ImGui.BeginChild("EditorCategories", new Vector2(180, bodyHeight), true))
        {
            ImGui.TextDisabled("トリガーの設定");
            ImGui.Spacing();
            string[] labels = { "検知条件", "実行内容", "見た目・位置", "名前・整理" };
            string[] hints = { "いつ反応させるか", "何を起こすか", "表示の調整", "名前と保存先" };
            FontAwesomeIcon[] icons = { FontAwesomeIcon.Search, FontAwesomeIcon.Play, FontAwesomeIcon.SlidersH, FontAwesomeIcon.Tags };
            for (var i = 0; i < labels.Length; i++)
                if (PaneNavigation($"0{i + 1}", labels[i], hints[i], this.editorPage == i, icons[i])) this.editorPage = i;
        }
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.075f, 0.08f, 0.105f, 1));
        if (ImGui.BeginChild($"EditorSettings{this.editorPage}", new Vector2(0, bodyHeight), true))
        {
            switch (this.editorPage)
            {
                case 0: this.DrawEditorConditions(); break;
                case 1: this.DrawEditorOutputs(); break;
                case 2: this.DrawEditorAppearance(); break;
                case 3: this.DrawEditorOrganization(); break;
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        if (ImGui.IsAnyItemActive()) this.editorNotice = string.Empty;

        ImGui.Separator();
        var problem = this.GetEditorProblem();
        var hasOutputText = this.editTrigger.DisplayTextMode ? (!string.IsNullOrWhiteSpace(this.editTrigger.DisplayText) || this.editTrigger.EnableCountdown) : !string.IsNullOrWhiteSpace(this.editTrigger.ImagePath);
        var output = hasOutputText ? (this.editTrigger.DisplayTextMode ? "テキスト" : "画像") : "画面表示なし";
        if (this.editTrigger.EnableTargetMarker) output += " / マーカー";
        if (this.editTrigger.EnableChatSend) output += " / チャット";
        ImGui.TextWrapped($"{(this.editTrigger.UseFfxivLogReference ? "ゲーム内ログ" : "チャットのキーワード")} → {output}");
        if (!string.IsNullOrEmpty(this.editorNotice)) ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.79f, 1), this.editorNotice);
        else if (problem != null) { ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.7f, 0.4f, 1)); ImGui.TextWrapped(problem); ImGui.PopStyleColor(); }
        else ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.79f, 1), string.IsNullOrEmpty(this.editorNotice) ? "入力が揃いました。保存できます。" : this.editorNotice);

        ImGui.BeginDisabled(problem != null);
        if (ImGui.Button(this.editingIndex >= 0 ? "変更を保存" : "トリガーを保存", new Vector2(170, 0))) this.SaveEditingTrigger();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("新規入力に戻す", new Vector2(150, 0))) { this.ResetEditing(); this.editorNotice = string.Empty; }
        ImGui.SameLine();
        ImGui.TextDisabled(this.editTrigger.Enabled ? "保存後：有効" : "保存後：無効");

        var hasDisplay = this.editTrigger.DisplayTextMode ? (!string.IsNullOrWhiteSpace(this.editTrigger.DisplayText) || this.editTrigger.EnableCountdown) : !string.IsNullOrWhiteSpace(this.editTrigger.ImagePath);
        ImGui.BeginDisabled(!hasDisplay);
        if (ImGui.Button("表示・読み上げをテスト")) this.testTrigger(this.editTrigger);
        ImGui.SameLine();
        if (ImGui.Button("表示位置をマウスで調整")) this.positionSettingTrigger(this.editTrigger);
        ImGui.EndDisabled();
        ImGui.TextDisabled("テストではマーカー付与・チャット送信は行いません。");
    }

    private void DrawEditorConditions()
    {
        EditorSection("どのログを合図にしますか？", "チャットの言葉で反応させる場合は「チャット」。バトルログや内部ログを組み合わせる場合は「ゲーム内ログ」を選びます。");
        var source = this.editTrigger.UseFfxivLogReference ? 1 : 0;
        SetEditorFieldWidth(420);
        if (ImGui.Combo("検知するログ", ref source, new[] { "チャットのキーワード", "ゲーム内ログ（FFXIV Log）" }, 2)) this.editTrigger.UseFfxivLogReference = source == 1;
        var exact = this.editTrigger.ExactMatch;
        SetEditorFieldWidth(420);
        var match = exact ? 1 : 0;
        if (ImGui.Combo("一致のしかた", ref match, new[] { "入力した言葉を含む（部分一致）", "入力した言葉と同じ（完全一致）" }, 2)) this.editTrigger.ExactMatch = match == 1;
        if (this.editTrigger.UseFfxivLogReference)
        {
            EditorSection("検知するログの内容", "左メニューの「Logs」で実際のログを確認し、反応させたい文言を入力してください。");
            this.DrawFfxivLogReferenceSettingArea();
        }
        else
        {
            EditorSection("反応させたい言葉", "例：「テスト」と入力すると、チャットに「テスト」が現れたときに実行します。");
            var keyword = this.editTrigger.Keyword ?? string.Empty;
            SetEditorFieldWidth(650);
            if (InputTextJapanese("検知する言葉", ref keyword, 512)) this.editTrigger.Keyword = keyword;
            ImGui.TextWrapped("ここに入力するのは合図になる言葉です。送信するチャット本文や画面に出す文章は「02 実行内容」に入力します。");
        }
    }

    private void DrawEditorOutputs()
    {
        EditorSection("画面に出す内容", "画像またはテキストを選んで内容を指定します。マーカー・チャットだけを使う場合は、表示内容を空欄にできます。");
        var mode = this.editTrigger.DisplayTextMode ? 1 : 0;
        SetEditorFieldWidth(320);
        if (ImGui.Combo("表示するもの", ref mode, new[] { "画像", "テキスト" }, 2)) { this.editTrigger.DisplayTextMode = mode == 1; if (mode == 0) this.editTrigger.EnableCountdown = false; }
        if (this.editTrigger.DisplayTextMode) { this.DrawTextContentSettings(); this.DrawCountdownSettings(); }
        else this.DrawImageContentSettings();

        if (!this.editTrigger.EnableCountdown && this.editTrigger.UseFfxivLogReference && this.editTrigger.DisplayTextMode && ImGui.CollapsingHeader("表示にステータスの残り時間を追加")) this.DrawStatusTimerSettings();
        ImGui.Spacing();
        this.DrawTriggerActions();
    }

    private void DrawCountdownSettings()
    {
        EditorSection("カウントダウン", "検知した瞬間から、指定した秒数をカウントダウンします。");
        var enabled = this.editTrigger.EnableCountdown;
        if (ImGui.Checkbox("表示テキストにカウントダウンを付ける", ref enabled))
        {
            this.editTrigger.EnableCountdown = enabled;
            if (enabled) this.editTrigger.EnableStatusRemainingAppend = false;
        }
        if (!enabled) return;
        var seconds = this.editTrigger.CountdownSeconds;
        SetEditorFieldWidth(220);
        if (ImGui.InputFloat("カウントダウン秒数", ref seconds, 1, 10, "%.2f")) this.editTrigger.CountdownSeconds = seconds;
        ImGui.TextWrapped($"表示例：{this.editTrigger.DisplayText}{this.editTrigger.CountdownSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}s");
        ImGui.TextWrapped("例：本文「次のバーストまで＠」＋120.00秒。0秒で非表示、再検知すると最初から開始します。本文を空欄にすると秒数だけ表示します。");
        ImGui.TextWrapped("表示待機中もカウントは進みます。ステータス残り時間とは併用できません。小数秒を滑らかに更新するため標準フォント描画を使用し、くっきり表示のWindowsフォント指定は適用しません。");
    }

    private void DrawEditorAppearance()
    {
        EditorSection("表示時間と位置", "画面表示の調整です。マーカー付与・チャット送信の実行タイミングには影響しません。");
        ImGui.BeginDisabled(this.editTrigger.EnableCountdown);
        var seconds = this.editTrigger.DisplaySeconds;
        SetEditorFieldWidth(180);
        if (ImGui.InputFloat("表示する秒数", ref seconds, 0.1f, 1)) this.editTrigger.DisplaySeconds = Math.Clamp(seconds, 0.1f, 3600);
        ImGui.EndDisabled();
        if (this.editTrigger.EnableCountdown) ImGui.TextWrapped("カウントダウン中は通常の表示秒数を使わず、残り0秒で終了します。");
        var wait = this.editTrigger.WaitSeconds;
        SetEditorFieldWidth(180);
        if (ImGui.InputFloat("表示までの待機秒数", ref wait, 0.1f, 1)) this.editTrigger.WaitSeconds = Math.Clamp(wait, 0, 600);
        ImGui.TextWrapped("待機0秒なら、条件成立と同時に表示します。位置は下部の「表示位置をマウスで調整」からドラッグできます。");
        if (ImGui.CollapsingHeader("座標を数値で指定"))
        {
            var x = this.editTrigger.PositionX; var y = this.editTrigger.PositionY;
            SetEditorFieldWidth(180);
            if (ImGui.InputFloat("横位置 X", ref x, 1, 10)) this.editTrigger.PositionX = x;
            SetEditorFieldWidth(180);
            if (ImGui.InputFloat("縦位置 Y", ref y, 1, 10)) this.editTrigger.PositionY = y;
            if (this.editTrigger.UseFfxivLogReference && this.editTrigger.UseTriggerLabelPosition && !string.IsNullOrWhiteSpace(this.editTrigger.TriggerLabelId)) ImGui.TextWrapped("ラベルの座標を使用中です。個別配置へ切り替えるには「04 名前・整理」でラベル座標を無効にしてください。");
        }
        EditorSection(this.editTrigger.DisplayTextMode ? "文字のデザイン" : "画像のサイズ", "通常は初期値のまま使えます。必要な項目だけ調整してください。");
        if (this.editTrigger.DisplayTextMode) this.DrawTextAppearanceSettings();
        else this.DrawImageAppearanceSettings();
    }

    private void DrawEditorOrganization()
    {
        EditorSection("名前を付けて整理", "管理用の名前です。検知する言葉・表示内容・送信本文とは別に、見つけやすい名前を付けられます。");
        var name = this.editTrigger.TriggerName ?? string.Empty;
        SetEditorFieldWidth(500);
        if (InputTextJapanese("トリガー名（任意）", ref name, 256)) this.editTrigger.TriggerName = RemoveLineBreaks(name);
        ImGui.TextWrapped("例：ナイトへ攻撃1を付与 ／ フェーズ開始のお知らせ");
        var enabled = this.editTrigger.Enabled;
        if (ImGui.Checkbox("保存後にこのトリガーを有効にする", ref enabled)) this.editTrigger.Enabled = enabled;
        ImGui.TextDisabled(string.IsNullOrWhiteSpace(this.editTrigger.TriggerId) ? "IDは保存時に自動で付けられます。" : $"ID：{this.editTrigger.TriggerId}");
        EditorSection("保存先の分類（任意）", "ボックスは大きな分類、ラベルはその中のグループです。未分類のままでも使えます。");
        this.DrawTriggerManagementAssignmentArea();
    }

    private string? GetEditorProblem()
    {
        if (this.editTrigger.UseFfxivLogReference)
        {
            if (string.IsNullOrWhiteSpace(this.editTrigger.BattleLogKeyword) && this.editTrigger.GetInternalLogKeywords().Count == 0) return "01 検知条件：検知するログの内容を入力してください。";
            if (this.editTrigger.UsePrerequisite && string.IsNullOrWhiteSpace(this.editTrigger.PrerequisiteTriggerId)) return "01 検知条件：前提トリガーを選択してください。";
        }
        else if (string.IsNullOrWhiteSpace(this.editTrigger.Keyword)) return "01 検知条件：検知する言葉を入力してください。";
        if (TriggerActionRules.Validate(this.editTrigger) is { } error) return $"02 実行内容：{error}";
        var hasDisplay = this.editTrigger.DisplayTextMode ? (!string.IsNullOrWhiteSpace(this.editTrigger.DisplayText) || this.editTrigger.EnableCountdown) : !string.IsNullOrWhiteSpace(this.editTrigger.ImagePath);
        if (!hasDisplay && !this.editTrigger.EnableTargetMarker && !this.editTrigger.EnableChatSend) return "02 実行内容：画像・テキスト・マーカー・チャットのいずれかを設定してください。";
        return null;
    }
}
