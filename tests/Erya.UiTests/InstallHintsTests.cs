using Erya.Core.Distributions.Linux;
using Erya.Services;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// Deployer-missing hints must name the distro's actual librime tools package
/// — its name is not uniform: librime-bin (Debian), librime (Arch, Fedora),
/// rime (openSUSE).
/// </summary>
public sealed class InstallHintsTests
{
    [Fact]
    public void OsRelease_ids_parse_id_and_id_like_with_quotes()
    {
        var content = """
            NAME="Ubuntu"
            ID=ubuntu
            ID_LIKE="debian"
            """;
        Assert.Equal(["ubuntu", "debian"], OsRelease.Ids(content));
    }

    [Fact]
    public void OsRelease_ids_skip_comments_and_missing_fields()
    {
        var content = "# comment\nPRETTY_NAME=\"Arch Linux\"\nID=arch\n";
        Assert.Equal(["arch"], OsRelease.Ids(content));
        Assert.Empty(OsRelease.Ids(null));
        Assert.Empty(OsRelease.Ids(""));
    }

    [Fact]
    public void Fcitx5_hint_names_the_apt_package_on_ubuntu()
    {
        var hint = InstallHints.DeployerMissingHint(
            new Fcitx5RimeDistribution("/tmp/rime", sharedDataDir: null, deployerPath: null),
            "ID=ubuntu\nID_LIKE=debian\n");

        Assert.Contains("sudo apt install librime-bin", hint);
        Assert.Contains("rime_deployer", hint);
    }

    [Fact]
    public void Fcitx5_hint_names_the_pacman_package_on_arch()
    {
        var hint = InstallHints.DeployerMissingHint(
            new Fcitx5RimeDistribution("/tmp/rime", sharedDataDir: null, deployerPath: null),
            "ID=arch\n");

        Assert.Contains("sudo pacman -S librime", hint);
    }

    [Fact]
    public void Ibus_hint_names_the_dnf_package_on_fedora()
    {
        var hint = InstallHints.DeployerMissingHint(
            new IbusRimeDistribution("/tmp/rime", sharedDataDir: null, ibusPath: null, deployerPath: null),
            "ID=fedora\n");

        Assert.Contains("sudo dnf install librime", hint);
        Assert.Contains("ibus", hint);
    }

    [Fact]
    public void Hint_names_the_zypper_package_on_opensuse()
    {
        var hint = InstallHints.DeployerMissingHint(
            new Fcitx5RimeDistribution("/tmp/rime", sharedDataDir: null, deployerPath: null),
            "ID=opensuse-tumbleweed\nID_LIKE=\"opensuse suse\"\n");

        Assert.Contains("sudo zypper install rime", hint);
    }

    [Fact]
    public void Unknown_distro_falls_back_to_common_package_names()
    {
        var hint = InstallHints.DeployerMissingHint(
            new Fcitx5RimeDistribution("/tmp/rime", sharedDataDir: null, deployerPath: null),
            "ID=gentoo\n");

        Assert.DoesNotContain("sudo", hint);
        Assert.Contains("librime", hint);
    }
}
