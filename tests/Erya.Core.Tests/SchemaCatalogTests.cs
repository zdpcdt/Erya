using Erya.Core.Configuration;

namespace Erya.Core.Tests;

public sealed class SchemaCatalogTests : IDisposable
{
    private readonly string _dir;

    public SchemaCatalogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "erya-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private RimeSchemaCatalog Catalog() => new(_dir);
    private string Write(string fileName, string content)
    {
        File.WriteAllText(Path.Combine(_dir, fileName), content);
        return Path.Combine(_dir, fileName);
    }

    // ---------- reading ----------

    [Fact]
    public void Selected_ids_come_from_default_yaml_when_no_patch_exists()
    {
        Write("default.yaml", """
            schema_list:
              - schema: luna_pinyin
              - schema: cangjie5
            """);

        Assert.Equal(["luna_pinyin", "cangjie5"], Catalog().ReadSelectedIds());
    }

    [Fact]
    public void Custom_patch_overrides_base_list()
    {
        Write("default.yaml", """
            schema_list:
              - schema: luna_pinyin
            """);
        Write("default.custom.yaml", """
            patch:
              schema_list:
                - schema: rime_ice
                - schema: luna_pinyin
            """);

        Assert.Equal(["rime_ice", "luna_pinyin"], Catalog().ReadSelectedIds());
    }

    [Fact]
    public void Flat_and_append_patch_forms_are_understood()
    {
        Write("default.yaml", "schema_list:\n  - schema: luna_pinyin\n");
        Write("default.custom.yaml", "patch/schema_list/+:\n  - schema: rime_ice\n");

        Assert.Equal(["luna_pinyin", "rime_ice"], Catalog().ReadSelectedIds());
    }

    [Fact]
    public void Empty_user_dir_yields_empty_selection()
    {
        Assert.Empty(Catalog().ReadSelectedIds());
        var snapshot = Catalog().LoadSnapshot();
        Assert.Empty(snapshot.Schemas);
        Assert.Empty(snapshot.SelectedSchemaIds);
    }

    [Fact]
    public void Snapshot_lists_disk_schemas_after_active_ones()
    {
        Write("luna_pinyin.schema.yaml", """
            schema:
              schema_id: luna_pinyin
              name: 朙月拼音
            """);
        Write("rime_ice.schema.yaml", """
            schema:
              schema_id: rime_ice
              name: 雾凇拼音
            """);
        Write("default.custom.yaml", """
            patch:
              schema_list:
                - schema: rime_ice
            """);

        var snapshot = Catalog().LoadSnapshot();
        Assert.Equal(
        [
            new("rime_ice", "雾凇拼音"),   // active, patch order
            new("luna_pinyin", "朙月拼音"), // inactive, discovered on disk
        ], snapshot.Schemas);
        Assert.Equal(["rime_ice"], snapshot.SelectedSchemaIds);
    }

    // ---------- writing ----------

    [Fact]
    public void Writing_to_fresh_dir_creates_patch_file()
    {
        Catalog().WriteSelectedSchemas(["rime_ice", "luna_pinyin"]);

        var text = File.ReadAllText(Path.Combine(_dir, "default.custom.yaml"));
        Assert.Contains("patch:", text);
        Assert.Contains("- schema: rime_ice", text);
        Assert.Contains("- schema: luna_pinyin", text);
        Assert.Equal(["rime_ice", "luna_pinyin"], Catalog().ReadSelectedIds());
    }

    [Fact]
    public void Writing_replaces_list_but_preserves_comments_and_other_patches()
    {
        Write("default.custom.yaml", """
            # 該文件由「尔雅」生成 —— 用户自定义配置
            # 修改后需重新部署方可生效

            patch:
              # 手动追加的开关
              menu/page_size: 7
              schema_list:
                - schema: luna_pinyin
                - schema: cangjie5
              ascii_composer/good_old_caps_lock: true
            """);

        Catalog().WriteSelectedSchemas(["rime_ice"]);

        var text = File.ReadAllText(Path.Combine(_dir, "default.custom.yaml"));
        Assert.Contains("# 該文件由「尔雅」生成", text);
        Assert.Contains("# 手动追加的开关", text);
        Assert.Contains("menu/page_size: 7", text);
        Assert.Contains("ascii_composer/good_old_caps_lock: true", text);
        Assert.DoesNotContain("cangjie5", text);
        Assert.Equal(["rime_ice"], Catalog().ReadSelectedIds());
    }

