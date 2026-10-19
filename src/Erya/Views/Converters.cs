using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;

namespace Erya.Views;

/// <summary>皮肤库 / 快捷键行与专用控件之间的值转换。</summary>
public static class Converters
{
    public static readonly RimeColorConverter RimeColor = new();
    public static readonly RimeHotkeyConverter RimeHotkey = new();
    public static readonly FieldIconConverter FieldIcon = new();
}

/// <summary>
/// schema 字段的 icon 声明（string）→ 图标实例：
/// "td:Name" → Irihi.Iconica.TDesign.Icons.Name（20×20，继承前景色）；
/// "ip:Name" → Irihi.Iconica.IconPark.Icons.Name（Line 单色模式见 App.axaml 样式）；
/// "semi:SemiIconX" → 应用资源里的 Geometry（Cell 模板会以 PathIcon 呈现）。
/// 无法解析时返回 null（不显示图标，行内仍占位保持对齐）。
/// </summary>
public sealed class FieldIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string spec) return null;
        var sep = spec.IndexOf(':');
        if (sep <= 0 || sep == spec.Length - 1) return null;
        var prefix = spec[..sep];
        var name = spec[(sep + 1)..];
        return prefix switch
        {
            "td" => CreateIcon("Irihi.Iconica.TDesign", name),
            "ip" => CreateIcon("Irihi.Iconica.IconPark", name),
            "semi" => Application.Current is { } app
                      && app.TryGetResource(name, app.ActualThemeVariant, out var resource)
                      && resource is Geometry
                ? resource
                : null,
            _ => null,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;

    private static Control? CreateIcon(string namespacePrefix, string name)
    {
        var type = Type.GetType($"{namespacePrefix}.Icons.{name}, {namespacePrefix}");
        if (type is null) return null;
        var icon = (Control?)Activator.CreateInstance(type);
        if (icon is not null)
        {
            icon.Width = 20;
            icon.Height = 20;
        }
        return icon;
    }
}

/// <summary>
/// RIME 0xBBGGRR 十六进制颜色字符串 ↔ Avalonia Color（皮肤库 ColorPicker 绑定用）。
/// ConverterParameter 为 RRGGBB 形式的兜底色（当前值缺失/非法时取色器显示的颜色）。
/// </summary>
public sealed class RimeColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fallback = ParseRgb(parameter?.ToString()) ?? Colors.White;
        var bgr = Normalize(value?.ToString());
        return bgr is null
            ? fallback
            : Color.FromRgb(
                Byte(bgr[4..6]),
                Byte(bgr[2..4]),
                Byte(bgr[0..2]));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Color c ? $"{c.B:X2}{c.G:X2}{c.R:X2}" : BindingOperations.DoNothing;

    private static Color? ParseRgb(string? hex)
    {
        var normalized = Normalize(hex);
        return normalized is null ? null : Color.FromRgb(
            Byte(normalized[0..2]),
            Byte(normalized[2..4]),
            Byte(normalized[4..6]));
    }

    private static byte Byte(string hex)
        => byte.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>容忍 0x / # 前缀，返回 6 位大写 BBGGRR；非法返回 null。</summary>
    private static string? Normalize(string? raw)
    {
        if (raw is null) return null;
        var hex = raw.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        else if (hex.StartsWith('#')) hex = hex[1..];
        return hex.Length == 6 && IsHex(hex) ? hex.ToUpperInvariant() : null;
    }

    private static bool IsHex(string text)
    {
        foreach (var ch in text)
            if (!Uri.IsHexDigit(ch)) return false;
        return true;
    }
}

