using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Erya.Views;

/// <summary>
/// 颜色色块（对比度主题式）：圆角矩形显示当前色，点击弹出扁平取色面板
/// （ColorView 直出，无嵌套弹层）。值为皮肤编辑器惯用的 RIME 0xBBGGRR
/// 十六进制字符串（无 0x 前缀），经 <see cref="SwatchColor"/> 桥接双向同步。
/// </summary>
public class ColorSwatch : TemplatedControl
{
    public static readonly StyledProperty<string?> RimeHexProperty =
        AvaloniaProperty.Register<ColorSwatch, string?>(nameof(RimeHex));

    public static readonly StyledProperty<Color> SwatchColorProperty =
        AvaloniaProperty.Register<ColorSwatch, Color>(nameof(SwatchColor), Colors.White);

    private Border? _swatch;
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

    /// <summary>取色面板双向绑定的桥：与 RimeHex 同步。</summary>
    public Color SwatchColor
    {
        get => GetValue(SwatchColorProperty);
        set => SetValue(SwatchColorProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _swatch = e.NameScope.Find("PART_Swatch") as Border;
        SyncSwatch();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_syncing) return;
        if (change.Property == RimeHexProperty)
        {
            _syncing = true;
            if (RimeHex is { } hex && TryParseBgr(hex, out var color)) SwatchColor = color;
            SyncSwatch();
            _syncing = false;
        }
        else if (change.Property == SwatchColorProperty)
        {
            _syncing = true;
            var c = SwatchColor;
            SetCurrentValue(RimeHexProperty, $"{c.B:X2}{c.G:X2}{c.R:X2}");
            SyncSwatch();
            _syncing = false;
        }
    }

    private void SyncSwatch()
    {
        if (_swatch is null) return;
        _swatch.Background = new SolidColorBrush(SwatchColor);
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
