using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Erya.ViewModels.Settings;

/// <summary>
/// 候选窗预览的一行候选：携带渲染所需的全部值（颜色/字体/圆角/内边距），
/// 模板里直接绑定，不需要回查上级 DataContext。
/// </summary>
public sealed record CandidatePreviewItem(
    string Label, string Text, string? Comment,
    IBrush RowBack, CornerRadius RowCorner, Thickness RowPadding,
    IBrush LabelBrush, FontFamily? LabelFont, double LabelSize, Thickness LabelMargin,
    IBrush TextBrush, FontFamily? TextFont, double TextSize,
    IBrush CommentBrush, FontFamily? CommentFont, double CommentSize, Thickness CommentMargin, bool HasComment);

/// <summary>
/// 候选窗实时预览：把外观页字段（字体/间距/圆角/排列/阴影…）与皮肤库当前
/// 选中皮肤的颜色合成一个小狼毫/鼠须管样子的候选窗。任一字段或皮肤变化都
/// 整体重算（属性少，整刷最稳）；未保存的编辑通过 <see
/// cref="SettingsFieldViewModel.CurrentValue"/> 立即反映。排版键在小狼毫
/// （style/layout/*）与鼠须管（style/*）两套路径里取第一个存在的，同一份
/// 预览逻辑通吃两个前端；竖排文本为近似预览（逐行竖排）。
/// </summary>
public sealed partial class CandidatePreviewViewModel : ObservableObject
{
    private static readonly (string Text, string? Comment)[] Sample =
    [
        ("你好", "ní hǎo"),
        ("拟好", null),
        ("尼奥", null),
    ];

    private readonly Dictionary<string, SettingsFieldViewModel> _fields = new(StringComparer.Ordinal);
    private ColorSchemeLibraryViewModel? _library;
    private ColorSchemeViewModel? _watchedScheme;

    /// <summary>把预览挂到一组字段与皮肤库上；每次 Load 后重新调用，旧的订阅自动解绑。</summary>
    public void Attach(IEnumerable<SettingsFieldViewModel> fields, ColorSchemeLibraryViewModel library)
    {
        Detach();
        _library = library;
        _fields.Clear();
        foreach (var field in fields)
        {
            _fields[field.KeyPath] = field;
            field.PropertyChanged += OnAnythingChanged;
        }
        library.EditsPending += OnLibraryEdited;
        Refresh();
    }

    private void Detach()
    {
        foreach (var field in _fields.Values) field.PropertyChanged -= OnAnythingChanged;
        _fields.Clear();
        if (_library is { } oldLibrary) oldLibrary.EditsPending -= OnLibraryEdited;
        _library = null;
        WatchScheme(null);
    }

    [ObservableProperty]
    private bool _previewDark;

    partial void OnPreviewDarkChanged(bool value) => Refresh();

    public string PreeditText => "ni hao";

    public bool ShowPreedit => !Flag("style/inline_preedit");

    public IBrush PreeditBrush => Brush(SchemeColor("text_color") ?? SchemeColor("candidate_text_color"), Colors.Black);

    public IBrush BackBrush => Brush(SchemeColor("back_color"), Colors.White);

    public IBrush FrameBrush => Brush(SchemeColor("border_color"), Colors.Transparent);

    public Orientation PanelOrientation { get; private set; } = Orientation.Horizontal;

    public double FontSize { get; private set; } = 22;

    public FontFamily? PreviewFont { get; private set; }

    public double LabelFontSize { get; private set; }

    public FontFamily? LabelFont { get; private set; }

    public double CommentFontSize { get; private set; }

    public FontFamily? CommentFont { get; private set; }

    public double PreeditGap { get; private set; } = 10;

    public double CandidateGap { get; private set; } = 5;

    public CornerRadius WindowCorner { get; private set; } = new(4);

    public Thickness WindowPadding { get; private set; } = new(12);

    public Thickness FrameThickness { get; private set; }