/// <summary>
/// RIME 快捷键字符串（如 "Control+grave"、"F4"）↔ Avalonia KeyGesture
/// （KeyGestureInput 绑定用）。无法识别的值显示为空且不会被覆盖；
/// 录入新组合时写回 RIME 命名（Control/Shift/Alt/Super + 键名）。
/// </summary>
public sealed class RimeHotkeyConverter : IValueConverter
{
    private static readonly Dictionary<string, Key> NameToKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = Key.Space,
        ["grave"] = Key.OemTilde,           // macOS 反引键；Windows 布局同键报告为 Oem3
        ["comma"] = Key.OemComma,
        ["period"] = Key.OemPeriod,
        ["semicolon"] = Key.OemSemicolon,
        ["apostrophe"] = Key.Oem7,
        ["slash"] = Key.Oem2,
        ["bracketleft"] = Key.OemOpenBrackets,
        ["bracketright"] = Key.OemCloseBrackets,
        ["minus"] = Key.OemMinus,
        ["equal"] = Key.OemPlus,
        ["backslash"] = Key.Oem5,
        ["tab"] = Key.Tab,
        ["escape"] = Key.Escape,
        ["return"] = Key.Return,
        ["enter"] = Key.Return,
        ["backspace"] = Key.Back,
        ["delete"] = Key.Delete,
        ["insert"] = Key.Insert,
        ["home"] = Key.Home,
        ["end"] = Key.End,
        ["page_up"] = Key.PageUp,
        ["pageup"] = Key.PageUp,
        ["page_down"] = Key.PageDown,
        ["pagedown"] = Key.PageDown,
        ["up"] = Key.Up,
        ["down"] = Key.Down,
        ["left"] = Key.Left,
        ["right"] = Key.Right,
    };

    // 反向表额外收录各平台同物理键的其它报告值，写回时统一用 RIME 命名。
    private static readonly Dictionary<Key, string> KeyToName = new()
    {
        [Key.OemTilde] = "grave",
        [Key.Oem3] = "grave",
        [Key.OemComma] = "comma",
        [Key.OemPeriod] = "period",
        [Key.OemSemicolon] = "semicolon",
        [Key.Oem1] = "semicolon",
        [Key.Oem7] = "apostrophe",
        [Key.Oem2] = "slash",
        [Key.OemQuestion] = "slash",
        [Key.OemOpenBrackets] = "bracketleft",
        [Key.Oem4] = "bracketleft",
        [Key.OemCloseBrackets] = "bracketright",
        [Key.Oem6] = "bracketright",
        [Key.OemMinus] = "minus",
        [Key.OemPlus] = "equal",
        [Key.Oem5] = "backslash",
        [Key.OemPipe] = "backslash",
        [Key.Space] = "space",
        [Key.Tab] = "Tab",
        [Key.Escape] = "Escape",
        [Key.Return] = "Return",
        [Key.Back] = "BackSpace",
        [Key.Delete] = "Delete",
        [Key.Insert] = "Insert",
        [Key.Home] = "Home",
        [Key.End] = "End",
        [Key.PageUp] = "Page_Up",
        [Key.PageDown] = "Page_Down",
        [Key.Up] = "Up",
        [Key.Down] = "Down",
        [Key.Left] = "Left",
        [Key.Right] = "Right",
    };

    static RimeHotkeyConverter()
    {
        // X11 键名：数字/字母小写、F 键与小键盘保持 RIME 写法。
        for (var i = 0; i < 10; i++)
        {
            KeyToName[Key.D0 + i] = i.ToString();
            KeyToName[Key.NumPad0 + i] = $"KP_{i}";
        }
        for (var letter = 'A'; letter <= 'Z'; letter++)
            KeyToName[Key.A + (letter - 'A')] = letter.ToString().ToLowerInvariant();
        for (var i = 1; i <= 24; i++)
            KeyToName[Key.F1 + i - 1] = $"F{i}";
    }

    private static readonly Dictionary<string, KeyModifiers> ModifierNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["control"] = KeyModifiers.Control,
            ["ctrl"] = KeyModifiers.Control,
            ["shift"] = KeyModifiers.Shift,
            ["alt"] = KeyModifiers.Alt,
            ["option"] = KeyModifiers.Alt,
            ["super"] = KeyModifiers.Meta,
            ["meta"] = KeyModifiers.Meta,
            ["cmd"] = KeyModifiers.Meta,
            ["command"] = KeyModifiers.Meta,
        };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || text.Trim().Length == 0) return null;

        var modifiers = KeyModifiers.None;
        Key? key = null;
        foreach (var part in text.Split('+'))
        {
            var token = part.Trim();
            if (token.Length == 0) return null;
            if (ModifierNames.TryGetValue(token, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }
            if (key is not null) return null; // 出现第二个非修饰键
            key = ParseKey(token);
            if (key is null) return null;
        }
        return key is { } k ? new KeyGesture(k, modifiers) : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not KeyGesture gesture) return BindingOperations.DoNothing;
        var name = KeyToName.TryGetValue(gesture.Key, out var mapped)
            ? mapped
            : gesture.Key.ToString().ToLowerInvariant();
        return gesture.KeyModifiers == KeyModifiers.None
            ? name
            : $"{ModifiersPrefix(gesture.KeyModifiers)}+{name}";
    }

    private static string ModifiersPrefix(KeyModifiers modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(KeyModifiers.Control)) parts.Add("Control");
        if (modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Super");
        return string.Join("+", parts);
    }

    private static Key? ParseKey(string token) =>
        NameToKey.TryGetValue(token, out var named) ? named
        : token.Length == 1 && char.IsDigit(token[0]) ? Key.D0 + (token[0] - '0')
        : token.Length == 1 && char.IsLetter(token[0]) && Enum.TryParse<Key>(token.ToUpperInvariant(), out var letter) ? letter
        : Enum.TryParse<Key>(token, ignoreCase: true, out var parsed) ? parsed
        : null;
}
