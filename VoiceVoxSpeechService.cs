using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HappyTrigger;

public sealed class VoiceVoxSpeechService : IDisposable
{
    private const int DefaultTimeoutSeconds = 10;

    private readonly HttpClient httpClient = new();
    private readonly Action<string> addInternalLog;
    private readonly SemaphoreSlim speechLock = new(1, 1);

    public VoiceVoxSpeechService(Action<string> addInternalLog)
    {
        this.addInternalLog = addInternalLog;
        this.httpClient.Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);
    }

    public void SpeakAsync(HappyTriggerSetting trigger)
    {
        if (!trigger.DisplayTextMode || !trigger.EnableVoiceVox || string.IsNullOrWhiteSpace(trigger.DisplayText))
        {
            return;
        }

        var text = trigger.DisplayText;
        var endpoint = string.IsNullOrWhiteSpace(trigger.VoiceVoxEndpoint)
            ? "http://127.0.0.1:50021"
            : trigger.VoiceVoxEndpoint.Trim().TrimEnd('/');
        var speakerId = trigger.VoiceVoxSpeakerId;
        var waitSeconds = Math.Clamp(trigger.WaitSeconds, 0.0f, 600.0f);

        _ = Task.Run(() => this.SpeakCoreAsync(text, endpoint, speakerId, waitSeconds));
    }

    private async Task SpeakCoreAsync(string text, string endpoint, int speakerId, float waitSeconds)
    {
        if (waitSeconds > 0.0f)
        {
            await Task.Delay(TimeSpan.FromSeconds(waitSeconds)).ConfigureAwait(false);
        }

        await this.speechLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var baseUri) ||
                (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            {
                this.addInternalLog("[ERROR] VOICEVOX Engineに接続できません。Endpointの形式が不正です。");
                return;
            }

            if (speakerId < 0)
            {
                this.addInternalLog("[ERROR] VOICEVOX Engineに接続できません。Speaker IDが不正です。");
                return;
            }

            var audioQueryUri = $"{endpoint}/audio_query?text={Uri.EscapeDataString(text)}&speaker={speakerId}";
            using var audioQueryResponse = await this.httpClient.PostAsync(audioQueryUri, null).ConfigureAwait(false);
            if (!audioQueryResponse.IsSuccessStatusCode)
            {
                this.addInternalLog($"[ERROR] VOICEVOX Engineに接続できません。audio_query failed. Status={(int)audioQueryResponse.StatusCode}");
                return;
            }

            var audioQueryJson = await audioQueryResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(audioQueryJson) || !IsJsonObject(audioQueryJson))
            {
                this.addInternalLog("[ERROR] VOICEVOX Engineに接続できません。audio_query responseが不正です。");
                return;
            }

            var synthesisUri = $"{endpoint}/synthesis?speaker={speakerId}";
            using var synthesisContent = new StringContent(audioQueryJson, Encoding.UTF8, "application/json");
            using var synthesisResponse = await this.httpClient.PostAsync(synthesisUri, synthesisContent).ConfigureAwait(false);
            if (!synthesisResponse.IsSuccessStatusCode)
            {
                this.addInternalLog($"[ERROR] VOICEVOX Engineに接続できません。synthesis failed. Status={(int)synthesisResponse.StatusCode}");
                return;
            }

            var wavBytes = await synthesisResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (wavBytes.Length == 0)
            {
                this.addInternalLog("[ERROR] VOICEVOX Engineに接続できません。音声データが空です。");
                return;
            }

            await Task.Run(() => PlayWavBytes(wavBytes)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.addInternalLog($"[ERROR] VOICEVOX Engineに接続できません。Reason={ex.Message}");
        }
        finally
        {
            this.speechLock.Release();
        }
    }

    private static bool IsJsonObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private static void PlayWavBytes(byte[] wavBytes)
    {
        // SND_MEMORY + SND_SYNC により、追加ライブラリなしでWAVバイト列を再生します。
        PlaySound(wavBytes, IntPtr.Zero, SoundFlags.SndMemory | SoundFlags.SndSync | SoundFlags.SndNoDefault);
    }

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(byte[] sound, IntPtr hmod, SoundFlags flags);

    [Flags]
    private enum SoundFlags
    {
        SndSync = 0x0000,
        SndNoDefault = 0x0002,
        SndMemory = 0x0004,
    }

    public void Dispose()
    {
        this.httpClient.Dispose();
        this.speechLock.Dispose();
    }
}
