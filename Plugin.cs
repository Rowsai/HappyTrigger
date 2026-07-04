using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using Dalamud.Game;
using Dalamud.Hooking;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using LuminaAction = Lumina.Excel.Sheets.Action;
using LuminaStatus = Lumina.Excel.Sheets.Status;

namespace HappyTrigger;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/happytrigger";
    private const int MaxLogEntries = 500;
    private const double FfxivLogReferencePairWindowSeconds = 10.0;

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    internal static IDataManager DataManager { get; private set; } = null!;

    [PluginService]
    internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;

    [PluginService]
    internal static ITextureProvider TextureProvider { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("HappyTrigger");
    private readonly Configuration configuration;
    private readonly ImageCacheService imageCacheService;
    private readonly VfxLogCollector vfxLogCollector;
    private readonly HappyTriggerWindow configWindow;
    private readonly List<PopupImageState> activePopups = new();
    private readonly List<FfxivLogEntry> battleLogs = new();
    private readonly List<FfxivLogEntry> internalLogs = new();
    private readonly object logLock = new();
    private readonly HashSet<string> activeEnemyCastingLogKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> activeMemberStatusLogKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> activeEnemyStatusLogKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StatusRemainingSnapshot> latestMemberStatusRemainingByJobAndName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FfxivLogReferenceMatchState> ffxivLogReferenceMatchStates = new(StringComparer.OrdinalIgnoreCase);
    private bool wasFullWipeDetected = false;

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
            trigger.UseFfxivLogReference = false;
        }

        foreach (var trigger in this.configuration.FfxivLogTriggers)
        {
            trigger.UseFfxivLogReference = true;
        }

        if (this.EnsureTriggerIds())
        {
            this.SaveConfig();
        }

        this.imageCacheService = new ImageCacheService(TextureProvider);
        this.vfxLogCollector = new VfxLogCollector(ObjectTable, GameInteropProvider, Log, this.AddVfxInternalLog);
        this.configWindow = new HappyTriggerWindow(
            this.configuration,
            this.SaveConfig,
            trigger => this.ActivatePopup(trigger, true),
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

    private bool EnsureTriggerIds()
    {
        var changed = false;
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var trigger in this.configuration.Triggers)
        {
            trigger.DisplayTextMode = false;
            changed |= EnsureTriggerId(trigger, "I", usedIds);
        }

        foreach (var trigger in this.configuration.TextTriggers)
        {
            trigger.DisplayTextMode = true;
            trigger.UseFfxivLogReference = false;
            changed |= EnsureTriggerId(trigger, "T", usedIds);
        }

        foreach (var trigger in this.configuration.FfxivLogTriggers)
        {
            trigger.UseFfxivLogReference = true;
            var beforeInternalLogKeywords = string.Join("\n", trigger.GetInternalLogKeywords());
            trigger.NormalizeInternalLogKeywords();
            changed |= !string.Equals(beforeInternalLogKeywords, string.Join("\n", trigger.GetInternalLogKeywords()), StringComparison.Ordinal);
            changed |= EnsureTriggerId(trigger, trigger.UsePrerequisite ? "X" : "F", usedIds);
        }

        return changed;
    }

    private bool EnsureTriggerId(HappyTriggerSetting trigger, string prefix, HashSet<string> usedIds)
    {
        var changed = false;

        if (!HappyTriggerSetting.IsValidTriggerId(trigger.TriggerId, prefix) || usedIds.Contains(trigger.TriggerId))
        {
            trigger.TriggerId = this.GenerateNextTriggerId(prefix, usedIds);
            changed = true;
        }

        usedIds.Add(trigger.TriggerId);
        return changed;
    }

    private string GenerateNextTriggerId(string prefix, HashSet<string> usedIds)
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

        string nextId;
        do
        {
            maxNumber++;
            nextId = HappyTriggerSetting.FormatTriggerId(prefix, maxNumber);
        }
        while (usedIds.Contains(nextId));

        return nextId;
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

        var battleLogEntry = this.AddBattleLog(chatType, logText);
        this.EvaluateBattleLogReferenceTriggers(battleLogEntry);

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

    private void AddVfxInternalLog(string text)
    {
        this.AddInternalLog(text);
    }

    private FfxivLogEntry AddBattleLog(string category, string text)
    {
        return this.AddLogEntry(this.battleLogs, category, text);
    }

    private FfxivLogEntry AddInternalLog(string text, bool evaluateFfxivLogTriggers = true)
    {
        var logEntry = this.AddLogEntry(this.internalLogs, "HappyTrigger", text);

        if (evaluateFfxivLogTriggers)
        {
            this.EvaluateInternalLogReferenceTriggers(logEntry);
        }

        return logEntry;
    }

    private FfxivLogEntry AddLogEntry(List<FfxivLogEntry> target, string category, string text)
    {
        var logEntry = new FfxivLogEntry(DateTime.Now, category, text);

        lock (this.logLock)
        {
            target.Add(logEntry);

            if (target.Count > MaxLogEntries)
            {
                target.RemoveRange(0, target.Count - MaxLogEntries);
            }
        }

        return logEntry;
    }

    private void EvaluateBattleLogReferenceTriggers(FfxivLogEntry logEntry)
    {
        foreach (var trigger in this.configuration.FfxivLogTriggers)
        {
            trigger.UseFfxivLogReference = true;

            // バトルログ欄が空の場合は、バトルログでは判定しません。
            if (!trigger.IsBattleLogReferenceMatch(logEntry))
            {
                continue;
            }

            this.HandleFfxivLogReferenceMatched(trigger, FfxivLogReferenceSource.BattleLog, logEntry);
        }
    }

    private void EvaluateInternalLogReferenceTriggers(FfxivLogEntry logEntry)
    {
        foreach (var trigger in this.configuration.FfxivLogTriggers)
        {
            trigger.UseFfxivLogReference = true;

            var internalLogKeywords = trigger.GetInternalLogKeywords();
            for (var i = 0; i < internalLogKeywords.Count; i++)
            {
                if (!trigger.IsInternalLogReferenceMatch(logEntry, i))
                {
                    continue;
                }

                this.HandleFfxivLogReferenceMatched(trigger, FfxivLogReferenceSource.InternalLog, logEntry, i);
            }
        }
    }

    private void HandleFfxivLogReferenceMatched(
        HappyTriggerSetting trigger,
        FfxivLogReferenceSource source,
        FfxivLogEntry logEntry,
        int internalLogKeywordIndex = -1)
    {
        var requiresBattleLog = !string.IsNullOrWhiteSpace(trigger.BattleLogKeyword);
        var requiredInternalLogCount = trigger.GetInternalLogKeywords().Count;

        if (!requiresBattleLog && requiredInternalLogCount == 0)
        {
            return;
        }

        if (trigger.UsePrerequisite && !this.HasActivePrerequisiteTrigger(trigger))
        {
            this.ResetFfxivLogReferenceMatchState(trigger);
            return;
        }

        // バトルログだけの条件は、バトルログに表示されたタイミングで即発火します。
        if (requiresBattleLog && requiredInternalLogCount == 0)
        {
            this.FireFfxivLogReferenceTrigger(trigger, source);
            return;
        }

        // 内部ログ1つだけ、かつバトルログ条件なしの場合は、内部ログに表示されたタイミングで即発火します。
        if (!requiresBattleLog && requiredInternalLogCount == 1)
        {
            this.FireFfxivLogReferenceTrigger(trigger, source);
            return;
        }

        // バトルログ + 内部ログ、または複数内部ログの場合は、
        // 条件ごとのマッチ情報を保持し、すべて揃った場合だけ発火します。
        // 内部ログが複数ある場合は「内部ログ1 → 内部ログ2 → ...」の順番でだけ進行します。
        var state = this.GetFfxivLogReferenceMatchState(trigger);
        this.RemoveExpiredFfxivLogReferenceMatches(state);

        if (source == FfxivLogReferenceSource.BattleLog)
        {
            state.BattleLogMatchedAtUtc = logEntry.Timestamp.ToUniversalTime();
        }
        else if (internalLogKeywordIndex >= 0)
        {
            if (!this.TryRecordSequentialInternalLogMatch(trigger, state, internalLogKeywordIndex, logEntry))
            {
                return;
            }
        }

        if (!this.IsFfxivLogReferencePairSatisfied(trigger, state))
        {
            return;
        }

        this.FireFfxivLogReferenceTrigger(trigger, FfxivLogReferenceSource.All);
        this.ResetFfxivLogReferenceMatchState(trigger);
    }

    private bool TryRecordSequentialInternalLogMatch(
        HappyTriggerSetting trigger,
        FfxivLogReferenceMatchState state,
        int matchedIndex,
        FfxivLogEntry logEntry)
    {
        var requiredInternalLogCount = trigger.GetInternalLogKeywords().Count;
        if (requiredInternalLogCount == 0 || matchedIndex < 0 || matchedIndex >= requiredInternalLogCount)
        {
            return false;
        }

        var expectedIndex = GetNextRequiredInternalLogIndex(state, requiredInternalLogCount);

        if (matchedIndex != expectedIndex)
        {
            // 内部ログ1が再度マッチした場合は、新しい組み合わせの開始として扱います。
            // 例: 内部ログ1 → 別ログ → 内部ログ1 → 内部ログ2 のようなケースで、後半の組み合わせを拾えます。
            if (matchedIndex != 0)
            {
                return false;
            }

            state.InternalLogMatchedAtUtcByIndex.Clear();
            expectedIndex = 0;
        }

        state.InternalLogMatchedAtUtcByIndex[expectedIndex] = logEntry.Timestamp.ToUniversalTime();
        return true;
    }

    private static int GetNextRequiredInternalLogIndex(
        FfxivLogReferenceMatchState state,
        int requiredInternalLogCount)
    {
        for (var i = 0; i < requiredInternalLogCount; i++)
        {
            if (!state.InternalLogMatchedAtUtcByIndex.ContainsKey(i))
            {
                return i;
            }
        }

        return requiredInternalLogCount;
    }

    private void RemoveExpiredFfxivLogReferenceMatches(FfxivLogReferenceMatchState state)
    {
        var nowUtc = DateTime.UtcNow;

        if (state.BattleLogMatchedAtUtc != null &&
            (nowUtc - state.BattleLogMatchedAtUtc.Value).TotalSeconds > FfxivLogReferencePairWindowSeconds)
        {
            state.BattleLogMatchedAtUtc = null;
        }

        var expiredInternalIndexes = state.InternalLogMatchedAtUtcByIndex
            .Where(pair => (nowUtc - pair.Value).TotalSeconds > FfxivLogReferencePairWindowSeconds)
            .Select(pair => pair.Key)
            .ToList();

        if (expiredInternalIndexes.Count == 0)
        {
            return;
        }

        // 途中の内部ログが期限切れになった場合、順序保証のため内部ログ側の保持情報はすべてリセットします。
        state.InternalLogMatchedAtUtcByIndex.Clear();
    }

    private FfxivLogReferenceMatchState GetFfxivLogReferenceMatchState(HappyTriggerSetting trigger)
    {
        var key = this.GetFfxivLogReferenceStateKey(trigger);

        if (!this.ffxivLogReferenceMatchStates.TryGetValue(key, out var state))
        {
            state = new FfxivLogReferenceMatchState();
            this.ffxivLogReferenceMatchStates[key] = state;
        }

        return state;
    }

    private bool IsFfxivLogReferencePairSatisfied(HappyTriggerSetting trigger, FfxivLogReferenceMatchState state)
    {
        var requiresBattleLog = !string.IsNullOrWhiteSpace(trigger.BattleLogKeyword);
        var requiredInternalLogCount = trigger.GetInternalLogKeywords().Count;
        var matchedTimes = new List<DateTime>();

        if (requiresBattleLog)
        {
            if (state.BattleLogMatchedAtUtc == null)
            {
                return false;
            }

            matchedTimes.Add(state.BattleLogMatchedAtUtc.Value);
        }

        for (var i = 0; i < requiredInternalLogCount; i++)
        {
            if (!state.InternalLogMatchedAtUtcByIndex.TryGetValue(i, out var matchedAtUtc))
            {
                return false;
            }

            matchedTimes.Add(matchedAtUtc);
        }

        if (matchedTimes.Count == 0)
        {
            return false;
        }

        var earliest = matchedTimes.Min();
        var latest = matchedTimes.Max();
        return (latest - earliest).TotalSeconds <= FfxivLogReferencePairWindowSeconds;
    }

    private void ResetFfxivLogReferenceMatchState(HappyTriggerSetting trigger)
    {
        this.ffxivLogReferenceMatchStates.Remove(this.GetFfxivLogReferenceStateKey(trigger));
    }

    private string GetFfxivLogReferenceStateKey(HappyTriggerSetting trigger)
    {
        if (!string.IsNullOrWhiteSpace(trigger.TriggerId))
        {
            return trigger.TriggerId;
        }

        return $"{trigger.BattleLogKeyword}::{string.Join("|", trigger.GetInternalLogKeywords())}::{trigger.DisplayTextMode}";
    }

    private bool HasActivePrerequisiteTrigger(HappyTriggerSetting trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger.PrerequisiteTriggerId))
        {
            return false;
        }

        this.activePopups.RemoveAll(x => x.IsExpired || x.IsClosed);

        foreach (var popup in this.activePopups)
        {
            if (popup.IsPositionSetting || !popup.IsReadyToDisplay || popup.IsExpired || popup.IsClosed)
            {
                continue;
            }

            var prerequisiteTrigger = popup.Trigger;
            if (string.Equals(prerequisiteTrigger.TriggerId, trigger.PrerequisiteTriggerId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void FireFfxivLogReferenceTrigger(HappyTriggerSetting trigger, FfxivLogReferenceSource source)
    {
        StatusRemainingSnapshot? statusRemainingSnapshot = null;
        if (trigger.HasStatusRemainingAppendSetting() && !this.TryGetStatusRemainingSnapshot(trigger, out statusRemainingSnapshot))
        {
            this.AddInternalLog(
                $"Status remaining not found. Id={trigger.TriggerId} job={trigger.StatusRemainingJob} StatusName={trigger.StatusRemainingStatusName}",
                false);
        }

        this.AddInternalLog(
            $"FFXIV Log trigger matched. Id={trigger.TriggerId} Source={source} Prerequisite={(trigger.UsePrerequisite ? "ON" : "OFF")} PrerequisiteId='{trigger.PrerequisiteTriggerId}' BattleLog='{trigger.BattleLogKeyword}' InternalLogs='{string.Join(" / ", trigger.GetInternalLogKeywords())}' StatusRemaining={(trigger.EnableStatusRemainingAppend ? $"ON:{trigger.StatusRemainingJob}/{trigger.StatusRemainingStatusName}" : "OFF")}",
            false);
        this.ActivatePopup(trigger, false, statusRemainingSnapshot);
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

        this.ffxivLogReferenceMatchStates.Clear();
        this.AddInternalLog("FFXIV Log cleared.");
    }

    private void ActivatePopup(
        HappyTriggerSetting trigger,
        bool writeInternalLog = true,
        StatusRemainingSnapshot? statusRemainingSnapshot = null)
    {
        if (trigger.DisplayTextMode)
        {
            if (string.IsNullOrWhiteSpace(trigger.DisplayText))
            {
                return;
            }

            trigger.DisplayTextMode = true;
            this.activePopups.RemoveAll(x => x.IsExpired || x.IsClosed);
            var statusRemainingDisplayState = statusRemainingSnapshot == null
                ? null
                : new StatusRemainingDisplayState(
                    statusRemainingSnapshot.StatusName,
                    statusRemainingSnapshot.RemainingSeconds,
                    statusRemainingSnapshot.CapturedAtUtc);
            this.activePopups.Add(new PopupImageState(trigger, false, statusRemainingDisplayState));
            if (writeInternalLog)
            {
                this.AddInternalLog($"Text display queued. Text='{trigger.DisplayText}', Wait={Math.Clamp(trigger.WaitSeconds, 0.0f, 600.0f):0.##}s, X={trigger.PositionX:0}, Y={trigger.PositionY:0}");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(trigger.ImagePath))
        {
            return;
        }

        trigger.DisplayTextMode = false;
        this.activePopups.RemoveAll(x => x.IsExpired || x.IsClosed);
        this.activePopups.Add(new PopupImageState(trigger));
        if (writeInternalLog)
        {
            this.AddInternalLog($"Image display queued. Image='{trigger.ImagePath}', Wait={Math.Clamp(trigger.WaitSeconds, 0.0f, 600.0f):0.##}s, X={trigger.PositionX:0}, Y={trigger.PositionY:0}");
        }
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
        this.UpdateEnemyCastingInternalLogs();
        this.UpdateMemberStatusInternalLogs();
        this.UpdateEnemyStatusInternalLogs();
        this.UpdateFullWipeDetection();
        this.windowSystem.Draw();
        this.DrawActivePopups();
    }

    private void UpdateFullWipeDetection()
    {
        try
        {
            var players = GetReplayPlayerCharacters()
                .Where(player => player.MaxHp > 0)
                .ToList();

            if (players.Count == 0)
            {
                this.wasFullWipeDetected = false;
                return;
            }

            var isFullWipe = players.All(player => player.CurrentHp <= 0);
            if (!isFullWipe)
            {
                this.wasFullWipeDetected = false;
                return;
            }

            var removedCount = this.CloseAllTextPopups();
            if (!this.wasFullWipeDetected)
            {
                this.wasFullWipeDetected = true;
                this.AddInternalLog($"Full wipe detected. Text displays closed. Count={removedCount}", false);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to update full wipe detection.");
        }
    }

    private int CloseAllTextPopups()
    {
        return this.activePopups.RemoveAll(popup =>
            !popup.IsPositionSetting &&
            popup.Trigger.DisplayTextMode);
    }

    private void UpdateMemberStatusInternalLogs()
    {
        try
        {
            var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var player in GetReplayPlayerCharacters())
            {
                var memberName = player.Name.TextValue;
                var job = player.ClassJob.ValueNullable?.Abbreviation.ExtractText() ?? "-";
                var statuses = player.StatusList;

                for (var i = 0; i < statuses.Length; i++)
                {
                    var status = statuses[i];

                    if (status == null || status.StatusId == 0)
                    {
                        continue;
                    }

                    var statusName = GetStatusName(status.StatusId);
                    this.UpdateStatusRemainingSnapshot(job, statusName, status.RemainingTime);
                    var key = $"Member:{player.EntityId}:{memberName}:{job}:{status.StatusId}:{status.Param}:{status.SourceId}:{i}";
                    currentKeys.Add(key);

                    if (this.activeMemberStatusLogKeys.Add(key))
                    {
                        this.AddMemberStatusInternalLog(
                            job,
                            status.StatusId,
                            statusName,
                            status.Param,
                            status.RemainingTime);
                    }
                }
            }

            this.activeMemberStatusLogKeys.RemoveWhere(key => !currentKeys.Contains(key));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to update member status internal logs.");
        }
    }

    private void UpdateStatusRemainingSnapshot(string job, string statusName, float remainingSeconds)
    {
        if (string.IsNullOrWhiteSpace(job) || string.IsNullOrWhiteSpace(statusName) || remainingSeconds <= 0.0f)
        {
            return;
        }

        this.latestMemberStatusRemainingByJobAndName[MakeStatusRemainingKey(job, statusName)] = new StatusRemainingSnapshot(
            job,
            statusName,
            remainingSeconds,
            DateTime.UtcNow);
    }

    private bool TryGetStatusRemainingSnapshot(HappyTriggerSetting trigger, out StatusRemainingSnapshot? snapshot)
    {
        snapshot = null;

        if (!trigger.HasStatusRemainingAppendSetting())
        {
            return false;
        }

        var key = MakeStatusRemainingKey(trigger.StatusRemainingJob, trigger.StatusRemainingStatusName);
        if (!this.latestMemberStatusRemainingByJobAndName.TryGetValue(key, out var found))
        {
            return false;
        }

        var currentRemaining = found.RemainingSeconds - (float)Math.Max(0.0, (DateTime.UtcNow - found.CapturedAtUtc).TotalSeconds);
        if (currentRemaining <= 0.0f)
        {
            this.latestMemberStatusRemainingByJobAndName.Remove(key);
            return false;
        }

        snapshot = new StatusRemainingSnapshot(found.Job, found.StatusName, currentRemaining, DateTime.UtcNow);
        return true;
    }

    private static string MakeStatusRemainingKey(string job, string statusName)
    {
        return $"{job.Trim()}::{statusName.Trim()}";
    }

    private void AddMemberStatusInternalLog(
        string job,
        uint statusId,
        string statusName,
        ushort param,
        float remaining)
    {
        this.AddInternalLog($"MemberStatus Information. job={job} StatusId={statusId} StatusName={statusName} Param={param} Remaining={remaining:0.00}s");
    }

    private void UpdateEnemyStatusInternalLogs()
    {
        try
        {
            var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var battleChara in GetEnemyBattleCharas())
            {
                var enemyName = battleChara.Name.TextValue;
                var entityId = battleChara.EntityId;
                var statuses = battleChara.StatusList;

                for (var i = 0; i < statuses.Length; i++)
                {
                    var status = statuses[i];

                    if (status == null || status.StatusId == 0)
                    {
                        continue;
                    }

                    var statusName = GetStatusName(status.StatusId);
                    var key = $"Enemy:{entityId}:{enemyName}:{status.StatusId}:{status.Param}:{status.SourceId}:{i}";
                    currentKeys.Add(key);

                    if (this.activeEnemyStatusLogKeys.Add(key))
                    {
                        this.AddEnemyStatusInternalLog(
                            enemyName,
                            status.StatusId,
                            statusName,
                            status.Param,
                            status.RemainingTime,
                            status.SourceId);
                    }
                }
            }

            this.activeEnemyStatusLogKeys.RemoveWhere(key => !currentKeys.Contains(key));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to update enemy status internal logs.");
        }
    }

    private void AddEnemyStatusInternalLog(
        string enemyName,
        uint statusId,
        string statusName,
        ushort param,
        float remaining,
        ulong sourceId)
    {
        this.AddInternalLog($"EnemyStatus Information. Enemy={enemyName} StatusId={statusId} StatusName={statusName} Param={param} Remaining={remaining:0.00}s SourceId={sourceId}");
    }

    private static string GetStatusName(uint statusId)
    {
        if (statusId == 0)
        {
            return "-";
        }

        try
        {
            var statusSheet = DataManager.GetExcelSheet<LuminaStatus>();
            var status = statusSheet.GetRow(statusId);
            var statusName = status.Name.ExtractText();
            return string.IsNullOrWhiteSpace(statusName) ? "-" : statusName;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, $"Failed to get status name. StatusId={statusId}");
            return "-";
        }
    }

    private static List<IPlayerCharacter> GetReplayPlayerCharacters()
    {
        return ObjectTable
            .Where(obj => obj is IPlayerCharacter)
            .Cast<IPlayerCharacter>()
            .Where(player => !string.IsNullOrWhiteSpace(player.Name.TextValue))
            .OrderBy(player => player.Name.TextValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(player => player.EntityId)
            .ToList();
    }

    private void UpdateEnemyCastingInternalLogs()
    {
        try
        {
            var currentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var battleChara in GetEnemyBattleCharas())
            {
                if (!battleChara.IsCasting)
                {
                    continue;
                }

                var enemyName = battleChara.Name.TextValue;
                var entityId = battleChara.EntityId;
                var actionId = battleChara.CastActionId;
                var actionName = GetActionName(actionId);
                var totalCast = battleChara.TotalCastTime;
                var statuses = battleChara.StatusList;
                var hasStatus = false;

                for (var i = 0; i < statuses.Length; i++)
                {
                    var status = statuses[i];

                    if (status == null || status.StatusId == 0)
                    {
                        continue;
                    }

                    hasStatus = true;
                    var key = $"{entityId}:{actionId}:{totalCast:0.00}:{status.StatusId}:{status.Param}:{i}";
                    currentKeys.Add(key);

                    if (this.activeEnemyCastingLogKeys.Add(key))
                    {
                        this.AddEnemyCastingInternalLog(
                            enemyName,
                            actionId,
                            actionName,
                            totalCast,
                            status.StatusId.ToString(),
                            status.Param.ToString());
                    }
                }

                if (!hasStatus)
                {
                    var key = $"{entityId}:{actionId}:{totalCast:0.00}:NoStatus";
                    currentKeys.Add(key);

                    if (this.activeEnemyCastingLogKeys.Add(key))
                    {
                        this.AddEnemyCastingInternalLog(
                            enemyName,
                            actionId,
                            actionName,
                            totalCast,
                            "-",
                            "-");
                    }
                }
            }

            this.activeEnemyCastingLogKeys.RemoveWhere(key => !currentKeys.Contains(key));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to update enemy casting internal logs.");
        }
    }

    private void AddEnemyCastingInternalLog(
        string enemyName,
        uint actionId,
        string actionName,
        float castTotal,
        string statusId,
        string param)
    {
        this.AddInternalLog($"EnemyCasting Infonmation. Enemy={enemyName} ActionId={actionId} ActionName={actionName} CastTotal={castTotal:0.00}s StatusId={statusId} Param={param}");
    }

    private static string GetActionName(uint actionId)
    {
        if (actionId == 0)
        {
            return "-";
        }

        try
        {
            var actionSheet = DataManager.GetExcelSheet<LuminaAction>();
            var action = actionSheet.GetRow(actionId);
            var actionName = action.Name.ExtractText();
            return string.IsNullOrWhiteSpace(actionName) ? "-" : actionName;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, $"Failed to get action name. ActionId={actionId}");
            return "-";
        }
    }

    private static List<IBattleChara> GetEnemyBattleCharas()
    {
        return ObjectTable
            .Where(obj => obj is IBattleChara)
            .Cast<IBattleChara>()
            .Where(battleChara => battleChara.ObjectKind == ObjectKind.BattleNpc)
            .Where(battleChara => !string.IsNullOrWhiteSpace(battleChara.Name.TextValue))
            .OrderBy(battleChara => battleChara.Name.TextValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(battleChara => battleChara.EntityId)
            .ToList();
    }

    private void DrawActivePopups()
    {
        this.activePopups.RemoveAll(x => x.IsExpired || x.IsClosed);

        for (var i = 0; i < this.activePopups.Count; i++)
        {
            var popup = this.activePopups[i];
            if (!popup.IsReadyToDisplay)
            {
                continue;
            }

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
        var shadowColor = new Vector4(
            Math.Clamp(trigger.TextShadowColorR, 0.0f, 1.0f),
            Math.Clamp(trigger.TextShadowColorG, 0.0f, 1.0f),
            Math.Clamp(trigger.TextShadowColorB, 0.0f, 1.0f),
            Math.Clamp(trigger.TextShadowColorA, 0.0f, 1.0f) * fadeAlpha);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4.0f, 2.0f));

        if (ImGui.Begin(windowName, flags))
        {
            var displayText = GetPopupDisplayText(popup);
            var drawList = ImGui.GetWindowDrawList();
            var font = ImGui.GetFont();
            var baseFontSize = Math.Max(1.0f, ImGui.GetFontSize());
            var fontSize = Math.Clamp(trigger.TextSize, 8.0f, 256.0f);
            var fontScale = fontSize / baseFontSize;
            var textSize = ImGui.CalcTextSize(displayText) * fontScale;
            var drawPos = ImGui.GetCursorScreenPos();

            if (trigger.EnableTextPixelSnap)
            {
                drawPos = new Vector2(MathF.Round(drawPos.X), MathF.Round(drawPos.Y));
            }

            DrawDecoratedText(
                drawList,
                font,
                fontSize,
                drawPos,
                displayText,
                ImGui.GetColorU32(textColor),
                ImGui.GetColorU32(outlineColor),
                ImGui.GetColorU32(shadowColor),
                trigger);

            var extraPadding = CalculateTextDecoratePadding(trigger);
            ImGui.Dummy(textSize + new Vector2(extraPadding * 2.0f, extraPadding * 2.0f));

            if (popup.IsPositionSetting)
            {
                this.HandlePositionSettingDrag(popup);
            }
        }

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private static string GetPopupDisplayText(PopupImageState popup)
    {
        var trigger = popup.Trigger;
        var displayText = trigger.DisplayText ?? string.Empty;

        if (!popup.HasStatusRemainingDisplay)
        {
            return displayText;
        }

        var remaining = popup.CurrentStatusRemainingSeconds;
        var suffix = $"{popup.StatusRemainingStatusName}（{remaining:0.00}s）";

        if (string.IsNullOrWhiteSpace(displayText))
        {
            return suffix;
        }

        if (displayText.Contains(popup.StatusRemainingStatusName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{displayText}（{remaining:0.00}s）";
        }

        return $"{displayText} {suffix}";
    }

    private static void DrawDecoratedText(
        ImDrawListPtr drawList,
        ImFontPtr font,
        float fontSize,
        Vector2 drawPos,
        string displayText,
        uint textColor,
        uint outlineColor,
        uint shadowColor,
        HappyTriggerSetting trigger)
    {
        if (string.IsNullOrEmpty(displayText))
        {
            return;
        }

        if (trigger.TextFontDesign == TextFontDesign.Shadow || trigger.TextFontDesign == TextFontDesign.Neon)
        {
            var shadowOffset = new Vector2(trigger.TextShadowOffsetX, trigger.TextShadowOffsetY);
            drawList.AddText(font, fontSize, drawPos + shadowOffset, shadowColor, displayText);
        }

        if (trigger.TextFontDesign == TextFontDesign.Neon)
        {
            DrawTextOffsets(drawList, font, fontSize, drawPos, shadowColor, displayText, 4.0f, true);
            DrawTextOffsets(drawList, font, fontSize, drawPos, shadowColor, displayText, 7.0f, false);
        }

        var useOutline = trigger.EnableTextOutline || trigger.TextFontDesign == TextFontDesign.StrongOutline;
        if (useOutline)
        {
            var outlineThickness = Math.Max(1.0f, trigger.TextOutlineThickness);
            if (trigger.TextFontDesign == TextFontDesign.StrongOutline)
            {
                outlineThickness = Math.Max(3.0f, outlineThickness * 1.5f);
            }

            DrawTextOffsets(drawList, font, fontSize, drawPos, outlineColor, displayText, outlineThickness, true);
        }

        if (trigger.TextFontDesign == TextFontDesign.Bold)
        {
            drawList.AddText(font, fontSize, drawPos + new Vector2(1.0f, 0.0f), textColor, displayText);
            drawList.AddText(font, fontSize, drawPos + new Vector2(0.0f, 1.0f), textColor, displayText);
            drawList.AddText(font, fontSize, drawPos + new Vector2(1.0f, 1.0f), textColor, displayText);
        }

        drawList.AddText(font, fontSize, drawPos, textColor, displayText);
    }

    private static void DrawTextOffsets(
        ImDrawListPtr drawList,
        ImFontPtr font,
        float fontSize,
        Vector2 drawPos,
        uint color,
        string text,
        float thickness,
        bool includeDiagonals)
    {
        var offsets = includeDiagonals
            ? new[]
            {
                new Vector2(-thickness, 0.0f),
                new Vector2(thickness, 0.0f),
                new Vector2(0.0f, -thickness),
                new Vector2(0.0f, thickness),
                new Vector2(-thickness, -thickness),
                new Vector2(-thickness, thickness),
                new Vector2(thickness, -thickness),
                new Vector2(thickness, thickness),
            }
            : new[]
            {
                new Vector2(-thickness, 0.0f),
                new Vector2(thickness, 0.0f),
                new Vector2(0.0f, -thickness),
                new Vector2(0.0f, thickness),
            };

        foreach (var offset in offsets)
        {
            drawList.AddText(font, fontSize, drawPos + offset, color, text);
        }
    }

    private static float CalculateTextDecoratePadding(HappyTriggerSetting trigger)
    {
        var padding = 4.0f;

        if (trigger.EnableTextOutline || trigger.TextFontDesign == TextFontDesign.StrongOutline)
        {
            padding = Math.Max(padding, Math.Max(1.0f, trigger.TextOutlineThickness) * 2.0f);
        }

        if (trigger.TextFontDesign == TextFontDesign.Shadow || trigger.TextFontDesign == TextFontDesign.Neon)
        {
            padding = Math.Max(padding, Math.Abs(trigger.TextShadowOffsetX) + 4.0f);
            padding = Math.Max(padding, Math.Abs(trigger.TextShadowOffsetY) + 4.0f);
        }

        if (trigger.TextFontDesign == TextFontDesign.Neon)
        {
            padding = Math.Max(padding, 10.0f);
        }

        return padding;
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


    private enum FfxivLogReferenceSource
    {
        BattleLog,
        InternalLog,
        Both,
        All,
    }

    private sealed class FfxivLogReferenceMatchState
    {
        public DateTime? BattleLogMatchedAtUtc { get; set; }

        public Dictionary<int, DateTime> InternalLogMatchedAtUtcByIndex { get; } = new();
    }

    public void Dispose()
    {
        ChatGui.ChatMessage -= this.OnChatMessage;

        PluginInterface.UiBuilder.Draw -= this.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;

        CommandManager.RemoveHandler(CommandName);

        this.windowSystem.RemoveAllWindows();
        this.vfxLogCollector.Dispose();
        this.imageCacheService.Dispose();
    }
}

internal sealed class StatusRemainingSnapshot
{
    public StatusRemainingSnapshot(string job, string statusName, float remainingSeconds, DateTime capturedAtUtc)
    {
        this.Job = job;
        this.StatusName = statusName;
        this.RemainingSeconds = remainingSeconds;
        this.CapturedAtUtc = capturedAtUtc;
    }

    public string Job { get; }

    public string StatusName { get; }

    public float RemainingSeconds { get; }

    public DateTime CapturedAtUtc { get; }
}
