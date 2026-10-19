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
using YamlDotNet.RepresentationModel;

namespace Erya.ViewModels.Settings;

/// <summary>
/// Editor for the frontend's <c>preset_color_schemes</c> collection. Colors
/// are RIME <c>0xBBGGRR</c> hex strings (the weasel/squirrel convention);
/// previews swap to RGB for display.
/// <para>
/// Saving writes one flat <c>preset_color_schemes/&lt;id&gt;</c> patch key per
/// created/edited scheme — RIME merges those into the shipped collection.
/// Writing the whole collection as one key would REPLACE it (patch semantics)
/// and wipe every built-in scheme, so a legacy whole-collection key found in
/// the file is migrated away on the next save.
/// </para>
/// </summary>
public sealed partial class ColorSchemeLibraryViewModel : ObservableObject
{
    private const string CollectionKey = "preset_color_schemes";
    private static readonly Regex HexPattern = new("^[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
    private readonly RimeConfigStore _store;
    private readonly string _targetFile;

    /// <summary>Schemes deleted in this session: (patch key, was actually patched).</summary>
    private readonly List<(string Name, bool WasPatched)> _removed = [];

    /// <summary>True while the file still carries the legacy whole-collection key.</summary>
    private bool _hasLegacyCollectionKey;

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

    /// <summary>At least one scheme carries a patch entry (not the shipped defaults).</summary>
    [ObservableProperty]
    private bool _isModified;

    public void Load()
    {
        foreach (var scheme in Schemes) scheme.PropertyChanged -= OnSchemeChanged;
        Schemes.Clear();
        _removed.Clear();

        var shipped = _store.LoadBase(_targetFile) is { } baseRoot
            && MergedConfig.TryGetByPath(baseRoot, CollectionKey) is YamlMappingNode baseMap
                ? baseMap.Children
                    .Where(kv => kv.Key is YamlScalarNode { Value: { } id })
                    .ToDictionary(kv => ((YamlScalarNode)kv.Key).Value!, kv => kv.Value)
                : new Dictionary<string, YamlNode>();

        var patched = PatchedSchemes();
        _hasLegacyCollectionKey = _store.ReadPatchValues(_targetFile).ContainsKey(CollectionKey);

        foreach (var (id, node) in shipped)
        {
            var isPatched = patched.Remove(id, out var patchedNode);
            var scheme = ColorSchemeViewModel.Create(
                id, PatchYaml.ToClrValue(isPatched ? patchedNode : node) as IReadOnlyDictionary<string, object?>,
                isShipped: true, isPatched: isPatched);
            Attach(scheme);
        }
        foreach (var (id, node) in patched) // user-created schemes live only in the patch
        {
            var scheme = ColorSchemeViewModel.Create(
                id, PatchYaml.ToClrValue(node) as IReadOnlyDictionary<string, object?>,
                isShipped: false, isPatched: true);
            Attach(scheme);
        }

        IsModified = Schemes.Any(s => s.IsPatched);
        HasPendingEdits = false;
    }

    /// <summary>
    /// Patch-defined schemes keyed by id: flat <c>preset_color_schemes/&lt;id&gt;</c>
    /// entries plus members of a legacy whole-collection key.
    /// </summary>
    private Dictionary<string, YamlNode> PatchedSchemes()
    {
        var result = new Dictionary<string, YamlNode>();
        foreach (var (key, value) in _store.ReadPatch(_targetFile))
        {
            if (key == CollectionKey && value is YamlMappingNode legacy)
            {
                foreach (var (id, node) in legacy.Children)
                    if (id is YamlScalarNode { Value: { } schemeId })
                        result[schemeId] = node;
            }
            else if (key.StartsWith(CollectionKey + "/", StringComparison.Ordinal)
                     && key.AsSpan(CollectionKey.Length + 1).IndexOf('/') < 0)
            {
                result[key[(CollectionKey.Length + 1)..]] = value;
            }
        }
        return result;
    }

    /// <summary>Flat patch keys to write on save (per scheme), keyed by patch path.</summary>
    public IReadOnlyDictionary<string, object?> PendingWrites()
    {
        if (!HasPendingEdits) return new Dictionary<string, object?>();

        var writes = new Dictionary<string, object?>();
        // Migrating off the legacy whole-collection key: every patched scheme
        // must be re-written per-scheme or it would vanish with the old key.
        foreach (var scheme in Schemes.Where(s => s.HasValidName))
        {
            bool dirty = scheme.IsDirty;
            bool mustCarry = _hasLegacyCollectionKey && (scheme.IsPatched || scheme.IsAdded);
            if (dirty || mustCarry)
                writes[$"{CollectionKey}/{scheme.Name.Trim()}"] = scheme.ToPatchValue();
        }
        return writes;
    }

    /// <summary>Patch keys to remove on save: deleted schemes, renames, legacy key.</summary>
    public IReadOnlyList<string> PendingRemovals()
    {
        if (!HasPendingEdits) return [];
        var removals = new List<string>();
        foreach (var (name, wasPatched) in _removed.Where(r => r.WasPatched))
            removals.Add($"{CollectionKey}/{name}");
        foreach (var scheme in Schemes.Where(s => s.HasValidName && s.OriginalName != s.Name.Trim() && s.IsPatched))
            removals.Add($"{CollectionKey}/{scheme.OriginalName}");
        if (_hasLegacyCollectionKey && (PendingWrites().Count > 0 || removals.Count > 0))
            removals.Add(CollectionKey);
        return removals;
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
        scheme.IsAdded = true;
        Attach(scheme);
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveScheme(ColorSchemeViewModel? scheme)
    {
        if (scheme is null || scheme.IsShipped) return;
        _removed.Add((scheme.OriginalName, scheme.IsPatched));
        scheme.PropertyChanged -= OnSchemeChanged;
        Schemes.Remove(scheme);
        MarkDirty();
    }

    private void Attach(ColorSchemeViewModel scheme)
    {
        scheme.Owner = this;
        scheme.PropertyChanged += OnSchemeChanged;
        Schemes.Add(scheme);
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

    /// <summary>Defined by the frontend's shipped yaml — patching may override it, deleting may not.</summary>
    public bool IsShipped { get; private init; }

    /// <summary>Carries patch entries (badge + included in legacy migration).</summary>
    public bool IsPatched { get; private init; }

    /// <summary>Created this collection (or a former session) via 新增皮肤 — never shipped.</summary>
    public bool IsAdded { get; internal set; }

    /// <summary>Patch key the scheme was loaded under; differs from <see cref="Name"/> after a rename.</summary>
    public string OriginalName { get; private init; } = "";

    /// <summary>Only non-shipped schemes can be deleted; shipped ones live in the base file.</summary>
    public bool CanRemove => !IsShipped;

    /// <summary>Any value differs from what loading produced (renames included).</summary>
    public bool IsDirty => OriginalName.Length == 0
                           || !string.Equals(OriginalName, Name.Trim(), StringComparison.Ordinal)
                           || CloneableState != _loadedState;
    private SchemeState? _loadedState;

    /// <summary>Set by the library so templates can bind the remove command directly.</summary>
    internal ColorSchemeLibraryViewModel? Owner { get; set; }

    /// <summary>Bind target inside the scheme card template.</summary>
    public System.Windows.Input.ICommand? RemoveCommand => Owner?.RemoveSchemeCommand;

    public bool HasValidName => Name.Trim().Length > 0;

    public SchemeState CloneableState => new(
        Name.Trim(), Author,
        BackColor, CandidateTextColor, HilitedBackColor, HilitedTextColor,
        NullIfEmpty(CommentTextColor), NullIfEmpty(HilitedCommentTextColor),
        NullIfEmpty(TextColor), NullIfEmpty(HilitedPreeditColor),
        FontFace, ParseLong(FontPointText) ?? 0,
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

    public static ColorSchemeViewModel Create(
        string name, IReadOnlyDictionary<string, object?>? definition, bool isShipped, bool isPatched)
    {
        string? Str(string key) => definition is not null && definition.TryGetValue(key, out var v) ? v?.ToString() : null;
        string Color(string key, string fallback) => Normalize(Str(key)) ?? fallback;
        var vm = new ColorSchemeViewModel
        {
            Name = name,
            OriginalName = name,
            IsShipped = isShipped,
            IsPatched = isPatched,
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
        vm._loadedState = vm.CloneableState;
        return vm;
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
