using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace Erya.Services;

/// <summary>
/// System font family names for the font pickers (设置页 style/font_face and
/// the skin library's font_face). FontManager enumerates via DirectWrite /
/// CoreText / fontconfig, so the same call covers Windows, macOS and Linux.
/// Cached after the first read; empty under headless tests where no real
/// platform font manager exists.
/// </summary>
public static class SystemFontCatalog
{
    private static IReadOnlyList<string>? _names;

    public static IReadOnlyList<string> Names => _names ??= Enumerate();

    private static IReadOnlyList<string> Enumerate()
    {
        try
        {
            // IFontCollection itself is IReadOnlyList<FontFamily>.
            return FontManager.Current.SystemFonts
                .Select(f => f.Name.Trim())
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
