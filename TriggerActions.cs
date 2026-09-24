using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Plugin.Services;

using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace HappyTrigger;

internal static class TriggerActionRules
{
    public static string? Validate(HappyTriggerSetting setting)
    {
        if (setting.EnableTargetMarker && (setting.TargetMarkerId is < 1 or > 17 || setting.TargetMarkerJobId == 0))
            return "ターゲットマーカーと対象ジョブを選択してください。";
        if (setting.EnableChatSend)
        {
            var text = setting.ChatMessageText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return "送信するチャット本文を入力してください。";
            if (text.Any(char.IsControl) || text.Contains('<') || text.Contains('>') || text.TrimStart().StartsWith('/'))
                return "チャット本文には改行・制御文字・テキストコマンド・山括弧を使用できません。";
            if (Encoding.UTF8.GetByteCount(text) > 450) return "チャット本文はUTF-8で450バイト以内にしてください。";
            if (setting.ChatChannel is not ("say" or "party" or "alliance" or "echo")) return "送信先が不正です。";
        }
        return null;
    }
}

// Framework更新で実行し、チャットイベント内での再入と大量送信を避けます。
internal sealed class TriggerActionService : IDisposable
{
    private readonly Queue<(HappyTriggerSetting Setting, DateTime Queued, uint Territory)> pending = new();
    private readonly Dictionary<string, DateTime> echoes = new(StringComparer.Ordinal);
    private readonly Action<string> log;
    private DateTime nextExecution;
    private bool executing;

    public TriggerActionService(Action<string> log)
    {
        this.log = log;
        Plugin.Framework.Update += this.Update;
    }

    public void Enqueue(HappyTriggerSetting setting)
    {
        if (!setting.EnableTargetMarker && !setting.EnableChatSend) return;
        var error = TriggerActionRules.Validate(setting);
        if (error != null) { this.log(error); return; }
        if (this.pending.Count >= 32) { this.log("追加動作の待機上限に達したためスキップしました。"); return; }
        this.pending.Enqueue((setting.Clone(), DateTime.UtcNow, Plugin.ClientState.TerritoryType));
    }

    public bool IsOwnOutput(string text, string sender)
    {
        var now = DateTime.UtcNow;
        foreach (var key in this.echoes.Where(x => x.Value < now).Select(x => x.Key).ToArray()) this.echoes.Remove(key);
        return this.executing || (this.echoes.ContainsKey(text) &&
            (string.IsNullOrEmpty(sender) || sender.StartsWith(Plugin.ObjectTable.LocalPlayer?.Name.TextValue ?? "\0", StringComparison.Ordinal)));
    }

    private void Update(IFramework framework)
    {
        if (!Plugin.ClientState.IsLoggedIn || Plugin.ObjectTable.LocalPlayer == null)
        {
            this.pending.Clear(); this.echoes.Clear(); return;
        }
        var now = DateTime.UtcNow;
        if (now < this.nextExecution || !this.pending.TryDequeue(out var entry)) return;
        if (entry.Territory != Plugin.ClientState.TerritoryType || now - entry.Queued > TimeSpan.FromSeconds(10)) return;
        this.nextExecution = now.AddSeconds(1);
        this.executing = true;
        try
        {
            if (entry.Setting.EnableTargetMarker)
            {
                try { this.ApplyMarker(entry.Setting); }
                catch (Exception ex) { this.log($"マーカー付与に失敗しました: {ex.Message}"); }
            }
            if (entry.Setting.EnableChatSend)
            {
                var body = Plugin.PluginInterface.Sanitizer.Sanitize(entry.Setting.ChatMessageText.Trim());
                if (string.IsNullOrWhiteSpace(body)) return;
                this.echoes[body] = now.AddSeconds(30);
                SendChat($"/{entry.Setting.ChatChannel} {body}");
            }
        }
        catch (Exception ex) { this.log($"追加動作に失敗しました: {ex.Message}"); }
        finally { this.executing = false; }
    }

    private unsafe void ApplyMarker(HappyTriggerSetting setting)
    {
        var members = Plugin.PartyList.Where(x => x.ClassJob.RowId == setting.TargetMarkerJobId).ToList();
        ulong targetId;
        if (Plugin.PartyList.Length == 0 && Plugin.ObjectTable.LocalPlayer is { } self && self.ClassJob.RowId == setting.TargetMarkerJobId)
        { targetId = self.GameObjectId; }
        else if (members.Count == 1 && members[0].GameObject is { } member)
        { targetId = member.GameObjectId; }
        else
        {
            this.log("マーカー付与をスキップ: 対象ジョブが不在、同じジョブが複数、または対象が読み込まれていません。");
            return;
        }
        var marking = FFXIVClientStructs.FFXIV.Client.Game.UI.MarkingController.Instance();
        if (marking != null && (ulong)marking->Markers[(int)setting.TargetMarkerId - 1] == targetId) return;
        string[] commands = { "", "attack1", "attack2", "attack3", "attack4", "attack5", "bind1", "bind2", "bind3", "ignore1", "ignore2", "square", "circle", "cross", "triangle", "attack6", "attack7", "attack8" };
        // ゲーム自身の代名詞解決を使い、パーティー表示順の並び替えにも対応します。
        var pronouns = FFXIVClientStructs.FFXIV.Client.UI.Misc.PronounModule.Instance();
        if (pronouns == null) return;
        for (var index = 1; index <= 8; index++)
        {
            var placeholder = $"<{index}>";
            var resolved = pronouns->ResolvePlaceholder(placeholder, 0, 0);
            if (resolved != null && (ulong)resolved->GetGameObjectId() == targetId)
            {
                SendChat($"/mk {commands[setting.TargetMarkerId]} {placeholder}");
                return;
            }
        }
        this.log("マーカー付与をスキップ: パーティー内の対象を解決できませんでした。");
    }

    private static unsafe void SendChat(string command)
    {
        var ui = UIModule.Instance();
        if (ui == null) return;
        var message = Utf8String.FromString(command);
        try { ui->ProcessChatBoxEntry(message); }
        finally { message->Dtor(true); }
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= this.Update;
        this.pending.Clear(); this.echoes.Clear();
    }
}
