using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Erya.Core.Distributions;
using Erya.ViewModels.Settings;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 候选窗实时预览：随外观字段（含未保存编辑）与皮肤库联动，排版键在小狼毫
/// （style/layout/*）与鼠须管（style/*）两套路径里取先存在者，颜色按 RIME
/// 的 0xBBGGRR 解析（可带高字节 alpha）。
/// </summary>
public sealed class CandidatePreviewTests : IDisposable
{
    private readonly string _dir;

    public CandidatePreviewTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "erya-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private void Write(string fileName, string content)
        => File.WriteAllText(Path.Combine(_dir, fileName), content);

    private SettingsEditorViewModel LoadUi(string uiConfigFile) => LoadUi(uiConfigFile, out _);

    private SettingsEditorViewModel LoadUi(string uiConfigFile, out ColorSchemeLibraryViewModel library)
    {
        var vm = new SettingsEditorViewModel(new StubDistribution(_dir, uiConfigFile), "ui");
        vm.LoadCommand.Execute(null);
        library = vm.SchemeLibrary!;
        return vm;
    }

    private static T Field<T>(SettingsEditorViewModel vm, string keyPath) where T : SettingsFieldViewModel
        => (T)vm.Sections.SelectMany(s => s.Fields).First(f => f.KeyPath == keyPath);

