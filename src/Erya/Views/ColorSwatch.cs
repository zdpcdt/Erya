using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Erya.Views;

/// <summary>
/// 颜色色块（对比度主题式）：圆角矩形显示当前色，点击弹出完整取色器。
/// 值为皮肤编辑器惯用的 RIME 0xBBGGRR 十六进制字符串（无 0x 前缀）。
/// </summary>
public class ColorSwatch : TemplatedControl
{
    public static readonly StyledProperty<string?> RimeHexProperty =
        AvaloniaProperty.Register<ColorSwatch, string?>(nameof(RimeHex));

    private Border? _swatch;
    private ColorPicker? _picker;
    private bool _syncing;

    public ColorSwatch()
    {
        Width = 48;
        Height = 28;
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
    }

    public string? RimeHex
    {
        get => GetValue(RimeHexProperty);
        set => SetValue(RimeHexProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _swatch = e.NameScope.Find("PART_Swatch") as Border;
        _picker = e.NameScope.Find("PART_Picker") as ColorPicker;
        if (_picker is not null)
            _picker.ColorChanged += (_, args) => SyncFromPicker(args.NewColor);
        SyncSwatch();
        SyncPicker();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RimeHexProperty)
        {
            SyncSwatch();
            SyncPicker();
        }
    }

    private void SyncSwatch()
    {
        if (_swatch is null) return;
        _swatch.Background = RimeHex is { } hex && TryParseBgr(hex, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Transparent;
    }

    private void SyncPicker()
    {
        if (_picker is null || _syncing) return;
        if (RimeHex is { } hex && TryParseBgr(hex, out var color))
        {
            _syncing = true;
            _picker.Color = color;
            _syncing = false;
        }
    }

    private void SyncFromPicker(Color color)
    {
        if (_syncing) return;
        _syncing = true;
        SetCurrentValue(RimeHexProperty, $"{color.B:X2}{color.G:X2}{color.R:X2}");
        _syncing = false;
    }

    private static bool TryParseBgr(string hex, out Color color)
    {
        color = default;
        return hex.Length == 6
               && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
               && (color = Color.FromRgb(
                       (byte)(value & 0xFF), (byte)(value >> 8 & 0xFF), (byte)(value >> 16 & 0xFF))) is { };
    }
}
