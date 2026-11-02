using System;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Erya.Controls;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// ValidationErrorIcon 与 InputStyles（silent-errors）的验收：校验错误跟随目标
/// 控件出现/消失、静默校验下数值清空进入错误态。设置行控件已由 Ursa 的
/// SettingsCard / SettingsCardGroup 提供，其结构级测试随自研 Cell 一并退役。
/// </summary>
public class ValidationErrorIconTests
{
    private static T ShowInWindow<T>(T content) where T : Control
    {
        var window = new Window { Width = 520, Height = 720, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return content;
    }

    private sealed class PageSizeModel
    {
        public int PageSize { get; set; } = 5;
    }

    [AvaloniaFact]
    public void Validation_error_icon_follows_target_errors()
    {
        var target = new TextBox();
        var icon = new ValidationErrorIcon { Target = target };
        ShowInWindow(new StackPanel { Children = { target, icon } });

        var root = icon.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Name == "PART_Root");
        Assert.False(icon.HasErrors);
        Assert.False(root.IsVisible); // 无错时零占位

        DataValidationErrors.SetError(target, new InvalidOperationException("请输入 3–10 之间的数字"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(icon.HasErrors);
        Assert.Equal("请输入 3–10 之间的数字", icon.Message);
        Assert.True(root.IsVisible);

        DataValidationErrors.ClearErrors(target);
        Dispatcher.UIThread.RunJobs();
        Assert.False(icon.HasErrors);
        Assert.False(root.IsVisible);
    }

    [AvaloniaFact]
    public void Cleared_numeric_binds_quietly_and_shows_error_state()
    {
        var nu = new NumericUpDown { Minimum = 3, Maximum = 10, Width = 130 };
        nu.Classes.Add("silent-errors");
        nu[!NumericUpDown.ValueProperty] = new Binding("PageSize")
        {
            Source = new PageSizeModel(),
            Mode = BindingMode.TwoWay,
        };
        ShowInWindow(nu);

        // silent-errors：模板内不再渲染默认的错误文字区
        Assert.DoesNotContain(nu.GetVisualDescendants(),
            v => v.Name == "InlineDataValidationContentControl");

        // 清空文本（decimal? Value → null → int 绑定失败）→ 进入错误状态
        Assert.Equal("5", nu.Text);
        nu.Text = "";
        Dispatcher.UIThread.RunJobs();
        Assert.True(DataValidationErrors.GetHasErrors(nu));
    }
}
