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

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
