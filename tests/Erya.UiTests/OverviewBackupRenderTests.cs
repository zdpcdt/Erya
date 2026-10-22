using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Erya.Controls;
using Erya.Core.Distributions;
using Erya.ViewModels;
using Erya.Views;
using Ursa.Controls;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 概览页（英雄卡 + 环境/维护分组）与备份恢复页（备份/恢复两组 + 空态）的
/// 端到端渲染：零 Banner / 零 PathPicker，状态长在行内与英雄卡上。
/// </summary>
public sealed class OverviewBackupRenderTests : IDisposable
{
    private readonly string _dir;

    public OverviewBackupRenderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "erya-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private sealed class StubDistribution(string userDir) : RimeDistributionBase
    {
        public override string Id => "stub";
        public override string DisplayName => "测试发行版";
        public override Version Version => new(0, 15, 2);
        public override string? InstallDir => "/Applications/Test.app";
        public override string UserDir => userDir;
        public override bool CanDeploy => true;
        public override Task DeployAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubLocator(StubDistribution distribution) : IRimeDistributionLocator
    {
        public IRimeDistribution? Locate() => distribution;
    }

    private static (TPage Page, Window Window) Show<TPage>(TPage page) where TPage : Control
    {
        var window = new Window { Width = 820, Height = 820, Content = page };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (page, window);
    }

    private static bool IsEffectivelyVisible(Control control)
        => control.IsVisible
           && control.GetVisualAncestors().OfType<Control>().All(a => a.IsVisible);

    private static IEnumerable<TextBlock> VisibleTexts(Control root)
        => root.GetVisualDescendants().OfType<TextBlock>().Where(IsEffectivelyVisible);

    /// <summary>只取普通按钮：Cell 与 IconButton 也派生自 Button，会混进断言。</summary>
    private static IEnumerable<Button> PlainButtons(Control root)
        => root.GetVisualDescendants().OfType<Button>()
            .Where(b => b is not Cell && b is not IconButton);

    [AvaloniaFact]
    public void Overview_detected_renders_hero_card_and_groups()
    {
        var vm = new MainViewModel(new DistributionManager(new StubLocator(new StubDistribution(_dir))));

        // 英雄卡状态：就绪徽标、发行版名与版本，无提示行。
        Assert.True(vm.IsReady);
        Assert.Equal("测试发行版", vm.HeroTitle);
        Assert.Equal("v0.15.2", vm.HeroVersion);
        Assert.Equal(string.Empty, vm.HeroHint);

        var (page, window) = Show(new OverviewPage { DataContext = vm });

        // 就绪徽标可见；环境 + 维护两组共三行（输入前端行仅在多前端时出现）。
        Assert.Contains(VisibleTexts(page), t => t.Text == "就绪");
        var logo = Assert.Single(page.GetVisualDescendants().OfType<Views.RimeLogoIcon>()
            .Where(l => IsEffectivelyVisible(l)));
        Assert.NotNull(logo.Source); // RIME logo 资源已加载（亮色主题取墨蓝版）

        // 暗色主题自动切换到近白变体（参考 AboutPageTests 的双变体遍历）。
        var lightSource = logo.Source;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        Assert.NotSame(lightSource, logo.Source);
        Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        Dispatcher.UIThread.RunJobs();
        var groups = page.GetVisualDescendants().OfType<CellGroup>().Where(IsEffectivelyVisible).ToList();
        Assert.Equal(2, groups.Count);
        var cells = page.GetVisualDescendants().OfType<Cell>().Where(IsEffectivelyVisible).ToList();
        Assert.Equal(3, cells.Count); // 安装目录 / 用户目录 / 重新部署

        var deploy = PlainButtons(page)
            .Single(b => IsEffectivelyVisible(b) && b.Content as string == "重新部署");
        Assert.True(deploy.IsEnabled); // CanDeploy=true

        // 零 Banner：页面不再有常驻色块。
        Assert.Empty(page.GetVisualDescendants().OfType<Banner>());
        window.Close();
    }

    [AvaloniaFact]
    public void Overview_missing_shows_hero_empty_state_only()
    {
        var vm = new MainViewModel(new DistributionManager());

        Assert.True(vm.IsMissing);
        Assert.Equal("未检测到 RIME 发行版", vm.HeroTitle);
        Assert.NotEmpty(vm.HeroHint); // 安装指引副行

        var (page, window) = Show(new OverviewPage { DataContext = vm });

        Assert.Contains(VisibleTexts(page), t => t.Text == "未检测到");
        Assert.DoesNotContain(page.GetVisualDescendants().OfType<CellGroup>(), IsEffectivelyVisible);
        Assert.Contains(VisibleTexts(page), t => t.Text == vm.HeroHint);
        window.Close();
    }

    [AvaloniaFact]
    public void Backup_page_renders_two_groups_with_quiet_rows()
    {
        var vm = new BackupRestoreViewModel(new StubDistribution(_dir));
        vm.LoadCommand.Execute(null);

        Assert.True(vm.HasUserDir);
        Assert.StartsWith("0 个文件", vm.UserDirSummary); // 空目录也能打包

        var (page, window) = Show(new BackupRestorePage { DataContext = vm });

        var groups = page.GetVisualDescendants().OfType<CellGroup>().Where(IsEffectivelyVisible).ToList();
        Assert.Equal(2, groups.Count); // 备份 / 恢复

        // 右侧按钮：备份（busy 文案入口）、选择备份文件、从备份恢复（未选文件时禁用）。
        var buttons = PlainButtons(page).Where(IsEffectivelyVisible).ToList();
        Assert.Contains(buttons, b => b.Content as string == "选择位置并备份…" && b.IsEnabled);
        var restore = Assert.Single(buttons, b => b.Content as string == "从备份恢复");
        Assert.False(restore.IsEnabled);
        Assert.Contains(VisibleTexts(page), t => t.Text == "未选择");

        // 选好备份文件后：Note 变文件名，恢复按钮点亮。
        vm.RestoreSourcePath = Path.Combine(_dir, "RimeBackup_20261022.zip");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(VisibleTexts(page), t => t.Text == "RimeBackup_20261022.zip");
        Assert.True(restore.IsEnabled);

        // busy：按钮变进度文案并禁用。
        vm.IsBusy = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(buttons, b => b.Content as string == "恢复中…" && !b.IsEnabled);
        vm.IsBusy = false;

        // 零 Banner / 零 PathPicker。
        Assert.Empty(page.GetVisualDescendants().OfType<Banner>());
        Assert.Empty(page.GetVisualDescendants().OfType<PathPicker>());
        window.Close();
    }

    [AvaloniaFact]
    public void Backup_page_without_distribution_shows_empty_state()
    {
        var vm = new BackupRestoreViewModel(null);
        vm.LoadCommand.Execute(null);

        Assert.False(vm.HasUserDir);

        var (page, window) = Show(new BackupRestorePage { DataContext = vm });

        Assert.DoesNotContain(page.GetVisualDescendants().OfType<CellGroup>(), IsEffectivelyVisible);
        Assert.Contains(VisibleTexts(page), t => t.Text == "未检测到 RIME 发行版");
        Assert.Empty(page.GetVisualDescendants().OfType<PathPicker>());
        window.Close();
    }
}
