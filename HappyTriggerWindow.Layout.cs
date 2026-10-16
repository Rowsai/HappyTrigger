using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace HappyTrigger;

public sealed partial class HappyTriggerWindow
{
    private int mainPage;

    private static bool PaneNavigation(string id, string title, string hint, bool selected, FontAwesomeIcon icon)
    {
        var start = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight() * 2 + 40);
        ImGui.PushID(id);
        var clicked = ImGui.InvisibleButton("navigation", size);
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        if (selected || hovered)
            draw.AddRectFilled(start, start + size, ImGui.GetColorU32(selected ? new Vector4(0.23f, 0.19f, 0.33f, 1) : new Vector4(0.14f, 0.13f, 0.19f, 1)), 8);
        if (selected)
            draw.AddRectFilled(start + new Vector2(0, 12), start + new Vector2(3, size.Y - 12), ImGui.GetColorU32(new Vector4(0.67f, 0.43f, 1, 1)), 2);
        ImGui.PushFont(UiBuilder.IconFont);
        var glyph = icon.ToIconString();
        var glyphSize = ImGui.CalcTextSize(glyph) * 1.5f;
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 1.5f, start + new Vector2((size.X - glyphSize.X) / 2, 8), ImGui.GetColorU32(selected ? new Vector4(0.76f, 0.60f, 1, 1) : new Vector4(0.60f, 0.62f, 0.70f, 1)), glyph);
        ImGui.PopFont();
        var textSize = ImGui.CalcTextSize(title);
        draw.AddText(start + new Vector2((size.X - textSize.X) / 2, ImGui.GetTextLineHeight() + 24), ImGui.GetColorU32(selected ? ImGuiCol.Text : ImGuiCol.TextDisabled), title);
        if (hovered) ImGui.SetTooltip(hint);
        ImGui.PopID();
        return clicked;
    }

    private void DrawPaneLayout()
    {
        if (this.requestOpenTriggerEditTab)
        {
            this.mainPage = 0;
            this.editorPage = 0;
            this.editorNotice = string.Empty;
            this.requestOpenTriggerEditTab = false;
        }
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.055f, 0.05f, 0.08f, 1));
        if (ImGui.BeginChild("MainNavigation", new Vector2(190, 0), true))
        {
            ImGui.TextColored(new Vector4(0.75f, 0.61f, 1, 1), "HappyTrigger");
            ImGui.TextDisabled("v1.0.1.0");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            string[] titles = { "ログトリガー", "トリガー一覧", "トリガー管理", "Logs" };
            string[] hints = { "作成・編集", "保存済みの設定", "ボックス・ラベル", "ログを確認する" };
            FontAwesomeIcon[] icons = { FontAwesomeIcon.Bolt, FontAwesomeIcon.List, FontAwesomeIcon.Sitemap, FontAwesomeIcon.Terminal };
            for (var i = 0; i < titles.Length; i++)
                if (PaneNavigation($"main{i}", titles[i], hints[i], this.mainPage == i, icons[i])) this.mainPage = i;
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled($"登録トリガー  {this.GetAllTriggers().Count} 件");
        }
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.SameLine();
        // Each main page owns its scroll state. Editor scrolling lives in the inner form.
        if (ImGui.BeginChild($"MainContent{this.mainPage}", Vector2.Zero, false,
                this.mainPage == 0 ? ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse : ImGuiWindowFlags.HorizontalScrollbar))
        {
            string[] headings = { this.editingIndex >= 0 ? "ログトリガーを編集" : "ログトリガーを作成", "トリガー一覧", "トリガー管理", "Logs" };
            string[] descriptions = { "左の設定カテゴリから、合図と実行する動作を設定します。", "保存したトリガーの確認・編集・テストを行います。", "ボックスとラベルでトリガーを整理します。", "条件の設定に使うバトルログと内部ログを確認します。" };
            ImGui.Text(headings[this.mainPage]);
            ImGui.TextDisabled(descriptions[this.mainPage]);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            switch (this.mainPage)
            {
                case 0: this.DrawTriggerEditTab(); break;
                case 1: this.DrawTriggerListTab(); break;
                case 2: this.DrawTriggerManagementTab(); break;
                case 3: this.DrawFfxivLogTab(); break;
            }
        }
        ImGui.EndChild();
    }
}
