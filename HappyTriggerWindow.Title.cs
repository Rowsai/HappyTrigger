using System;
using System.Numerics;
using Dalamud.Interface.Textures.TextureWraps;
using System.IO;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace HappyTrigger;

public sealed partial class HappyTriggerWindow
{
    private bool titleIconFailed;
    private bool headerCollapsed;
    private bool restoreHeaderSize;
    private Vector2 expandedWindowSize = new(1320, 820);

    private void DrawCustomTitleBar()
    {
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + new Vector2(width, 44), ImGui.GetColorU32(new Vector4(0.07f, 0.065f, 0.10f, 1)), 10);
        // A dedicated drag area keeps dragging away from form controls.
        ImGui.InvisibleButton("TitleDrag", new Vector2(width - 104, 44));
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
        var icon = this.GetTitleIcon();
        if (icon != null) draw.AddImage(icon.Handle, start + new Vector2(4, 3), start + new Vector2(42, 41));
        else
        {
            ImGui.PushFont(UiBuilder.IconFont);
            draw.AddText(start + new Vector2(12, 13), ImGui.GetColorU32(ImGuiCol.Text), FontAwesomeIcon.Bolt.ToIconString());
            ImGui.PopFont();
        }
        draw.AddText(start + new Vector2(54, 13), ImGui.GetColorU32(ImGuiCol.Text), "HappyTrigger");
        var versionX = 72 + ImGui.CalcTextSize("HappyTrigger").X;
        draw.AddText(start + new Vector2(versionX, 13), ImGui.GetColorU32(ImGuiCol.TextDisabled), "v1.0.1.0");
        var badgeX = versionX + ImGui.CalcTextSize("v1.0.1.0").X + 20;
        draw.AddRectFilled(start + new Vector2(badgeX, 7), start + new Vector2(badgeX + 88, 37), ImGui.GetColorU32(new Vector4(0.24f, 0.16f, 0.36f, 1)), 15);
        draw.AddText(start + new Vector2(badgeX + 14, 13), ImGui.GetColorU32(new Vector4(0.78f, 0.61f, 1, 1)), "API 15");
        ImGui.SetCursorScreenPos(start + new Vector2(width - 94, 0));
        ImGui.PushFont(UiBuilder.IconFont);
        var toggle = ImGui.Button((this.headerCollapsed ? FontAwesomeIcon.ChevronUp : FontAwesomeIcon.ChevronDown).ToIconString() + "##collapse", new Vector2(42, 42));
        ImGui.PopFont();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(this.headerCollapsed ? "展開" : "折りたたむ");
        if (toggle)
        {
            if (!this.headerCollapsed) this.expandedWindowSize = ImGui.GetWindowSize();
            this.headerCollapsed = !this.headerCollapsed;
            this.restoreHeaderSize = !this.headerCollapsed;
            ImGui.SetWindowSize(this.headerCollapsed ? new Vector2(this.expandedWindowSize.X, 76) : this.expandedWindowSize);
        }
        ImGui.SetCursorScreenPos(start + new Vector2(width - 42, 0));
        ImGui.PushFont(UiBuilder.IconFont);
        if (ImGui.Button(FontAwesomeIcon.Times.ToIconString() + "##close", new Vector2(42, 42))) this.IsOpen = false;
        ImGui.PopFont();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("閉じる");
        ImGui.SetCursorScreenPos(start + new Vector2(0, 52));
        ImGui.Separator();
        ImGui.Spacing();
    }
    private IDalamudTextureWrap? GetTitleIcon()
    {
        if (this.titleIconFailed) return null;
        try
        {
            // Dalamud can load assemblies from memory: Assembly.Location may be empty.
            var directory = Plugin.PluginInterface.AssemblyLocation?.DirectoryName;
            if (string.IsNullOrWhiteSpace(directory))
            {
                this.titleIconFailed = true;
                return null;
            }
            var path = Path.Combine(directory, "icon.png");
            if (!File.Exists(path))
            {
                this.titleIconFailed = true;
                return null;
            }
            // A null wrap while loading is transient; retry on the next frame.
            return Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault();
        }
        catch (Exception ex)
        {
            this.titleIconFailed = true;
            Plugin.Log.Warning(ex, "タイトルアイコンを読み込めないため代替アイコンを表示します。");
            return null;
        }
    }

}
