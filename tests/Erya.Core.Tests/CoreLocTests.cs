using Erya.Core.Localization;

namespace Erya.Core.Tests;

/// <summary>
/// CoreLoc 的 base 行为与语言切换回落。翻译变体（zh-Hant/en）落地后，
/// 本文件里的 VariantTests 断言会随资源文件自动生效。
/// </summary>
public sealed class CoreLocTests : IDisposable
{
    private readonly string _originalLanguage = CoreLoc.Language;

    public void Dispose() => CoreLoc.Language = _originalLanguage;

    [Fact]
    public void Base_ReturnsSimplifiedChinese()
    {
        CoreLoc.Language = "";
        Assert.Equal("小狼毫 Weasel", CoreLoc.Get("distName_weasel"));
        Assert.StartsWith("正在写入用户目录", CoreLoc.Get("installer_progressWriting"));
    }

    [Fact]
    public void Format_FillsPlaceholders()
    {
        CoreLoc.Language = "";
        Assert.Equal("用户目录不存在：D:\\Rime", CoreLoc.Format("backup_userDirMissing", "D:\\Rime"));
        Assert.Equal("下载失败：HTTP 404（https://x）",
            CoreLoc.Format("installer_httpError", 404, "https://x"));
    }

    [Fact]
    public void UnknownKey_FallsBackToKeyItself()
    {
        CoreLoc.Language = "";
        Assert.Equal("no_such_key", CoreLoc.Get("no_such_key"));
    }

    [Fact]
    public void MissingVariant_FallsBackToBase()
    {
        // 某语言没有变体文件（或某 key 未翻译）时必须回落 base，而不是空串。
        CoreLoc.Language = "ja";
        Assert.Equal("小狼毫 Weasel", CoreLoc.Get("distName_weasel"));
    }

    [Fact]
    public void TraditionalVariant_IsLoaded()
    {
        CoreLoc.Language = "zh-Hant";
        Assert.Equal("正在寫入使用者目錄…", CoreLoc.Get("installer_progressWriting"));
    }

    [Fact]
    public void EnglishVariant_IsLoaded()
    {
        CoreLoc.Language = "en";
        Assert.Equal("Weasel (小狼毫)", CoreLoc.Get("distName_weasel"));
        Assert.Equal("Writing to user directory…", CoreLoc.Get("installer_progressWriting"));
        Assert.Equal("Download failed: HTTP 404 (https://x)",
            CoreLoc.Format("installer_httpError", 404, "https://x"));
    }

    [Fact]
    public void TryLoadMap_MissingResource_ReturnsNull()
    {
        Assert.Null(CoreLoc.TryLoadMap("Erya.Core.Localization.NoSuch.json"));
    }
}
