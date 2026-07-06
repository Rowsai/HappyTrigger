using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace HappyTrigger;

public sealed class TextTextureCacheService : IDisposable
{
    public sealed class TextTextureResult
    {
        public IDalamudTextureWrap? Texture { get; set; }

        public Vector2Size Size { get; set; }

        public bool Loading { get; set; }

        public string? Error { get; set; }
    }

    public readonly struct Vector2Size
    {
        public Vector2Size(float width, float height)
        {
            this.Width = width;
            this.Height = height;
        }

        public float Width { get; }

        public float Height { get; }
    }

    private sealed class RenderedPng
    {
        public RenderedPng(byte[] pngBytes, int width, int height)
        {
            this.PngBytes = pngBytes;
            this.Width = width;
            this.Height = height;
        }

        public byte[] PngBytes { get; }

        public int Width { get; }

        public int Height { get; }
    }

    private sealed class FontFamilyHolder : IDisposable
    {
        public FontFamilyHolder(FontFamily family, PrivateFontCollection? privateCollection, bool disposeFamily)
        {
            this.Family = family;
            this.PrivateCollection = privateCollection;
            this.DisposeFamily = disposeFamily;
        }

        public FontFamily Family { get; }

        private PrivateFontCollection? PrivateCollection { get; }

        private bool DisposeFamily { get; }

        public void Dispose()
        {
            if (this.DisposeFamily)
            {
                this.Family.Dispose();
            }

            this.PrivateCollection?.Dispose();
        }
    }

    private readonly ITextureProvider textureProvider;
    private readonly Dictionary<string, TextTextureResult> cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextTextureResult> lastReadyByLayoutKey = new(StringComparer.OrdinalIgnoreCase);

    public TextTextureCacheService(ITextureProvider textureProvider)
    {
        this.textureProvider = textureProvider;
    }

    public TextTextureResult GetTextTexture(
        string text,
        HappyTriggerSetting trigger,
        float fadeAlpha)
    {
        return this.GetTextTexture(text, text, trigger, fadeAlpha);
    }

