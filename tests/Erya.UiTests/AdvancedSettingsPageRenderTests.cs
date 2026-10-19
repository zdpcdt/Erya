using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Erya.Core.Distributions;
using Erya.ViewModels.Advanced;
using Erya.Views;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 高级设置（源码编辑器版）的端到端渲染：AvaloniaEdit 编辑器出现并绑定文档、
/// 行号打开、YAML 高亮跟随明暗主题变体切换。
/// </summary>
public sealed class AdvancedSettingsPageRenderTests : IDisposable
{
    private readonly string _dir;

    public AdvancedSettingsPageRenderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "erya-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private sealed class StubDistribution : RimeDistributionBase
    {
        public StubDistribution(string userDir) => UserDir = userDir;

        public override string Id => "stub";

        public override string DisplayName => "测试发行版";

        public override string UserDir { get; }

        public override string? UiConfigFile => "squirrel.yaml";

        public override bool CanDeploy => false;

        public override Task DeployAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [AvaloniaFact]
    public void Source_editor_renders_and_highlighting_follows_theme_variant()
    {
        File.WriteAllText(Path.Combine(_dir, "default.custom.yaml"), """
            # 引擎补丁
            patch:
              menu/page_size: 9
              switcher/caption: "方案選單"
              ascii_composer/good_old_caps_lock: true
            """);

        var vm = new AdvancedSettingsViewModel(new StubDistribution(_dir));
        vm.LoadCommand.Execute(null);

        var page = new AdvancedSettingsPage { DataContext = vm, Width = 780, Height = 620 };
        var window = new Window { Width = 820, Height = 660, Content = page };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // 编辑器挂载：文档绑定、行号打开、默认浅色高亮。
        var editor = page.GetVisualDescendants().OfType<TextEditor>().Single();
        Assert.Same(vm.Document, editor.Document);
        Assert.True(editor.ShowLineNumbers);
        Assert.Same(YamlHighlighting.Light, editor.SyntaxHighlighting);

        // 深色变体：高亮换到暗色一套。
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        Assert.Same(YamlHighlighting.Dark, editor.SyntaxHighlighting);

        window.CaptureRenderedFrame()?.Save(Path.Combine(Path.GetTempPath(), "erya-advanced-dark.png"));
        Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(Path.Combine(Path.GetTempPath(), "erya-advanced-light.png"));
        window.Close();
        Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        Dispatcher.UIThread.RunJobs();
    }
}
