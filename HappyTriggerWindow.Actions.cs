using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using Dalamud.Interface.Textures;
using Lumina.Excel.Sheets;

namespace HappyTrigger;

public sealed partial class HappyTriggerWindow
{
    private string MarkerName(uint id) => Plugin.DataManager.GetExcelSheet<Marker>(ClientLanguage.Japanese).GetRowOrDefault(id)?.Name.ToString() ?? "未選択";
    private string JobName(uint id)
    {
        var row = Plugin.DataManager.GetExcelSheet<ClassJob>(ClientLanguage.Japanese).GetRowOrDefault(id);
        return row is { } job ? $"{job.Name}＜{job.Abbreviation}＞" : "未選択";
    }

    private static void DrawMarkerIcon(uint icon)
    {
        var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrDefault();
        var size = new Vector2(ImGui.GetFrameHeight());
        if (texture != null) ImGui.Image(texture.Handle, size);
        else ImGui.Dummy(size);
    }

    private void DrawTriggerActions()
    {
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.79f, 1), "合致時の追加アクション");
        ImGui.TextWrapped("マーカー付与とチャット送信は、それぞれ必要なものだけ有効にしてください。条件成立時に実行します。");
        var markerEnabled = this.editTrigger.EnableTargetMarker;
        if (ImGui.Checkbox("対象ジョブへターゲットマーカーを付与", ref markerEnabled)) this.editTrigger.EnableTargetMarker = markerEnabled;
        if (markerEnabled)
        {
            var markers = Plugin.DataManager.GetExcelSheet<Marker>(ClientLanguage.Japanese);
            var selected = markers.GetRowOrDefault(this.editTrigger.TargetMarkerId);
            if (selected is { } current) { DrawMarkerIcon((uint)current.Icon); ImGui.SameLine(); }
            SetEditorFieldWidth(230);
            if (ImGui.BeginCombo("マーカー", this.MarkerName(this.editTrigger.TargetMarkerId)))
            {
                foreach (var marker in markers.Where(m => m.RowId is >= 1 and <= 17))
                {
                    DrawMarkerIcon((uint)marker.Icon);
                    ImGui.SameLine();
                    if (ImGui.Selectable($"{marker.Name}##marker{marker.RowId}", this.editTrigger.TargetMarkerId == marker.RowId)) this.editTrigger.TargetMarkerId = marker.RowId;
                }
                ImGui.EndCombo();
            }
            SetEditorFieldWidth(300);
            if (ImGui.BeginCombo("対象ジョブ", this.JobName(this.editTrigger.TargetMarkerJobId)))
            {
                foreach (var job in Plugin.DataManager.GetExcelSheet<ClassJob>(ClientLanguage.Japanese).Where(j => j.JobIndex > 0))
                    if (ImGui.Selectable(this.JobName(job.RowId), this.editTrigger.TargetMarkerJobId == job.RowId)) this.editTrigger.TargetMarkerJobId = job.RowId;
                ImGui.EndCombo();
            }
            ImGui.TextWrapped("自分を含むパーティーが対象です。同一ジョブが複数いる場合や対象が不在の場合はスキップします。ソロでは自分が対象です。");
        }
        var chatEnabled = this.editTrigger.EnableChatSend;
        if (ImGui.Checkbox("チャットを自動送信", ref chatEnabled)) this.editTrigger.EnableChatSend = chatEnabled;
        if (chatEnabled)
        {
            string[] channels = { "say", "party", "alliance", "echo" };
            string[] labels = { "Say（周囲）", "パーティー", "アライアンス", "自分のみ（エコー）" };
            var index = System.Array.IndexOf(channels, this.editTrigger.ChatChannel);
            SetEditorFieldWidth(260);
            if (ImGui.Combo("送信先", ref index, labels, labels.Length)) this.editTrigger.ChatChannel = channels[index];
            var message = this.editTrigger.ChatMessageText ?? string.Empty;
            SetEditorFieldWidth(600);
            if (InputTextJapanese("送信する本文", ref message, 1024)) this.editTrigger.ChatMessageText = message;
            ImGui.TextWrapped("本文のみ入力してください（UTF-8で450バイトまで）。自動送信した本文は30秒間、自己送信ログからの再発火を抑止します。");
        }
        if (TriggerActionRules.Validate(this.editTrigger) is { } error)
            ImGui.TextColored(new Vector4(1, 0.55f, 0.4f, 1), error);

        ImGui.Spacing();
    }
}
