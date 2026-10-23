using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Erya.Core.Localization;

/// <summary>
/// Core 层的轻量字符串本地化（Erya.Core 不依赖 Avalonia，故不用 Irihi.Lingua）。
/// 内嵌 JSON 字典：base = 简体中文，变体 <c>CoreStrings.zh-Hant.json</c> /
/// <c>CoreStrings.en.json</c>。语言由 App 层在切换时设置；缺 key 时回落 base。
/// </summary>
public static class CoreLoc
{
    private static Dictionary<string, string>? _base;
    private static Dictionary<string, string>? _variant;

    /// <summary>当前语言：""（base=简体）/ "zh-Hant" / "en"。</summary>
    private static string _language = "";

    public static string Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            _variant = null;
        }
    }

    /// <summary>取当前语言下的字符串（缺 key 回落 base，再缺返回 key 本身）。</summary>
    public static string Get(string key)
    {
        _variant ??= LoadVariant(_language);
        _base ??= LoadVariant("");
        return (_variant is not null && _variant.TryGetValue(key, out var v)) || _base!.TryGetValue(key, out v)
            ? v
            : key;
    }

    /// <summary>带格式参数的 <see cref="Get"/>（资源里用 {0} 占位）。</summary>
    public static string Format(string key, params object?[] args) => string.Format(Get(key), args);

    /// <summary>
    /// 读取任意内嵌扁平字符串字典（语言变体 overlay 用）；资源缺失返回 null。
    /// </summary>
    internal static Dictionary<string, string>? TryLoadMap(string resourceName)
    {
        try
        {
            using var stream = typeof(CoreLoc).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return null;
            return JsonSerializer.Deserialize(stream, CoreLocContext.Default.DictionaryStringString);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string>? LoadVariant(string language)
    {
        var suffix = language.Length == 0 ? "" : $".{language}";
        var resourceName = $"Erya.Core.Localization.CoreStrings{suffix}.json";
        try
        {
            using var stream = typeof(CoreLoc).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null) return null;
            return JsonSerializer.Deserialize(stream, CoreLocContext.Default.DictionaryStringString);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>Native AOT 下反射序列化不可用，字典走源生成。</summary>
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class CoreLocContext : JsonSerializerContext;
