using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace HappyTrigger;

public sealed class ImageCacheService : IDisposable
{
    private sealed class CacheEntry
    {
        public IDalamudTextureWrap? Texture { get; set; }

        public bool Loading { get; set; }

        public string? Error { get; set; }
    }

    private readonly ITextureProvider textureProvider;
    private readonly HttpClient httpClient = new();
    private readonly Dictionary<string, CacheEntry> cache = new();

    public ImageCacheService(ITextureProvider textureProvider)
    {
        this.textureProvider = textureProvider;
        this.httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public IDalamudTextureWrap? GetTexture(string imagePath, bool isWebImage)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        var key = MakeKey(imagePath, isWebImage);

        if (!this.cache.TryGetValue(key, out var entry))
        {
            entry = new CacheEntry();
            this.cache[key] = entry;
        }

        if (entry.Texture != null)
        {
            return entry.Texture;
        }

        if (!entry.Loading)
        {
            entry.Loading = true;
            _ = this.LoadTextureAsync(entry, imagePath, isWebImage);
        }

        return null;
    }

    public string? GetError(string imagePath, bool isWebImage)
    {
        var key = MakeKey(imagePath, isWebImage);
        return this.cache.TryGetValue(key, out var entry) ? entry.Error : null;
    }

    public void Clear()
    {
        foreach (var entry in this.cache.Values)
        {
            entry.Texture?.Dispose();
            entry.Texture = null;
        }

        this.cache.Clear();
    }

    private static string MakeKey(string imagePath, bool isWebImage)
    {
        return $"{isWebImage}:{imagePath}";
    }

    private async Task LoadTextureAsync(CacheEntry entry, string imagePath, bool isWebImage)
    {
        try
        {
            byte[] bytes;

            if (isWebImage)
            {
                if (!Uri.TryCreate(imagePath, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    entry.Error = "Web画像URLは http または https のURLを指定してください。";
                    return;
                }

                bytes = await this.httpClient.GetByteArrayAsync(uri).ConfigureAwait(false);
            }
            else
            {
                if (!File.Exists(imagePath))
                {
                    entry.Error = $"画像ファイルが見つかりません: {imagePath}";
                    return;
                }

                bytes = await File.ReadAllBytesAsync(imagePath).ConfigureAwait(false);
            }

            entry.Texture = await this.textureProvider.CreateFromImageAsync(
                bytes,
                $"HappyTrigger:{imagePath}").ConfigureAwait(false);

            entry.Error = null;
        }
        catch (Exception ex)
        {
            entry.Error = ex.Message;
        }
        finally
        {
            entry.Loading = false;
        }
    }

    public void Dispose()
    {
        this.Clear();
        this.httpClient.Dispose();
    }
}
