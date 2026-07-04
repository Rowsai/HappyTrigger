using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;

namespace HappyTrigger;

public sealed class VfxLogCollector : IDisposable
{
    private const double DuplicateSuppressSeconds = 0.30;

    private static readonly string[] ActorVfxCreateSignatures =
    {
        // Splatoon 現行版に近い ActorVFX Create 系シグネチャ候補です。
        "40 53 55 56 57 48 81 EC ?? ?? ?? ?? 0F 29 B4 24 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 0F B6 AC 24 ?? ?? ?? ?? 0F 28 F3 49 8B F8",

        // 旧候補。環境によってはこちらで見つかる可能性があるため、フォールバックとして残します。
        "40 53 48 83 EC 20 48 8B DA 48 85 D2 74 28 48 8B 0D ?? ?? ?? ?? 48 85 C9 74 1F 45 33 C9 4C 8B C2 48 8B D3 E8 ?? ?? ?? ?? 48 85 C0 74 04 48 83 C4 20 5B C3",
    };

    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly Action<string> addInternalLog;
    private readonly Dictionary<string, DateTime> recentLogKeys = new(StringComparer.OrdinalIgnoreCase);

    private Hook<ActorVfxCreateDelegate>? actorVfxCreateHook;

    public VfxLogCollector(
        IObjectTable objectTable,
        IGameInteropProvider gameInteropProvider,
        IPluginLog log,
        Action<string> addInternalLog)
    {
        this.objectTable = objectTable;
        this.log = log;
        this.addInternalLog = addInternalLog;

        var hookErrors = new List<string>();

        foreach (var signature in ActorVfxCreateSignatures)
        {
            try
            {
                this.actorVfxCreateHook = gameInteropProvider.HookFromSignature<ActorVfxCreateDelegate>(
                    signature,
                    this.ActorVfxCreateDetour);

                this.actorVfxCreateHook.Enable();
                this.addInternalLog("VFX Effect logger hook enabled.");
                return;
            }
            catch (Exception ex)
            {
                hookErrors.Add($"Signature={signature} / Reason={ex.Message}");
                this.log.Debug(ex, $"Failed VFX signature candidate. Signature={signature}");
            }
        }

        var message = string.Join(" | ", hookErrors);
        this.log.Warning($"Failed to initialize VFX Effect logger hook. {message}");
        this.addInternalLog($"VFX Effect logger hook failed. Reason={message}");
    }

    private nint ActorVfxCreateDetour(
        nint pathAddress,
        nint actorAddress,
        nint targetAddress,
        float scale,
        byte a5,
        ushort a6,
        byte a7)
    {
        try
        {
            var path = Marshal.PtrToStringAnsi(pathAddress) ?? string.Empty;
            this.TryLogActorVfx(path, actorAddress, targetAddress, scale);
        }
        catch (Exception ex)
        {
            this.log.Debug(ex, "Failed to log actor VFX.");
        }

        return this.actorVfxCreateHook!.Original(pathAddress, actorAddress, targetAddress, scale, a5, a6, a7);
    }

    private void TryLogActorVfx(string path, nint actorAddress, nint targetAddress, float scale)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.Contains(".avfx", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var matchedObject = this.FindMatchedBattleChara(actorAddress, targetAddress);
        if (matchedObject == null)
        {
            return;
        }

        var objectAddress = TryGetObjectAddress(matchedObject);
        var actorHex = ToHex(actorAddress);
        var targetHex = ToHex(targetAddress);
        var objectHex = ToHex(objectAddress);
        var key = $"{matchedObject.GameObjectId}:{objectHex}:{actorHex}:{targetHex}:{path}";

        if (this.ShouldSuppressDuplicate(key))
        {
            return;
        }

        var spawnTarget = GetSpawnTargetLabel(matchedObject);
        var name = matchedObject.Name.TextValue;
        var npcId = GetUIntProperty(matchedObject, "DataId", "NpcId", "BNpcBase", "BNpcName");
        var modelId = GetUIntProperty(matchedObject, "ModelCharaId", "ModelId", "ModelChara");
        var nameNpcId = GetUIntProperty(matchedObject, "NameId", "NameNpcId");
        var position = GetPositionText(matchedObject);

        this.addInternalLog(
            $"VFX {path} spawned on {spawnTarget} npc id={npcId}, model id={modelId}, name npc id={nameNpcId}, position={position}, name={name} ActorPtr={actorHex} TargetPtr={targetHex} Scale={scale:0.##}");
    }

