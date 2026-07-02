using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace HappyTrigger;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/happytrigger";
    private const int MaxLogEntries = 500;

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static ITextureProvider TextureProvider { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("HappyTrigger");
    private readonly Configuration configuration;
    private readonly ImageCacheService imageCacheService;
    private readonly HappyTriggerWindow configWindow;
    private readonly List<PopupImageState> activePopups = new();
    private readonly List<FfxivLogEntry> battleLogs = new();
    private readonly List<FfxivLogEntry> internalLogs = new();
    private readonly object logLock = new();

    public Plugin()
    {
        this.configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // 既存設定の念のための補正です。
        foreach (var trigger in this.configuration.Triggers)
        {
            trigger.DisplayTextMode = false;
        }

        foreach (var trigger in this.configuration.TextTriggers)
        {
            trigger.DisplayTextMode = true;
        }

        this.imageCacheService = new ImageCacheService(TextureProvider);
        this.configWindow = new HappyTriggerWindow(
            this.configuration,
            this.SaveConfig,
            this.ActivatePopup,
            this.ActivatePositionSettingTrigger,
            this.ClosePositionSettingPopup,
            this.GetBattleLogsSnapshot,
            this.GetInternalLogsSnapshot,
            this.ClearFfxivLogs);

        this.windowSystem.AddWindow(this.configWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open HappyTrigger config window.",
        });

        PluginInterface.UiBuilder.Draw += this.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;

        ChatGui.ChatMessage += this.OnChatMessage;

        this.AddInternalLog("HappyTrigger loaded.");
    }

    private void OnCommand(string command, string args)
    {
        this.configWindow.Toggle();
    }

    private void OpenConfigUi()
    {
        this.configWindow.Toggle();
    }

    private void SaveConfig()
    {
        this.configuration.Save();
    }

    private void OnChatMessage(IHandleableChatMessage chatMessage)
    {
        var text = chatMessage.Message.TextValue;
        var sender = chatMessage.Sender.TextValue;
        var chatType = "Chat";

        var logText = string.IsNullOrWhiteSpace(sender)
            ? text
            : $"{sender}: {text}";

        this.AddBattleLog(chatType, logText);

        foreach (var trigger in this.configuration.Triggers)
        {
            trigger.DisplayTextMode = false;

            if (trigger.IsMatch(text))
            {
                this.AddInternalLog($"Image trigger matched. Keyword='{trigger.Keyword}', Message='{text}'");
                this.ActivatePopup(trigger);
            }
        }

        foreach (var trigger in this.configuration.TextTriggers)
        {
            trigger.DisplayTextMode = true;

            if (trigger.IsMatch(text))
            {
                this.AddInternalLog($"Text trigger matched. Keyword='{trigger.Keyword}', Text='{trigger.DisplayText}'");
                this.ActivatePopup(trigger);
            }
        }
    }

    private void AddBattleLog(string category, string text)
    {
        this.AddLogEntry(this.battleLogs, category, text);
    }

    private void AddInternalLog(string text)
    {
        this.AddLogEntry(this.internalLogs, "HappyTrigger", text);
    }

    private void AddLogEntry(List<FfxivLogEntry> target, string category, string text)
    {
        lock (this.logLock)
        {
            target.Add(new FfxivLogEntry(DateTime.Now, category, text));

            if (target.Count > MaxLogEntries)
            {
                target.RemoveRange(0, target.Count - MaxLogEntries);
            }
        }
    }

    private IReadOnlyList<FfxivLogEntry> GetBattleLogsSnapshot()
    {
        lock (this.logLock)
        {
            return this.battleLogs.ToList();
        }
    }

    private IReadOnlyList<FfxivLogEntry> GetInternalLogsSnapshot()
    {
        lock (this.logLock)
        {
            return this.internalLogs.ToList();
        }
    }

    private void ClearFfxivLogs()
    {
        lock (this.logLock)
        {
            this.battleLogs.Clear();
            this.internalLogs.Clear();
        }

        this.AddInternalLog("FFXIV Log cleared.");
    }

    private void ActivatePopup(HappyTriggerSetting trigger)
    {
        if (trigger.DisplayTextMode)
        {
            if (string.IsNullOrWhiteSpace(trigger.DisplayText))
            {
                return;
            }

            trigger.DisplayTextMode = true;
            this.activePopups.RemoveAll(x => x.IsExpired || x.IsClosed);
            this.activePopups.Add(new PopupImageState(trigger));
            this.AddInternalLog($"Text displayed. Text='{trigger.DisplayText}', X={trigger.PositionX:0}, Y={trigger.PositionY:0}");
            return;
        }

        if (string.IsNullOrWhiteSpace(trigger.ImagePath))
        {
            return;
        }

        trigger.DisplayTextMode = false;
        this.activePopups.RemoveAll(x => x.IsExpired || x.IsClosed);
        this.activePopups.Add(new PopupImageState(trigger));
        this.AddInternalLog($"Image displayed. Image='{trigger.ImagePath}', X={trigger.PositionX:0}, Y={trigger.PositionY:0}");
    }

    private void ActivatePositionSettingTrigger(HappyTriggerSetting trigger)
    {
        if (trigger.DisplayTextMode)
        {
            if (string.IsNullOrWhiteSpace(trigger.DisplayText))
            {
                return;
            }
        }
        else if (string.IsNullOrWhiteSpace(trigger.ImagePath))
        {
            return;
        }

        this.activePopups.RemoveAll(x => x.IsPositionSetting || x.IsExpired);
        this.activePopups.Add(new PopupImageState(trigger, true));
        this.AddInternalLog(trigger.DisplayTextMode
            ? $"Text position setting popup displayed. Text='{trigger.DisplayText}'"
            : $"Image position setting popup displayed. Image='{trigger.ImagePath}'");
    }

    private void ClosePositionSettingPopup()
    {
        this.activePopups.RemoveAll(x => x.IsPositionSetting);
        this.AddInternalLog("Position setting popup closed.");
    }

    private void Draw()
    {
        this.windowSystem.Draw();
        this.DrawActivePopups();
    }

    private void DrawActivePopups()
    {
        this.activePopups.RemoveAll(x => x.IsExpired || x.IsClosed);

        for (var i = 0; i < this.activePopups.Count; i++)
        {
            var popup = this.activePopups[i];
            var trigger = popup.Trigger;

            if (trigger.DisplayTextMode)
            {
                this.DrawTextPopup(i, popup);
            }
            else
            {
                this.DrawImagePopup(i, popup);
            }
        }
    }

    private void DrawImagePopup(int index, PopupImageState popup)
    {
        var trigger = popup.Trigger;
        var texture = this.imageCacheService.GetTexture(trigger.ImagePath, trigger.IsWebImage);
        if (texture == null)
        {
            return;
        }

        var position = new Vector2(trigger.PositionX, trigger.PositionY);
        var imageSize = CalculateImageSize(trigger, texture);
        var windowName = $"HappyTrigger_image_popup_{index}";

        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(imageSize, ImGuiCond.Always);

        var flags =
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        if (ImGui.Begin(windowName, flags))
        {
            ImGui.Image(texture.Handle, imageSize);

            if (popup.IsPositionSetting)
            {
                this.HandlePositionSettingDrag(popup);
            }
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void DrawTextPopup(int index, PopupImageState popup)
    {
        var trigger = popup.Trigger;
        var position = new Vector2(trigger.PositionX, trigger.PositionY);
        var windowName = $"HappyTrigger_text_popup_{index}";

        ImGui.SetNextWindowPos(position, ImGuiCond.Always);

        var flags =
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoFocusOnAppearing |
            ImGuiWindowFlags.NoNav |
            ImGuiWindowFlags.NoBackground |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.AlwaysAutoResize;

        var fontScale = Math.Max(0.25f, trigger.TextSize / 16.0f);
        var fadeAlpha = CalculateTextFadeAlpha(popup);
        var textColor = new Vector4(
            Math.Clamp(trigger.TextColorR, 0.0f, 1.0f),
            Math.Clamp(trigger.TextColorG, 0.0f, 1.0f),
            Math.Clamp(trigger.TextColorB, 0.0f, 1.0f),
            Math.Clamp(trigger.TextColorA, 0.0f, 1.0f) * fadeAlpha);
        var outlineColor = new Vector4(
            Math.Clamp(trigger.TextOutlineColorR, 0.0f, 1.0f),
            Math.Clamp(trigger.TextOutlineColorG, 0.0f, 1.0f),
            Math.Clamp(trigger.TextOutlineColorB, 0.0f, 1.0f),
            Math.Clamp(trigger.TextOutlineColorA, 0.0f, 1.0f) * fadeAlpha);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4.0f, 2.0f));

        if (ImGui.Begin(windowName, flags))
        {
            ImGui.SetWindowFontScale(fontScale);

            var displayText = trigger.DisplayText ?? string.Empty;
            var textSize = ImGui.CalcTextSize(displayText);
            var drawPos = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize();

            if (trigger.EnableTextOutline)
            {
                var outlineThickness = Math.Max(1.0f, trigger.TextOutlineThickness);
                var offsets = new[]
                {
                    new Vector2(-outlineThickness, 0.0f),
                    new Vector2(outlineThickness, 0.0f),
                    new Vector2(0.0f, -outlineThickness),
                    new Vector2(0.0f, outlineThickness),
                    new Vector2(-outlineThickness, -outlineThickness),
                    new Vector2(-outlineThickness, outlineThickness),
                    new Vector2(outlineThickness, -outlineThickness),
                    new Vector2(outlineThickness, outlineThickness),
                };

                var outlineU32 = ImGui.GetColorU32(outlineColor);
                foreach (var offset in offsets)
                {
                    drawList.AddText(font, fontSize, drawPos + offset, outlineU32, displayText);
                }
            }

            drawList.AddText(font, fontSize, drawPos, ImGui.GetColorU32(textColor), displayText);
            ImGui.Dummy(textSize);
            ImGui.SetWindowFontScale(1.0f);

            if (popup.IsPositionSetting)
            {
                this.HandlePositionSettingDrag(popup);
            }
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private static float CalculateTextFadeAlpha(PopupImageState popup)
    {
        if (popup.IsPositionSetting)
        {
            return 1.0f;
        }

        var trigger = popup.Trigger;
        if (!trigger.EnableTextFadeIn)
        {
            return 1.0f;
        }

        var fadeSeconds = Math.Max(0.01f, trigger.TextFadeInSeconds);
        var elapsed = (float)(DateTime.UtcNow - popup.StartTimeUtc).TotalSeconds;
        return Math.Clamp(elapsed / fadeSeconds, 0.0f, 1.0f);
    }

    private static Vector2 CalculateImageSize(HappyTriggerSetting trigger, IDalamudTextureWrap texture)
    {
        float width;
        float height;

        if (trigger.UseOriginalImageSize)
        {
            width = Math.Max(1.0f, texture.Width);
            height = Math.Max(1.0f, texture.Height);
        }
        else
        {
            var fallbackSize = Math.Max(1.0f, trigger.ImageSize);
            width = Math.Max(1.0f, trigger.ImageWidth > 0.0f ? trigger.ImageWidth : fallbackSize);
            height = Math.Max(1.0f, trigger.ImageHeight > 0.0f ? trigger.ImageHeight : fallbackSize);
        }

        var scale = Math.Clamp(trigger.ScalePercent <= 0.0f ? 100.0f : trigger.ScalePercent, 1.0f, 10000.0f) / 100.0f;
        width *= scale;
        height *= scale;

        return new Vector2(Math.Max(1.0f, width), Math.Max(1.0f, height));
    }

    private void HandlePositionSettingDrag(PopupImageState popup)
    {
        var trigger = popup.Trigger;

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            popup.IsClosed = true;
            return;
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            popup.IsDragging = true;
        }

        if (!popup.IsDragging)
        {
            return;
        }

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var delta = ImGui.GetIO().MouseDelta;

            if (delta.X != 0.0f || delta.Y != 0.0f)
            {
                trigger.PositionX += delta.X;
                trigger.PositionY += delta.Y;
                popup.PositionChanged = true;
            }

            return;
        }

        popup.IsDragging = false;

        if (popup.PositionChanged)
        {
            popup.PositionChanged = false;
        }
    }

    public void Dispose()
    {
        ChatGui.ChatMessage -= this.OnChatMessage;

        PluginInterface.UiBuilder.Draw -= this.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;

        CommandManager.RemoveHandler(CommandName);

        this.windowSystem.RemoveAllWindows();
        this.imageCacheService.Dispose();
    }
}
