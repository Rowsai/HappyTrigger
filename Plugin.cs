using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
using LuminaContentFinderCondition = Lumina.Excel.Sheets.ContentFinderCondition;

namespace HappyTrigger;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/happytrigger";
    private const int MaxLogEntries = 5000;

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IClientState ClientState { get; private set; } = null!;

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
    private readonly TextTextureCacheService textTextureCacheService;
    private readonly VoiceVoxSpeechService voiceVoxSpeechService;
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

    // ステータス残り時間つきトリガーの重複発火抑止用です。
    // activePopups だけを見ると、同一フレーム内や表示開始前/表示状態更新前のタイミングで
    // 別トリガーが同じ StatusName に対して再度発火するケースがあるため、
    // 「マッチ済み」の時点で StatusName をロックします。
    private readonly Dictionary<string, DateTime> activeStatusRemainingMatchLocks = new(StringComparer.OrdinalIgnoreCase);

    // 同一ステータスの複数表示を許可しているラベル/トリガーでも、
    // 「同じログトリガー自身」が表示中・残り時間中に再マッチして重複表示されるのは避けます。
    // 例: F00059(Param=1121) と F00058(Param=1122) はそれぞれ1回ずつ表示可能、
    //     ただし F00059 が表示済みの間に再度 F00059 が揃っても抑止します。
    private readonly Dictionary<string, DateTime> activeFfxivLogTriggerMatchLocks = new(StringComparer.OrdinalIgnoreCase);

    // 同一ステータス複数表示許可ラベルでは、複数のログトリガーが同じ MemberStatus / EnemyCasting ログを
    // 猶予時間内に使い回して別テキストを発火してしまうことがあります。
    // そのため、例外設定が有効なトリガーで一度成立に使用したログは、猶予時間内だけ消費済みとして扱います。
    private readonly Dictionary<string, DateTime> consumedFfxivLogReferenceLogKeys = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, FfxivLogReferenceMatchState> ffxivLogReferenceMatchStates = new(StringComparer.OrdinalIgnoreCase);
    private bool wasFullWipeDetected = false;

    private double FfxivLogReferencePairWindowSeconds =>
        Math.Clamp(this.configuration.FfxivLogReferencePairWindowSeconds, 1.0f, 120.0f);

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
        this.textTextureCacheService = new TextTextureCacheService(TextureProvider);
        this.voiceVoxSpeechService = new VoiceVoxSpeechService(message => this.AddInternalLog(message, false));
        this.vfxLogCollector = new VfxLogCollector(ObjectTable, GameInteropProvider, Log, this.AddVfxInternalLog);
        this.configWindow = new HappyTriggerWindow(
            this.configuration,
            this.SaveConfig,
            trigger => this.ActivatePopup(trigger, true),
            this.ActivatePopups,
            this.ActivatePositionSettingTrigger,
            this.ActivatePositionSettingTriggers,
            this.ActivatePositionSettingForLabel,
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
            changed |= EnsureFfxivLogTriggerId(trigger, trigger.UsePrerequisite ? "X" : "F", usedIds);
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

        usedIds.Add(trigger.TriggerId ?? string.Empty);
        return changed;
    }

    private bool EnsureFfxivLogTriggerId(HappyTriggerSetting trigger, string prefix, HashSet<string> usedIds)
    {
        var changed = false;
        var currentId = trigger.TriggerId?.Trim() ?? string.Empty;

        if (!HappyTriggerSetting.IsValidFfxivLogTriggerId(currentId) || usedIds.Contains(currentId))
        {
            trigger.TriggerId = this.GenerateNextTriggerId(prefix, usedIds);
            changed = true;
        }
        else if (!string.Equals(trigger.TriggerId, currentId, StringComparison.Ordinal))
        {
            trigger.TriggerId = currentId;
            changed = true;
        }

        usedIds.Add(trigger.TriggerId ?? string.Empty);
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

            if (!this.IsTriggerHierarchyEnabled(trigger))
            {
                continue;
            }

            if (trigger.IsMatch(text))
            {
                this.AddInternalLog($"Image trigger matched. Keyword='{trigger.Keyword}', Message='{text}'");
                this.ActivatePopup(trigger);
            }
        }

        foreach (var trigger in this.configuration.TextTriggers)
        {
            trigger.DisplayTextMode = true;

            if (!this.IsTriggerHierarchyEnabled(trigger))
            {
                continue;
            }

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

    private bool IsTriggerLocationConditionSatisfied(HappyTriggerSetting trigger)
    {
        if (!this.IsTriggerHierarchyEnabled(trigger))
        {
            return false;
        }

        if (!trigger.UseFfxivLogReference)
        {
            return true;
        }

        var effectiveLocationCondition = this.GetEffectiveLocationCondition(trigger);
        if (effectiveLocationCondition.LocationRestrictionType == TriggerLocationRestrictionType.None)
        {
            return true;
        }

        var currentTerritoryTypeId = (uint)ClientState.TerritoryType;
        if (currentTerritoryTypeId == 0)
        {
            return false;
        }

        var requiredTerritoryTypeId = effectiveLocationCondition.RequiredTerritoryTypeId;
        if (requiredTerritoryTypeId == 0 && effectiveLocationCondition.LocationRestrictionType == TriggerLocationRestrictionType.Content)
        {
            requiredTerritoryTypeId = this.TryGetTerritoryTypeIdFromContentFinderCondition(effectiveLocationCondition.RequiredContentFinderConditionId);
        }

        return requiredTerritoryTypeId != 0 && currentTerritoryTypeId == requiredTerritoryTypeId;
    }

    private bool IsTriggerHierarchyEnabled(HappyTriggerSetting trigger)
    {
        if (!trigger.Enabled)
        {
            return false;
        }

        var label = this.GetTriggerLabel(trigger);
        if (label != null && !label.Enabled)
        {
            return false;
        }

        var box = this.GetTriggerBox(trigger, label);
        if (box != null && !box.Enabled)
        {
            return false;
        }

        return true;
    }

    private EffectiveLocationCondition GetEffectiveLocationCondition(HappyTriggerSetting trigger)
    {
        var label = this.GetTriggerLabel(trigger);
        var box = this.GetTriggerBox(trigger, label);

        // 優先順: トリガーボックス > トリガーラベル > ログトリガー自身
        // 上位階層に場所条件が設定されている場合、配下の個別設定より優先します。
        if (box != null && box.LocationRestrictionType != TriggerLocationRestrictionType.None)
        {
            return new EffectiveLocationCondition(
                box.LocationRestrictionType,
                box.RequiredTerritoryTypeId,
                box.RequiredContentFinderConditionId);
        }

        if (label != null && label.LocationRestrictionType != TriggerLocationRestrictionType.None)
        {
            return new EffectiveLocationCondition(
                label.LocationRestrictionType,
                label.RequiredTerritoryTypeId,
                label.RequiredContentFinderConditionId);
        }

        return new EffectiveLocationCondition(
            trigger.LocationRestrictionType,
            trigger.RequiredTerritoryTypeId,
            trigger.RequiredContentFinderConditionId);
    }

    private TriggerLabelSetting? GetTriggerLabel(HappyTriggerSetting trigger)
    {
        if (string.IsNullOrWhiteSpace(trigger.TriggerLabelId))
        {
            return null;
        }

        return this.configuration.TriggerLabels.FirstOrDefault(label =>
            string.Equals(label.LabelId, trigger.TriggerLabelId, StringComparison.OrdinalIgnoreCase));
    }

    private TriggerBoxSetting? GetTriggerBox(HappyTriggerSetting trigger, TriggerLabelSetting? label = null)
    {
        var boxId = label != null && !string.IsNullOrWhiteSpace(label.BoxId)
            ? label.BoxId
            : trigger.TriggerBoxId;

        if (string.IsNullOrWhiteSpace(boxId))
        {
            return null;
        }

        return this.configuration.TriggerBoxes.FirstOrDefault(box =>
            string.Equals(box.BoxId, boxId, StringComparison.OrdinalIgnoreCase));
    }

    private readonly record struct EffectiveLocationCondition(
        TriggerLocationRestrictionType LocationRestrictionType,
        uint RequiredTerritoryTypeId,
        uint RequiredContentFinderConditionId);

    private uint TryGetTerritoryTypeIdFromContentFinderCondition(uint contentFinderConditionId)
    {
        if (contentFinderConditionId == 0)
        {
            return 0;
        }

        try
        {
            var contentFinderCondition = DataManager.GetExcelSheet<LuminaContentFinderCondition>()
                .FirstOrDefault(row => row.RowId == contentFinderConditionId);
            return TryReadRowIdFromProperty(contentFinderCondition, "TerritoryType");
        }
        catch (Exception ex)
        {
            Log.Debug(ex, $"Failed to resolve ContentFinderCondition territory. ContentFinderConditionId={contentFinderConditionId}");
            return 0;
        }
    }

    private static uint TryReadRowIdFromProperty(object row, string propertyName)
    {
        var property = row.GetType().GetProperty(propertyName);
        var value = property?.GetValue(row);
        if (value == null)
        {
            return 0;
        }

        var rowIdProperty = value.GetType().GetProperty("RowId");
        if (rowIdProperty?.GetValue(value) is uint rowId)
        {
            return rowId;
        }

        var valueProperty = value.GetType().GetProperty("Value");
        var nestedValue = valueProperty?.GetValue(value);
        if (nestedValue != null)
        {
            var nestedRowIdProperty = nestedValue.GetType().GetProperty("RowId");
            if (nestedRowIdProperty?.GetValue(nestedValue) is uint nestedRowId)
            {
                return nestedRowId;
            }
        }

        return 0;
    }

    private void EvaluateBattleLogReferenceTriggers(FfxivLogEntry logEntry)
    {
        foreach (var trigger in this.configuration.FfxivLogTriggers)
        {
            trigger.UseFfxivLogReference = true;

            if (!this.IsTriggerLocationConditionSatisfied(trigger))
            {
                this.ResetFfxivLogReferenceMatchState(trigger);
                continue;
            }

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
        // FFXIV Log参照トリガーのデバッグログは、内部ログ一覧には表示しても、
        // トリガー判定の入力としては扱いません。
        // これを許可すると、デバッグログ内の Matched / Missing / ActionId 等が
        // 別トリガーの条件に誤ってヒットし、意図しない発火につながります。
        if (IsFfxivLogReferenceDebugLog(logEntry))
        {
            return;
        }

        foreach (var trigger in this.configuration.FfxivLogTriggers)
        {
            trigger.UseFfxivLogReference = true;

            if (!this.IsTriggerLocationConditionSatisfied(trigger))
            {
                this.ResetFfxivLogReferenceMatchState(trigger);
                continue;
            }

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
        if (!this.IsTriggerLocationConditionSatisfied(trigger))
        {
            this.ResetFfxivLogReferenceMatchState(trigger);
            return;
        }

        var requiresBattleLog = !string.IsNullOrWhiteSpace(trigger.BattleLogKeyword);
        var requiredInternalLogCount = trigger.GetInternalLogKeywords().Count;
        var matchedLogStatusRemainingSnapshot = this.TryGetStatusRemainingSnapshotFromMatchedLog(trigger, logEntry, out var parsedStatusRemainingSnapshot)
            ? parsedStatusRemainingSnapshot
            : null;

        if (!requiresBattleLog && requiredInternalLogCount == 0)
        {
            return;
        }

        if (trigger.UsePrerequisite && !this.HasActivePrerequisiteTrigger(trigger))
        {
            var prerequisiteState = this.GetFfxivLogReferenceMatchState(trigger);
            this.AddFfxivLogReferenceMatchDebug(trigger, prerequisiteState, "PrerequisiteMissing");
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
            this.FireFfxivLogReferenceTrigger(trigger, source, matchedLogStatusRemainingSnapshot);
            return;
        }

        // バトルログ + 内部ログ、または複数内部ログの場合は、
        // 条件ごとのマッチ情報を保持し、すべて揃った場合だけ発火します。
        // 複数内部ログはゲーム側の出力順が前後することがあるため、順番不問で判定します。
        var state = this.GetFfxivLogReferenceMatchState(trigger);
        this.RemoveExpiredFfxivLogReferenceMatches(state);
        this.RemoveConsumedFfxivLogReferenceMatches(trigger, state);

        if (source == FfxivLogReferenceSource.BattleLog)
        {
            if (this.ShouldSkipConsumedFfxivLogReferenceLog(trigger, logEntry))
            {
                this.AddFfxivLogReferenceMatchDebug(trigger, state, "ConsumedBattleLog");
                return;
            }

            state.BattleLogMatchedAtUtc = logEntry.Timestamp.ToUniversalTime();
            state.BattleLogMatchedLogKey = MakeFfxivLogReferenceLogKey(logEntry);
        }
        else if (internalLogKeywordIndex >= 0)
        {
            if (this.ShouldSkipConsumedFfxivLogReferenceLog(trigger, logEntry))
            {
                this.AddFfxivLogReferenceMatchDebug(trigger, state, $"ConsumedInternalLog{internalLogKeywordIndex + 1}");
                return;
            }

            if (!this.TryRecordInternalLogMatch(trigger, state, internalLogKeywordIndex, logEntry))
            {
                return;
            }

            if (matchedLogStatusRemainingSnapshot != null)
            {
                state.MatchedLogStatusRemainingSnapshot = matchedLogStatusRemainingSnapshot;
            }
        }

        if (!this.IsFfxivLogReferencePairSatisfied(trigger, state))
        {
            this.AddFfxivLogReferenceMatchDebug(trigger, state, "Waiting");
            return;
        }

        this.AddFfxivLogReferenceMatchDebug(trigger, state, "Matched");
        this.FireFfxivLogReferenceTrigger(trigger, FfxivLogReferenceSource.All, state.MatchedLogStatusRemainingSnapshot);
        this.ReserveConsumedFfxivLogReferenceLogs(trigger, state);

        // マッチ済み条件は、発火後に必ず全てリセットします。
        // 以前は「同一ステータスの複数表示許可」がONの場合、MemberStatus 条件だけを外し、
        // EnemyCasting などの共通条件を状態に残していました。
        // その結果、Param=1122 で成立した詠唱条件や Param=1121 で成立した詠唱条件が
        // 次の共通 MemberStatus ログと再結合し、別ラベル/別ログトリガーの表示テキストまで
        // 同時に発火するケースがありました。
        // 「設定しているログトリガー以外は表示しない」ため、1回の成立ごとに状態を閉じます。
        this.ResetFfxivLogReferenceMatchState(trigger);
    }

    private void RemoveStatusRemainingInternalLogMatches(HappyTriggerSetting trigger, FfxivLogReferenceMatchState state)
    {
        var keywords = trigger.GetInternalLogKeywords();
        for (var i = 0; i < keywords.Count; i++)
        {
            var keyword = keywords[i] ?? string.Empty;
            if (keyword.Contains("MemberStatus Information.", StringComparison.OrdinalIgnoreCase) ||
                keyword.Contains("StatusName=", StringComparison.OrdinalIgnoreCase))
            {
                state.InternalLogMatchedAtUtcByIndex.Remove(i);
            }
        }

        state.MatchedLogStatusRemainingSnapshot = null;
    }

    private bool TryRecordInternalLogMatch(
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

        // 同じ条件が再度マッチした場合は、最新時刻で上書きします。
        // これにより、内部ログ1/2/3 の出現順に依存せず、指定秒数内に全条件が揃えば発火します。
        state.InternalLogMatchedAtUtcByIndex[matchedIndex] = logEntry.Timestamp.ToUniversalTime();
        state.InternalLogMatchedKeyByIndex[matchedIndex] = MakeFfxivLogReferenceLogKey(logEntry);
        return true;
    }

    private void RemoveExpiredFfxivLogReferenceMatches(FfxivLogReferenceMatchState state)
    {
        var nowUtc = DateTime.UtcNow;

        if (state.BattleLogMatchedAtUtc != null &&
            (nowUtc - state.BattleLogMatchedAtUtc.Value).TotalSeconds > FfxivLogReferencePairWindowSeconds)
        {
            state.BattleLogMatchedAtUtc = null;
            state.BattleLogMatchedLogKey = null;
        }

        var expiredInternalIndexes = state.InternalLogMatchedAtUtcByIndex
            .Where(pair => (nowUtc - pair.Value).TotalSeconds > FfxivLogReferencePairWindowSeconds)
            .Select(pair => pair.Key)
            .ToList();

        if (expiredInternalIndexes.Count == 0)
        {
            return;
        }

        // 期限切れになった条件だけを外し、残りのマッチ状態は維持します。
        // 複数内部ログは順番不問のため、片方が期限切れになっても他条件まで破棄しません。
        foreach (var expiredInternalIndex in expiredInternalIndexes)
        {
            state.InternalLogMatchedAtUtcByIndex.Remove(expiredInternalIndex);
            state.InternalLogMatchedKeyByIndex.Remove(expiredInternalIndex);
        }
    }

    private void RemoveConsumedFfxivLogReferenceMatches(HappyTriggerSetting trigger, FfxivLogReferenceMatchState state)
    {
        if (!this.ShouldUseConsumedFfxivLogReferenceGuard(trigger))
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        this.RemoveExpiredConsumedFfxivLogReferenceLogKeys(nowUtc);

        if (!string.IsNullOrWhiteSpace(state.BattleLogMatchedLogKey) &&
            this.consumedFfxivLogReferenceLogKeys.TryGetValue(state.BattleLogMatchedLogKey, out var battleLogConsumedUntilUtc) &&
            battleLogConsumedUntilUtc > nowUtc)
        {
            state.BattleLogMatchedAtUtc = null;
            state.BattleLogMatchedLogKey = null;
        }

        var consumedInternalIndexes = state.InternalLogMatchedKeyByIndex
            .Where(pair => this.consumedFfxivLogReferenceLogKeys.TryGetValue(pair.Value, out var consumedUntilUtc) && consumedUntilUtc > nowUtc)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var consumedInternalIndex in consumedInternalIndexes)
        {
            state.InternalLogMatchedAtUtcByIndex.Remove(consumedInternalIndex);
            state.InternalLogMatchedKeyByIndex.Remove(consumedInternalIndex);
        }
    }

    private bool ShouldSkipConsumedFfxivLogReferenceLog(HappyTriggerSetting trigger, FfxivLogEntry logEntry)
    {
        if (!this.ShouldUseConsumedFfxivLogReferenceGuard(trigger))
        {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        this.RemoveExpiredConsumedFfxivLogReferenceLogKeys(nowUtc);

        var logKey = MakeFfxivLogReferenceLogKey(logEntry);
        return this.consumedFfxivLogReferenceLogKeys.TryGetValue(logKey, out var consumedUntilUtc) && consumedUntilUtc > nowUtc;
    }

    private void ReserveConsumedFfxivLogReferenceLogs(HappyTriggerSetting trigger, FfxivLogReferenceMatchState state)
    {
        if (!this.ShouldUseConsumedFfxivLogReferenceGuard(trigger))
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        this.RemoveExpiredConsumedFfxivLogReferenceLogKeys(nowUtc);

        var consumedUntilUtc = nowUtc.AddSeconds(Math.Max(0.1, this.FfxivLogReferencePairWindowSeconds));

        if (!string.IsNullOrWhiteSpace(state.BattleLogMatchedLogKey))
        {
            this.consumedFfxivLogReferenceLogKeys[state.BattleLogMatchedLogKey] = consumedUntilUtc;
        }

        foreach (var logKey in state.InternalLogMatchedKeyByIndex.Values)
        {
            if (!string.IsNullOrWhiteSpace(logKey))
            {
                this.consumedFfxivLogReferenceLogKeys[logKey] = consumedUntilUtc;
            }
        }
    }

    private void RemoveExpiredConsumedFfxivLogReferenceLogKeys(DateTime nowUtc)
    {
        var expiredKeys = this.consumedFfxivLogReferenceLogKeys
            .Where(pair => pair.Value <= nowUtc)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var expiredKey in expiredKeys)
        {
            this.consumedFfxivLogReferenceLogKeys.Remove(expiredKey);
        }
    }

    private bool ShouldUseConsumedFfxivLogReferenceGuard(HappyTriggerSetting trigger)
    {
        // 影響範囲を「同一ステータス複数表示許可」の例外設定かつ、ステータス残り時間表示を持つ
        // FFXIVログ参照トリガーに限定します。
        return trigger.HasStatusRemainingAppendSetting() && this.IsDuplicateStatusRemainingDisplayAllowed(trigger);
    }

    private static string MakeFfxivLogReferenceLogKey(FfxivLogEntry logEntry)
    {
        var category = logEntry.Category ?? string.Empty;
        var text = logEntry.Text ?? string.Empty;

        // 例外設定ONの同一ステータス処理では、jobだけが違う MemberStatus ログが
        // 同一タイミングに複数行出ることがあります。
        // 例: job=RPR / job=WHM の 呪詛の叫声 が同じ秒に発生するケース。
        // 片方だけを消費済みにすると、もう片方が猶予時間内に別トリガーの材料として
        // 再利用され、「見る」「見ない」などが意図しないタイミングで発火します。
        //
        // そのため MemberStatus Information. についてだけ、
        //   - 発生秒
        //   - StatusId
        //   - StatusName
        //   - Param
        // を消費キーにし、job と Remaining はキーから外します。
        // これにより同一秒に出た同一ステータスの別jobログはまとめて消費済みになり、
        // 別秒に出た同一ステータスログには影響しません。
        if (text.Contains("MemberStatus Information.", StringComparison.OrdinalIgnoreCase))
        {
            var secondBucket = new DateTime(
                logEntry.Timestamp.Year,
                logEntry.Timestamp.Month,
                logEntry.Timestamp.Day,
                logEntry.Timestamp.Hour,
                logEntry.Timestamp.Minute,
                logEntry.Timestamp.Second,
                logEntry.Timestamp.Kind);

            var statusId = ExtractLogFieldValue(text, "StatusId");
            var statusName = ExtractLogFieldValue(text, "StatusName");
            var param = ExtractLogFieldValue(text, "Param");

            if (!string.IsNullOrWhiteSpace(statusId) ||
                !string.IsNullOrWhiteSpace(statusName) ||
                !string.IsNullOrWhiteSpace(param))
            {
                return $"MemberStatusGroup|{secondBucket.Ticks}|StatusId={statusId}|StatusName={statusName}|Param={param}";
            }
        }

        return $"Exact|{logEntry.Timestamp.Ticks}|{category}|{text}";
    }

    private static string ExtractLogFieldValue(string text, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(fieldName))
        {
            return string.Empty;
        }

        var match = Regex.Match(
            text,
            $@"(?:^|\s){Regex.Escape(fieldName)}=([^\s]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
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

    private void AddFfxivLogReferenceMatchDebug(
        HappyTriggerSetting trigger,
        FfxivLogReferenceMatchState state,
        string reason)
    {
        if (!this.configuration.ShowFfxivLogReferenceDebugLogs)
        {
            return;
        }

        var requiresBattleLog = !string.IsNullOrWhiteSpace(trigger.BattleLogKeyword);
        var battleLogStatus = requiresBattleLog
            ? state.BattleLogMatchedAtUtc == null ? "Missing" : "Matched"
            : "NotRequired";

        var internalLogKeywords = trigger.GetInternalLogKeywords();
        var internalLogStatuses = new List<string>();
        for (var i = 0; i < internalLogKeywords.Count; i++)
        {
            var status = state.InternalLogMatchedAtUtcByIndex.ContainsKey(i) ? "Matched" : "Missing";
            internalLogStatuses.Add($"{i + 1}:{status}='{internalLogKeywords[i]}'");
        }

        var triggerName = string.IsNullOrWhiteSpace(trigger.TriggerName) ? "名称未設定" : trigger.TriggerName.Trim();
        this.AddInternalLog(
            $"FFXIV Log trigger debug. Id={trigger.TriggerId} Name='{triggerName}' Reason={reason} BattleLog={battleLogStatus} InternalLogs=[{string.Join(" / ", internalLogStatuses)}]",
            false);
    }

    private static bool IsFfxivLogReferenceDebugLog(FfxivLogEntry logEntry)
    {
        var text = logEntry.Text ?? string.Empty;
        var displayText = logEntry.DisplayText ?? string.Empty;

        return text.StartsWith("FFXIV Log trigger debug.", StringComparison.OrdinalIgnoreCase)
            || displayText.Contains("]FFXIV Log trigger debug.", StringComparison.OrdinalIgnoreCase)
            || displayText.Contains("[HappyTrigger]FFXIV Log trigger debug.", StringComparison.OrdinalIgnoreCase);
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

        var nowUtc = DateTime.UtcNow;

        // 通常の表示終了済み・手動クローズ済みのポップアップは掃除します。
        // ただし、ステータス残り時間表示つきのポップアップは IsExpired が DisplaySeconds ではなく
        // Remaining=0 を基準にするため、前提条件の判定では必ず StartTimeUtc～EndTimeUtc を使います。
        this.activePopups.RemoveAll(x => x.IsClosed || (!x.HasStatusRemainingDisplay && x.IsExpired));

        foreach (var popup in this.activePopups)
        {
            if (popup.IsPositionSetting || popup.IsClosed)
            {
                continue;
            }

            var prerequisiteTrigger = popup.Trigger;
            if (!string.Equals(prerequisiteTrigger.TriggerId, trigger.PrerequisiteTriggerId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 前提条件は「対象トリガーが表示中であること」を条件にします。
            // 例: 表示時間10秒なら、表示開始から10秒以内だけ true です。
            // ステータス残り時間表示で画面に長く残っていても、前提条件として有効なのは DisplaySeconds の範囲だけです。
            if (nowUtc >= popup.StartTimeUtc && nowUtc < popup.EndTimeUtc)
            {
                return true;
            }
        }

        return false;
    }

    private void FireFfxivLogReferenceTrigger(
        HappyTriggerSetting trigger,
        FfxivLogReferenceSource source,
        StatusRemainingSnapshot? matchedLogStatusRemainingSnapshot = null)
    {
        if (!this.IsTriggerLocationConditionSatisfied(trigger))
        {
            this.ResetFfxivLogReferenceMatchState(trigger);
            return;
        }

        StatusRemainingSnapshot? statusRemainingSnapshot = matchedLogStatusRemainingSnapshot;
        if (statusRemainingSnapshot == null && trigger.HasStatusRemainingAppendSetting() && !this.TryGetStatusRemainingSnapshot(trigger, out statusRemainingSnapshot))
        {
            this.AddInternalLog(
                $"Status remaining not found. Id={trigger.TriggerId} job={trigger.StatusRemainingJob} StatusName={trigger.StatusRemainingStatusName}",
                false);
        }

        if (this.ShouldSuppressAlreadyMatchedFfxivLogTrigger(trigger, statusRemainingSnapshot))
        {
            this.AddInternalLog(
                $"FFXIV Log trigger suppressed. Reason=AlreadyMatched Id={trigger.TriggerId} StatusName='{GetStatusRemainingLockName(trigger, statusRemainingSnapshot)}'",
                false);
            return;
        }

        if (this.ShouldSuppressDuplicateStatusRemainingTrigger(trigger, statusRemainingSnapshot))
        {
            this.AddInternalLog(
                $"FFXIV Log trigger suppressed. Reason=DuplicateStatusRemaining Id={trigger.TriggerId} StatusName='{GetStatusRemainingLockName(trigger, statusRemainingSnapshot)}'",
                false);
            return;
        }

        this.ReserveAlreadyMatchedFfxivLogTriggerLock(trigger, statusRemainingSnapshot);
        this.ReserveStatusRemainingMatchLock(trigger, statusRemainingSnapshot);

        this.AddInternalLog(
            $"FFXIV Log trigger matched. Id={trigger.TriggerId} Source={source} Prerequisite={(trigger.UsePrerequisite ? "ON" : "OFF")} PrerequisiteId='{trigger.PrerequisiteTriggerId}' BattleLog='{trigger.BattleLogKeyword}' InternalLogs='{string.Join(" / ", trigger.GetInternalLogKeywords())}' StatusRemaining={(trigger.EnableStatusRemainingAppend ? $"ON:{trigger.StatusRemainingJob}/{trigger.StatusRemainingStatusName} AllowDuplicate={(this.IsDuplicateStatusRemainingDisplayAllowed(trigger) ? "ON" : "OFF")}" : "OFF")}",
            false);
        this.ActivatePopup(trigger, false, statusRemainingSnapshot);
    }

    private bool IsDuplicateStatusRemainingDisplayAllowed(HappyTriggerSetting trigger)
    {
        if (trigger.AllowDuplicateStatusRemainingDisplay)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(trigger.TriggerLabelId))
        {
            return false;
        }

        var label = this.configuration.TriggerLabels.FirstOrDefault(label =>
            string.Equals(label.LabelId, trigger.TriggerLabelId, StringComparison.OrdinalIgnoreCase));

        return label?.AllowDuplicateStatusRemainingDisplay == true;
    }

    private bool ShouldSuppressAlreadyMatchedFfxivLogTrigger(
        HappyTriggerSetting trigger,
        StatusRemainingSnapshot? statusRemainingSnapshot)
    {
        // 影響範囲をステータス残り時間つきログトリガーに限定します。
        // 通常のログトリガーやテキストトリガーの再発火仕様は変えません。
        if (!trigger.HasStatusRemainingAppendSetting())
        {
            return false;
        }

        var triggerId = trigger.TriggerId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(triggerId))
        {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        this.RemoveExpiredAlreadyMatchedFfxivLogTriggerLocks(nowUtc);

        var lockKey = MakeAlreadyMatchedFfxivLogTriggerLockKey(trigger, statusRemainingSnapshot);
        if (this.activeFfxivLogTriggerMatchLocks.TryGetValue(lockKey, out var lockedUntilUtc) && lockedUntilUtc > nowUtc)
        {
            return true;
        }

        // activePopups 側も確認します。
        // これにより、ロックDictionaryだけが消えた/更新タイミングが前後した場合でも、
        // 同じログトリガーが表示中なら再表示しません。
        this.activePopups.RemoveAll(x => x.IsClosed || x.IsExpired);
        return this.activePopups.Any(popup =>
            !popup.IsClosed &&
            !popup.IsExpired &&
            popup.Trigger != null &&
            string.Equals(popup.Trigger.TriggerId?.Trim(), triggerId, StringComparison.OrdinalIgnoreCase));
    }

    private void ReserveAlreadyMatchedFfxivLogTriggerLock(
        HappyTriggerSetting trigger,
        StatusRemainingSnapshot? statusRemainingSnapshot)
    {
        if (!trigger.HasStatusRemainingAppendSetting())
        {
            return;
        }

        var triggerId = trigger.TriggerId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(triggerId))
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        this.RemoveExpiredAlreadyMatchedFfxivLogTriggerLocks(nowUtc);

        var lockUntilUtc = nowUtc.AddSeconds(Math.Max(0.1f, trigger.DisplaySeconds));
        if (statusRemainingSnapshot != null)
        {
            var statusRemainingUntilUtc = statusRemainingSnapshot.CapturedAtUtc.AddSeconds(Math.Max(0.1f, statusRemainingSnapshot.RemainingSeconds));
            if (statusRemainingUntilUtc > lockUntilUtc)
            {
                lockUntilUtc = statusRemainingUntilUtc;
            }
        }

        this.activeFfxivLogTriggerMatchLocks[MakeAlreadyMatchedFfxivLogTriggerLockKey(trigger, statusRemainingSnapshot)] = lockUntilUtc;
    }

    private void RemoveExpiredAlreadyMatchedFfxivLogTriggerLocks(DateTime nowUtc)
    {
        var expiredKeys = this.activeFfxivLogTriggerMatchLocks
            .Where(pair => pair.Value <= nowUtc)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var expiredKey in expiredKeys)
        {
            this.activeFfxivLogTriggerMatchLocks.Remove(expiredKey);
        }
    }

    private static string MakeAlreadyMatchedFfxivLogTriggerLockKey(
        HappyTriggerSetting trigger,
        StatusRemainingSnapshot? statusRemainingSnapshot)
    {
        var triggerId = trigger.TriggerId?.Trim() ?? string.Empty;
        var statusName = GetStatusRemainingLockName(trigger, statusRemainingSnapshot);
        return $"{triggerId}|{statusName}";
    }

    private bool ShouldSuppressDuplicateStatusRemainingTrigger(
        HappyTriggerSetting trigger,
        StatusRemainingSnapshot? statusRemainingSnapshot)
    {
        if (!trigger.HasStatusRemainingAppendSetting() || this.IsDuplicateStatusRemainingDisplayAllowed(trigger))
        {
            return false;
        }

        var statusName = GetStatusRemainingLockName(trigger, statusRemainingSnapshot);
        if (string.IsNullOrWhiteSpace(statusName))
        {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        this.RemoveExpiredStatusRemainingMatchLocks(nowUtc);
        this.activePopups.RemoveAll(x => x.IsClosed || x.IsExpired);

        var lockKey = MakeStatusRemainingMatchLockKey(statusName);
        if (this.activeStatusRemainingMatchLocks.TryGetValue(lockKey, out var lockedUntilUtc) && lockedUntilUtc > nowUtc)
        {
            return true;
        }

        return this.activePopups.Any(popup =>
            popup.HasStatusRemainingDisplay &&
            !popup.IsClosed &&
            !popup.IsExpired &&
            string.Equals(popup.StatusRemainingStatusName, statusName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private void ReserveStatusRemainingMatchLock(
        HappyTriggerSetting trigger,
        StatusRemainingSnapshot? statusRemainingSnapshot)
    {
        if (!trigger.HasStatusRemainingAppendSetting() || this.IsDuplicateStatusRemainingDisplayAllowed(trigger))
        {
            return;
        }

        var statusName = GetStatusRemainingLockName(trigger, statusRemainingSnapshot);
        if (string.IsNullOrWhiteSpace(statusName))
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        this.RemoveExpiredStatusRemainingMatchLocks(nowUtc);

        var lockUntilUtc = nowUtc.AddSeconds(Math.Max(0.1f, trigger.DisplaySeconds));
        if (statusRemainingSnapshot != null)
        {
            // 実ログから取得した Remaining を基準に、同一ステータスの重複発火を抑止します。
            // これにより、F00038 がマッチ済み/表示中の間に F00039 が同じ StatusName でマッチしても発火しません。
            var statusRemainingUntilUtc = statusRemainingSnapshot.CapturedAtUtc.AddSeconds(Math.Max(0.1f, statusRemainingSnapshot.RemainingSeconds));
            if (statusRemainingUntilUtc > lockUntilUtc)
            {
                lockUntilUtc = statusRemainingUntilUtc;
            }
        }

        this.activeStatusRemainingMatchLocks[MakeStatusRemainingMatchLockKey(statusName)] = lockUntilUtc;
    }

    private void RemoveExpiredStatusRemainingMatchLocks(DateTime nowUtc)
    {
        var expiredKeys = this.activeStatusRemainingMatchLocks
            .Where(pair => pair.Value <= nowUtc)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var expiredKey in expiredKeys)
        {
            this.activeStatusRemainingMatchLocks.Remove(expiredKey);
        }
    }

    private static string GetStatusRemainingLockName(
        HappyTriggerSetting trigger,
        StatusRemainingSnapshot? statusRemainingSnapshot)
    {
        return (statusRemainingSnapshot?.StatusName ?? trigger.StatusRemainingStatusName ?? string.Empty).Trim();
    }

    private static string MakeStatusRemainingMatchLockKey(string statusName)
    {
        return statusName.Trim();
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
        this.activeStatusRemainingMatchLocks.Clear();
        this.activeFfxivLogTriggerMatchLocks.Clear();
        this.consumedFfxivLogReferenceLogKeys.Clear();
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
            var labelStack = this.GetTriggerLabelForStack(trigger);
            this.activePopups.Add(new PopupImageState(trigger, false, statusRemainingDisplayState, string.Empty, labelStack));
            if (writeInternalLog)
            {
                this.AddInternalLog($"Text display queued. Text='{trigger.DisplayText}', Wait={Math.Clamp(trigger.WaitSeconds, 0.0f, 600.0f):0.##}s, X={trigger.PositionX:0}, Y={trigger.PositionY:0}");
            }

            this.voiceVoxSpeechService.SpeakAsync(trigger);

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

    private void ActivatePopups(IReadOnlyList<HappyTriggerSetting> triggers)
    {
        foreach (var trigger in triggers.OrderBy(trigger => GetTriggerSortNumber(trigger.TriggerId)).ThenBy(trigger => trigger.TriggerId, StringComparer.OrdinalIgnoreCase))
        {
            this.ActivatePopup(trigger, true);
        }
    }

    private void ActivatePositionSettingTrigger(HappyTriggerSetting trigger)
    {
        this.ActivatePositionSettingTriggers(new[] { trigger });
    }

    private void ActivatePositionSettingTriggers(IReadOnlyList<HappyTriggerSetting> triggers)
    {
        this.activePopups.RemoveAll(x => x.IsPositionSetting || x.IsExpired);

        var validTriggers = triggers
            .Where(CanDisplayPositionSettingPopup)
            .ToList();

        if (validTriggers.Count == 0)
        {
            return;
        }

        var groupId = validTriggers.Count >= 2
            ? $"group:{Guid.NewGuid():N}"
            : string.Empty;

        foreach (var trigger in validTriggers)
        {
            this.activePopups.Add(new PopupImageState(trigger, true, null, groupId));
        }

        if (validTriggers.Count >= 2)
        {
            this.AddInternalLog($"Group position setting popups displayed. Count={validTriggers.Count}. Drag one popup to move all popups in the group.");
        }
        else
        {
            var trigger = validTriggers[0];
            this.AddInternalLog(trigger.DisplayTextMode
                ? $"Text position setting popup displayed. Text='{trigger.DisplayText}'"
                : $"Image position setting popup displayed. Image='{trigger.ImagePath}'");
        }
    }

    private void ActivatePositionSettingForLabel(TriggerLabelSetting label, IReadOnlyList<HappyTriggerSetting> triggers)
    {
        this.activePopups.RemoveAll(x => x.IsPositionSetting || x.IsExpired);

        var validTriggers = triggers
            .Where(CanDisplayPositionSettingPopup)
            .OrderBy(trigger => GetTriggerSortNumber(trigger.TriggerId))
            .ThenBy(trigger => trigger.TriggerId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validTriggers.Count == 0)
        {
            return;
        }

        var labelPositionTriggers = validTriggers
            .Where(trigger => trigger.UseTriggerLabelPosition)
            .ToList();
        var individualPositionTriggers = validTriggers
            .Where(trigger => !trigger.UseTriggerLabelPosition)
            .ToList();

        var groupId = $"label:{label.LabelId}";
        foreach (var trigger in labelPositionTriggers)
        {
            this.activePopups.Add(new PopupImageState(trigger, true, null, groupId, label));
        }

        foreach (var trigger in individualPositionTriggers)
        {
            this.activePopups.Add(new PopupImageState(trigger, true));
        }

        this.AddInternalLog($"Label position setting popups displayed. LabelId={label.LabelId}, LabelStack={labelPositionTriggers.Count}, Individual={individualPositionTriggers.Count}. Drag label-stack rows to move the label base position. Drag individual rows to move only that trigger.");
    }

    private TriggerLabelSetting? GetTriggerLabelForStack(HappyTriggerSetting trigger)
    {
        if (!trigger.DisplayTextMode || !trigger.UseTriggerLabelPosition || string.IsNullOrWhiteSpace(trigger.TriggerLabelId))
        {
            return null;
        }

        return this.configuration.TriggerLabels.FirstOrDefault(label =>
            string.Equals(label.LabelId, trigger.TriggerLabelId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool CanDisplayPositionSettingPopup(HappyTriggerSetting trigger)
    {
        if (trigger.DisplayTextMode)
        {
            return !string.IsNullOrWhiteSpace(trigger.DisplayText);
        }

        return !string.IsNullOrWhiteSpace(trigger.ImagePath);
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

    private bool TryGetStatusRemainingSnapshotFromMatchedLog(
        HappyTriggerSetting trigger,
        FfxivLogEntry logEntry,
        out StatusRemainingSnapshot? snapshot)
    {
        snapshot = null;

        if (!trigger.HasStatusRemainingAppendSetting())
        {
            return false;
        }

        var candidates = new[]
        {
            logEntry.Text ?? string.Empty,
            logEntry.DisplayText ?? string.Empty,
        };

        foreach (var candidate in candidates)
        {
            if (TryParseMemberStatusRemaining(candidate, out var job, out var statusName, out var remainingSeconds) &&
                IsStatusRemainingJobMatch(trigger.StatusRemainingJob, job) &&
                string.Equals(statusName, trigger.StatusRemainingStatusName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                remainingSeconds > 0.0f)
            {
                snapshot = new StatusRemainingSnapshot(job, statusName, remainingSeconds, logEntry.Timestamp.ToUniversalTime());
                return true;
            }
        }

        return false;
    }

    private static bool TryParseMemberStatusRemaining(
        string text,
        out string job,
        out string statusName,
        out float remainingSeconds)
    {
        job = string.Empty;
        statusName = string.Empty;
        remainingSeconds = 0.0f;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = StripLeadingLogPrefix(text);
        if (!normalized.Contains("MemberStatus Information.", StringComparison.OrdinalIgnoreCase) ||
            !normalized.Contains("Remaining=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        job = GetLogFieldValue(normalized, "job");
        statusName = GetLogFieldValue(normalized, "StatusName");
        var remainingText = GetLogFieldValue(normalized, "Remaining");

        if (string.IsNullOrWhiteSpace(job) ||
            string.IsNullOrWhiteSpace(statusName) ||
            string.IsNullOrWhiteSpace(remainingText))
        {
            return false;
        }

        remainingText = remainingText.Trim();
        if (remainingText.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            remainingText = remainingText[..^1];
        }

        return float.TryParse(
            remainingText,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out remainingSeconds);
    }

    private static string StripLeadingLogPrefix(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        // [19:52:54] のようなタイムスタンプを外します。
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
            trimmed = trimmed[10..].TrimStart();
        }

        // [HappyTrigger] のようなカテゴリを外します。
        if (trimmed.Length >= 3 && trimmed[0] == '[')
        {
            var closeIndex = trimmed.IndexOf(']');
            if (closeIndex > 0 && closeIndex + 1 < trimmed.Length)
            {
                trimmed = trimmed[(closeIndex + 1)..].TrimStart();
            }
        }

        return trimmed;
    }

    private static string GetLogFieldValue(string text, string fieldName)
    {
        var match = Regex.Match(
            text,
            $@"(?:^|\s){Regex.Escape(fieldName)}=(?<value>.*?)(?=\s[A-Za-z][A-Za-z0-9_]*=|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success ? match.Groups["value"].Value.Trim() : string.Empty;
    }

    private bool TryGetStatusRemainingSnapshot(HappyTriggerSetting trigger, out StatusRemainingSnapshot? snapshot)
    {
        snapshot = null;

        if (!trigger.HasStatusRemainingAppendSetting())
        {
            return false;
        }

        var statusNameFilter = trigger.StatusRemainingStatusName?.Trim() ?? string.Empty;
        var matchingSnapshots = this.latestMemberStatusRemainingByJobAndName
            .Where(pair =>
                IsStatusRemainingJobMatch(trigger.StatusRemainingJob, pair.Value.Job) &&
                string.Equals(pair.Value.StatusName, statusNameFilter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(pair => pair.Value.CapturedAtUtc)
            .ToList();

        foreach (var pair in matchingSnapshots)
        {
            var found = pair.Value;
            var currentRemaining = found.RemainingSeconds - (float)Math.Max(0.0, (DateTime.UtcNow - found.CapturedAtUtc).TotalSeconds);
            if (currentRemaining <= 0.0f)
            {
                this.latestMemberStatusRemainingByJobAndName.Remove(pair.Key);
                continue;
            }

            snapshot = new StatusRemainingSnapshot(found.Job, found.StatusName, currentRemaining, DateTime.UtcNow);
            return true;
        }

        return false;
    }

    private static bool IsStatusRemainingJobMatch(string? jobFilter, string logJob)
    {
        var normalizedLogJob = (logJob ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(jobFilter) || string.IsNullOrWhiteSpace(normalizedLogJob))
        {
            return false;
        }

        var allowedJobs = ParseStatusRemainingJobFilter(jobFilter);
        return allowedJobs.Any(job =>
            string.Equals(job, "ALL", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(job, normalizedLogJob, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ParseStatusRemainingJobFilter(string jobFilter)
    {
        var normalized = (jobFilter ?? string.Empty).Trim();
        if (normalized.Length >= 2 && normalized.StartsWith("<", StringComparison.Ordinal) && normalized.EndsWith(">", StringComparison.Ordinal))
        {
            normalized = normalized[1..^1];
        }

        return normalized
            .Split(new[] { '|', ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(job => !string.IsNullOrWhiteSpace(job))
            .ToList();
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

    private float GetDuplicateStatusRemainingPopupOffset(int currentIndex, PopupImageState popup)
    {
        if (!popup.HasStatusRemainingDisplay || !this.IsDuplicateStatusRemainingDisplayAllowed(popup.Trigger))
        {
            return 0.0f;
        }

        var statusName = popup.StatusRemainingStatusName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(statusName))
        {
            return 0.0f;
        }

        var duplicateIndex = 0;
        var maxIndex = Math.Min(currentIndex, this.activePopups.Count);

        for (var i = 0; i < maxIndex; i++)
        {
            var other = this.activePopups[i];
            if (ReferenceEquals(other, popup))
            {
                break;
            }

            if (other.IsPositionSetting || other.IsClosed || other.IsExpired || !other.IsReadyToDisplay)
            {
                continue;
            }

            if (!other.HasStatusRemainingDisplay || !this.IsDuplicateStatusRemainingDisplayAllowed(other.Trigger))
            {
                continue;
            }

            if (string.Equals(other.StatusRemainingStatusName, statusName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                duplicateIndex++;
            }
        }

        if (duplicateIndex <= 0)
        {
            return 0.0f;
        }

        var textSize = Math.Clamp(popup.Trigger.TextSize, 8.0f, 256.0f);
        var outlineExtra = popup.Trigger.EnableTextOutline
            ? Math.Max(0.0f, popup.Trigger.TextOutlineThickness) * 2.0f
            : 0.0f;
        var lineHeight = Math.Max(28.0f, textSize + outlineExtra + 10.0f);

        return duplicateIndex * lineHeight;
    }

    private Vector2 GetTextPopupPosition(int currentIndex, PopupImageState popup)
    {
        if (popup.HasLabelStack)
        {
            var label = popup.LabelStack!;
            return new Vector2(label.PositionX, label.PositionY + this.GetLabelStackPopupOffset(currentIndex, popup));
        }

        var position = new Vector2(popup.Trigger.PositionX, popup.Trigger.PositionY);
        position.Y += this.GetDuplicateStatusRemainingPopupOffset(currentIndex, popup);
        return position;
    }

    private float GetLabelStackPopupOffset(int currentIndex, PopupImageState popup)
    {
        if (!popup.HasLabelStack)
        {
            return 0.0f;
        }

        var label = popup.LabelStack!;
        var targetId = label.LabelId ?? string.Empty;
        var readyPopups = this.activePopups
            .Where(other => !other.IsClosed && !other.IsExpired && other.IsReadyToDisplay)
            .Where(other => other.HasLabelStack && string.Equals(other.LabelStack!.LabelId, targetId, StringComparison.OrdinalIgnoreCase))
            // ラベル座標で縦積み表示するログトリガーは、残り時間表示があるものを上に寄せます。
            // 残り時間表示つき同士は、残り時間が短い順で並べます。
            // 残り時間表示がないものは従来どおりID順で後ろに並べます。
            .OrderBy(other => other.HasStatusRemainingDisplay ? 0 : 1)
            .ThenBy(other => other.HasStatusRemainingDisplay ? other.CurrentStatusRemainingSeconds : float.MaxValue)
            .ThenBy(other => GetTriggerSortNumber(other.Trigger.TriggerId))
            .ThenBy(other => other.Trigger.TriggerId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var offset = 0.0f;
        foreach (var other in readyPopups)
        {
            if (ReferenceEquals(other, popup))
            {
                return offset;
            }

            offset += GetTextPopupLineHeight(other.Trigger) + Math.Max(0.0f, label.LineSpacing);
        }

        return offset;
    }

    private static float GetTextPopupLineHeight(HappyTriggerSetting trigger)
    {
        var textSize = Math.Clamp(trigger.TextSize, 8.0f, 256.0f);
        var outlineExtra = trigger.EnableTextOutline || trigger.TextFontDesign == TextFontDesign.StrongOutline
            ? Math.Max(1.0f, trigger.TextOutlineThickness) * 2.0f
            : 0.0f;
        var shadowExtra = trigger.TextFontDesign == TextFontDesign.Shadow || trigger.TextFontDesign == TextFontDesign.Neon
            ? Math.Abs(trigger.TextShadowOffsetY) + 4.0f
            : 0.0f;

        return Math.Max(28.0f, textSize + outlineExtra + shadowExtra + 10.0f);
    }

    private static int GetTriggerSortNumber(string? triggerId)
    {
        if (string.IsNullOrWhiteSpace(triggerId))
        {
            return int.MaxValue;
        }

        var digits = new string(triggerId.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number) ? number : int.MaxValue;
    }

    private void DrawTextPopup(int index, PopupImageState popup)
    {
        var trigger = popup.Trigger;
        var position = this.GetTextPopupPosition(index, popup);
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

            // ImGui標準フォントを大きく拡大すると、フォントアトラスの小さい字形を引き伸ばすため滲みます。
            // くっきり表示ONでは、Windows側で表示サイズそのものの文字画像を作り、テクスチャとして描画します。
            // これにより大きい文字でも拡大ボケせず、縁取りもはっきりします。
            if (trigger.EnableTextSharpRendering && !string.IsNullOrEmpty(displayText))
            {
                var layoutText = GetPopupDisplayTextLayout(popup);
                var textureResult = this.textTextureCacheService.GetTextTexture(displayText, layoutText, trigger, fadeAlpha);
                if (textureResult.Texture != null && textureResult.Size.Width > 0.0f && textureResult.Size.Height > 0.0f)
                {
                    var imageSize = new Vector2(textureResult.Size.Width, textureResult.Size.Height);
                    if (trigger.EnableTextPixelSnap)
                    {
                        var cursorPos = ImGui.GetCursorScreenPos();
                        ImGui.SetCursorScreenPos(new Vector2(MathF.Round(cursorPos.X), MathF.Round(cursorPos.Y)));
                    }

                    ImGui.Image(
                        textureResult.Texture.Handle,
                        imageSize,
                        Vector2.Zero,
                        Vector2.One,
                        new Vector4(1.0f, 1.0f, 1.0f, fadeAlpha));

                    if (popup.IsPositionSetting)
                    {
                        this.HandlePositionSettingDrag(popup);
                    }

                    ImGui.End();
                    ImGui.PopStyleVar();
                    return;
                }
            }

            var drawList = ImGui.GetWindowDrawList();
            var font = ImGui.GetFont();
            var baseFontSize = Math.Max(1.0f, ImGui.GetFontSize());
            var fontSize = GetEffectiveTextFontSize(trigger);
            var fontScale = fontSize / baseFontSize;
            var textSize = ImGui.CalcTextSize(displayText) * fontScale;
            var drawPos = ImGui.GetCursorScreenPos();

            if (trigger.EnableTextPixelSnap || trigger.EnableTextSharpRendering)
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

    private static float GetEffectiveTextFontSize(HappyTriggerSetting trigger)
    {
        var fontSize = Math.Clamp(trigger.TextSize, 8.0f, 256.0f);

        if (!trigger.EnableTextSharpRendering)
        {
            return fontSize;
        }

        // 非整数の拡大率は滲みの原因になりやすいため、くっきり表示では整数pxへ丸めます。
        // 大きいサイズでは偶数pxへ寄せることで、縁取り位置も安定します。
        fontSize = MathF.Round(fontSize);
        if (fontSize >= 48.0f)
        {
            fontSize = MathF.Round(fontSize / 2.0f) * 2.0f;
        }

        return Math.Clamp(fontSize, 8.0f, 256.0f);
    }

    private static string GetPopupDisplayText(PopupImageState popup)
    {
        var displayText = popup.Trigger.DisplayText ?? string.Empty;

        if (!popup.HasStatusRemainingDisplay)
        {
            return displayText;
        }

        var remaining = popup.CurrentStatusRemainingSeconds;
        if (popup.Trigger.EnableTextSharpRendering)
        {
            // テクスチャ化表示中に0.01秒単位で更新すると、毎フレームに近い頻度で再生成されます。
            // 表示は2桁小数のまま、0.10秒単位へ丸めて生成頻度と視覚的なブレを抑えます。
            remaining = MathF.Round(remaining * 10.0f) / 10.0f;
        }

        return BuildPopupDisplayTextWithRemaining(popup, $"{remaining:0.00}");
    }

    private static string GetPopupDisplayTextLayout(PopupImageState popup)
    {
        var displayText = popup.Trigger.DisplayText ?? string.Empty;

        if (!popup.HasStatusRemainingDisplay)
        {
            return displayText;
        }

        // 秒数表示つきテクスチャは、実際の秒数が減るたびに文字幅が変わると位置が微妙に揺れます。
        // レイアウト計測用には実表示とは別の「幅取り用テキスト」を使い、テクスチャサイズを固定します。
        var maxRemaining = Math.Max(popup.StatusRemainingInitialSeconds, popup.CurrentStatusRemainingSeconds);
        var integerDigits = Math.Max(2, ((int)MathF.Floor(Math.Max(0.0f, maxRemaining))).ToString().Length);
        var layoutRemaining = new string('8', integerDigits) + ".88";
        return BuildPopupDisplayTextWithRemaining(popup, layoutRemaining);
    }

    private static string BuildPopupDisplayTextWithRemaining(PopupImageState popup, string remainingText)
    {
        var displayText = popup.Trigger.DisplayText ?? string.Empty;
        var statusName = popup.StatusRemainingStatusName ?? string.Empty;
        var suffix = $"{statusName}（{remainingText}s）";

        if (string.IsNullOrWhiteSpace(displayText))
        {
            return suffix;
        }

        if (!string.IsNullOrWhiteSpace(statusName) &&
            displayText.Contains(statusName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{displayText}（{remainingText}s）";
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
            if (trigger.EnableTextSharpRendering)
            {
                shadowOffset = new Vector2(MathF.Round(shadowOffset.X), MathF.Round(shadowOffset.Y));
            }

            drawList.AddText(font, fontSize, drawPos + shadowOffset, shadowColor, displayText);
        }

        if (trigger.TextFontDesign == TextFontDesign.Neon)
        {
            DrawTextOffsets(drawList, font, fontSize, drawPos, shadowColor, displayText, 4.0f, true, trigger.EnableTextSharpRendering);
            DrawTextOffsets(drawList, font, fontSize, drawPos, shadowColor, displayText, 7.0f, false, trigger.EnableTextSharpRendering);
        }

        var useOutline = trigger.EnableTextOutline || trigger.TextFontDesign == TextFontDesign.StrongOutline;
        if (useOutline)
        {
            var outlineThickness = Math.Max(1.0f, trigger.TextOutlineThickness);
            if (trigger.TextFontDesign == TextFontDesign.StrongOutline)
            {
                outlineThickness = Math.Max(3.0f, outlineThickness * 1.5f);
            }

            DrawTextOffsets(drawList, font, fontSize, drawPos, outlineColor, displayText, outlineThickness, true, trigger.EnableTextSharpRendering);
        }

        if (trigger.TextFontDesign == TextFontDesign.Bold)
        {
            drawList.AddText(font, fontSize, drawPos + new Vector2(1.0f, 0.0f), textColor, displayText);
            drawList.AddText(font, fontSize, drawPos + new Vector2(0.0f, 1.0f), textColor, displayText);
            drawList.AddText(font, fontSize, drawPos + new Vector2(1.0f, 1.0f), textColor, displayText);
        }

        // くっきり表示では同じ座標に重ね描きして、拡大時の薄さを少し補正します。
        if (trigger.EnableTextSharpRendering && fontSize >= 48.0f)
        {
            drawList.AddText(font, fontSize, drawPos, textColor, displayText);
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
        bool includeDiagonals,
        bool sharpRendering)
    {
        if (!sharpRendering)
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

            return;
        }

        var radius = Math.Clamp((int)MathF.Round(thickness), 1, 24);
        for (var y = -radius; y <= radius; y++)
        {
            for (var x = -radius; x <= radius; x++)
            {
                if (x == 0 && y == 0)
                {
                    continue;
                }

                if (!includeDiagonals && x != 0 && y != 0)
                {
                    continue;
                }

                var distance = MathF.Sqrt((x * x) + (y * y));
                if (distance > radius + 0.01f)
                {
                    continue;
                }

                drawList.AddText(font, fontSize, drawPos + new Vector2(x, y), color, text);
            }
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

    private void ApplyPositionSettingDragDelta(PopupImageState popup, Vector2 delta)
    {
        if (popup.IsLabelPositionSetting && popup.LabelStack != null)
        {
            popup.LabelStack.PositionX += delta.X;
            popup.LabelStack.PositionY += delta.Y;

            foreach (var labelPopup in this.activePopups)
            {
                if (labelPopup.IsPositionSetting && labelPopup.HasLabelStack &&
                    string.Equals(labelPopup.LabelStack!.LabelId, popup.LabelStack.LabelId, StringComparison.OrdinalIgnoreCase))
                {
                    labelPopup.PositionChanged = true;
                }
            }

            return;
        }

        if (popup.IsGroupedPositionSetting)
        {
            foreach (var groupedPopup in this.activePopups)
            {
                if (!groupedPopup.IsPositionSetting || groupedPopup.IsClosed)
                {
                    continue;
                }

                if (!string.Equals(groupedPopup.PositionSettingGroupId, popup.PositionSettingGroupId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                groupedPopup.Trigger.PositionX += delta.X;
                groupedPopup.Trigger.PositionY += delta.Y;
                groupedPopup.PositionChanged = true;
            }

            return;
        }

        popup.Trigger.PositionX += delta.X;
        popup.Trigger.PositionY += delta.Y;
        popup.PositionChanged = true;
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
                this.ApplyPositionSettingDragDelta(popup, delta);
            }

            return;
        }

        popup.IsDragging = false;

        if (popup.PositionChanged)
        {
            popup.PositionChanged = false;
            this.SaveConfig();
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

        public string? BattleLogMatchedLogKey { get; set; }

        public Dictionary<int, DateTime> InternalLogMatchedAtUtcByIndex { get; } = new();

        public Dictionary<int, string> InternalLogMatchedKeyByIndex { get; } = new();

        public StatusRemainingSnapshot? MatchedLogStatusRemainingSnapshot { get; set; }
    }

    public void Dispose()
    {
        ChatGui.ChatMessage -= this.OnChatMessage;

        PluginInterface.UiBuilder.Draw -= this.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;

        CommandManager.RemoveHandler(CommandName);

        this.windowSystem.RemoveAllWindows();
        this.vfxLogCollector.Dispose();
        this.voiceVoxSpeechService.Dispose();
        this.textTextureCacheService.Dispose();
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
