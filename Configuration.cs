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
}