    public TextTextureResult GetTextTexture(
        string text,
        string layoutText,
        HappyTriggerSetting trigger,
        float fadeAlpha)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new TextTextureResult();
        }

        if (string.IsNullOrWhiteSpace(layoutText))
        {
            layoutText = text;
        }

        var key = MakeKey(text, layoutText, trigger);
        var layoutKey = MakeLayoutKey(layoutText, trigger);
        if (!this.cache.TryGetValue(key, out var result))
        {
            result = new TextTextureResult { Loading = true };
            this.cache[key] = result;
            _ = this.RenderTextureAsync(key, layoutKey, result, text, layoutText, trigger);
        }

        // 秒数表示などでテキストが頻繁に変わる場合、生成中フレームにImGui描画へ戻ると表示がブレます。
        // 同じレイアウト幅の直前テクスチャがあれば、それを維持してチラつきと位置ズレを抑えます。
        if (result.Texture == null && this.lastReadyByLayoutKey.TryGetValue(layoutKey, out var lastReady) && lastReady.Texture != null)
        {
            return lastReady;
        }

        return result;
    }

    public void Clear()
    {
        foreach (var entry in this.cache.Values)
        {
            entry.Texture?.Dispose();
            entry.Texture = null;
        }

        this.cache.Clear();
        this.lastReadyByLayoutKey.Clear();
    }

    private async Task RenderTextureAsync(
        string key,
        string layoutKey,
        TextTextureResult result,
        string text,
        string layoutText,
        HappyTriggerSetting trigger)
    {
        try
        {
            var renderResult = await Task.Run(() => RenderTextPng(text, layoutText, trigger)).ConfigureAwait(false);
            result.Texture = await this.textureProvider.CreateFromImageAsync(
                renderResult.PngBytes,
                $"HappyTrigger:Text:{key}").ConfigureAwait(false);
            result.Size = new Vector2Size(renderResult.Width, renderResult.Height);
            result.Error = null;
            this.lastReadyByLayoutKey[layoutKey] = result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }
        finally
        {
            result.Loading = false;
        }
    }

    private static RenderedPng RenderTextPng(string text, string layoutText, HappyTriggerSetting trigger)
    {
        var fontSize = Math.Clamp(trigger.TextSize, 8.0f, 256.0f);
        fontSize = MathF.Round(fontSize);

        var fontStyle = trigger.TextFontDesign == TextFontDesign.Bold ||
                        trigger.TextFontDesign == TextFontDesign.StrongOutline
            ? FontStyle.Bold
            : FontStyle.Regular;

        using var fontHolder = CreateFontFamily(trigger);
        var fontFamily = fontHolder.Family;
        using var layoutPath = new GraphicsPath();
        layoutPath.AddString(
            layoutText,
            fontFamily,
            (int)fontStyle,
            fontSize,
            new PointF(0.0f, 0.0f),
            StringFormat.GenericTypographic);

        using var textPath = new GraphicsPath();
        textPath.AddString(
            text,
            fontFamily,
            (int)fontStyle,
            fontSize,
            new PointF(0.0f, 0.0f),
            StringFormat.GenericTypographic);

        var outlineThickness = trigger.EnableTextOutline || trigger.TextFontDesign == TextFontDesign.StrongOutline
            ? Math.Max(1.0f, trigger.TextOutlineThickness)
            : 0.0f;
        if (trigger.TextFontDesign == TextFontDesign.StrongOutline)
        {
            outlineThickness = Math.Max(3.0f, outlineThickness * 1.5f);
        }

        var shadowEnabled = trigger.TextFontDesign == TextFontDesign.Shadow || trigger.TextFontDesign == TextFontDesign.Neon;
        var shadowOffsetX = shadowEnabled ? MathF.Round(trigger.TextShadowOffsetX) : 0.0f;
        var shadowOffsetY = shadowEnabled ? MathF.Round(trigger.TextShadowOffsetY) : 0.0f;
        var neonPadding = trigger.TextFontDesign == TextFontDesign.Neon ? 10.0f : 0.0f;
        var bounds = layoutPath.GetBounds();
        var paddingLeft = MathF.Ceiling(outlineThickness + neonPadding + Math.Max(0.0f, -shadowOffsetX)) + 4.0f;
        var paddingTop = MathF.Ceiling(outlineThickness + neonPadding + Math.Max(0.0f, -shadowOffsetY)) + 4.0f;
        var paddingRight = MathF.Ceiling(outlineThickness + neonPadding + Math.Max(0.0f, shadowOffsetX)) + 4.0f;
        var paddingBottom = MathF.Ceiling(outlineThickness + neonPadding + Math.Max(0.0f, shadowOffsetY)) + 4.0f;

        var width = Math.Max(1, (int)MathF.Ceiling(bounds.Width + paddingLeft + paddingRight));
        var height = Math.Max(1, (int)MathF.Ceiling(bounds.Height + paddingTop + paddingBottom));

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        bitmap.SetResolution(96.0f, 96.0f);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using var matrix = new Matrix();
            matrix.Translate(paddingLeft - bounds.Left, paddingTop - bounds.Top);
            textPath.Transform(matrix);

            if (shadowEnabled)
            {
                using var shadowPath = (GraphicsPath)textPath.Clone();
                using var shadowMatrix = new Matrix();
                shadowMatrix.Translate(shadowOffsetX, shadowOffsetY);
                shadowPath.Transform(shadowMatrix);
                using var shadowBrush = new SolidBrush(ToDrawingColor(
                    trigger.TextShadowColorR,
                    trigger.TextShadowColorG,
                    trigger.TextShadowColorB,
                    trigger.TextShadowColorA));
                graphics.FillPath(shadowBrush, shadowPath);
            }

            if (trigger.TextFontDesign == TextFontDesign.Neon)
            {
                using var neonPen1 = new Pen(ToDrawingColor(
                    trigger.TextShadowColorR,
                    trigger.TextShadowColorG,
                    trigger.TextShadowColorB,
                    trigger.TextShadowColorA * 0.55f), 8.0f)
                {
                    LineJoin = LineJoin.Round,
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };
                graphics.DrawPath(neonPen1, textPath);
            }

            if (outlineThickness > 0.0f)
            {
                using var outlinePen = new Pen(ToDrawingColor(
                    trigger.TextOutlineColorR,
                    trigger.TextOutlineColorG,
                    trigger.TextOutlineColorB,
                    trigger.TextOutlineColorA), outlineThickness * 2.0f)
                {
                    LineJoin = LineJoin.Round,
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };
                graphics.DrawPath(outlinePen, textPath);
            }

            using var textBrush = new SolidBrush(ToDrawingColor(
                trigger.TextColorR,
                trigger.TextColorG,
                trigger.TextColorB,
                trigger.TextColorA));
            graphics.FillPath(textBrush, textPath);
        }

        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, ImageFormat.Png);
        return new RenderedPng(memoryStream.ToArray(), width, height);
    }

    private static FontFamilyHolder CreateFontFamily(HappyTriggerSetting trigger)
    {
        var customFontPath = trigger.CustomTextFontPath?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(customFontPath) && File.Exists(customFontPath))
        {
            try
            {
                var privateFontCollection = new PrivateFontCollection();
                privateFontCollection.AddFontFile(customFontPath);
                if (privateFontCollection.Families.Length > 0)
                {
                    return new FontFamilyHolder(privateFontCollection.Families[0], privateFontCollection, false);
                }

                privateFontCollection.Dispose();
            }
            catch
            {
                // fall back to installed font
            }
        }

        var preferredFontName = trigger.TextFontFamilyName?.Trim() ?? string.Empty;
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferredFontName))
        {
            candidates.Add(preferredFontName);
        }

        candidates.AddRange(new[]
        {
            "Meiryo",
            "Yu Gothic UI",
            "Yu Gothic",
            "BIZ UDPGothic",
            "BIZ UDGothic",
            "Noto Sans JP",
            "M PLUS 1p",
            "MS Gothic",
            FontFamily.GenericSansSerif.Name,
        });

        foreach (var fontName in candidates)
        {
            try
            {
                return new FontFamilyHolder(new FontFamily(fontName), null, true);
            }
            catch
            {
                // try next font
            }
        }

        return new FontFamilyHolder(FontFamily.GenericSansSerif, null, false);
    }

    private static Color ToDrawingColor(float r, float g, float b, float a)
    {
        return Color.FromArgb(
            ToByte(a),
            ToByte(r),
            ToByte(g),
            ToByte(b));
    }

    private static int ToByte(float value)
    {
        return (int)Math.Clamp(MathF.Round(Math.Clamp(value, 0.0f, 1.0f) * 255.0f), 0.0f, 255.0f);
    }

    private static string MakeKey(string text, string layoutText, HappyTriggerSetting trigger)
    {
        var raw = string.Join("|",
            text,
            layoutText,
            MathF.Round(Math.Clamp(trigger.TextSize, 8.0f, 256.0f)),
            (int)trigger.TextFontDesign,
            trigger.EnableTextOutline,
            trigger.TextOutlineThickness,
            trigger.TextOutlineColorR,
            trigger.TextOutlineColorG,
            trigger.TextOutlineColorB,
            trigger.TextOutlineColorA,
            trigger.TextColorR,
            trigger.TextColorG,
            trigger.TextColorB,
            trigger.TextColorA,
            trigger.TextShadowOffsetX,
            trigger.TextShadowOffsetY,
            trigger.TextShadowColorR,
            trigger.TextShadowColorG,
            trigger.TextShadowColorB,
            trigger.TextShadowColorA,
            trigger.TextFontFamilyName,
            trigger.CustomTextFontPath);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    private static string MakeLayoutKey(string layoutText, HappyTriggerSetting trigger)
    {
        var raw = string.Join("|layout|",
            layoutText,
            MathF.Round(Math.Clamp(trigger.TextSize, 8.0f, 256.0f)),
            (int)trigger.TextFontDesign,
            trigger.EnableTextOutline,
            trigger.TextOutlineThickness,
            trigger.TextOutlineColorR,
            trigger.TextOutlineColorG,
            trigger.TextOutlineColorB,
            trigger.TextOutlineColorA,
            trigger.TextColorR,
            trigger.TextColorG,
            trigger.TextColorB,
            trigger.TextColorA,
            trigger.TextShadowOffsetX,
            trigger.TextShadowOffsetY,
            trigger.TextShadowColorR,
            trigger.TextShadowColorG,
            trigger.TextShadowColorB,
            trigger.TextShadowColorA,
            trigger.TextFontFamilyName,
            trigger.CustomTextFontPath);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    public void Dispose()
    {
        this.Clear();
    }
}
