using Erya.Core.Configuration;
using Erya.Core.Distributions.Linux;
using Erya.Core.Distributions.MacOS;

namespace Erya.Core.Tests;

/// <summary>
/// Ground rules for <see cref="RimeConfigStore"/> / <see cref="CustomYamlPatcher"/>:
/// patches preserve unrelated content, all three RIME patch shapes are read,
/// and writes migrate everything to the flat-in-patch shape.
/// </summary>
public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _sharedDir;

    public ConfigStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "erya-tests", Guid.NewGuid().ToString("N"));
        _sharedDir = Path.Combine(_dir, "shared");
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(_sharedDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private RimeConfigStore Store() => new(_dir, [_sharedDir]);
    private string CustomPath => Path.Combine(_dir, "default.custom.yaml");
    private string WriteCustom(string content)
    {
        File.WriteAllText(CustomPath, content);
        return CustomPath;
    }

    // ---------- writing ----------

    [Fact]
    public void Writing_into_fresh_dir_creates_patch_file()
    {
        Store().WritePatch("default.yaml", new Dictionary<string, object?> { ["menu/page_size"] = 7 });

        Assert.True(File.Exists(CustomPath));
        Assert.Equal(7L, Store().ReadMergedValue("default.yaml", "menu/page_size"));
        Assert.Contains("menu/page_size: 7", File.ReadAllText(CustomPath));
    }

    [Fact]
    public void Writing_preserves_comments_and_unrelated_patch_keys()
    {
        WriteCustom("""
            # 該文件由「尔雅」生成 —— 用户自定义配置
            patch:
              # 手动追加的开关
              ascii_composer/good_old_caps_lock: true
              schema_list:
                - schema: luna_pinyin
            """);

        Store().WritePatch("default.yaml", new Dictionary<string, object?> { ["menu/page_size"] = 7 });

        var text = File.ReadAllText(CustomPath);
        Assert.Contains("# 該文件由「尔雅」生成", text);
        Assert.Contains("# 手动追加的开关", text);
        Assert.Contains("ascii_composer/good_old_caps_lock: true", text);
        Assert.Contains("- schema: luna_pinyin", text);
        Assert.Contains("menu/page_size: 7", text);
    }

    [Fact]
    public void Writing_replaces_previous_value_of_same_key()
    {
        Store().WritePatch("default.yaml", new Dictionary<string, object?> { ["menu/page_size"] = 7 });
        Store().WritePatch("default.yaml", new Dictionary<string, object?> { ["menu/page_size"] = 9 });

        Assert.Equal(9L, Store().ReadMergedValue("default.yaml", "menu/page_size"));
        Assert.Single(Store().ReadPatch("default.yaml"), kv => kv.Key == "menu/page_size");
    }

    [Fact]
    public void Writing_migrates_nested_and_flat_top_shapes_to_flat_in_patch()
    {
        WriteCustom("""
            patch/menu/page_size: 5
            patch:
              switcher:
                caption: 旧标题
            """);

        Store().WritePatch("default.yaml", new Dictionary<string, object?>
        {
            ["menu/page_size"] = 6,
            ["switcher/caption"] = "新标题",
        });

        var text = File.ReadAllText(CustomPath);
        Assert.DoesNotContain("patch/", text);      // flat-top form migrated
        Assert.DoesNotContain("旧标题", text);       // nested form replaced
        var patch = Store().ReadPatch("default.yaml").ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
        Assert.Equal("6", patch["menu/page_size"]);
        Assert.Equal("新标题", patch["switcher/caption"]);
    }

    [Fact]
    public void Values_render_with_correct_yaml_types()
    {
        Store().WritePatch("default.yaml", new Dictionary<string, object?>
        {
            ["a/bool"] = true,
            ["a/float"] = 0.35,
            ["a/number_like_string"] = "true",
            ["a/plain_string"] = "Control+grave",
            ["a/hotkeys"] = new List<object?> { "Control+grave", "F4" },
            ["app_options/com.apple.Terminal"] = new Dictionary<string, object?> { ["ascii_mode"] = true },
        });

        var merged = Store().LoadMerged("default.yaml")!;
        var text = File.ReadAllText(CustomPath);

        Assert.Contains("a/bool: true", text);
        Assert.Contains("a/float: 0.35", text);
        Assert.Contains("\"true\"", text);          // must stay a string
        Assert.Contains("- Control+grave", text);

        Assert.Equal(true, Store().ReadMergedValue("default.yaml", "a/bool"));
        Assert.Equal(0.35, Store().ReadMergedValue("default.yaml", "a/float"));
        Assert.Equal("true", Store().ReadMergedValue("default.yaml", "a/number_like_string"));
        Assert.Equal(
            new List<object?> { "Control+grave", "F4" },
            Store().ReadMergedValue("default.yaml", "a/hotkeys"));
        var appOptions = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            Store().ReadMergedValue("default.yaml", "app_options/com.apple.Terminal"));
        Assert.Equal(true, appOptions["ascii_mode"]);
    }

    [Fact]
    public void Writing_over_empty_file_appends_patch()
    {
        WriteCustom("");

        Store().WritePatch("default.yaml", new Dictionary<string, object?> { ["menu/page_size"] = 7 });

        Assert.Equal(7L, Store().ReadMergedValue("default.yaml", "menu/page_size"));
    }

    [Fact]
    public void Writing_flow_style_patch_falls_back_to_structural_rewrite_with_backup()
    {
        WriteCustom("patch: {menu/page_size: 5}\n");

        Store().WritePatch("default.yaml", new Dictionary<string, object?> { ["menu/page_size"] = 9 });

        Assert.True(File.Exists(CustomPath + ".bak"));
        Assert.Equal(9L, Store().ReadMergedValue("default.yaml", "menu/page_size"));
    }

    [Fact]
    public void Writing_creates_backup_of_previous_file()
    {
        WriteCustom("patch:\n  menu/page_size: 5\n");

        Store().WritePatch("default.yaml", new Dictionary<string, object?> { ["menu/page_size"] = 9 });

        Assert.Contains("menu/page_size: 5", File.ReadAllText(CustomPath + ".bak"));
    }

    // ---------- removing ----------

    [Fact]
    public void Removing_a_key_leaves_other_keys_intact()
    {
        WriteCustom("""
            patch:
              menu/page_size: 7
              switcher:
                caption: 任务一
                fold_options: true
            """);

        Assert.True(Store().RemovePatchKeys("default.yaml", ["switcher/caption"]));

        var text = File.ReadAllText(CustomPath);
        Assert.DoesNotContain("caption", text);
        Assert.Contains("menu/page_size: 7", text);
        Assert.Contains("fold_options: true", text);
        Assert.Contains("switcher:", text); // kept: still has a child
    }

    [Fact]
    public void Removing_last_nested_child_prunes_empty_parent()
    {
        WriteCustom("""
            patch:
              menu/page_size: 7
              switcher:
                caption: 任务一
            """);

        Store().RemovePatchKeys("default.yaml", ["switcher/caption"]);

        Assert.DoesNotContain("switcher", File.ReadAllText(CustomPath));
    }

    [Fact]
    public void Removing_from_flat_top_and_append_forms()
    {
        WriteCustom("""
            patch/menu/page_size: 7
            patch/schema_list/+:
              - schema: rime_ice
            """);

        Store().RemovePatchKeys("default.yaml", ["menu/page_size", "schema_list/+"]);

        var patch = Store().ReadPatch("default.yaml");
        Assert.DoesNotContain(patch, kv => kv.Key is "menu/page_size" or "schema_list/+");
    }

    [Fact]
    public void Removing_from_missing_file_is_a_no_op()
    {
        Assert.False(Store().RemovePatchKeys("default.yaml", ["menu/page_size"]));
    }

    // ---------- merged reading ----------

    [Fact]
    public void Merged_view_overlays_patch_onto_base()
    {
        File.WriteAllText(Path.Combine(_dir, "default.yaml"), """
            menu:
              page_size: 5
            switcher:
              caption: 方案選單
              hotkeys:
                - Control+grave
            """);
        WriteCustom("""
            patch:
              menu/page_size: 9
            """);

        Assert.Equal(9L, Store().ReadMergedValue("default.yaml", "menu/page_size"));
        Assert.Equal("方案選單", Store().ReadMergedValue("default.yaml", "switcher/caption"));
        Assert.Equal(
            new List<object?> { "Control+grave" },
            Store().ReadMergedValue("default.yaml", "switcher/hotkeys"));
    }

    [Fact]
    public void Merged_view_appends_plus_keys_to_base_lists()
    {
        File.WriteAllText(Path.Combine(_dir, "default.yaml"), """
            schema_list:
              - schema: luna_pinyin
            """);
        WriteCustom("""
            patch:
              schema_list/+:
                - schema: rime_ice
            """);

        Assert.Equal(
            new List<object?>
            {
                new Dictionary<string, object?> { ["schema"] = "luna_pinyin" },
                new Dictionary<string, object?> { ["schema"] = "rime_ice" },
            },
            Store().ReadMergedValue("default.yaml", "schema_list"));
    }

    [Fact]
    public void Merged_view_without_base_builds_tree_from_patch()
    {
        WriteCustom("patch:\n  switcher/caption: 自定义\n");

        Assert.Equal("自定义", Store().ReadMergedValue("default.yaml", "switcher/caption"));
    }

    [Fact]
    public void Missing_paths_read_as_null()
    {
        Assert.Null(Store().ReadMergedValue("default.yaml", "menu/page_size"));
        Assert.Null(Store().ReadMergedValue("default.yaml", "no/such/path"));
    }

    // ---------- base resolution & naming ----------

    [Fact]
    public void User_dir_base_file_shadows_shared_dir()
    {
        File.WriteAllText(Path.Combine(_sharedDir, "default.yaml"), "menu:\n  page_size: 5\n");
        Assert.Equal(Path.Combine(_sharedDir, "default.yaml"), Store().BasePath("default.yaml"));

        File.WriteAllText(Path.Combine(_dir, "default.yaml"), "menu:\n  page_size: 6\n");
        Assert.Equal(Path.Combine(_dir, "default.yaml"), Store().BasePath("default.yaml"));
        Assert.Equal(6L, Store().ReadMergedValue("default.yaml", "menu/page_size"));
    }

    [Fact]
    public void Custom_file_name_is_derived_from_base_name()
    {
        Assert.Equal(
            Path.Combine(_dir, "squirrel.custom.yaml"),
            new RimeConfigStore(_dir).CustomPath("squirrel.yaml"));
        Assert.Equal(
            Path.Combine(_dir, "weasel.custom.yaml"),
            new RimeConfigStore(_dir).CustomPath("weasel.yaml"));
    }

    [Fact]
    public void Frontend_ui_config_file_is_platform_specific()
    {
        // Linux frontends have no appearance YAML — theming belongs to fcitx5/ibus.
        Assert.Null(new Fcitx5RimeDistribution(_dir, null, null).UiConfigFile);

        // Squirrel/Weasel patch squirrel.yaml / weasel.yaml respectively.
        var squirrelApp = Path.Combine(_dir, "Squirrel.app");
        Directory.CreateDirectory(Path.Combine(squirrelApp, "Contents", "MacOS"));
        Assert.Equal("squirrel.yaml", new SquirrelDistribution(squirrelApp).UiConfigFile);
    }
}
