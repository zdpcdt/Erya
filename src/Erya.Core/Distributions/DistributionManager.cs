namespace Erya.Core.Distributions;

/// <summary>
/// Aggregates locators across platforms and returns the first distribution
/// found. Locator order is the priority order (multiple frontends rarely
/// coexist, but e.g. ibus-rime and fcitx5-rime can be installed side by side).
/// </summary>
public sealed class DistributionManager
{
    private readonly IReadOnlyList<IRimeDistributionLocator> _locators;

    public DistributionManager(params IRimeDistributionLocator[] locators)
        => _locators = locators;

    public IRimeDistribution? Locate()
        => _locators.Select(l => l.Locate()).FirstOrDefault(d => d is not null);
}