    private static Color ColorOf(IBrush brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [AvaloniaFact]
    public void Weasel_preview_follows_fields_schemes_and_dark_toggle()
    {
        Write("weasel.yaml", """
            style:
              color_scheme: aqua
              color_scheme_dark: aqua_dark
              font_point: 14
              inline_preedit: false
              label_format: "%s."
              layout:
                type: vertical
                corner_radius: 6
                margin_x: 20
                margin_y: 10
                min_width: 160
            preset_color_schemes:
              aqua:
                name: 碧水
                back_color: 0xECEEEE
                candidate_text_color: 0x000000
                hilited_candidate_back_color: 0xD3E3F6
                hilited_candidate_text_color: 0x000000
              aqua_dark:
                name: 碧水暗
                back_color: 0x1C1C1E
                candidate_text_color: 0xFFFFFF
                hilited_candidate_back_color: 0x2C2C2E
                hilited_candidate_text_color: 0xFFFFFF
            """);
        var vm = LoadUi("weasel.yaml");

        var preview = vm.CandidatePreview!;
        Assert.True(vm.HasCandidatePreview);
        Assert.Contains("aqua", preview.Caption);

        // 皮肤颜色：0xECEEEE 是 BBGGRR —— 显示时应翻成 R=EE G=EE B=EC。
        Assert.Equal(0xEE, ColorOf(preview.BackBrush).R);
        Assert.Equal(0xEE, ColorOf(preview.BackBrush).G);
        Assert.Equal(0xEC, ColorOf(preview.BackBrush).B);
        Assert.Equal(0xFF, ColorOf(preview.BackBrush).A); // 6 位色默认不透明

        // 排版：layout/type=vertical 竖排；圆角与边距来自 layout 层。
        Assert.Equal(Orientation.Vertical, preview.PanelOrientation);
        Assert.Equal(6, preview.WindowCorner.TopLeft);
        Assert.Equal(20, preview.WindowPadding.Left);
        Assert.Equal(10, preview.WindowPadding.Top);
        Assert.Equal(160, preview.MinWidth);
        Assert.Equal(double.PositiveInfinity, preview.MaxWidth); // 0 = 不限制
        Assert.True(preview.ShowPreedit);
        Assert.Equal(14, preview.FontSize);

        // 第一项高亮、序号走 label_format，注释只挂在第一项上。
        var first = preview.Items[0];
        Assert.Equal("1.", first.Label);
        Assert.Equal("你好", first.Text);
        Assert.Equal("ní hǎo", first.Comment);
        Assert.True(first.HasComment);
        Assert.Equal(ColorOf(preview.Items[1].RowBack), Colors.Transparent);
        // 0xD3E3F6 是 BBGGRR：高亮背景显示为 R=F6 G=E3 B=D3。
        Assert.Equal(0xF6, ColorOf(first.RowBack).R);
        Assert.Equal(0xD3, ColorOf(first.RowBack).B);

        // 未保存的编辑立即反映：横排、字号、内嵌编码隐藏编码行。
        Field<ChoiceFieldViewModel>(vm, "style/layout/type").Selected = "horizontal";
        Field<NumberFieldViewModel>(vm, "style/font_point").Value = 18;
        Field<ToggleFieldViewModel>(vm, "style/inline_preedit").IsChecked = true;
        Assert.Equal(Orientation.Horizontal, preview.PanelOrientation);
        Assert.Equal(18, preview.FontSize);
        Assert.False(preview.ShowPreedit);

        // 暗色实例独立解析 color_scheme_dark：与亮色实例并排显示、互不影响。
        var dark = vm.CandidatePreviewDark!;
        Assert.Contains("aqua_dark", dark.Caption);
        Assert.Equal(0x1C, ColorOf(dark.BackBrush).B);
        Assert.Contains("aqua", preview.Caption);
        Assert.Equal(0xEC, ColorOf(preview.BackBrush).B);

        // 皮肤库里的编辑也联动：改激活皮肤的背景色，亮色实例跟着变。
        var aqua = vm.SchemeLibrary!.Schemes.First(s => s.Name == "aqua");
        aqua.BackColor = "FFFFFF";
        Assert.Equal(0xFF, ColorOf(preview.BackBrush).R);
    }

    [AvaloniaFact]
    public void Weasel_scheme_level_overrides_win_over_style_level()
    {
        Write("weasel.yaml", """
            style:
              color_scheme: aqua
              font_point: 14
              layout:
                corner_radius: 6
            preset_color_schemes:
              aqua:
                name: 碧水
                back_color: 0xECEEEE
                candidate_text_color: 0x000000
                hilited_candidate_back_color: 0xD3E3F6
                hilited_candidate_text_color: 0x000000
                font_point: 24
                corner_radius: 12
            """);
        var vm = LoadUi("weasel.yaml");

        var preview = vm.CandidatePreview!;
        Assert.Equal(24, preview.FontSize);       // 皮肤级字号盖过 style/font_point
        Assert.Equal(12, preview.WindowCorner.TopLeft); // 皮肤级圆角盖过 layout/corner_radius
    }

    [AvaloniaFact]
    public void Squirrel_style_keys_drive_the_same_preview()
    {
        Write("squirrel.yaml", """
            style:
              color_scheme: macos_light
              candidate_list_layout: linear
              font_point: 21
              margin_x: 14
              alpha: 0.8
              hilite_padding: 3
            preset_color_schemes:
              macos_light:
                name: 浅色
                back_color: 0xECECEC
                candidate_text_color: 0x000000
                hilited_candidate_back_color: 0xD3E3F6
                hilited_candidate_text_color: 0x000000
            """);
        var vm = LoadUi("squirrel.yaml");

        var preview = vm.CandidatePreview!;
        Assert.Equal(Orientation.Horizontal, preview.PanelOrientation); // linear 横排
        Assert.Equal(21, preview.FontSize);
        Assert.Equal(14, preview.WindowPadding.Left);
        Assert.Equal(0.8, preview.WindowOpacity, 2);
        Assert.Equal(3, preview.Items[0].RowPadding.Left); // 高亮扩张
    }

    [AvaloniaFact]
    public void ParseRime_handles_hex_decimal_and_alpha_forms()
    {
        // 0xBBGGRR；8 位带高字节 alpha；十进制 long；坏值 → null。
        Assert.Equal(Color.FromRgb(0xEE, 0xEE, 0xEC), CandidatePreviewViewModel.ParseRime("0xECEEEE"));
        Assert.Equal(Color.FromRgb(0xEE, 0xEE, 0xEC), CandidatePreviewViewModel.ParseRime(0x00ECEEEE));
        Assert.Equal(Color.FromArgb(0x80, 0xEE, 0xEE, 0xEC), CandidatePreviewViewModel.ParseRime("80ECEEEE"));
        Assert.Null(CandidatePreviewViewModel.ParseRime("not-a-color"));
        Assert.Null(CandidatePreviewViewModel.ParseRime(-1L));
    }

    [AvaloniaFact]
    public void Edit_dialog_preview_is_pinned_to_the_scheme_and_revert_restores()
    {
        Write("weasel.yaml", """
            style:
              color_scheme: aqua
              font_point: 14
            preset_color_schemes:
              aqua:
                name: 碧水
                back_color: 0xECEEEE
                candidate_text_color: 0x000000
                hilited_candidate_back_color: 0xD3E3F6
                hilited_candidate_text_color: 0x000000
            """);
        var vm = LoadUi("weasel.yaml");
        var aqua = vm.SchemeLibrary!.Schemes.Single(s => s.Name == "aqua");
        var fields = vm.Sections.SelectMany(s => s.Fields).ToList();

        var dialog = new SchemeEditDialogViewModel(aqua, fields);
        // 预览固定渲染正在编辑的皮肤；排版参数仍取字段（字号 14）。
        Assert.Contains("aqua", dialog.Preview.Caption);
        Assert.Equal(14, dialog.Preview.FontSize);

        // 对话框里的编辑直接落在皮肤 VM 上，预览立即反映。
        aqua.BackColor = "FFFFFF";
        Assert.Equal(0xFF, ColorOf(dialog.Preview.BackBrush).R);

        // 取消：开框快照写回原实例，预览与皮肤一起复原（绑定不断）。
        dialog.Revert();
        Assert.Equal(0xEC, ColorOf(dialog.Preview.BackBrush).B);
        Assert.Equal("ECEEEE", aqua.BackColor);
    }

    [AvaloniaFact]
    public void Input_settings_have_no_preview()
    {
        Write("default.yaml", "menu:\n  page_size: 5\n");
        var vm = new SettingsEditorViewModel(new StubDistribution(_dir, "squirrel.yaml"), "default");
        vm.LoadCommand.Execute(null);

        Assert.Null(vm.CandidatePreview);
        Assert.Null(vm.CandidatePreviewDark);
        Assert.False(vm.HasCandidatePreview);
    }

    private sealed class StubDistribution : RimeDistributionBase
    {
        public StubDistribution(string userDir, string uiConfigFile)
        {
            UserDir = userDir;
            UiConfigFile = uiConfigFile;
        }

        public override string Id => "stub";

        public override string DisplayName => "测试发行版";

        public override string UserDir { get; }

        public override string? UiConfigFile { get; }

        public override bool CanDeploy => false;

        public override Task DeployAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