    public double MinWidth { get; private set; }

    public double MinHeight { get; private set; }

    public double MaxWidth { get; private set; } = double.PositiveInfinity;

    public double MaxHeight { get; private set; } = double.PositiveInfinity;

    public double WindowOpacity { get; private set; } = 1;

    public IEffect? Shadow { get; private set; }

    public IReadOnlyList<CandidatePreviewItem> Items { get; private set; } = [];

    /// <summary>当前预览的皮肤名（找不到选中皮肤时的兜底也如实显示）。</summary>
    public string Caption { get; private set; } = "";

    private void OnAnythingChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void OnLibraryEdited(object? sender, EventArgs e) => Refresh();

    /// <summary>整体重算并广播所有属性；也把皮肤 VM 的编辑接到重算上。</summary>
    private void Refresh()
    {
        var scheme = ActiveScheme();
        WatchScheme(scheme);

        var baseFaces = SchemeText(scheme, s => s.FontFace) ?? Str("style/font_face");
        PreviewFont = FontFamilyOf(baseFaces);
        FontSize = SchemeLong(scheme, s => s.FontPointText) ?? Num("style/font_point", 22);
        LabelFontSize = Num("style/label_font_point", FontSize);
        LabelFont = FontFamilyOf(Str("style/label_font_face") ?? baseFaces);
        CommentFontSize = Num("style/comment_font_point", FontSize);
        CommentFont = FontFamilyOf(Str("style/comment_font_face") ?? baseFaces);

        PanelOrientation = ComputeOrientation();

        PreeditGap = Num("style/layout/spacing", "style/spacing", 10);
        CandidateGap = Num("style/layout/candidate_spacing", "style/candidate_spacing", 5)
                       + (PanelOrientation == Orientation.Vertical ? LineSpacingPx(scheme) : 0);
        double hiliteSpacing = Num("style/layout/hilite_spacing", "style/hilite_spacing", 4);
        double hilitePadding = Num("style/layout/hilite_padding", "style/hilite_padding", 0);

        double corner = SchemeLong(scheme, s => s.CornerRadiusText)
                        ?? Num("style/layout/corner_radius", "style/corner_radius", 4);
        double hiliteCorner = Num("style/layout/hilited_corner_radius",
            Num("style/hilited_corner_radius", Num("style/layout/round_corner", corner)));
        WindowCorner = new(corner);

        double marginX = Num("style/layout/margin_x", "style/margin_x", 12);
        double marginY = Num("style/layout/margin_y", "style/margin_y", 12);
        WindowPadding = new(marginX, marginY);
        double border = Num("style/layout/border_width", "style/border_width", 0);
        FrameThickness = new(border);

        MinWidth = Math.Max(0, Num("style/layout/min_width", 0));
        MinHeight = Math.Max(0, Num("style/layout/min_height", 0));
        MaxWidth = Num("style/layout/max_width", 0) is { } maxWidth and > 0 ? maxWidth : double.PositiveInfinity;
        MaxHeight = Num("style/layout/max_height", 0) is { } maxHeight and > 0 ? maxHeight : double.PositiveInfinity;

        WindowOpacity = Math.Clamp(Num("style/alpha", 1), 0.15, 1);

        Shadow = BuildShadow(scheme);

        var format = Str("style/label_format") ?? Str("style/candidate_format") ?? "%s.";
        var mark = Str("style/mark_text");
        var labelBrush = BrushOr(SchemeColor("label_color"),
            BrushOr(SchemeColor("candidate_text_color"), Brushes.Black));
        var hilitedLabelBrush = BrushOr(SchemeColor("hilited_label_color"),
            BrushOr(SchemeColor("hilited_candidate_text_color"), Brushes.Black));
        var candidateBrush = BrushOr(SchemeColor("candidate_text_color"), Brushes.Black);
        var hilitedBrush = BrushOr(SchemeColor("hilited_candidate_text_color"), Brushes.Black);
        var commentBrush = BrushOr(SchemeColor("comment_text_color"), candidateBrush);
        var hilitedCommentBrush = BrushOr(SchemeColor("hilited_comment_text_color"), hilitedBrush);
        var hiliteBack = BrushOr(SchemeColor("hilited_candidate_back_color"),
            new SolidColorBrush(Color.FromRgb(0xD3, 0xE3, 0xF6)));

        Items = Sample.Select((candidate, i) =>
        {
            bool hilited = i == 0;
            return new CandidatePreviewItem(
                (hilited ? mark : "") + LabelOf(format, i + 1), candidate.Text, candidate.Comment,
                hilited ? hiliteBack : Brushes.Transparent,
                hilited ? new CornerRadius(hiliteCorner) : new CornerRadius(0),
                hilited ? new Thickness(hilitePadding) : default,
                hilited ? hilitedLabelBrush : labelBrush, LabelFont, LabelFontSize,
                new Thickness(0, 0, hiliteSpacing, 0),
                hilited ? hilitedBrush : candidateBrush, PreviewFont, FontSize,
                hilited ? hilitedCommentBrush : commentBrush, CommentFont, CommentFontSize,
                new Thickness(8, 0, 0, 0), candidate.Comment is not null);
        }).ToList();

        Caption = $"皮肤：{scheme?.Name ?? "（默认）"}";
        OnPropertyChanged(string.Empty);
    }

