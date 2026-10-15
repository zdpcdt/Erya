using System.Globalization;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Erya.Views;
using Xunit;

namespace Erya.UiTests;

/// <summary>皮肤颜色 / 快捷键转换器的往返与容错。</summary>
public class ConvertersTests
{
    private static readonly RimeColorConverter ColorConv = new();
    private static readonly RimeHotkeyConverter HotkeyConv = new();
    private static readonly CultureInfo C = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData("112233", 0x33, 0x22, 0x11)]   // BBGGRR → R=33 G=22 B=11
    [InlineData("0xD3E3F6", 0xF6, 0xE3, 0xD3)] // 容忍 0x 前缀
    [InlineData("#FFFFFF", 0xFF, 0xFF, 0xFF)]  // 容忍 # 前缀
    public void Rime_color_parses_bgr_order(string hex, byte r, byte g, byte b)
    {
        var color = Assert.IsType<Color>(ColorConv.Convert(hex, typeof(Color), null, C));
        Assert.Equal(Color.FromRgb(r, g, b), color);
    }

    [Fact]
    public void Rime_color_round_trips_back_to_bgr()
    {
        var back = ColorConv.ConvertBack(Color.FromRgb(0x33, 0x22, 0x11), typeof(string), null, C);
        Assert.Equal("112233", back);
    }

    [Fact]
    public void Rime_color_falls_back_for_invalid_values()
    {
        var fallback = Assert.IsType<Color>(ColorConv.Convert("not-hex", typeof(Color), "ABCDEF", C));
        Assert.Equal(Color.FromRgb(0xAB, 0xCD, 0xEF), fallback);
        Assert.Equal(BindingOperations.DoNothing, ColorConv.ConvertBack("nope", typeof(string), null, C));
    }

    [Theory]
    [InlineData("Control+grave", "Control+grave")]
    [InlineData("Control+space", "Control+space")]
    [InlineData("F4", "F4")]
    [InlineData("Shift+comma", "Shift+comma")]
    [InlineData("a", "a")]
    [InlineData("5", "5")]
    [InlineData("Super+space", "Super+space")]
    public void Rime_hotkey_round_trips(string rime, string expected)
    {
        var gesture = Assert.IsType<KeyGesture>(HotkeyConv.Convert(rime, typeof(KeyGesture), null, C));
        Assert.Equal(expected, HotkeyConv.ConvertBack(gesture, typeof(string), null, C));
    }

    [Fact]
    public void Rime_hotkey_windows_grave_layout_maps_back_to_grave()
    {
        // Windows 布局下反引键报告 Oem3，写回仍应是 RIME 的 grave。
        var back = HotkeyConv.ConvertBack(new KeyGesture(Key.Oem3, KeyModifiers.Control), typeof(string), null, C);
        Assert.Equal("Control+grave", back);
    }

    [Fact]
    public void Rime_hotkey_unparseable_shows_empty_and_keeps_original()
    {
        Assert.Null(HotkeyConv.Convert("KP_Add", typeof(KeyGesture), null, C));
        Assert.Equal(BindingOperations.DoNothing, HotkeyConv.ConvertBack(null, typeof(string), null, C));
    }
}
