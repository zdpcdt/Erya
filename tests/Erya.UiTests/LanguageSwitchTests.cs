using System.IO;
using Avalonia;
using Avalonia.Headless.XUnit;
using Erya.Core.Distributions;
using Erya.Core.Localization;
using Erya.Localization;
using Erya.Services;
using Erya.ViewModels;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 语言切换端到端（VM 层）：SelectedCulture 变更 → Lingua 管理器、CoreLoc、
/// 导航标题与偏好持久化全部联动；切回简体恢复原文。翻译资源缺 key 时
/// Lingua 停留在旧值，故断言用「确实换成该语言的值」而非固定英文串。
/// </summary>
public sealed class LanguageSwitchTests : IDisposable
{
    private readonly string _prefsPath = Path.Combine(
        Path.GetTempPath(), "erya-tests", $"prefs-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { File.Delete(_prefsPath); } catch (IOException) { }
        // 还原全局单例，避免影响其他测试的默认（简体）假设。
        LanguageSwitchReset();
    }

    private static void LanguageSwitchReset()
    {
        LanguageManager.Instance.UpdateCulture(System.Globalization.CultureInfo.InvariantCulture);
        CoreLoc.Language = "";
    }

    private static ShellViewModel Shell(AppPreferences prefs)
        => new(new DistributionManager(), prefs);

    [AvaloniaFact]
    public void Default_is_simplified_chinese()
    {
        var vm = Shell(AppPreferences.Load(_prefsPath));
        Assert.Equal("zh-Hans", vm.SelectedCulture!.CultureName);
        Assert.Equal("概览", vm.NavItems[0].Header);
        Assert.Equal("方案管理", vm.NavItems[1].Header);
    }

    [AvaloniaFact]
    public void Persisted_language_is_applied_on_construction()
    {
        var prefs = AppPreferences.Load(_prefsPath);
        prefs.Language = "zh-Hant";
        var vm = Shell(prefs);
        Assert.Equal("概覽", vm.NavItems[0].Header);
    }

    [AvaloniaFact]
    public void Switch_updates_nav_core_and_prefs()
    {
        var prefs = AppPreferences.Load(_prefsPath);
        var vm = Shell(prefs);

        vm.SelectedCulture = vm.Cultures.Single(c => c.CultureName == "en");
        Assert.Equal("en", LanguageManager.Instance.CurrentCulture.Name);
        Assert.Equal("en", CoreLoc.Language);
        Assert.Equal("en", prefs.Language);
        Assert.NotEqual("概览", vm.NavItems[0].Header);
        Assert.Contains("\"Language\": \"en\"", File.ReadAllText(_prefsPath));

        vm.SelectedCulture = vm.Cultures.Single(c => c.CultureName == "zh-Hant");
        Assert.Equal("概覽", vm.NavItems[0].Header);
        Assert.Equal("zh-Hant", CoreLoc.Language);

        vm.SelectedCulture = vm.Cultures.Single(c => c.CultureName == "zh-Hans");
        Assert.Equal("概览", vm.NavItems[0].Header);
        Assert.Equal("zh-Hans", prefs.Language);
    }

    [AvaloniaFact]
    public void Page_title_follows_current_nav_language()
    {
        var vm = Shell(AppPreferences.Load(_prefsPath));
        var schemasNav = vm.NavItems.Single(n => n.Key == "schemas");
        vm.SelectedNav = schemasNav;
        Assert.Equal("方案管理", vm.PageTitle);

        vm.SelectedCulture = vm.Cultures.Single(c => c.CultureName == "en");
        Assert.Equal(vm.NavItems.Single(n => n.Key == "schemas").Header, vm.PageTitle);
        Assert.Equal(vm.SelectedNav!.Key, "schemas");
    }
}