    private Orientation ComputeOrientation()
    {
        switch (Str("style/layout/type"))
        {
            case "horizontal": return Orientation.Horizontal;
            case "vertical" or "vertical_text": return Orientation.Vertical;
        }
        if (Str("style/text_orientation") == "vertical") return Orientation.Vertical;
        if (Str("style/candidate_list_layout") is { } layout) return layout == "linear" ? Orientation.Horizontal : Orientation.Vertical;
        if (_fields.TryGetValue("style/horizontal", out var horizontal) && horizontal.CurrentValue() is { } raw)
            return raw is true ? Orientation.Horizontal : Orientation.Vertical;
        return Orientation.Horizontal;
    }

    /// <summary>行距：小狼毫 layout/linespacing 是字号的百分比，鼠须管 line_spacing 是像素。</summary>
    private double LineSpacingPx(ColorSchemeViewModel? scheme)
    {
        if (Num0("style/layout/linespacing") is { } pct and > 0) return FontSize * pct / 100.0;
        if (SchemeLong(scheme, s => s.LineSpacingText) is { } schemePx and > 0) return schemePx;
        if (Num0("style/line_spacing") is { } px and > 0) return px;
        return 0;
    }

    private IEffect? BuildShadow(ColorSchemeViewModel? scheme)
    {
        double radius = Num("style/layout/shadow_radius", Num("style/shadow_size", 0));
        var color = SchemeColor("shadow_color");
        if (radius <= 0 || color is not { A: > 0 }) return null;
        return new DropShadowEffect
        {
            BlurRadius = radius,
            Color = color.Value,
            OffsetX = Num("style/layout/shadow_offset_x", 4),
            OffsetY = Num("style/layout/shadow_offset_y", 4),
        };
    }

    // ---------- 取值：字段（含未保存编辑）→ 两套键路径取先存在者 ----------

    private object? Raw(string keyPath) => _fields.TryGetValue(keyPath, out var field) ? field.CurrentValue() : null;

    private string? Str(string keyPath) => Raw(keyPath)?.ToString() is { Length: > 0 } text ? text : null;

    private bool Flag(string keyPath) => Raw(keyPath) is true;

    private double Num(string keyPath, string fallbackKey, double fallback)
        => Num0(keyPath) ?? Num0(fallbackKey) ?? fallback;

    private double Num(string keyPath, double fallback) => Num0(keyPath) ?? fallback;

    private double? Num0(string keyPath) => Raw(keyPath) switch
    {
        long l => l,
        double d => d,
        _ => null,
    };

