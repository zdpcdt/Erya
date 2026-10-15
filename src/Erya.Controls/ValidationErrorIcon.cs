using System.Collections;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;

namespace Erya.Controls;

/// <summary>
/// 校验错误指示器：目标控件出现数据校验错误（DataValidationErrors）时，在控件
/// 右侧显示一个红色感叹号，悬停可看错误信息；无错误时完全不占位。配合
/// NumericUpDown / TextBox 的 Classes="silent-errors"（CellStyles.axaml）使用，
/// 把默认的整片红色报错 UI 换成安静的行内指示：
/// <code>
/// &lt;StackPanel Orientation="Horizontal" Spacing="8"&gt;
///   &lt;NumericUpDown Name="nu" Classes="silent-errors" Value="{Binding N}" /&gt;
///   &lt;c:ValidationErrorIcon Target="{Binding #nu}" /&gt;
/// &lt;/StackPanel&gt;
/// </code>
/// </summary>
[TemplatePart(PART_Root, typeof(Border))]
public class ValidationErrorIcon : TemplatedControl
{
    public const string PART_Root = "PART_Root";

    public static readonly StyledProperty<Control?> TargetProperty =
        AvaloniaProperty.Register<ValidationErrorIcon, Control?>(nameof(Target));

    /// <summary>监听其校验错误的控件。</summary>
    public Control? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    public static readonly StyledProperty<bool> HasErrorsProperty =
        AvaloniaProperty.Register<ValidationErrorIcon, bool>(nameof(HasErrors));

    /// <summary>目标控件当前是否有校验错误（模板绑定用）。</summary>
    public bool HasErrors
    {
        get => GetValue(HasErrorsProperty);
        private set => SetValue(HasErrorsProperty, value);
    }

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<ValidationErrorIcon, string?>(nameof(Message));

    /// <summary>第一条错误信息，作为感叹号的 Tooltip。</summary>
    public string? Message
    {
        get => GetValue(MessageProperty);
        private set => SetValue(MessageProperty, value);
    }

    public ValidationErrorIcon()
    {
        Focusable = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TargetProperty) return;
        if (change.OldValue is Control old) old.PropertyChanged -= OnTargetPropertyChanged;
        if (change.NewValue is Control neu) neu.PropertyChanged += OnTargetPropertyChanged;
        Refresh();
    }

    private void OnTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == DataValidationErrors.HasErrorsProperty
            || e.Property == DataValidationErrors.ErrorsProperty)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        HasErrors = Target is { } target && DataValidationErrors.GetHasErrors(target);
        Message = (DataValidationErrors.GetErrors(Target) as IEnumerable)?.OfType<object>()
            .Select(e => e is Exception ex ? ex.Message : e.ToString())
            .FirstOrDefault();
    }
}
