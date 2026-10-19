namespace Erya.Core.Distributions;

/// <summary>
/// Aggregates locators across platforms. Locator order is the priority order
/// (multiple frontends rarely coexist, but e.g. ibus-rime and fcitx5-rime can
/// be installed side by side; on such machines <see cref="LocateAll"/> feeds
/// the frontend switcher).
/// </summary>
public sealed class DistributionManager
{
    private readonly IReadOnlyList<IRimeDistributionLocator> _locators;

    public DistributionManager(params IRimeDistributionLocator[] locators)
        => _locators = locators;

    /// <summary>Every distribution present on this machine, in locator order.</summary>
    public IReadOnlyList<IRimeDistribution> LocateAll()
        => [.. _locators.Select(l => l.Locate()).OfType<IRimeDistribution>()];

    public IRimeDistribution? Locate() => LocateAll().FirstOrDefault();
}