    private static string LabelOf(string format, int index) => format.Replace("%s", index.ToString());

    // ---------- 皮肤：编辑中的值优先，其次选中方案，再是内置兜底 ----------

    private ColorSchemeViewModel? ActiveScheme()
    {
        if (_library is null) return null;
        var wanted = (PreviewDark ? Str("style/color_scheme_dark") : null) ?? Str("style/color_scheme");
        return _library.Schemes.FirstOrDefault(s => string.Equals(s.Name, wanted, StringComparison.OrdinalIgnoreCase))
               ?? _library.Schemes.FirstOrDefault();
    }

    /// <summary>
    /// 皮肤颜色：已建模字段读控件里的活值（皮肤库里未保存的编辑也立即反映），
    /// 编辑器没有建模的键（border_color/shadow_color/label_color…）读加载的定义。
    /// </summary>
    private Color? SchemeColor(string key) => ActiveScheme() is not { } scheme ? null : ParseRime(key switch
    {
        "back_color" => scheme.BackColor,
        "candidate_text_color" => scheme.CandidateTextColor,
        "hilited_candidate_back_color" => scheme.HilitedBackColor,
        "hilited_candidate_text_color" => scheme.HilitedTextColor,
        "comment_text_color" => scheme.CommentTextColor,
        "hilited_comment_text_color" => scheme.HilitedCommentTextColor,
        "text_color" => scheme.TextColor,
        "hilited_text_color" => scheme.HilitedPreeditColor,
        _ => scheme.RawEntry(key), // border_color / shadow_color / label_color 等未建模键
    });

    private static string? SchemeText(ColorSchemeViewModel? scheme, Func<ColorSchemeViewModel, string> pick)
        => scheme is null ? null : pick(scheme).Trim() is { Length: > 0 } text ? text : null;

    private static long? SchemeLong(ColorSchemeViewModel? scheme, Func<ColorSchemeViewModel, string> pick)
        => SchemeText(scheme, pick) is { } text
           && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static FontFamily? FontFamilyOf(string? faces)
    {
        var first = faces?.Split(',')[0].Trim();
        if (string.IsNullOrEmpty(first)) return null;
        try { return FontFamily.Parse(first); }
        catch (Exception) { return null; } // 坏字体名只影响预览，回退默认字体
    }

    /// <summary>RIME 颜色（0xBBGGRR，可带高字节 alpha）→ 显示色；long 形式（十进制）一并兼容。</summary>
    internal static Color? ParseRime(object? raw) => raw switch
    {
        long v and >= 0 and <= 0xFFFFFFFF => FromRime((uint)v),
        int i and >= 0 => FromRime((uint)i),
        string s => ParseRimeText(s),
        _ => null,
    };

    private static Color? ParseRimeText(string text)
    {
        var hex = text.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        else if (hex.StartsWith('#')) hex = hex[1..];
        return hex.Length is 6 or 8
               && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? FromRime(value)
            : null;
    }

    private static Color FromRime(uint value) => Color.FromArgb(
        value > 0xFFFFFF ? (byte)(value >> 24) : byte.MaxValue,
        (byte)(value & 0xFF),          // low byte = red
        (byte)(value >> 8 & 0xFF),
        (byte)(value >> 16 & 0xFF));

    private static IBrush Brush(Color? color, Color fallback) => new SolidColorBrush(color ?? fallback);

    private static IBrush BrushOr(Color? color, IBrush fallback) => color is { } c ? new SolidColorBrush(c) : fallback;

    private void WatchScheme(ColorSchemeViewModel? scheme)
    {
        if (ReferenceEquals(_watchedScheme, scheme)) return;
        if (_watchedScheme is { } old) old.PropertyChanged -= OnAnythingChanged;
        _watchedScheme = scheme;
        if (scheme is { } neu) neu.PropertyChanged += OnAnythingChanged;
    }
}