    [Fact]
    public void Writing_inserts_schema_list_into_existing_patch_block()
    {
        Write("default.custom.yaml", """
            # 我的配置
            patch:
              menu/page_size: 7
            """);

        Catalog().WriteSelectedSchemas(["rime_ice", "luna_pinyin"]);

        var text = File.ReadAllText(Path.Combine(_dir, "default.custom.yaml"));
        Assert.Contains("# 我的配置", text);
        Assert.Contains("menu/page_size: 7", text);
        Assert.Equal(["rime_ice", "luna_pinyin"], Catalog().ReadSelectedIds());
    }

    [Fact]
    public void Writing_appends_patch_block_when_file_has_other_top_level_keys()
    {
        Write("default.custom.yaml", """
            # 仅含其它顶层键的少见文件
            menu/page_size: 7
            """);

        Catalog().WriteSelectedSchemas(["rime_ice"]);

        var text = File.ReadAllText(Path.Combine(_dir, "default.custom.yaml"));
        Assert.Contains("menu/page_size: 7", text);
        Assert.Equal(["rime_ice"], Catalog().ReadSelectedIds());
    }

    [Fact]
    public void Writing_empty_selection_writes_explicit_empty_list()
    {
        Write("default.custom.yaml", """
            patch:
              schema_list:
                - schema: luna_pinyin
            """);

        Catalog().WriteSelectedSchemas([]);

        Assert.Empty(Catalog().ReadSelectedIds());
    }

    [Fact]
    public void Writing_over_an_empty_custom_file_appends_patch()
    {
        // Weasel creates an empty default.custom.yaml on first run.
        Write("default.custom.yaml", "");

        Catalog().WriteSelectedSchemas(["rime_ice"]);

        Assert.Equal(["rime_ice"], Catalog().ReadSelectedIds());
    }

    [Fact]
    public void Writing_is_idempotent()
    {
        Catalog().WriteSelectedSchemas(["rime_ice", "luna_pinyin"]);
        var first = File.ReadAllText(Path.Combine(_dir, "default.custom.yaml"));

        Catalog().WriteSelectedSchemas(["rime_ice", "luna_pinyin"]);
        var second = File.ReadAllText(Path.Combine(_dir, "default.custom.yaml"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void Writing_flow_style_patch_falls_back_to_structural_rewrite_with_backup()
    {
        var path = Write("default.custom.yaml", "patch: {menu/page_size: 7}\n");

        Catalog().WriteSelectedSchemas(["rime_ice"]);

        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(["rime_ice"], Catalog().ReadSelectedIds());
        var custom = File.ReadAllText(Path.Combine(_dir, "default.custom.yaml"));
        Assert.Contains("menu/page_size: 7", custom);
    }

    [Fact]
    public void Writing_creates_a_backup_of_the_previous_file()
    {
        Write("default.custom.yaml", """
            patch:
              schema_list:
                - schema: luna_pinyin
            """);

        Catalog().WriteSelectedSchemas(["rime_ice"]);

        var backup = File.ReadAllText(Path.Combine(_dir, "default.custom.yaml.bak"));
        Assert.Contains("luna_pinyin", backup);
    }

    [Fact]
    public void Same_indent_sequence_items_are_recognized_as_the_list()
    {
        // YamlDotNet emits sequences at the same indent as their key;
        // hand-written files sometimes look like this too.
        Write("default.custom.yaml", """
            patch:
              schema_list:
              - schema: luna_pinyin
              menu/page_size: 7
            """);

        Catalog().WriteSelectedSchemas(["rime_ice"]);

        var text = File.ReadAllText(Path.Combine(_dir, "default.custom.yaml"));
        Assert.Contains("menu/page_size: 7", text);
        Assert.DoesNotContain("luna_pinyin", text);
        Assert.Equal(["rime_ice"], Catalog().ReadSelectedIds());
    }
}
