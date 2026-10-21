using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Configuration;
using Erya.Services;
using YamlDotNet.RepresentationModel;

namespace Erya.ViewModels.Settings;

/// <summary>
/// Editor for the frontend's <c>preset_color_schemes</c> collection. Colors
/// are RIME <c>0xBBGGRR</c> hex strings (the weasel/squirrel convention);
/// previews swap to RGB for display.
/// <para>
/// Two patch shapes, chosen per scheme. A scheme the frontend ships is edited
/// through per-field keys — <c>preset_color_schemes/&lt;id&gt;/back_color</c> —
/// which RIME merges into the shipped definition, so every field this editor
/// does not model (label_color, border_*, name…) survives untouched. A scheme
/// the user created lives only in the patch and is written whole as
/// <c>preset_color_schemes/&lt;id&gt;</c>. Writing the whole collection as one key
/// would REPLACE it (patch semantics) and wipe every built-in scheme, so a
/// legacy whole-collection key found in the file is migrated away on the
/// next save; likewise a whole-scheme key on a shipped scheme (what older
/// builds wrote, dropping the fields they did not know) is converted to
/// per-field keys the next time that scheme is saved.
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

    /// <summary>Flat patch keys present at load time — the only keys a removal can target.</summary>
    private HashSet<string> _patchKeys = new(StringComparer.Ordinal);

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

    /// <summary>
    /// 展开区的候选窗预览工厂（设置页注入：预览需要外观字段做排版参数）。
    /// 每个皮肤首次展开时惰性创建一个固定渲染自己的预览。
    /// </summary>
    internal Func<ColorSchemeViewModel, CandidatePreviewViewModel>? PreviewFactory { get; set; }

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

        var (blocks, fields) = PatchedSchemes();
        _patchKeys = _store.ReadPatch(_targetFile).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
        _hasLegacyCollectionKey = _patchKeys.Contains(CollectionKey);

        foreach (var (id, node) in shipped)
        {
            // RIME applies the patch over the shipped tree: a whole-scheme key
            // replaces the scheme, per-field keys then override single fields.
            bool hasBlock = blocks.Remove(id, out var block);
            bool hasFields = fields.Remove(id, out var overrides);
            var shippedDefinition = AsDefinition(node);
            var scheme = ColorSchemeViewModel.Create(
                id, Overlay(hasBlock ? AsDefinition(block) : shippedDefinition, overrides),
                isShipped: true, isPatched: hasBlock || hasFields, shippedDefinition);
            Attach(scheme);
        }
        foreach (var (id, node) in blocks) // user-created schemes live only in the patch
        {
            fields.Remove(id, out var overrides);
            Attach(ColorSchemeViewModel.Create(id, Overlay(AsDefinition(node), overrides), isShipped: false, isPatched: true));
        }
        foreach (var (id, overrides) in fields) // per-field keys with no definition anywhere else
            Attach(ColorSchemeViewModel.Create(id, Overlay(null, overrides), isShipped: false, isPatched: true));

        IsModified = Schemes.Any(s => s.IsPatched);
        HasPendingEdits = false;
    }

    /// <summary>
    /// Patch-defined scheme content keyed by id: whole-scheme nodes (flat
    /// <c>preset_color_schemes/&lt;id&gt;</c> entries plus members of a legacy
    /// whole-collection key) and per-field overrides
    /// (<c>preset_color_schemes/&lt;id&gt;/&lt;field&gt;</c>).
    /// </summary>
    private (Dictionary<string, YamlNode> Blocks, Dictionary<string, Dictionary<string, YamlNode>> Fields) PatchedSchemes()
    {
        var blocks = new Dictionary<string, YamlNode>();
        var fields = new Dictionary<string, Dictionary<string, YamlNode>>();
        foreach (var (key, value) in _store.ReadPatch(_targetFile))
        {
            if (key == CollectionKey && value is YamlMappingNode legacy)
            {
                foreach (var (id, node) in legacy.Children)
                    if (id is YamlScalarNode { Value: { } schemeId })
                        blocks[schemeId] = node;
                continue;
            }
            if (!key.StartsWith(CollectionKey + "/", StringComparison.Ordinal) || key.EndsWith("/+", StringComparison.Ordinal))
                continue;

            var path = key[(CollectionKey.Length + 1)..].Split('/');
            switch (path)
            {
                case [var id]:
                    blocks[id] = value;
                    break;
                case [var id, var field] when field.Length > 0:
                    (fields.TryGetValue(id, out var overrides) ? overrides : fields[id] = new())[field] = value;
                    break;
            }
        }
        return (blocks, fields);
    }

    private static IReadOnlyDictionary<string, object?>? AsDefinition(YamlNode? node)
        => PatchYaml.ToClrValue(node) as IReadOnlyDictionary<string, object?>;

    private static IReadOnlyDictionary<string, object?>? Overlay(
        IReadOnlyDictionary<string, object?>? definition, Dictionary<string, YamlNode>? overrides)
    {
        if (overrides is null) return definition;
        var merged = definition is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(definition);
        foreach (var (field, value) in overrides) merged[field] = PatchYaml.ToClrValue(value);
        return merged;
    }

    /// <summary>Flat patch keys to write on save, keyed by patch path.</summary>
    public IReadOnlyDictionary<string, object?> PendingWrites() => PendingChanges().Writes;

    /// <summary>Patch keys to remove on save: deleted schemes, renames, fields reverted to shipped, legacy keys.</summary>
    public IReadOnlyList<string> PendingRemovals() => PendingChanges().Removals;

    /// <summary>
    /// Writes and removals are derived together: converting a whole-scheme key
    /// into per-field keys is one write plus one removal, and they must agree.
    /// </summary>
    private (Dictionary<string, object?> Writes, List<string> Removals) PendingChanges()
    {
        var writes = new Dictionary<string, object?>();
        var removals = new List<string>();
        if (!HasPendingEdits) return (writes, removals);

        foreach (var (name, wasPatched) in _removed.Where(r => r.WasPatched))
            removals.AddRange(PatchKeysOf(name));

        foreach (var scheme in Schemes.Where(s => s.HasValidName))
        {
            var id = scheme.Name.Trim();
            bool renamed = scheme.OriginalName.Length > 0 && scheme.OriginalName != id;
            // Migrating off the legacy whole-collection key: every patched scheme
            // must be re-written on its own or it would vanish with the old key.
            bool mustCarry = _hasLegacyCollectionKey && (scheme.IsPatched || scheme.IsAdded);
            if (!scheme.IsDirty && !mustCarry) continue;

            if (renamed && scheme.IsPatched)
                removals.AddRange(PatchKeysOf(scheme.OriginalName));

            if (scheme.IsShipped && !renamed)
                AppendFieldChanges(scheme, id, writes, removals);
            else
                writes[$"{CollectionKey}/{id}"] = scheme.ToPatchValue(); // renaming a shipped scheme copies it under the new id
        }

        if (_hasLegacyCollectionKey && (writes.Count > 0 || removals.Count > 0))
            removals.Add(CollectionKey);
        return (writes, removals);
    }

    /// <summary>
    /// Per-field keys for a shipped scheme, diffed against what the frontend
    /// ships: a field equal to the shipped value needs no key (and loses one
    /// it had), a cleared field falls back to the shipped value the same way.
    /// A whole-scheme key found on the scheme is dropped — it would mask the
    /// per-field keys, which the patch applies before it — and whatever the
    /// patch set that this editor does not model is carried over as
    /// per-field keys so converting loses nothing the user had.
    /// </summary>
    private void AppendFieldChanges(ColorSchemeViewModel scheme, string id, Dictionary<string, object?> writes, List<string> removals)
    {
        var block = $"{CollectionKey}/{id}";
        var shipped = scheme.ShippedEntries();
        foreach (var (field, value) in scheme.ModelEntries())
        {
            var key = $"{block}/{field}";
            shipped.TryGetValue(field, out var baseline);
            if (value is null || Equals(value, baseline))
            {
                if (_patchKeys.Contains(key)) removals.Add(key);
            }
            else
            {
                writes[key] = value;
            }
        }

        if (_patchKeys.Contains(block)) removals.Add(block);
        if (!scheme.IsPatched) return;
        foreach (var (field, value) in scheme.UnmodeledEntriesDifferingFromShipped())
            writes[$"{block}/{field}"] = value;
    }

    /// <summary>Every patch key that defines part of the scheme: the whole-scheme key and its per-field keys.</summary>
    private IEnumerable<string> PatchKeysOf(string id)
    {
        var block = $"{CollectionKey}/{id}";
        yield return block;
        foreach (var key in _patchKeys.Where(k => k.StartsWith(block + "/", StringComparison.Ordinal)))
            yield return key;
    }

    /// <summary>以此皮肤为底新增一套自定义皮肤（内置皮肤只读，派生后可编辑）。</summary>
    [RelayCommand]
    private void DuplicateScheme(ColorSchemeViewModel? scheme)
    {
        if (scheme is null) return;
        AddFromState(scheme.CloneableState with { Name = UniqueName(scheme.Name) });
    }

    private void AddFromState(SchemeState state)
    {
        var scheme = ColorSchemeViewModel.FromState(state);
        scheme.IsAdded = true;
        Attach(scheme);
        MarkDirty();
    }

    /// <summary>
    /// 内置皮肤在编辑对话框里的「另存为」：编辑结果（当前控件值）存成新的自定义
    /// 皮肤（唯一名），原内置皮肤还原开框快照——内置定义永远不被写。
    /// </summary>
    public ColorSchemeViewModel SaveEditedAsCustom(ColorSchemeViewModel scheme, SchemeState snapshot)
    {
        var edited = scheme.CloneableState;
        scheme.Restore(snapshot);
        var copy = edited with { Name = UniqueName(edited.Name) };
        var custom = ColorSchemeViewModel.FromState(copy);
        custom.IsAdded = true;
        Attach(custom);
        MarkDirty();
        return custom;
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

    /// <summary>以此皮肤为底新增自定义皮肤。</summary>
    public System.Windows.Input.ICommand? DuplicateCommand => Owner?.DuplicateSchemeCommand;

    /// <summary>行说明：内置皮肤 vs 自定义皮肤。</summary>
    public string KindText => IsShipped ? "内置皮肤" : "自定义皮肤";

    private CandidatePreviewViewModel? _detailPreview;

    /// <summary>
    /// 展开区的候选窗预览：首次展开时惰性创建，固定渲染本皮肤
    /// （排版参数取外观字段），编辑颜色/字号立即反映。
    /// </summary>
    public CandidatePreviewViewModel? DetailPreview => _detailPreview ??= Owner?.PreviewFactory?.Invoke(this);

    public bool HasValidName => Name.Trim().Length > 0;

    /// <summary>Dropdown source for the font_face editor — this machine's font families.</summary>
    public IReadOnlyList<string> Fonts => SystemFontCatalog.Names;

    public string FontUnsetLabel => "未设置";

    private IReadOnlyList<string>? _fontOptions;

    /// <summary>
    /// Built lazily (first binding read) and kept stable afterwards: swapping
    /// the ComboBox's ItemsSource on selection would clear its selection and
    /// write null back — the same regression FontFieldViewModel guards against.
    /// </summary>
    public IReadOnlyList<string> FontOptions => _fontOptions ??= BuildFontOptions();

    /// <summary>Bind target for the font combo — maps 未设置 to/from an empty font_face.</summary>
    public string SelectedFontFace
    {
        get => FontFace.Trim().Length == 0 ? FontUnsetLabel : FontFace.Trim();
        // null-tolerant on purpose: a ComboBox whose items reset writes null.
        set => FontFace = string.IsNullOrEmpty(value) || value == FontUnsetLabel ? "" : value;
    }

    partial void OnFontFaceChanged(string value) => OnPropertyChanged(nameof(SelectedFontFace));

    private IReadOnlyList<string> BuildFontOptions()
    {
        var fonts = Fonts;
        var current = FontFace.Trim();
        return current.Length > 0 && !fonts.Contains(current)
            ? [FontUnsetLabel, current, .. fonts]
            : [FontUnsetLabel, .. fonts];
    }

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

    /// <summary>把快照写回当前实例（编辑对话框「取消」/内置皮肤「另存为」用），不换实例、不断绑定。</summary>
    public void Restore(SchemeState state)
    {
        Name = state.Name;
        Author = state.Author;
        BackColor = state.BackColor;
        CandidateTextColor = state.CandidateTextColor;
        HilitedBackColor = state.HilitedBackColor;
        HilitedTextColor = state.HilitedTextColor;
        CommentTextColor = state.CommentTextColor ?? "";
        HilitedCommentTextColor = state.HilitedCommentTextColor ?? "";
        TextColor = state.TextColor ?? "";
        HilitedPreeditColor = state.HilitedPreeditColor ?? "";
        FontFace = state.FontFace ?? "";
        FontPointText = state.FontPoint > 0 ? state.FontPoint.ToString() : "";
        CornerRadiusText = state.CornerRadius >= 0 ? state.CornerRadius.ToString() : "";
        LineSpacingText = state.LineSpacing >= 0 ? state.LineSpacing.ToString() : "";
    }

    public static ColorSchemeViewModel Create(
        string name, IReadOnlyDictionary<string, object?>? definition, bool isShipped, bool isPatched,
        IReadOnlyDictionary<string, object?>? shippedDefinition = null)
    {
        string? Str(string key) => definition is not null && definition.TryGetValue(key, out var v) ? v?.ToString() : null;
        string Color(string key)
            => Normalize(definition is not null && definition.TryGetValue(key, out var v) ? ColorText(v) : null) ?? CoreColorFallbacks[key];
        var vm = new ColorSchemeViewModel
        {
            Name = name,
            OriginalName = name,
            IsShipped = isShipped,
            IsPatched = isPatched,
            Definition = definition,
            ShippedDefinition = shippedDefinition,
            Author = Str("author") ?? "",
            BackColor = Color("back_color"),
            CandidateTextColor = Color("candidate_text_color"),
            HilitedBackColor = Color("hilited_candidate_back_color"),
            HilitedTextColor = Color("hilited_candidate_text_color"),
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

    /// <summary>The scheme as loaded — shipped definition with any patch applied; null for a scheme added this session.</summary>
    public IReadOnlyDictionary<string, object?>? Definition { get; private init; }

    /// <summary>What the frontend ships for this id, untouched by the patch; null for user-created schemes.</summary>
    public IReadOnlyDictionary<string, object?>? ShippedDefinition { get; private init; }

    /// <summary>
    /// Any field of the loaded definition, modeled or not — the候选窗预览
    /// reads border_color / shadow_color / label_color through this.
    /// </summary>
    public object? RawEntry(string key)
        => Definition is not null && Definition.TryGetValue(key, out var value) ? value : null;

    private static readonly string[] ColorKeys =
    [
        "back_color", "candidate_text_color", "hilited_candidate_back_color", "hilited_candidate_text_color",
        "comment_text_color", "hilited_comment_text_color", "text_color", "hilited_text_color",
    ];

    private static readonly string[] TextKeys = ["author", "font_face"];

    private static readonly string[] NumberKeys = ["font_point", "corner_radius", "line_spacing"];

    /// <summary>Every YAML key this editor has a control for.</summary>
    private static readonly HashSet<string> ModeledKeys = [.. ColorKeys, .. TextKeys, .. NumberKeys];

    /// <summary>
    /// What the controls show for the four base colors when a scheme leaves
    /// them out. Shared by loading, the diff baseline and whole-scheme writes:
    /// if the baseline did not fill them in exactly like loading does, every
    /// save would write these fallbacks back as if the user had picked them.
    /// </summary>
    private static readonly Dictionary<string, string> CoreColorFallbacks = new()
    {
        ["back_color"] = "FFFFFF",
        ["candidate_text_color"] = "000000",
        ["hilited_candidate_back_color"] = "D3E3F6",
        ["hilited_candidate_text_color"] = "000000",
    };

    /// <summary>
    /// The modeled fields as the controls hold them, in YAML-ready form
    /// (colors <c>0xBBGGRR</c>, numbers as long); null marks a field left
    /// empty or holding invalid input.
    /// </summary>
    internal Dictionary<string, object?> ModelEntries() => EntriesOf(new Dictionary<string, object?>
    {
        ["back_color"] = BackColor,
        ["candidate_text_color"] = CandidateTextColor,
        ["hilited_candidate_back_color"] = HilitedBackColor,
        ["hilited_candidate_text_color"] = HilitedTextColor,
        ["comment_text_color"] = CommentTextColor,
        ["hilited_comment_text_color"] = HilitedCommentTextColor,
        ["text_color"] = TextColor,
        ["hilited_text_color"] = HilitedPreeditColor,
        ["author"] = Author,
        ["font_face"] = FontFace,
        ["font_point"] = FontPointText,
        ["corner_radius"] = CornerRadiusText,
        ["line_spacing"] = LineSpacingText,
    });

    /// <summary>
    /// The modeled fields of the shipped definition as the controls would show
    /// them — base colors the scheme leaves out filled in like loading does —
    /// so a diff against <see cref="ModelEntries"/> only reports real edits.
    /// </summary>
    internal Dictionary<string, object?> ShippedEntries()
    {
        var entries = EntriesOf(ShippedDefinition);
        foreach (var (key, fallback) in CoreColorFallbacks) entries[key] ??= "0x" + fallback;
        return entries;
    }

    private static Dictionary<string, object?> EntriesOf(IReadOnlyDictionary<string, object?>? definition)
    {
        object? Raw(string key) => definition is not null && definition.TryGetValue(key, out var v) ? v : null;
        var entries = new Dictionary<string, object?>();
        foreach (var key in ColorKeys)
            entries[key] = Normalize(ColorText(Raw(key))) is { } hex ? "0x" + hex : null;
        foreach (var key in TextKeys)
            entries[key] = Raw(key)?.ToString()?.Trim() is { Length: > 0 } text ? text : null;
        foreach (var key in NumberKeys)
            entries[key] = Raw(key) switch
            {
                long l => l,
                double d when d % 1 == 0 => (long)d,
                string s => ParseLong(s),
                _ => null,
            };
        return entries;
    }

    /// <summary>
    /// Fields of the loaded definition this editor has no control for, where
    /// the value differs from the shipped one — carried into per-field keys
    /// when a whole-scheme key is converted, so nothing the user set is lost.
    /// </summary>
    internal IEnumerable<KeyValuePair<string, object?>> UnmodeledEntriesDifferingFromShipped()
    {
        if (Definition is null) yield break;
        foreach (var (key, value) in Definition)
        {
            if (value is null || ModeledKeys.Contains(key)) continue;
            if (ShippedDefinition is { } shipped && shipped.TryGetValue(key, out var baseline) && Equals(value, baseline)) continue;
            yield return new(key, value);
        }
    }

    /// <summary>
    /// Whole-scheme patch body: the loaded definition (so a copy keeps fields
    /// this editor does not model) with every modeled field replaced by its
    /// control value — cleared fields drop out, and the four base colors always
    /// get a value because a scheme without them renders unpredictably.
    /// </summary>
    public Dictionary<string, object?> ToPatchValue()
    {
        var map = Definition is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(Definition);
        foreach (var (key, value) in ModelEntries())
        {
            if (value is null) map.Remove(key);
            else map[key] = value;
        }
        foreach (var (key, fallback) in CoreColorFallbacks) map.TryAdd(key, "0x" + fallback);
        // A user-created scheme has no shipped definition to carry the display
        // name — without this key the frontend would show the raw id.
        map.TryAdd("name", Name.Trim());
        return map;
    }

    /// <summary>复制到剪贴板的补丁片段：整皮肤定义，颜色保持 0xBBGGRR 原样。</summary>
    [RelayCommand]
    private async Task CopyYaml()
    {
        var yaml = BuildYamlSnippet();
        if (AppNotifications.ClipboardOrNull() is { } clipboard)
            await clipboard.SetTextAsync(yaml);
        AppNotifications.Toast($"已复制皮肤「{Name.Trim()}」的 YAML 定义", NotificationType.Success);
    }

    internal string BuildYamlSnippet()
    {
        var id = HasValidName ? Name.Trim() : OriginalName;
        var map = ToPatchValue();
        var sb = new StringBuilder("preset_color_schemes:\n  ").Append(Quote(id)).Append(":\n");
        foreach (var key in SnippetOrder.Where(map.ContainsKey))
            AppendEntry(sb, key, map[key]);
        foreach (var (key, value) in map.Where(kv => !SnippetOrder.Contains(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            AppendEntry(sb, key, value);
        return sb.ToString();
    }

    private static readonly string[] SnippetOrder =
    [
        "name", "author",
        "back_color", "candidate_text_color",
        "hilited_candidate_back_color", "hilited_candidate_text_color",
        "comment_text_color", "hilited_comment_text_color", "text_color", "hilited_text_color",
        "label_color", "border_color", "shadow_color",
        "font_face", "font_point", "corner_radius", "line_spacing",
    ];

    private static void AppendEntry(StringBuilder sb, string key, object? value)
    {
        if (value is null) return;
        sb.Append("    ").Append(key).Append(": ")
          .Append(value is long or double or bool ? value.ToString() : Quote(value.ToString()!))
          .Append('\n');
    }

    private static string Quote(string text)
        => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

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

    /// <summary>
    /// A color as read from YAML. A plain <c>0xFFFFFF</c> survives as text, but
    /// other tools write the same color as the integer 16777215, which the
    /// parser hands over as a long — render that back to hex digits.
    /// </summary>
    private static string? ColorText(object? raw) => raw switch
    {
        long value and >= 0 and <= 0xFFFFFF => value.ToString("X6", CultureInfo.InvariantCulture),
        _ => raw?.ToString(),
    };

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
