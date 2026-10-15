using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Configuration;

namespace Erya.ViewModels.Settings;

/// <summary>
/// Editor for the frontend's <c>preset_color_schemes</c> collection. Colors
/// are RIME <c>0xBBGGRR</c> hex strings (the weasel/squirrel convention);
/// previews swap to RGB for display. Saving rewrites the whole collection
/// as one patch key.
/// </summary>
public sealed partial class ColorSchemeLibraryViewModel : ObservableObject
{
    private static readonly Regex HexPattern = new("^[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private readonly RimeConfigStore _store;
    private readonly string _targetFile;

    public ColorSchemeLibraryViewModel(RimeConfigStore store, string targetFile)
    {
        _store = store;
        _targetFile = targetFile;
    }

    public ObservableCollection<ColorSchemeViewModel> Schemes { get; } = [];

    /// <summary> Fires when any scheme edit/add/remove is pending a save.</summary>
    public event EventHandler? EditsPending;

    [ObservableProperty]
    private bool _hasPendingEdits;

    /// <summary>The collection itself is patched (not the shipped defaults).</summary>
    [ObservableProperty]
    private bool _isModified;

    public void Load()
    {
        foreach (var scheme in Schemes) scheme.PropertyChanged -= OnSchemeChanged;
        Schemes.Clear();

        var merged = _store.ReadMergedValue(_targetFile, "preset_color_schemes")
            as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>();
        foreach (var (name, definition) in merged)
        {
            var scheme = ColorSchemeViewModel.From(name, definition as IReadOnlyDictionary<string, object?>);
            scheme.Owner = this;
            scheme.PropertyChanged += OnSchemeChanged;
            Schemes.Add(scheme);
        }

        IsModified = _store.ReadPatchValues(_targetFile).ContainsKey("preset_color_schemes");
        HasPendingEdits = false;
    }

    public void Revert() => Load();

    /// <summary>The whole collection as one patch value; null when empty (removes the key).</summary>
    public object? PendingValue()
    {
        if (!HasPendingEdits) return null;
        var map = new Dictionary<string, object?>();
        foreach (var scheme in Schemes.Where(s => s.HasValidName))
            map[scheme.Name.Trim()] = scheme.ToPatchValue();
        return map.Count == 0 ? null : map;
    }

    [RelayCommand]
    private void AddScheme()
    {
        // Seed a new scheme from a sensible light default (or the first
        // existing one when the frontend ships its own baseline).
        var seed = Schemes.FirstOrDefault()?.CloneableState is { } existing
            ? existing with { Name = UniqueName(existing.Name) }
            : new SchemeState(
                UniqueName("custom"), "",
                "FFFFFF", "000000", "D3E3F6", "000000", "333333", "333333", "000000", "000000");
        var scheme = ColorSchemeViewModel.FromState(seed);
        scheme.Owner = this;
        scheme.PropertyChanged += OnSchemeChanged;
        Schemes.Add(scheme);
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveScheme(ColorSchemeViewModel? scheme)
    {
        if (scheme is null) return;
        scheme.PropertyChanged -= OnSchemeChanged;
        Schemes.Remove(scheme);
        MarkDirty();
    }

    private string UniqueName(string baseName)
    {
        var stem = Regex.Replace(baseName, @"\d+$", "");
        for (int i = 1; ; i++)
        {
            var candidate = $"{stem}{i}";
            if (Schemes.All(s => !string.Equals(s.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
    }

    private void OnSchemeChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

    private void MarkDirty()
    {
        HasPendingEdits = true;
        EditsPending?.Invoke(this, EventArgs.Empty);
    }

    internal static bool IsHexColor(string? text)
        => text is not null && HexPattern.IsMatch(text);
}

public sealed record SchemeState(
    string Name, string Author,
    string BackColor, string CandidateTextColor, string HilitedBackColor, string HilitedTextColor,
    string? CommentTextColor, string? HilitedCommentTextColor, string? TextColor, string? HilitedPreeditColor,
    string? FontFace = null, long FontPoint = 0, long CornerRadius = -1, long LineSpacing = -1);

public sealed partial class ColorSchemeViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _backColor = "";
    [ObservableProperty] private string _candidateTextColor = "";
    [ObservableProperty] private string _hilitedBackColor = "";
    [ObservableProperty] private string _hilitedTextColor = "";
    [ObservableProperty] private string _commentTextColor = "";
    [ObservableProperty] private string _hilitedCommentTextColor = "";
    [ObservableProperty] private string _textColor = "";
    [ObservableProperty] private string _hilitedPreeditColor = "";
    [ObservableProperty] private string _fontFace = "";
    [ObservableProperty] private string _fontPointText = "";
    [ObservableProperty] private string _cornerRadiusText = "";
    [ObservableProperty] private string _lineSpacingText = "";

    /// <summary>Set by the library so templates can bind the remove command directly.</summary>
    internal ColorSchemeLibraryViewModel? Owner { get; set; }

    /// <summary>Bind target inside the scheme card template.</summary>
    public System.Windows.Input.ICommand? RemoveCommand => Owner?.RemoveSchemeCommand;

    public bool HasValidName => Name.Trim().Length > 0;

    public SchemeState CloneableState => new(
        Name, Author,
        BackColor, CandidateTextColor, HilitedBackColor, HilitedTextColor,
        NullIfEmpty(CommentTextColor), NullIfEmpty(HilitedCommentTextColor),
        NullIfEmpty(TextColor), NullIfEmpty(HilitedPreeditColor),
        NullIfEmpty(FontFace), ParseLong(FontPointText) ?? 0,
        ParseLong(CornerRadiusText) ?? -1, ParseLong(LineSpacingText) ?? -1);

    public static ColorSchemeViewModel FromState(SchemeState state) => new()
    {
        Name = state.Name,
        Author = state.Author,
        BackColor = state.BackColor,
        CandidateTextColor = state.CandidateTextColor,
        HilitedBackColor = state.HilitedBackColor,
        HilitedTextColor = state.HilitedTextColor,
        CommentTextColor = state.CommentTextColor ?? "",
        HilitedCommentTextColor = state.HilitedCommentTextColor ?? "",
        TextColor = state.TextColor ?? "",
        HilitedPreeditColor = state.HilitedPreeditColor ?? "",
        FontFace = state.FontFace ?? "",
        FontPointText = state.FontPoint > 0 ? state.FontPoint.ToString() : "",
        CornerRadiusText = state.CornerRadius >= 0 ? state.CornerRadius.ToString() : "",
        LineSpacingText = state.LineSpacing >= 0 ? state.LineSpacing.ToString() : "",
    };

    public static ColorSchemeViewModel From(string name, IReadOnlyDictionary<string, object?>? definition)
    {
        string? Str(string key) => definition is not null && definition.TryGetValue(key, out var v) ? v?.ToString() : null;
        string Color(string key, string fallback) => Normalize(Str(key)) ?? fallback;
        return new ColorSchemeViewModel
        {
            Name = name,
            Author = Str("author") ?? "",
            BackColor = Color("back_color", "FFFFFF"),
            CandidateTextColor = Color("candidate_text_color", "000000"),
            HilitedBackColor = Color("hilited_candidate_back_color", "D3E3F6"),
            HilitedTextColor = Color("hilited_candidate_text_color", "000000"),
            CommentTextColor = Str("comment_text_color") ?? "",
            HilitedCommentTextColor = Str("hilited_comment_text_color") ?? "",
            TextColor = Str("text_color") ?? "",
            HilitedPreeditColor = Str("hilited_text_color") ?? "",
            FontFace = Str("font_face") ?? "",
            FontPointText = Str("font_point") ?? "",
            CornerRadiusText = Str("corner_radius") ?? "",
            LineSpacingText = Str("line_spacing") ?? "",
        };
    }

    /// <summary>Patch body for this scheme; colors without valid hex are skipped.</summary>
    public Dictionary<string, object?> ToPatchValue()
    {
        var map = new Dictionary<string, object?>
        {
            ["back_color"] = ColorValue(BackColor, "FFFFFF"),
            ["candidate_text_color"] = ColorValue(CandidateTextColor, "000000"),
            ["hilited_candidate_back_color"] = ColorValue(HilitedBackColor, "D3E3F6"),
            ["hilited_candidate_text_color"] = ColorValue(HilitedTextColor, "000000"),
        };
        if (Author.Trim().Length > 0) map["author"] = Author.Trim();
        if (ColorSchemeLibraryViewModel.IsHexColor(CommentTextColor)) map["comment_text_color"] = ColorValue(CommentTextColor, "");
        if (ColorSchemeLibraryViewModel.IsHexColor(HilitedCommentTextColor)) map["hilited_comment_text_color"] = ColorValue(HilitedCommentTextColor, "");
        if (ColorSchemeLibraryViewModel.IsHexColor(TextColor)) map["text_color"] = ColorValue(TextColor, "");
        if (ColorSchemeLibraryViewModel.IsHexColor(HilitedPreeditColor)) map["hilited_text_color"] = ColorValue(HilitedPreeditColor, "");
        if (FontFace.Trim().Length > 0) map["font_face"] = FontFace.Trim();
        if (ParseLong(FontPointText) is { } point) map["font_point"] = point;
        if (ParseLong(CornerRadiusText) is { } radius) map["corner_radius"] = radius;
        if (ParseLong(LineSpacingText) is { } spacing) map["line_spacing"] = spacing;
        return map;
    }

    // ---------- previews (BGR → RGB) ----------

    public IBrush BackPreview => BrushOf(BackColor, Colors.White);
    public IBrush CandidateTextPreview => BrushOf(CandidateTextColor, Colors.Black);
    public IBrush HilitedBackPreview => BrushOf(HilitedBackColor, Color.FromRgb(0xD3, 0xE3, 0xF6));
    public IBrush HilitedTextPreview => BrushOf(HilitedTextColor, Colors.Black);
    public IBrush CommentTextPreview => BrushOf(CommentTextColor, Color.FromRgb(0x33, 0x33, 0x33));

    partial void OnBackColorChanged(string value) => RefreshPreviews();
    partial void OnCandidateTextColorChanged(string value) => RefreshPreviews();
    partial void OnHilitedBackColorChanged(string value) => RefreshPreviews();
    partial void OnHilitedTextColorChanged(string value) => RefreshPreviews();
    partial void OnCommentTextColorChanged(string value) => RefreshPreviews();

    private void RefreshPreviews()
    {
        OnPropertyChanged(nameof(BackPreview));
        OnPropertyChanged(nameof(CandidateTextPreview));
        OnPropertyChanged(nameof(HilitedBackPreview));
        OnPropertyChanged(nameof(HilitedTextPreview));
        OnPropertyChanged(nameof(CommentTextPreview));
    }

    internal static string? Normalize(string? raw)
    {
        if (raw is null) return null;
        var hex = raw.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        else if (hex.StartsWith('#')) hex = hex[1..];
        return ColorSchemeLibraryViewModel.IsHexColor(hex) ? hex.ToUpperInvariant() : null;
    }

    private static object ColorValue(string hex, string fallback)
        => "0x" + (ColorSchemeLibraryViewModel.IsHexColor(hex) ? hex.ToUpperInvariant() : fallback);

    private static IBrush BrushOf(string hex, Color fallback)
    {
        var normalized = Normalize(hex);
        if (normalized is null) return new SolidColorBrush(fallback);
        // RIME writes 0xBBGGRR — swap to RGB for display.
        return new SolidColorBrush(Color.FromRgb(
            Convert.ToByte(normalized[4..6], 16),
            Convert.ToByte(normalized[2..4], 16),
            Convert.ToByte(normalized[0..2], 16)));
    }

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    private static long? ParseLong(string text)
        => long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
}
