using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace Erya.Views;

/// <summary>
/// RIME 官方 logo（「中」字图形，取自 rime GitHub 组织头像：黑底白字，
/// 白色像素提取为形状蒙版）。亮色主题用墨蓝、暗色主题用近白，随
/// ActualThemeVariant 自动切换；尺寸由使用处直接设 Width/Height。
/// </summary>
public class RimeLogoIcon : Image
{
    private static readonly IImage Light = Load("avares://Erya/Assets/rime-logo.png");
    private static readonly IImage Dark = Load("avares://Erya/Assets/rime-logo-dark.png");

    private static IImage Load(string uri) => new Bitmap(AssetLoader.Open(new Uri(uri)));

    public RimeLogoIcon()
    {
        Update();
        ActualThemeVariantChanged += (_, _) => Update();
    }

    private void Update()
        => Source = ActualThemeVariant == ThemeVariant.Dark ? Dark : Light;
}