    private IGameObject? FindMatchedBattleChara(nint actorAddress, nint targetAddress)
    {
        foreach (var battleChara in this.GetBattleCharas())
        {
            var objectAddress = TryGetObjectAddress(battleChara);
            if (objectAddress == nint.Zero)
            {
                continue;
            }

            if (objectAddress == actorAddress || objectAddress == targetAddress)
            {
                return battleChara;
            }
        }

        return null;
    }

    private List<IBattleChara> GetBattleCharas()
    {
        return this.objectTable
            .Where(obj => obj is IBattleChara)
            .Cast<IBattleChara>()
            .Where(battleChara => battleChara.ObjectKind == ObjectKind.BattleNpc || battleChara is IPlayerCharacter)
            .Where(battleChara => !string.IsNullOrWhiteSpace(battleChara.Name.TextValue))
            .ToList();
    }

    private static string GetSpawnTargetLabel(IGameObject gameObject)
    {
        if (gameObject is IPlayerCharacter player)
        {
            var job = player.ClassJob.ValueNullable?.Abbreviation.ExtractText() ?? string.Empty;
            return string.IsNullOrWhiteSpace(job) ? "player" : job;
        }

        if (gameObject.ObjectKind == ObjectKind.BattleNpc)
        {
            return "enemy";
        }

        return gameObject.ObjectKind.ToString();
    }

    private bool ShouldSuppressDuplicate(string key)
    {
        var now = DateTime.UtcNow;

        var expiredKeys = this.recentLogKeys
            .Where(pair => (now - pair.Value).TotalSeconds > DuplicateSuppressSeconds)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var expiredKey in expiredKeys)
        {
            this.recentLogKeys.Remove(expiredKey);
        }

        if (this.recentLogKeys.TryGetValue(key, out var lastLoggedAt) &&
            (now - lastLoggedAt).TotalSeconds <= DuplicateSuppressSeconds)
        {
            return true;
        }

        this.recentLogKeys[key] = now;
        return false;
    }

    private static nint TryGetObjectAddress(IGameObject gameObject)
    {
        try
        {
            var value = GetPropertyValue(gameObject, "Address");
            return value switch
            {
                nint nativeInt => nativeInt,
                ulong ulongValue => unchecked((nint)(long)ulongValue),
                long longValue => (nint)longValue,
                uint uintValue => (nint)uintValue,
                int intValue => (nint)intValue,
                _ => nint.Zero,
            };
        }
        catch
        {
            return nint.Zero;
        }
    }

    private static uint GetUIntProperty(object source, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = GetPropertyValue(source, propertyName);
            if (value == null)
            {
                continue;
            }

            try
            {
                return value switch
                {
                    byte byteValue => byteValue,
                    ushort ushortValue => ushortValue,
                    uint uintValue => uintValue,
                    ulong ulongValue => unchecked((uint)ulongValue),
                    short shortValue => unchecked((uint)shortValue),
                    int intValue => unchecked((uint)intValue),
                    long longValue => unchecked((uint)longValue),
                    _ => 0u,
                };
            }
            catch
            {
                return 0u;
            }
        }

        return 0u;
    }

    private static string GetPositionText(IGameObject gameObject)
    {
        try
        {
            var value = GetPropertyValue(gameObject, "Position");
            if (value is Vector3 position)
            {
                return $"<{position.X}, {position.Y}, {position.Z}>";
            }
        }
        catch
        {
            // ignored
        }

        return "<0, 0, 0>";
    }

    private static object? GetPropertyValue(object source, string propertyName)
    {
        var type = source.GetType();
        var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(source);
    }

    private static string ToHex(nint value)
    {
        return $"0x{value.ToInt64():X}";
    }

    public void Dispose()
    {
        try
        {
            this.actorVfxCreateHook?.Disable();
            this.actorVfxCreateHook?.Dispose();
        }
        catch (Exception ex)
        {
            this.log.Debug(ex, "Failed to dispose VFX Effect logger hook.");
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ActorVfxCreateDelegate(
        nint pathAddress,
        nint actorAddress,
        nint targetAddress,
        float scale,
        byte a5,
        ushort a6,
        byte a7);
}
