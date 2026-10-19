using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Erya.ViewModels.Advanced;

namespace Erya.Views;

public partial class AdvancedSettingsPage : UserControl
{
    public AdvancedSettingsPage()
    {
        InitializeComponent();
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 2;
    }

    private AdvancedSettingsViewModel? ViewModel => DataContext as AdvancedSettingsViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyHighlighting();
        ActualThemeVariantChanged += OnThemeVariantChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActualThemeVariantChanged -= OnThemeVariantChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnThemeVariantChanged(object? sender, System.EventArgs e) => ApplyHighlighting();

    private void ApplyHighlighting() =>
        Editor.SyntaxHighlighting = ActualThemeVariant == ThemeVariant.Dark
            ? YamlHighlighting.Dark
            : YamlHighlighting.Light;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && ViewModel?.SaveAndDeployCommand.CanExecute(null) == true)
        {
            ViewModel.SaveAndDeployCommand.Execute(null);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
