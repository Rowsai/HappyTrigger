using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace HappyTrigger;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // 既存の画像表示用トリガーです。
    // これまで保存していたトリガーはこのリストに残ります。
    public List<HappyTriggerSetting> Triggers { get; set; } = new();

    // 新規追加のテキスト表示用トリガーです。
    public List<HappyTriggerSetting> TextTriggers { get; set; } = new();

    // FFXIV Logタブに出力されたバトルログ / 内部ログを参照するトリガーです。
    public List<HappyTriggerSetting> FfxivLogTriggers { get; set; } = new();

    // FFXIV Log参照トリガーで、バトルログ / 内部ログの組み合わせ条件が揃う猶予秒数です。
    // 例: 15.0 の場合、最初にマッチした条件から最後にマッチした条件までが15秒以内なら発火します。
    public float FfxivLogReferencePairWindowSeconds { get; set; } = 10.0f;

    // FFXIV Log参照トリガーのマッチ状態デバッグログを内部ログに表示するかどうかです。
    // OFFの場合も判定自体は行いますが、デバッグログは出力しません。
    public bool ShowFfxivLogReferenceDebugLogs { get; set; } = false;

    // トリガー管理用のボックスです。IDは BOX001, BOX002 ... の形式です。
    public List<TriggerBoxSetting> TriggerBoxes { get; set; } = new();

    // トリガー管理用のラベルです。IDは Lab001, Lab002 ... の形式です。
    public List<TriggerLabelSetting> TriggerLabels { get; set; } = new();

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

[Serializable]
public sealed class TriggerBoxSetting
{
    public string BoxId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[Serializable]
public sealed class TriggerLabelSetting
{
    public string LabelId { get; set; } = string.Empty;

    public string BoxId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    // ラベル配下のログトリガーをまとめて表示するときの基準X座標です。
    public float PositionX { get; set; } = 100.0f;

    // ラベル配下のログトリガーをまとめて表示するときの基準Y座標です。
    public float PositionY { get; set; } = 100.0f;

    // ラベル配下のログトリガーを縦並びで表示するときの行間です。
    public float LineSpacing { get; set; } = 4.0f;

    // ラベル配下ログトリガーへ継承する発火場所条件です。
    // None の場合はラベル側では制限せず、ログトリガー自身の場所条件を使用します。
    // Area / Content の場合は、配下ログトリガー側の場所条件より優先します。
    public TriggerLocationRestrictionType LocationRestrictionType { get; set; } = TriggerLocationRestrictionType.None;

    // LocationRestrictionType=Area の場合に参照する TerritoryType.RowId です。
    // LocationRestrictionType=Content の場合も、選択したコンテンツに紐づく TerritoryType.RowId を保持します。
    public uint RequiredTerritoryTypeId { get; set; } = 0;

    // UI表示用のエリア名キャッシュです。
    public string RequiredTerritoryName { get; set; } = string.Empty;

    // LocationRestrictionType=Content の場合に参照する ContentFinderCondition.RowId です。
    public uint RequiredContentFinderConditionId { get; set; } = 0;

    // UI表示用のコンテンツ名キャッシュです。
    public string RequiredContentName { get; set; } = string.Empty;

    // true の場合、このラベル配下では同じステータス名の残り時間表示を複数同時に許可します。
    // 例: 「呪詛の叫び声（見る）」と「呪詛の叫び声（見ない）」を別ラベルで同時表示したい場合に使用します。
    public bool AllowDuplicateStatusRemainingDisplay { get; set; } = false;
}
