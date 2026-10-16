using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace HappyTrigger;

public sealed partial class HappyTriggerWindow
{
    private static float TreeButtonWidth => Math.Max(68, ImGui.CalcTextSize("座標設定").X + 20);
    private static float TreeActionsStart => ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - (TreeButtonWidth + ImGui.GetStyle().ItemSpacing.X) * 5;

    private static void TreeActionSlot(int index)
    {
        var x = TreeActionsStart + index * (TreeButtonWidth + ImGui.GetStyle().ItemSpacing.X);
        ImGui.SameLine();
        ImGui.SetCursorPosX(x);
    }

    private static string FitTreeName(string text)
    {
        var width = Math.Max(40, TreeActionsStart - ImGui.GetCursorPosX() - 30);
        if (ImGui.CalcTextSize(text).X <= width) return text;
        var length = text.Length;
        while (length > 0 && ImGui.CalcTextSize(text[..length] + "…").X > width) length--;
        return text[..length] + "…";
    }

    private void DrawRoundedTreeRow()
    {
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddRectFilled(pos, pos + new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetFrameHeight()), ImGui.GetColorU32(new Vector4(0.11f, 0.105f, 0.15f, 1)), 7);
        ImGui.AlignTextToFramePadding();
    }

    private void DrawTriggerCards(string title, List<HappyTriggerSetting> triggers, TriggerListKind kind)
    {
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.75f, 0.61f, 1, 1), $"{title}  /  {triggers.Count}件");
        if (triggers.Count == 0) { ImGui.TextDisabled("まだ登録されていません。"); return; }
        for (var i = 0; i < triggers.Count; i++)
        {
            var trigger = triggers[i];
            var key = GetManagementTriggerKey(trigger, kind, i);
            ImGui.PushID(key);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.09f, 0.095f, 0.125f, 1));
            var height = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetTextLineHeightWithSpacing() + 30;
            if (ImGui.BeginChild("card", new Vector2(0, height), true))
            {
                var enabled = trigger.Enabled;
                if (ImGui.Checkbox("有効", ref enabled)) { trigger.Enabled = enabled; this.saveConfig(); }
                ImGui.SameLine();
                var name = GetTriggerDisplayName(trigger);
                var width = Math.Max(80, ImGui.GetContentRegionAvail().X - 330);
                var shown = name;
                while (shown.Length > 0 && ImGui.CalcTextSize(shown + "…").X > width) shown = shown[..^1];
                ImGui.TextUnformatted(shown == name ? name : shown + "…");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(name);
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - 310);
                this.DrawListOperationButtons(i, kind, trigger);
                var condition = trigger.UseFfxivLogReference ? trigger.BattleLogKeyword + " / " + trigger.GetInternalLogKeywordText() : trigger.Keyword;
                ImGui.TextUnformatted("条件：" + condition.Replace('\n', ' ').Replace('\r', ' '));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(condition);
                if (trigger.EnableCountdown) ImGui.TextDisabled($"カウントダウン {trigger.CountdownSeconds:0.00}s / 検知時から開始");
                else ImGui.TextDisabled($"{(trigger.DisplayTextMode ? "テキスト" : "画像")}  /  待機 {trigger.WaitSeconds:0.#}秒  /  表示 {trigger.DisplaySeconds:0.#}秒");
                ImGui.SameLine();
                if (ImGui.SmallButton("詳細を確認")) this.selectedManagementTriggerKey = this.selectedManagementTriggerKey == key ? string.Empty : key;
            }
            ImGui.EndChild();
            ImGui.PopStyleColor();
            if (this.selectedManagementTriggerKey == key) this.DrawManagementTriggerDetail(trigger);
            ImGui.PopID();
        }
    }
}
